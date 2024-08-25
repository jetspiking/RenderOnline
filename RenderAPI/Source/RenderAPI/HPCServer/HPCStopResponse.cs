namespace RenderAPI.HPCServer
{
    public class HPCStopResponse
    {
        public String IsSuccess { get; set; }
        public String Message { get; set; }

        public HPCStopResponse(String isSuccess, String message)
        {
            IsSuccess = isSuccess;
            Message = message;
        }
    }
}
