namespace RenderAPI.Core.Requests
{
    public class ApiDeleteRequest
    {
        public UInt64 TaskId { get; set; }

        public ApiDeleteRequest(UInt64 taskId)
        {
            TaskId = taskId;
        }
    }
}
