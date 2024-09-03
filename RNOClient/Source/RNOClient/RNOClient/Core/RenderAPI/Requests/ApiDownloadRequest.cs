using System;

namespace RNOClient.Core.RenderAPI.Requests
{
    public class ApiDownloadRequest
    {
        public UInt64 TaskId { get; set; }

        public ApiDownloadRequest(UInt64 taskId)
        {
            TaskId = taskId;
        }
    }
}
