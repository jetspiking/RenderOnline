namespace RenderAPI.Core.Database
{
    public class DbMachine
    {
        public Byte MachineId { get; set; }
        public String IpAddress { get; set; }
        public Int32 Port { get; set; }

        public DbMachine(Byte machineId, String ipAddress, Int32 port)
        {
            MachineId = machineId;
            IpAddress = ipAddress;
            Port = port;
        }
    }
}
