namespace HPCServer.Core.Responses
{
    public class HPCRenderRequestResponse
    {
        public Boolean IsError { get; set; }
        public String Message { get; set; }

        public HPCRenderRequestResponse(Boolean isError, String message)
        {
            IsError = isError;
            Message = message;
        }
    }
}
