namespace RenderAPI.Core.Responses
{
    public class ApiDequeueResponse
    {
        public Boolean IsRemoved { get; set; }
        public String ErrorMessage { get; set; }

        public ApiDequeueResponse(Boolean isRemoved, String errorMessage)
        {
            this.IsRemoved = isRemoved;
            this.ErrorMessage = errorMessage;
        }
    }
}
