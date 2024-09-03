using RNOClient.Core.RenderAPI.Api;
using System.Collections.Generic;

namespace RNOClient.Core.RenderAPI.Responses
{
    public class ApiInfoResponse
    {
        public ApiUser User { get; set; }
        public List<ApiTaskInfo> Tasks { get; set; }

        public ApiInfoResponse(ApiUser user, List<ApiTaskInfo> tasks)
        {
            User = user;
            Tasks = tasks;
        }
    }
}
