namespace RenderAPI.Core.Responses
{
    public class ApiEnqueueResponse
    {
        public Boolean IsAdded { get; set; }
        public String ErrorMessage { get; set; }

        public ApiEnqueueResponse(Boolean isAdded, String errorMessage)
        {
            this.IsAdded = isAdded;
            this.ErrorMessage = errorMessage;
        }
    }
}
