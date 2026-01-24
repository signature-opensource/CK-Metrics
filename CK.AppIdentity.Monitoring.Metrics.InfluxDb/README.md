# CK.AppIdentity.Monitoring.Metrics.InfluxDb

Metrics consumer that posts measurements to InfluxDB v2.x using the line protocol format over HTTP(S).

## Configuration

Add the InfluxDB consumer configuration to your AppIdentity local configuration:

```json
{
  "CK-AppIdentity": {
    "FullName": "MyDomain/$MyApp/#Dev",
    "Local": {
      "Metrics": {
        "Path": "FasterLog/Metrics"
      },
      "MetricsInfluxDb": {
        "Name": "influxdb",
        "ServerUrl": "https://influxdb.example.com:8086",
        "Org": "my-org",
        "Bucket": "metrics",
        "Token": "my-api-token",
        "UseGzip": true,
        "FlushIntervalMs": 1000,
        "RetryDelayMs": 2000,
        "BatchThresholdBytes": 4194304
      }
    }
  }
}
```

## Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `Name` | `influxdb-export` | Consumer name (max 20 chars, must be unique) |
| `ServerUrl` | *required* | InfluxDB server URL (e.g., `https://localhost:8086`) |
| `Org` | *required* | InfluxDB organization name |
| `Bucket` | *required* | InfluxDB bucket name |
| `Token` | - | API token for authentication |
| `Username` | - | Username for basic auth (alternative to Token) |
| `Password` | - | Password for basic auth |
| `UseGzip` | `true` | Compress HTTP requests with gzip |
| `FlushIntervalMs` | `1000` | Maximum delay between flushes |
| `RetryDelayMs` | `2000` | Delay before retrying after failure |
| `BatchThresholdBytes` | `4194304` | Size threshold for batching (4 MiB) |
| `Tags` | - | Optional static tags added to every measurement. Values support environment variables (e.g., `%COMPUTERNAME%`). |

## Line Protocol Format

Metrics are written using the InfluxDB line protocol:

```
<instrument_name>,ck_domain=<domain>,ck_environment=<env>,ck_party=<party>,meter=<meter_name>,<tags> value=<value> <timestamp_ns>
```

Example:
```
http.requests,ck_domain=MyCompany,ck_environment=Production,ck_party=MyApp,meter=System.Net.Http,method=GET value=42 1705329000000000000
```

## Tags

The consumer automatically includes these tags:

- `ck_domain`: AppIdentity domain name
- `ck_environment`: AppIdentity environment name
- `ck_party`: AppIdentity party name
- `meter`: The meter name that owns the instrument

Plus all measurement-specific tags from the metrics entry.

### Static Tags

You can configure static tags that are included in every measurement. This is useful when multiple
machines share the same domain/party/environment and you need additional discriminators for querying.

Tag values support environment variable expansion using the `%VARIABLE%` syntax:

```json
"MetricsInfluxDb": {
  "ServerUrl": "https://influxdb.example.com:8086",
  "Org": "my-org",
  "Bucket": "metrics",
  "Token": "my-api-token",
  "Tags": {
    "host": "%COMPUTERNAME%",
    "user": "%USERNAME%",
    "region": "eu-west-1"
  }
}
```

These tags appear after the automatic `ck_*` tags in the line protocol:
```
http.requests,ck_domain=MyCompany,ck_environment=Production,ck_party=MyApp,meter=System.Net.Http,host=SERVER01,user=john,region=eu-west-1,method=GET value=42 1705329000000000000
```

## GrandOutput Handler

Remember to configure the metrics handler in your GrandOutput configuration:

```json
{
  "CK-Monitoring": {
    "GrandOutput": {
      "Handlers": {
        "MetricsLogHandler, CK.Monitoring.Metrics": {
          "CommitRate": 1
        }
      }
    }
  }
}
```
