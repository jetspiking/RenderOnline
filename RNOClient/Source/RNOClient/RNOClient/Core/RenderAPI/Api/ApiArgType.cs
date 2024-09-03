using System;

namespace RNOClient.Core.RenderAPI.Api
{
    public class ApiArgType
    {
        public String ArgTypeId { get; set; }
        public String Value { get; set; }
        public String Regex { get; set; }

        public ApiArgType(String argTypeId, String value, String regex)
        {
            ArgTypeId = argTypeId;
            Value = value;
            Regex = regex;
        }
    }
}
