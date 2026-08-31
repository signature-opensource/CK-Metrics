# CK.Monitoring.Metrics

Integrates [CK.Metrics](https://www.nuget.org/packages/CK.Metrics) with [CK.Monitoring](https://www.nuget.org/packages/CK.Monitoring)
by providing a `GrandOutputHandler` that writes metrics to a high-performance [FasterLog](https://microsoft.github.io/FASTER/docs/fasterlog/).

> **Important:** This package does not work on its own. The `MetricsLogHandler` requires a FasterLog instance
> to be injected at runtime. Use [CK.AppIdentity.Monitoring.Metrics](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics)
> which provides `MetricsFeatureDriver` that manages the FasterLog lifecycle and injects it into the handler automatically.

## Overview

This package provides:
- **MetricsLogHandler**: A sealed `IGrandOutputHandler` that receives metrics from the GrandOutput pipeline and writes them to FasterLog

The handler acts as a **producer only** - it writes metrics entries to FasterLog but does not consume them.
Consumers are implemented separately (see [CK.AppIdentity.Monitoring.Metrics](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics)).

## Configuration

Add the handler to GrandOutput configuration:

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

Or simply enable with defaults:

```json
{
  "CK-Monitoring": {
    "GrandOutput": {
      "Handlers": {
        "MetricsLogHandler, CK.Monitoring.Metrics": true
      }
    }
  }
}
```

### Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CommitRate` | int | 1 | Multiplier of `GrandOutput.TimerDuration` (default 500ms) between FasterLog commits. A value of 1 commits every 500ms, 2 every 1 second, etc. |

## How It Works

1. The handler receives log entries tagged with `DotNetMetrics.MetricsTag` from the GrandOutput pipeline
2. Each entry is written to FasterLog with a timestamp prefix (8 bytes DateTime binary + ASCII text)
3. FasterLog provides durability and supports multiple named consumers via persisted iterators

## FasterLog Injection

The handler does not create or own the FasterLog instance. Instead, the FasterLog must be injected at runtime
by calling `MetricsLogHandler.SetFasterLog(FasterLog)`. Without this injection, the handler silently ignores all metrics entries.

**Recommended approach:** Use [CK.AppIdentity.Monitoring.Metrics](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics)
which provides `MetricsFeatureDriver`. This feature driver:
1. Creates and manages the FasterLog instance
2. Automatically injects it into the handler via `SetMetricsFasterLogAction` (a `GrandOutputHandlersAction`)
3. Handles consumer registration and log truncation
4. Cleans up orphaned consumers on shutdown

This design allows:
- The handler to be configured via standard GrandOutput configuration
- The FasterLog lifecycle to be managed externally (by AppIdentity or custom code)
- Multiple consumers to share the same FasterLog instance

## Entry Format

Each FasterLog entry contains:
- **8 bytes**: DateTime as binary (via `DateTime.ToBinary()`)
- **Remaining bytes**: ASCII-encoded metrics text

The text format follows the CK.Metrics log format for meters, instruments, and measurements.

## Related Packages

- [CK.Metrics](https://www.nuget.org/packages/CK.Metrics) - Core metrics collection
- [CK.AppIdentity.Monitoring.Metrics](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics) - AppIdentity integration with consumer support
- [CK.AppIdentity.Monitoring.Metrics.Csv](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics.Csv) - CSV export consumer
