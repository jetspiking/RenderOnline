namespace RenderAPI.HPCServer
{
    public class HPCStartArgs
    {
        public String? EngineId { get; set; }
        public UInt64 TaskId { get; set; }
        public String? Arguments { get; set; }

        public HPCStartArgs(String? engineId, UInt64 taskId, String? arguments)
        {
            EngineId = engineId;
            TaskId = taskId;
            Arguments = arguments;
        }
    }
}
