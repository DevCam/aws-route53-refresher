# aws-route-refresher

For those of us who are too cheap to purchase a DDNS provider.

`aws-route-refresher` is a ~~glorified bash script~~ .NET worker service that acquires the public facing IPv4 Address of the host and if necessary updates the DNS A records of a Route53 HostedZone.

## Usage

 1. As a standalone .NET app (consider running as a background service)
	 - `dotnet publish`
	 -  `dotnet /path/to/AwsRoute53Refresher.dll`
 2. docker-compose
	 - `docker-compose up`
 3. As a standalone `Docker` container
   - `docker build -t image-name .`
   - `docker run -d image-name -v $HOME/.aws/credentials:/root/.aws/credentials:ro`
 4. from dockerhub
   - ` docker run -d \
      -e 'RefresherOptions__TargetDomain=yourdomain.com' \
      -v $HOME/.aws/credentials:/root/.aws/credentials:ro devcam/aws-route53-refresher `

Note: if preferred, global AWS credentials can be configured as ENV variables ( [although not recommended](https://diogomonica.com/2017/03/27/why-you-shouldnt-use-env-variables-for-secret-data/) ) or passed as CLI args, if so it is not required to mount the `.aws` folder. 

## Configuration

### AMI
Correct [IAM](https://aws.amazon.com/iam/) Credentials are required, the minimum actions within the policy are the following:
```json
"Action": [
   "route53:GetChange",
   "route53:ListHostedZones",
   "route53:ChangeResourceRecordSets",
   "route53:ListResourceRecordSets"
],
```
A valid user with programmatic access can be configured as [prefered](https://docs.aws.amazon.com/sdk-for-net/v2/developer-guide/net-dg-config-creds.html#creds-file). 

To customize the behavior
| Flag | Value | Description
|--|--|--
| AWS_PROFILE | `string` | The IAM profile that will be used (from `.aws/credentials` file)
| AWS_REGION | `string` | AWS region (required if no `default` defined in profile)
| RefresherOptions__Enabled | `bool` | Flag to enable/disable DNS refreshing.
| RefresherOptions__RefreshRateInMs | `int` | Rate (in milliseconds) for checking if the public IP has changed.
| RefresherOptions__PublicIpProvider | `URL` | Public IP provider. Must respond in plain text with the detected public IP (*i.e* http://icanhazip.com/ or https://api.ipify.org)
| RefresherOptions__TargetDomain | `string` | domain(s) to update. The refresher will check all available hosted zones to match the `TargetDomain` and will attempt to update the `A` record of **all matching domains**. A match is defined if `TargetDomain` is a **substring** of the Hosted zone name. I.e if TargetDomain is `google` the following are ALL MATCHES: mail.google.com google.ca google.microsoft.com

Logging can also be [configured as desired](https://docs.microsoft.com/en-us/dotnet/core/extensions/logging) to reduce verbosity.

Values can be set using standard `appsettings.json`, `ENV` variables or as command-line args when executing `dotnet ./AwsRoute53Refresher.dll` , if multiple setting providers exist, [default precedence is used](https://docs.microsoft.com/en-us/dotnet/core/extensions/generic-host):

-   _appsettings.json_.
-   _appsettings.{Environment}.json_.
-   Environment variables.
-   Command-line arguments. 


[![License: LGPL v3](https://img.shields.io/badge/License-LGPL%20v3-blue.svg)](https://www.gnu.org/licenses/lgpl-3.0)
