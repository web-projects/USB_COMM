using System.Threading.Tasks;

namespace SerialComm.PortEvents.Common
{
    public delegate Task ComPortEventHandler(PortEventType comPortEvent, string portNumber);
}
