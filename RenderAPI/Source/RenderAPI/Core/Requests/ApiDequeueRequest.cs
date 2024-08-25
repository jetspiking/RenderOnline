namespace RenderAPI.Core.Requests
{
    public class ApiDequeueRequest
    {
        public UInt64 TaskId { get; set; }

        public ApiDequeueRequest(UInt64 taskId)
        {
            TaskId = taskId;
        }
    }
}
