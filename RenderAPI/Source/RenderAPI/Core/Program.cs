using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using RenderAPI.Misc;
using System.Security.Cryptography.X509Certificates;

namespace RenderAPI.Core
{
    public class Program
    {
        public static void Main(String[] args)
        {
            const String configurationFileName = "RenderAPI.json";
            String configurationFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configurationFileName);

            Core.Configuration.RenderServer? configuration = JsonManager.DeserializeFromFile<Core.Configuration.RenderServer>(configurationFilePath);

            if (configuration == null)
            {
                Console.WriteLine($"{configurationFileName} not found!");
                Console.ReadKey();
                Environment.Exit(0);
            }

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthorization();

            if (configuration.Certificate != null)
            {
                Console.WriteLine("☑ SSL-certificate paths provided. Using HTTPS...");

                if (File.Exists(configuration.Certificate.FullchainPemPath) && File.Exists(configuration.Certificate.PrivPemPath))
                    Console.WriteLine("☑ Verified SSL-certificate paths as valid.");
                else
                {
                    Console.WriteLine("☒ Failed to verify SSL-certificate paths as valid. Does the application have read access? Exiting...");
                    Environment.Exit(0);
                }

                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenAnyIP(int.Parse(configuration.Port), listenOptions =>
                    {
                        listenOptions.UseHttps(X509Certificate2.CreateFromPem(File.ReadAllText(configuration.Certificate.FullchainPemPath), File.ReadAllText(configuration.Certificate.PrivPemPath)));
                    });
                });
            }
            else
            {
                Console.WriteLine("☒ SSL-certificate paths not provided. Using HTTP...");
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(int.Parse(configuration.Port));
                });
            }

            WebApplication? app = builder.Build();

            RequestHandler requestHandler = new(app, configuration);
        }
    }
}
