# CK.AppIdentity.Monitoring.Metrics.Csv

A CSV export consumer for [CK.AppIdentity.Monitoring.Metrics](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics).
Writes metrics measurements to a CSV file for analysis or integration with other tools.

## Overview

This package provides:
- **CsvMetricsConsumer**: A metrics consumer that writes measurements to CSV format
- **CsvMetricsConsumerFeatureDriver**: An `ApplicationIdentityFeatureDriver` that manages the consumer lifecycle

## Configuration

```json
{
  "CK-AppIdentity": {
    "FullName": "MyDomain/$MyApp/#Dev",
    "Local": {
      "Metrics": {
        "Path": "FasterLog/Metrics"
      },
      "MetricsCsv": {
        "Name": "csv-export",
        "Path": "Exports/metrics.csv",
        "RetryDelayMs": 2000,
        "BatchThresholdBytes": 4194304
      }
    }
  }
}
```

### Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | string | `csv-export` | Consumer name (max 20 chars, used for FasterLog iterator) |
| `Path` | string | `Exports/metrics.csv` | Relative path within LocalFileStore for CSV output |
| `RetryDelayMs` | int | 2000 | Delay before retrying on write failure |
| `BatchThresholdBytes` | int | 4194304 | Size threshold for batching entries |

## CSV Format

The output CSV has the following columns:

| Column | Description |
|--------|-------------|
| `Timestamp` | ISO 8601 formatted timestamp |
| `MeterName` | Name of the meter (e.g., `System.Net.Http`) |
| `InstrumentName` | Name of the instrument (e.g., `http.client.request.duration`) |
| `InstrumentType` | Type of instrument (Counter, Histogram, etc.) |
| `Value` | The measurement value |
| `Tags` | Comma-separated key=value pairs |

Example output:
```csv
Timestamp,MeterName,InstrumentName,InstrumentType,Value,Tags
2024-01-15T10:30:00.0000000Z,myapp.metrics,http.requests,Counter,42,"method=GET,status=200"
2024-01-15T10:30:00.0000000Z,myapp.metrics,http.duration,Histogram,0.125,"method=GET"
```

## Usage

1. Add the package reference
2. Configure both `Metrics` and `MetricsCsv` sections in your AppIdentity configuration
3. The consumer starts automatically when the application starts

The CSV file is created in append mode, so data persists across restarts. A header row is written
only when creating a new file or when the file is empty.

## Recovery

Like all metrics consumers, the CSV consumer:
- Resumes from its last position on restart (no duplicate entries)
- Retries on write failures without losing data
- Is automatically cleaned up if removed from configuration

## Related Packages

- [CK.Metrics](https://www.nuget.org/packages/CK.Metrics) - Core metrics collection
- [CK.Monitoring.Metrics](https://www.nuget.org/packages/CK.Monitoring.Metrics) - GrandOutput handler
- [CK.AppIdentity.Monitoring.Metrics](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics) - AppIdentity integration
