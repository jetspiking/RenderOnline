namespace HPCServer.Core.Configuration
{
    public class HPCEngine
    {
        public String EngineId { get; set; }
        public String ExecutablePath { get; set; }

        public HPCEngine(String engineId, String executablePath)
        {
            EngineId = engineId;
            ExecutablePath = executablePath;
        }
    }
}
