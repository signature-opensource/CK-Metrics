# CK-Metrics

Structured collection of .NET diagnostics metrics via [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics).

[![Licence](https://img.shields.io/github/license/signature-opensource/CK-Metrics.svg)](LICENSE)

| Package | Description | Latest stable |
|---------|-------------|---------------|
| [CK.Metrics](CK.Metrics/README.md) | Core package with static API for metrics collection and configuration | [![nuget](https://img.shields.io/nuget/v/CK.Metrics.svg?label=CK.Metrics)](https://www.nuget.org/packages/CK.Metrics/) |
| [CK.Monitoring.Metrics](CK.Monitoring.Metrics/README.md) | GrandOutput handler that writes metrics to FasterLog | [![nuget](https://img.shields.io/nuget/v/CK.Monitoring.Metrics.svg?label=CK.Monitoring.Metrics)](https://www.nuget.org/packages/CK.Monitoring.Metrics/) |
| [CK.AppIdentity.Monitoring.Metrics](CK.AppIdentity.Monitoring.Metrics/README.md) | AppIdentity integration with FasterLog lifecycle and consumer base class | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Monitoring.Metrics.svg?label=CK.AppIdentity.Monitoring.Metrics)](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics/) |
| [CK.AppIdentity.Monitoring.Metrics.Csv](CK.AppIdentity.Monitoring.Metrics.Csv/README.md) | CSV export consumer for metrics analysis | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Monitoring.Metrics.Csv.svg?label=CK.AppIdentity.Monitoring.Metrics.Csv)](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics.Csv/) |
| [CK.AppIdentity.Monitoring.Metrics.InfluxDb](CK.AppIdentity.Monitoring.Metrics.InfluxDb/README.md) | InfluxDB v2.x consumer using Line Protocol over HTTP(S) | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.Monitoring.Metrics.InfluxDb.svg?label=CK.AppIdentity.Monitoring.Metrics.InfluxDb)](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics.InfluxDb/) |

How the packages fit together: [docs/Architecture.md](docs/Architecture.md).
