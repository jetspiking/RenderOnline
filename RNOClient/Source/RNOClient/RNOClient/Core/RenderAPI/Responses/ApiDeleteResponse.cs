using System;

namespace RNOClient.Core.RenderAPI.Responses
{
    public class ApiDeleteResponse
    {
        public Boolean IsDeleted { get; set; }
        public String ErrorMessage { get; set; }

        public ApiDeleteResponse(Boolean isDeleted, String errorMessage)
        {
            IsDeleted = isDeleted;
            ErrorMessage = errorMessage;
        }
    }
}
