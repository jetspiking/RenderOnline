using RenderAPI.Core.Api;

namespace RenderAPI.Core.Requests
{
    public class ApiEnqueueRequest
    {
        public String EngineId { get; set; }
        public List<ApiArgType> Arguments { get; set; }
    }
}
