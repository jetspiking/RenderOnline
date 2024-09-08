using Newtonsoft.Json;

namespace RenderAPI.Core.Configuration
{
    public class RenderServer
    {
        public String? ConnectionString { get; set; }
        public String? Port { get; set; }
        public ServerCertificate? Certificate { get; set; }

        public RenderServer(String? connectionString, String? port, ServerCertificate? certificate)
        {
            ConnectionString = connectionString;
            Port = port;
            Certificate = certificate;
        }
    }
}
