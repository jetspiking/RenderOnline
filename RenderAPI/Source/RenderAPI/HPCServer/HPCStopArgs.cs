namespace RenderAPI.HPCServer
{
    public class HPCStopArgs
    {
        public UInt64 TaskId { get; set; }

        public HPCStopArgs(UInt64 taskId)
        {
            TaskId = taskId;
        }
    }
}
