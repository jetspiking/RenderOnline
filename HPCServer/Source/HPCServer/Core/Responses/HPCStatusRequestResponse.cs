using HPCServer.Core.Configuration;

namespace HPCServer.Core.Responses
{
    public class HPCStatusRequestResponse
    {
        public String[]? EngineIds { get; set; } = null;
        public HPCTask? Task { get; set; } = null;

        public HPCStatusRequestResponse(String[]? engineIds, HPCTask? task)
        {
            this.EngineIds = engineIds;
            Task = task;
        }
    }
}
