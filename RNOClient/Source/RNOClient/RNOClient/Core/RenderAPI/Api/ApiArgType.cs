using System;

namespace RNOClient.Core.RenderAPI.Api
{
    public class ApiArgType
    {
        public String ArgTypeId { get; set; }
        public String Value { get; set; }

        public ApiArgType(String argTypeId, String value)
        {
            ArgTypeId = argTypeId;
            Value = value;
        }
    }
}
