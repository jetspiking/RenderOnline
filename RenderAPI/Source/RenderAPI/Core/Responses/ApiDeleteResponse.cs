namespace RenderAPI.Core.Responses
{
    public class ApiDeleteResponse
    {
        public Boolean IsDeleted { get; set; }
        public String ErrorMessage { get; set; }

        public ApiDeleteResponse(Boolean isDeleted, String errorMessage)
        {
            this.IsDeleted = isDeleted;
            this.ErrorMessage = errorMessage;
        }
    }
}
