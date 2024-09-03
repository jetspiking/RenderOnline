using System;

namespace RNOClient.Core.RenderAPI.Responses
{
    public class ApiDequeueResponse
    {
        public Boolean IsRemoved { get; set; }
        public String ErrorMessage { get; set; }

        public ApiDequeueResponse(Boolean isRemoved, String errorMessage)
        {
            IsRemoved = isRemoved;
            ErrorMessage = errorMessage;
        }
    }
}
