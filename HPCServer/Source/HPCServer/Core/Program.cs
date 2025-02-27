using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography.X509Certificates;
using HPCServer.Misc;

namespace HPCServer.Core
{
    public class Program
    {
        public static void Main(String[] args)
        {
            const String configurationFileName = "HPCServer.json";
            String configurationFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configurationFileName);

            Core.Configuration.HPCServer? configuration = JsonManager.DeserializeFromFile<Core.Configuration.HPCServer>(configurationFilePath);

            if (configuration == null)
            {
                Console.WriteLine($"{configurationFileName} not found!");
                Console.ReadKey();
                Environment.Exit(0);
            }

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthorization();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll",
                    policyBuilder =>
                    {
                        policyBuilder.AllowAnyOrigin()
                                     .AllowAnyMethod()
                                     .AllowAnyHeader();
                    });
            });

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(int.Parse(configuration.Port));
            });

            WebApplication? app = builder.Build();
            app.UseCors("AllowAll");

            RequestHandler requestHandler = new(app, configuration);

            Console.ReadKey();
        }
    }
}
