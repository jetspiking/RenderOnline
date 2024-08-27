namespace RenderAPI.Core.Database
{
    public class DbArgType
    {
        public String ArgTypeId { get; set; }
        public String Type { get; set; }
        public String? Regex { get; set; }

        public DbArgType(String argTypeId, String type, string? regex)
        {
            ArgTypeId = argTypeId;
            Type = type;
            Regex = regex;
        }
    }
}
