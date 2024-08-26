using SerialComm.PortEvents.Common;
using System.Collections.Concurrent;

namespace SerialComm.PortEvents
{
    internal class ComPortEventLocker
    {
        public object LockObj { get; set; } = new object();
        public bool IsProcessing { get; set; } = false;
        public ConcurrentQueue<PortEventType> ComPortEventQueue { get; } = new ConcurrentQueue<PortEventType>();
    }
}
