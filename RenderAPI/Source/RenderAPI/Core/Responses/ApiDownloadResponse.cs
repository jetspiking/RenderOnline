namespace RenderAPI.Core.Responses
{
    public class ApiDownloadResponse
    {
        public Boolean DownloadProvided { get; set; }
        public String ErrorMessage { get; set; }

        public ApiDownloadResponse(Boolean downloadProvided, String errorMessage)
        {
            DownloadProvided = downloadProvided;
            ErrorMessage = errorMessage;
        }
    }
}
