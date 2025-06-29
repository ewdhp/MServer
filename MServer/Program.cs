using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace MServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.ListenAnyIP(5000); // HTTP always enabled

                        // Enable HTTPS only if the certificate exists
                        var certPath = "aspnetapp.pfx"; // relative to /home/ewd/MServer/MServer
                        var certPassword = "admin";
                        var fullCertPath = Path.Combine(Directory.GetCurrentDirectory(), certPath);
                        if (File.Exists(fullCertPath))
                        {
                            options.ListenAnyIP(5001, listenOptions =>
                            {
                                listenOptions.UseHttps(fullCertPath, certPassword);
                            });
                        }
                    });
                    webBuilder.UseStartup<Startup>();
                });
    }
}