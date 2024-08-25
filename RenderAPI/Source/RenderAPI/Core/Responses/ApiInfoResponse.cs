using RenderAPI.Core.Api;
using RenderAPI.Core.Database;

namespace RenderAPI.Core.Responses
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
