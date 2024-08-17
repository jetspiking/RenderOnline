namespace HPCServer.Core.Responses
{
    public class HPCRenderRequestResponse
    {
        public Boolean IsSuccess { get; set; }
        public String Message { get; set; }

        public HPCRenderRequestResponse(Boolean isSuccess, String message)
        {
            IsSuccess = isSuccess;
            Message = message;
        }
    }
}
