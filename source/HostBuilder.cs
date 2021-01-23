
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Amazon.Route53;

namespace AwsRoute53Refresher
{
  public static class HostBuilder
  {
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
      var host = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((hostingContext, configuration) =>
        {
          IConfigurationRoot configurationRoot = configuration.Build();
          RefresherOptions options = new();

          configurationRoot.GetSection(nameof(RefresherOptions))
                          .Bind(options);
        })
        .ConfigureLogging(logging =>
        {
          logging.ClearProviders();
          logging.AddConsole();
        })
        .ConfigureServices((hostContext, services) =>
        {
          services.Configure<RefresherOptions>(hostContext.Configuration.GetSection(nameof(RefresherOptions)));
          services.AddHostedService<Refresher>()
            .AddAWSService<IAmazonRoute53>();

        });
      return host;
    }
  }
}