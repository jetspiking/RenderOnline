using Newtonsoft.Json;

namespace RenderAPI.Core.Configuration
{
    public class RenderServer
    {
        public String? ConnectionString { get; set; }
        public String? Port { get; set; }

        public RenderServer(String? connectionString, String? port)
        {
            ConnectionString = connectionString;
            Port = port;
        }
    }
}
