using RenderAPI.Core.Api;

namespace RenderAPI.HPCServer
{
    public class HPCStatusResponse
    {
        public String[]? EngineIds { get; set; } = null;
        public HPCTask? Task { get; set; } = null;

        public HPCStatusResponse(String[]? engineIds, HPCTask? task)
        {
            EngineIds=engineIds;
            Task=task;
        }
    }
}
