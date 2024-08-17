namespace HPCServer.Core.Configuration
{
    public class HPCTask
    {
        public UInt64 TaskId { get; set; }
        public Boolean IsSuccess { get; set; }
        public Boolean IsRunning { get; set; }
        public UInt64 TotalSeconds { get; set; }

        public HPCTask(UInt64 taskId, Boolean isSuccess, Boolean isRunning, UInt64 totalSeconds)
        {
            TaskId = taskId;
            IsSuccess = isSuccess;
            IsRunning = isRunning;
            TotalSeconds = totalSeconds;
        }
    }
}
