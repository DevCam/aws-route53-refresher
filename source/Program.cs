using Microsoft.Extensions.Hosting;

namespace AwsRoute53Refresher
{
  class Program
  {
    static void Main(string[] args)
    {
      HostBuilder.CreateHostBuilder(args).Build().Run();
    }
  }
}
