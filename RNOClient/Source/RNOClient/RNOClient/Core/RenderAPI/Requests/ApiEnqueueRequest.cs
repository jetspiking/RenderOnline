using RNOClient.Core.RenderAPI.Api;
using System;
using System.Collections.Generic;

namespace RNOClient.Core.RenderAPI.Requests
{
    public class ApiEnqueueRequest
    {
        public String EngineId { get; set; }
        public List<ApiArgType> Arguments { get; set; }

        public ApiEnqueueRequest(String engineId, List<ApiArgType> arguments)
        {
            EngineId = engineId;
            Arguments = arguments;
        }
    }
}
