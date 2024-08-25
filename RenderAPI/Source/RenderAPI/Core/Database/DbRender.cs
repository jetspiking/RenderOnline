namespace RenderAPI.Core.Database
{
    public class DbRender
    {
        public UInt64 RenderId { get; set; }
        public String FileName { get; set; }
        public String FilePath { get; set; }
        public UInt64 FileSize { get; set; }
        public String Arguments { get; set; }
        public Byte EngineId { get; set; }

        public DbRender(UInt64 renderId, String fileName, String filePath, UInt64 fileSize, String arguments, Byte engineId)
        {
            RenderId = renderId;
            FileName = fileName;
            FilePath = filePath;
            FileSize = fileSize;
            Arguments = arguments;
            EngineId = engineId;
        }
    }
}
