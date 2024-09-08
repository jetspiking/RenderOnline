namespace RenderAPI.Core.Configuration
{
    public class ServerCertificate
    {
        public String? FullchainPemPath { get; set; }
        public String? PrivPemPath { get; set; }

        public ServerCertificate(String? fullchainPemPath, String? privPemPath)
        {
            FullchainPemPath = fullchainPemPath;
            PrivPemPath = privPemPath;
        }
    }
}
