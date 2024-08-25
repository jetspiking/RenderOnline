namespace RenderAPI.Core.Database
{
    public class DbArgType
    {
        public String ArgTypeId { get; set; }
        public String Type { get; set; }

        public DbArgType(String argTypeId, String type)
        {
            ArgTypeId = argTypeId;
            Type = type;
        }
    }
}
