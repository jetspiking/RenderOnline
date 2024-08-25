using RenderAPI.Core.Api;
using RenderAPI.Core.Database;

namespace RenderAPI.Core.Responses
{
    public class ApiTaskInfo
    {
        public ApiTask Task { get; set; }
        public ApiRender Render { get; set; }

        public ApiTaskInfo(ApiTask task, ApiRender render)
        {
            Task = task;
            Render = render;
        }
    }
}
