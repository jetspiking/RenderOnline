namespace RenderAPI.Core.Api
{
    public class ApiEngine
    {
        public Byte EngineId { get; set; }
        public String Name { get; set; }
        public String Extension { get; set; }
        public String DownloadPath { get; set; }
        public String RenderArgument { get; set; }

        public ApiEngine(Byte engineId, String name, String extension, String downloadPath, String renderArgument)
        {
            EngineId = engineId;
            Name = name;
            Extension = extension;
            DownloadPath = downloadPath;
            RenderArgument = renderArgument;
        }
    }
}
