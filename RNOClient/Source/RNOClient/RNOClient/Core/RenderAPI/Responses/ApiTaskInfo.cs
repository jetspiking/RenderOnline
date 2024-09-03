using RNOClient.Core.RenderAPI.Api;

namespace RNOClient.Core.RenderAPI.Responses
{
    public class ApiTaskInfo
    {
        public ApiTask Task { get; set; }
        public ApiRender? Render { get; set; }
        public ApiEngine? Engine { get; set; }

        public ApiTaskInfo(ApiTask task, ApiRender? render, ApiEngine? engine)
        {
            Task = task;
            Render = render;
            Engine = engine;
        }
    }
}
