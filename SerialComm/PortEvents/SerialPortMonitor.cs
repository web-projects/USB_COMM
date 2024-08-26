using SerialComm.PortEvents.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace SerialComm.PortEvents
{
    public sealed class SerialPortMonitor : ISerialPortMonitor
    {
        private static readonly string deviceId = "DeviceID";
        private static readonly string usb = "USB";
        private static readonly string specificIdTechSearcherString = $"Select * From Win32_PnPEntity WHERE DeviceID Like \"%usb%{SerialDeviceVid.IdTechVid}%\" ";

        private static List<string> SupportedIdTechPids = new List<string>
        {
            SerialDevicePid.SREDKEY2_PID_HID,
            SerialDevicePid.SREDKEY2_PID_KB,
            SerialDevicePid.SECUREKEY_PID_HID,
            SerialDevicePid.SECUREKEY_PID_KB,
            SerialDevicePid.AUGUSTAS_PID_HID,
            SerialDevicePid.AUGUSTAS_PID_KB,
            SerialDevicePid.AUGUSTA_PID_HID,
            SerialDevicePid.AUGUSTA_PID_KB,
            SerialDevicePid.SECUREMAG_PID_HID,
            SerialDevicePid.SECUREMAG_PID_KB,
            SerialDevicePid.SECURED_PID_HID,
            SerialDevicePid.SECURED_PID_KB,
        };

        public event ComPortEventHandler ComportEventOccured;

        private string[] serialPorts;
        private ManagementEventWatcher usbHubArrival;
        private bool usbHubArrivalSubscribed;
        private ManagementEventWatcher usbHubRemoval;
        private bool usbHubRemovalSubscribed;

        private ManagementEventWatcher usbSerialArrival;
        private ManagementEventWatcher usbSerialRemoval;

        public void StartMonitoring()
        {
            serialPorts = GetAvailableSerialPorts();

            // USB Port devices
            MonitorUSBSerialDevicesPortChanges();

            // USB HUB devices
            MonitorUsbHubDevicesChanges();
        }

        public void StopMonitoring() => Dispose();

        public void Dispose()
        {
            if (usbSerialArrival != null)
            {
                if (usbHubArrivalSubscribed)
                {
                    usbHubArrivalSubscribed = false;
                    usbSerialArrival.EventArrived -= (sender, eventArgs) => RaiseUsbSerialPortsChangedIfNecessary(PortEventType.Insertion, eventArgs);
                    usbSerialArrival.Stop();
                }
                usbSerialArrival.Dispose();
                usbSerialArrival = null;
            }
            if (usbSerialRemoval != null)
            {
                if (usbHubRemovalSubscribed)
                {
                    usbHubRemovalSubscribed = false;
                    usbSerialRemoval.EventArrived -= (sender, eventArgs) => RaiseUsbSerialPortsChangedIfNecessary(PortEventType.Removal, eventArgs);
                    usbSerialRemoval.Stop();
                }
                usbSerialRemoval.Dispose();
                usbSerialRemoval = null;
            }
            if (usbHubArrival != null)
            {
                usbHubArrival.Stop();
                usbHubArrival.Dispose();
                usbHubArrival = null;
            }
            if (usbHubRemoval != null)
            {
                usbHubRemoval.Stop();
                usbHubRemoval.Dispose();
                usbHubRemoval = null;
            }
        }

        private string[] GetAvailableSerialPorts()
        {
            string[] portNames = System.IO.Ports.SerialPort.GetPortNames();
            List<string> supportedPidVid = GetAvailableIdTechDevices();
            return portNames.Concat(supportedPidVid).ToArray();
        }

        private List<string> GetAvailableIdTechDevices()
        {
            List<string> supportedPidVidFound = new List<string>();

            using ManagementObjectSearcher searcher = new ManagementObjectSearcher(specificIdTechSearcherString);

            using ManagementObjectCollection collection = searcher.Get();

            foreach (ManagementBaseObject device in collection)
            {
                string deviceIDStr = device.GetPropertyValue(deviceId)?.ToString();
                if (!string.IsNullOrWhiteSpace(deviceIDStr)
                    && deviceIDStr.Contains(usb, StringComparison.OrdinalIgnoreCase)
                    && deviceIDStr.Contains(SerialDeviceVid.IdTechVid, StringComparison.OrdinalIgnoreCase))
                {
                    string foundDevice = SupportedIdTechPids.Where(a => deviceIDStr.Contains(a, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(foundDevice))
                    {
                        continue;
                    }
                    supportedPidVidFound.Add($"{SerialDeviceVid.IdTechVid}_{foundDevice}");
                }
            }

            return supportedPidVidFound;
        }

        #region --- USB PORT DEVICES ----
        private void MonitorUSBSerialDevicesPortChanges()
        {
            try
            {
                // Detect insertion of all USBSerial devices - Query every 1 second for device remove/insert
                WqlEventQuery deviceArrivalQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");
                usbSerialArrival = new ManagementEventWatcher(deviceArrivalQuery);
                usbSerialArrival.EventArrived += new EventArrivedEventHandler(RaiseUsbSerialPortsChangedIfNecessary);
                // Start listening for USB device Arrival events
                usbSerialArrival.Start();
                usbHubArrivalSubscribed = true;

                // Detect removal of all USBSerial devices
                WqlEventQuery deviceRemovalQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");
                usbSerialRemoval = new ManagementEventWatcher(deviceRemovalQuery);
                usbSerialRemoval.EventArrived += new EventArrivedEventHandler(RaiseUsbSerialPortsChangedIfNecessary);
                // Start listening for USB Removal events
                usbSerialRemoval.Start();
                usbHubRemovalSubscribed = true;
            }
            catch (ManagementException e)
            {
                Console.WriteLine($"serial: COMM exception={e.Message}");
            }
        }

        private PortEventType GetEventType(string eventName) => eventName switch
        {
            "2" => PortEventType.Insertion,
            "3" => PortEventType.Removal,
            _ => PortEventType.Unknown
        };

        private void RaiseUsbSerialPortsChangedIfNecessary(object sender, EventArrivedEventArgs eventArgs)
        {
            PortEventType eventType = GetEventType(eventArgs.NewEvent.GetPropertyValue("EventType").ToString());
            lock (serialPorts)
            {
                string[] availableSerialPorts = GetAvailableSerialPorts();

                if (eventType == PortEventType.Insertion)
                {
                    if (!serialPorts?.SequenceEqual(availableSerialPorts) ?? false)
                    {
                        string[] added = availableSerialPorts.Except(serialPorts).ToArray();
                        if (added.Length > 0)
                        {
                            serialPorts = availableSerialPorts;

                            ComportEventOccured?.Invoke(PortEventType.Insertion, added[0]);
                        }
                    }
                }
                else if (eventType == PortEventType.Removal)
                {
                    string[] removed = serialPorts.Except(availableSerialPorts).ToArray();
                    if (removed.Length > 0)
                    {
                        serialPorts = availableSerialPorts;

                        ComportEventOccured?.Invoke(PortEventType.Removal, removed[0]);
                    }
                }
            }
        }
        #endregion --- USB PORT DEVICES

        #region --- USB HUB DEVICES ----
        private void MonitorUsbHubDevicesChanges()
        {
            try
            {
                // Detect insertion of all USBHub devices - Query every 1 second for device remove/insert
                WqlEventQuery deviceArrivalQuery = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
                usbHubArrival = new ManagementEventWatcher(deviceArrivalQuery);
                usbHubArrival.EventArrived += (sender, eventArgs) => RaiseUsbHubPortsChangedIfNecessary(PortEventType.Insertion, eventArgs);
                // Start listening for events
                usbHubArrival.Start();

                // Detect removal of all USBHub devices
                WqlEventQuery deviceRemovalQuery = new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
                usbHubRemoval = new ManagementEventWatcher(deviceRemovalQuery);
                usbHubRemoval.EventArrived += (sender, eventArgs) => RaiseUsbHubPortsChangedIfNecessary(PortEventType.Removal, eventArgs);
                // Start listening for events
                usbHubRemoval.Start();
            }
            catch (ManagementException e)
            {
                Console.WriteLine($"serial: COMM exception={e.Message}");
            }
        }

        private void RaiseUsbHubPortsChangedIfNecessary(PortEventType eventType, EventArrivedEventArgs eventArgs)
        {
            lock (serialPorts)
            {
                using (ManagementBaseObject targetObject = eventArgs.NewEvent["TargetInstance"] as ManagementBaseObject)
                {
                    string targetDeviceID = targetObject["DeviceID"]?.ToString() ?? string.Empty;
                    if (targetDeviceID.Contains(SerialDeviceVid.MagTekVid, StringComparison.OrdinalIgnoreCase))        //hardcode value for Magtek
                    {
                        ComportEventOccured?.Invoke(eventType, targetDeviceID);
                        return;
                    }
                }
            }
        }
        #endregion --- MAGTEK DEVICES
    }
}
