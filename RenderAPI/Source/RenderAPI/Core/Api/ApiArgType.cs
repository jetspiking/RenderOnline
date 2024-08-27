namespace RenderAPI.Core.Api
{
    public class ApiArgType
    {
        public String ArgTypeId { get; set; }
        public String Value { get; set; }
        public String Regex { get; set; }

        public ApiArgType(String argTypeId, String value, string regex)
        {
            ArgTypeId=argTypeId;
            Value=value;
            Regex=regex;
        }
    }
}
