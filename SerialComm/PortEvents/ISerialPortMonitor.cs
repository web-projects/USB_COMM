using SerialComm.PortEvents.Common;
using System;

namespace SerialComm.PortEvents
{
    public interface ISerialPortMonitor : IDisposable
    {
        event ComPortEventHandler ComportEventOccured;
        void StartMonitoring();
        void StopMonitoring();
    }
}
