using SerialComm.PortEvents;
using SerialComm.PortEvents.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SerialComm
{
    class SerialComm
    {
        private SerialPort comPort;
        private SerialDataReceivedEventHandler receiver;

        private SerialPortMonitor serialPortMonitor = new SerialPortMonitor();
        private ConcurrentDictionary<string, ComPortEventLocker> comPortEventLock = new ConcurrentDictionary<string, ComPortEventLocker>();
        public PortEventType LastPortEvent { get; private set; } = PortEventType.Unknown;

        public int DeviceDiscoveryDelay = Convert.ToInt32(ConfigurationManager.AppSettings["DeviceDiscoveryDelay"] ?? "1000");
        public List<string> ComPortBlackList = new List<string>()
        { 
            ConfigurationManager.AppSettings["ComPortBlackList"] ?? string.Empty
        };

        public string[] Initialize(SerialDataReceivedEventHandler handler)
        {
            receiver = handler;

            serialPortMonitor.ComportEventOccured += OnComPortEventReceived;

            string[] ports = SerialPort.GetPortNames();

            foreach (var port in ports)
            {
                Debug.WriteLine("PORT FOUND: {0}", (object)port);
            }

            return ports;
        }

        public Boolean Open(string port, string baud, string databits, string stopbits, string handshake, string parity)
        {
            if (!comPort?.IsOpen ?? true)
            {
                try
                {
                    comPort = new SerialPort();
                    comPort.PortName = Convert.ToString(port);
                    comPort.BaudRate = Convert.ToInt32(baud);
                    comPort.DataBits = Convert.ToInt16(databits);
                    comPort.StopBits = (StopBits)Enum.Parse(typeof(StopBits), stopbits);
                    comPort.Handshake = (Handshake)Enum.Parse(typeof(Handshake), handshake);
                    comPort.Parity = (Parity)Enum.Parse(typeof(Parity), parity);
                    comPort.Open();
                    comPort.ReadTimeout = 4000;
                    comPort.WriteTimeout = 6000;
                    comPort.DataReceived += receiver;
                    Debug.WriteLine("Open PORT: {0} at {1} BAUD", comPort.PortName, comPort.BaudRate);

                    serialPortMonitor.StartMonitoring();

                    return true;
                }
                catch (UnauthorizedAccessException ex)
                {
                    Debug.WriteLine("Open exception : {0}", (object)ex.Message);
                }
            }

            return false;
        }

        public void Close()
        {
            if (comPort?.IsOpen ?? false)
            {
                comPort.Close();
                serialPortMonitor.Dispose();
            }
        }

        public void Write(string data)
        {
            if (comPort?.IsOpen ?? false)
            {
                try
                {
                    byte[] asciiBytes = Encoding.ASCII.GetBytes(data);
                    comPort.Write(asciiBytes, 0, asciiBytes.Length);
                    Debug.WriteLine("PORT WRITE: [{0}]", (object)data);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("PORT WRITE Exception : {0}", (object)ex.Message);
                }
            }
        }

        public string Read()
        {
            if (comPort?.IsOpen ?? false)
            {
                try
                {
                    string data = comPort.ReadLine();
                    Debug.WriteLine("PORT READ: [{0}]", (object)data);
                    return data;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("PORT READ Exception : {0}", (object)ex.Message);
                }
            }

            return null;
        }

        private async Task OnComPortEventReceived(PortEventType comPortEvent, string portNumber)
        {
            if (ComPortBlackList.Where(x => x.Equals(portNumber, StringComparison.OrdinalIgnoreCase)).Count() > 0)
            {
                string comEvent = (comPortEvent == PortEventType.Insertion) ? "plugged" : (comPortEvent == PortEventType.Removal) ? "unplugged" : "unknown";
                //_ = LoggingClient.LogWarnAsync($"Comport '{comEvent}' event detected on BLACKLISTED port '{portNumber}': no further action will be performed.");
                Debug.WriteLine($"Comport '{comEvent}' event detected on BLACKLISTED port '{portNumber}': no further action will be performed.");
                return;
            }

            if (!comPortEventLock.ContainsKey(portNumber))
            {
                comPortEventLock.TryAdd(portNumber, new ComPortEventLocker());
            }

            if (comPortEventLock.TryGetValue(portNumber, out var eventLocker))
            {
                eventLocker.ComPortEventQueue.Enqueue(comPortEvent);
                //_ = LoggingClient.LogInfoAsync($"Queued comport event '{comPortEvent}' on port '{portNumber}'.");
                Debug.WriteLine($"Queued comport event '{comPortEvent}' on port '{portNumber}'.");
            }

            await CheckComPortQueue(portNumber).ConfigureAwait(false);
        }

        private async Task HandleComPortEvent(PortEventType comPortEvent, string portNumber)
        {
            int thisDeviceDiscoverCounter = 0;

            try
            {
                //_ = LoggingClient.LogInfoAsync($"Processing Comport event '{comPortEvent}' on port '{portNumber}'.");
                Debug.WriteLine($"Processing Comport event '{comPortEvent}' on port '{portNumber}'.");

                bool performDeviceDiscovery = false;
                if (comPortEvent == PortEventType.Insertion)
                {
                    LastPortEvent = PortEventType.Insertion;
                    //_ = LoggingClient.LogInfoAsync($"Comport Plugged in event on port '{portNumber}'. Detecting a new connection...");
                    Debug.WriteLine($"Comport Plugged in event on port '{portNumber}'. Detecting a new connection...");
                    performDeviceDiscovery = true;

                    // wait for USB driver to detach/reattach device
                    await Task.Delay(DeviceDiscoveryDelay * 1024);
                }
                else if (comPortEvent == PortEventType.Removal)
                {
                    LastPortEvent = PortEventType.Removal;
                    //_ = LoggingClient.LogInfoAsync($"Comport Unplugged event on port '{portNumber}'. Updating target device list..");
                    Debug.WriteLine($"Comport Unplugged event on port '{portNumber}'. Updating target device list..");
                    //performDeviceDiscovery = await PublishDeviceDisconnectEventAsync(portNumber);
                    //DeviceActionCoordinator.RemoveUnpluggedDevice(portNumber);
                }
                else
                {
                    LastPortEvent = PortEventType.Unknown;
                    //_ = LoggingClient.LogInfoAsync($"Unknown Comport event has been raised on port '{portNumber}'. Handler not implemented for this action.");
                    Debug.WriteLine($"Unknown Comport event has been raised on port '{portNumber}'. Handler not implemented for this action.");
                }

                // Only perform discovery when an existing device is disconnected or a new connection is detected
                if (!performDeviceDiscovery)
                {
                    //_ = LoggingClient.LogWarnAsync($"Device discovery is not needed on port {portNumber}.");
                    Debug.WriteLine($"Device discovery is not needed on port {portNumber}.");
                    return;
                }

                var stopwatch = Stopwatch.StartNew();
                //Interlocked.CompareExchange(ref deviceDiscoverCounter, 0, int.MaxValue);
                //Interlocked.Increment(ref deviceDiscoverCounter);

                //thisDeviceDiscoverCounter = deviceDiscoverCounter;

                //_ = LoggingClient.LogInfoAsync($"Device discovery #{thisDeviceDiscoverCounter} started on port {portNumber}.");
                Debug.WriteLine($"Device discovery #{thisDeviceDiscoverCounter} started on port {portNumber}.");

                // Only perform discovery when an existing device is disconnected or a new connection is detected
                try
                {
                    /**
                     * Utilize a low level port event filter to determine the best
                     * time that device discovery should take place. This solves 3
                     * key problems that can happen.
                     * 
                     * 1. Unplugged device.
                     * 
                     *      The filter should quickly turn-around and allow device
                     *      discovery since we are in a low-risk situation.
                     *      
                     * 2. Plugged in device (ON)
                     * 
                     *      The filter should validate connectivity to the specified
                     *      port and a response should be given back in a short
                     *      time-frame when the device is in a good state. If this is
                     *      true then device discovery will be allowed.
                     *      
                     * 3. Plugged in device (Power Cycle)
                     * 
                     *      The filter will fail validation on step 2 quickly and have
                     *      a reasonable timeout before it fails completely. It will 
                     *      attempt to check the port continuously to see if it can be
                     *      opened. The minute that it can do so before complete timeout
                     *      occurs, then it will allow device discovery to take place.
                     */
                    bool deviceFound = false;
                    /*
                    DeviceFilterOptions deviceFilterOptions = new DeviceFilterOptions()
                    {
                        PortNumber = portNumber,
                        SuppressIdTechDevices = ExtendedConfiguration.SuppressIdTechDevices,
                        DelayForIdTechHidSwitchSeconds = Configuration.IdTech?.HIDModeSwitchDelaySeconds ?? DeviceFilterOptions.DefaultDelayForIdTechHidSwitchSeconds
                    };

                    List<IDeviceDiscovery> deviceDiscoveryList = DeviceDiscoveryProvider.GetDeviceDiscoveryList();

                    foreach (IDeviceDiscovery deviceDiscovery in deviceDiscoveryList)
                    {
                        deviceDiscovery.SetDeviceMockConfig(Configuration.DeviceMockSettingsConfig);

                        if (deviceDiscovery.FindDevices())
                        {
                            foreach (USBDeviceInfo device in deviceDiscovery.DeviceInfo)
                            {
                                if (device.ComPort.Equals(portNumber, StringComparison.OrdinalIgnoreCase))
                                {
                                    using ILowPassPortEventFilter lowPassPortEventFilter = await PortEventFilterProvider.GetLowPassPortFilterAsync(LoggingClient, deviceFilterOptions);

                                    if (await lowPassPortEventFilter.FilterAsync(new PortFilterEvent(comPortEvent, portNumber)))
                                    {
                                        _ = LoggingClient.LogInfoAsync($"Device discovery #{thisDeviceDiscoverCounter} in progress for port '{portNumber}'.");

                                        await DeviceDiscoveryManagementCoordinator.PerformSingleEventDiscoveryAsync(new DeviceDiscoveryOptions(comPortEvent, portNumber));
                                        deviceFound = true;
                                    }
                                    else
                                    {
                                        _ = LoggingClient.LogInfoAsync($"Device discovery #{thisDeviceDiscoverCounter} low pass port filter failed for port '{portNumber}'.");
                                        deviceFound = false;
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    */

                    if (!deviceFound)
                    {
                        //_ = LoggingClient.LogInfoAsync($"Device discovery #{thisDeviceDiscoverCounter} canceled for port '{portNumber}' - no qualified device found.");
                        Debug.WriteLine($"Device discovery #{thisDeviceDiscoverCounter} canceled for port '{portNumber}' - no qualified device found.");

                        //int deviceCount;
                        //lock (targetDevicesLocker)
                        //{
                        //    deviceCount = TargetDevices.Count;
                        //}

                        //if (deviceCount == 0)
                        //{
                        //    PublishNoDeviceConnectedEvent(StringValueAttribute.GetStringValue(DeviceDiscoveryResults.NoDeviceAvailable));
                        //}
                    }
                }
                catch (Exception ex)
                {
                    // Log exception and let it bubble up
                    //_ = LoggingClient.LogErrorAsync($"Device discovery #{thisDeviceDiscoverCounter} exception on port '{portNumber}': {ex}.");
                    Debug.WriteLine($"Device discovery #{thisDeviceDiscoverCounter} exception on port '{portNumber}': {ex}.");
                    throw;
                }
                finally
                {
                    stopwatch.Stop();
                    //_ = LoggingClient.LogInfoAsync($"Device discovery #{thisDeviceDiscoverCounter} took {stopwatch.Elapsed.TotalSeconds}s on port '{portNumber}'.");
                    Debug.WriteLine($"Device discovery #{thisDeviceDiscoverCounter} took {stopwatch.Elapsed.TotalSeconds}s on port '{portNumber}'.");
                }
            }
            catch (Exception ex)
            {
                //_ = LoggingClient.LogErrorAsync($"Device discovery #{thisDeviceDiscoverCounter} Unhandled exception in HandleComPortEvent for com event '{comPortEvent}' on port '{portNumber}'.", ex);
                Debug.WriteLine($"Device discovery #{thisDeviceDiscoverCounter} Unhandled exception in HandleComPortEvent for com event '{comPortEvent}' on port '{portNumber}'.", ex);
            }
        }

        private async Task<bool> CheckComPortQueue(string portNumber)
        {
            if (comPortEventLock.TryGetValue(portNumber, out var eventLock))
            {
                lock (eventLock.LockObj)
                {
                    // Set the process lock, or exit if busy processing another event
                    if (eventLock.IsProcessing)
                    {
                        //_ = LoggingClient.LogInfoAsync($"Comport '{portNumber}' is busy processing queue.");
                        Debug.WriteLine($"Comport '{portNumber}' is busy processing queue.");
                        return false;
                    }
                    else
                    {
                        eventLock.IsProcessing = true;
                    }
                }

                // Dequeue from com port queue
                if (eventLock.ComPortEventQueue.TryDequeue(out PortEventType comPortEvent))
                {
                    await HandleComPortEvent(comPortEvent, portNumber).ConfigureAwait(false);

                    // Release the process lock
                    lock (eventLock.LockObj)
                    {
                        eventLock.IsProcessing = false;
                    }

                    // Check queue again until cleared
                    await CheckComPortQueue(portNumber).ConfigureAwait(false);

                    // Successful check and complete
                    return true;
                }
                else
                {
                    // Release the process lock
                    lock (eventLock.LockObj)
                    {
                        eventLock.IsProcessing = false;
                    }

                    // Successful check and complete
                    return true;
                }
            }

            return false;
        }
    }
}
