using System;

namespace RNOClient.Core.RenderAPI.Api
{
    public class ApiTask
    {
        public UInt64 TaskId { get; set; }
        public UInt16 UserId { get; set; }
        public DateTime? QueueTime { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public Boolean IsRunning { get; set; }
        public Boolean IsSuccess { get; set; }
        public UInt64 RenderId { get; set; }
        public Byte? MachineId { get; set; }

        public ApiTask(UInt64 taskId, UInt16 userId, DateTime? queueTime, DateTime? startTime, DateTime? endTime, Boolean isRunning, Boolean isSuccess, UInt64 renderId, Byte? machineId)
        {
            TaskId = taskId;
            UserId = userId;
            QueueTime = queueTime;
            StartTime = startTime;
            EndTime = endTime;
            IsRunning = isRunning;
            IsSuccess = isSuccess;
            RenderId = renderId;
            MachineId = machineId;
        }
    }
}
