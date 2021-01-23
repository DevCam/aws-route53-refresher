
namespace AwsRoute53Refresher
{
  public class RefresherOptions
  {
    public bool Enabled { get; set; }
    public int RefreshRateInMs { get; set; }
    public string PublicIpProvider { get; set; }
    public string TargetDomain { get; set; }
  }
}