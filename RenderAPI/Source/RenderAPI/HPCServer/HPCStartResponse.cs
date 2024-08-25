namespace RenderAPI.HPCServer
{
    public class HPCStartResponse
    {
        public Boolean IsSuccess { get; set; }
        public String Message { get; set; }

        public HPCStartResponse(Boolean isSuccess, String message)
        {
            IsSuccess = isSuccess;
            Message = message;
        }
    }
}
