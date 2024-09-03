using System;

namespace RNOClient.Core.RenderAPI.Responses
{
    public class ApiEnqueueResponse
    {
        public Boolean IsAdded { get; set; }
        public String ErrorMessage { get; set; }

        public ApiEnqueueResponse(Boolean isAdded, String errorMessage)
        {
            IsAdded = isAdded;
            ErrorMessage = errorMessage;
        }
    }
}
