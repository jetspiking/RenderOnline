using Newtonsoft.Json;

namespace HPCServer.Core.Configuration
{
    public class HPCServer
    {
        public HPCEngine[]? RenderingEngines { get; set; } // Rendering Configuration
        public String? Port { get; set; } // Server Port

        public HPCServer(HPCEngine[]? renderingEngines, String? port)
        {
            RenderingEngines = renderingEngines;
            Port = port;
        }
    }
}
