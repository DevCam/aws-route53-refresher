
using System;
using System.Net;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

using Amazon.Route53;
using Amazon.Route53.Model;

namespace AwsRoute53Refresher
{
  public class Refresher : BackgroundService
  {
    private readonly RefresherOptions _options;
    private readonly IAmazonRoute53 _amazonRoute53;
    private readonly ILogger<Refresher> _logger;
    private IPAddress _publicIp;
    private readonly Dictionary<HostedZone, ResourceRecordSet> _zonesToUpdate = new();

    public Refresher(IOptions<RefresherOptions> options, IAmazonRoute53 amazonRoute53, ILogger<Refresher> logger) =>
      (_options, _amazonRoute53, _logger) = (options.Value, amazonRoute53, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      await InitializeRefresher();
      while (!stoppingToken.IsCancellationRequested)
      {
        await Task.Delay(_options.RefreshRateInMs, stoppingToken);
        var newIp = await TryGetPublicIp();

        if (newIp.Equals(_publicIp))
          continue;

        _logger.LogInformation($"Public ip changed detected from {_publicIp} to {newIp}!");
        _publicIp = newIp;
        await TryUpdateRoute53Records();
      }
    }

    private async Task InitializeRefresher()
    {
      _logger.LogInformation($@"
      Initializing Refresher w/ config:
       > Enabled: {_options.Enabled}
       > Public Ip Provider: {_options.PublicIpProvider}
       > Refresh Rate: {_options.RefreshRateInMs}
       > TargetDomain: {_options.TargetDomain}
      ");
      if (!_options.Enabled)
        _logger.LogWarning("Refresher is not enabled, and will NOT update DNS records!");

      _logger.LogInformation("Retrieving initial public ip...");
      _publicIp = await TryGetPublicIp();
      _logger.LogInformation($"Current public IP Address is: {_publicIp}");
      _logger.LogInformation($"Retrieving HostedZones that match target domain {_options.TargetDomain}");
      await TryGetHostedZones();
      _logger.LogInformation($"Updating route53 records...");
      await TryUpdateRoute53Records();
    }

    private async Task<IPAddress> TryGetPublicIp()
    {
      string externalIp = await GetIPAddressFromProvider();
      if(string.IsNullOrEmpty(externalIp))
      {
        _logger.LogWarning("Could not resolve correct public IP, will return old (probably stale IP) for NOP behaviour");
        return _publicIp;
      }
      var address = TryParseIPv4Address(externalIp);
      return address;
    }

    private async Task<string> GetIPAddressFromProvider()
    {
      try
      {
        var grabExternalIp = new WebClient().DownloadStringTaskAsync(_options.PublicIpProvider);

        if(await Task.WhenAny(grabExternalIp, Task.Delay(1000)) == grabExternalIp)
        {
          _logger.LogInformation($"Got correct IP {grabExternalIp.Result} from provider");
          return grabExternalIp.Result;
        } else {
          _logger.LogInformation($"External IP provider timed out.. (might have an IP change)...");
        }
        return null;
      }
      catch
      {
        _logger.LogCritical($"External IP provider refused connection!");
        return null;
      }
    }

    private IPAddress TryParseIPv4Address(string ipAddress)
    {
      var couldParseIp = IPAddress.TryParse(ipAddress, out var parsedAddress);
      if (!couldParseIp)
      {
        _logger.LogCritical($"Could not parse {ipAddress}! Consider using another publicIp provider");
        throw new Exception("IPAddress Parsing exception");
      }
      if (parsedAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
      {
        _logger.LogCritical($"Parsed IP {parsedAddress} appears to not be an IPv4 address, consider changing providers!");
        throw new Exception("IPAddress Parsing exception");
      }
      return parsedAddress;
    }

    private async Task TryGetHostedZones()
    {
      ListHostedZonesResponse zonesResponse;

      try{
        zonesResponse = await _amazonRoute53.ListHostedZonesAsync();
      } catch {
        _logger.LogCritical("Route53 could not get hosted zones (does the IAM profile have access to route53:ListHostedZones?)");
        throw;
      }

      var zonesToUpdate =
        from zone in zonesResponse.HostedZones
        where zone.Name.Contains(_options.TargetDomain)
        select zone;

      if(!zonesToUpdate.Any())
      {
        _logger.LogError($"No valid Zones matched {_options.TargetDomain}! no DNS records will be updated!");
        return;
      }

      foreach (var zone in zonesToUpdate)
      {
        var recordsResponse = await _amazonRoute53.ListResourceRecordSetsAsync(new ListResourceRecordSetsRequest(zone.Id));

        var targetRecord = recordsResponse.ResourceRecordSets.FirstOrDefault(
          (record) => record.Type == RRType.A
        );

        if (targetRecord != null)
        {
          _logger.LogInformation($"Got zone {zone.Name} & Target record w/ val {targetRecord.ResourceRecords[0].Value}");
          _zonesToUpdate.Add(zone, targetRecord);
        }
      }
    }

    private async Task TryUpdateRoute53Records()
    {
      foreach (var zone in _zonesToUpdate)
      {

        if (_publicIp.ToString().Equals(zone.Value.ResourceRecords[0].Value))
          continue;

        if(!_options.Enabled)
        {
          _logger.LogWarning($"Stale ip found @ {zone.Value.Name} but since refresher is disabled no ChangeRequest will be sent!");
          continue;
        }

        _logger.LogInformation($"{zone.Key.Name} @ {zone.Value.Name} has stale IP {zone.Value.ResourceRecords[0].Value} will update to {_publicIp}");
        zone.Value.ResourceRecords[0].Value = _publicIp.ToString();
        var recordSetRequest = new ChangeResourceRecordSetsRequest
        {
          HostedZoneId = zone.Key.Id,
          ChangeBatch = new ChangeBatch
          {
            Comment = $"Refresher auto action @ { DateTime.Now }",
            Changes = new List<Change> { new Change { ResourceRecordSet = zone.Value, Action = ChangeAction.UPSERT } }
          }
        };

        _logger.LogInformation($"Applying recordSetRequest @ {zone.Value.Name}...");
        var recordsetResponse = await _amazonRoute53.ChangeResourceRecordSetsAsync(recordSetRequest);
        var changeRequest = new GetChangeRequest { Id = recordsetResponse.ChangeInfo.Id };
        await Task.Delay(TimeSpan.FromSeconds(5));

        var status = await _amazonRoute53.GetChangeAsync(changeRequest);

        while (status.ChangeInfo.Status != ChangeStatus.INSYNC)
        {
          _logger.LogInformation($"request @ {zone.Value.Name} is pending...");
          await Task.Delay(TimeSpan.FromSeconds(10));
          status = await _amazonRoute53.GetChangeAsync(changeRequest);
        }
        _logger.LogInformation($"request @ {zone.Value.Name} has completed!");

      }
      _logger.LogInformation($"All valid hostedZones are in sync!");
    }
  }
}