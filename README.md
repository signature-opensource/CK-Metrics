# CK-Metrics

Structured collection of .NET diagnostics metrics via [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics).

[![CK.Metrics](https://img.shields.io/nuget/v/CK.Metrics?label=CK.Metrics)](https://www.nuget.org/packages/CK.Metrics)
[![CK.Monitoring.Metrics](https://img.shields.io/nuget/v/CK.Monitoring.Metrics?label=CK.Monitoring.Metrics)](https://www.nuget.org/packages/CK.Monitoring.Metrics)
[![CK.AppIdentity.Monitoring.Metrics](https://img.shields.io/nuget/v/CK.AppIdentity.Monitoring.Metrics?label=CK.AppIdentity.Monitoring.Metrics)](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics)
[![CK.AppIdentity.Monitoring.Metrics.Csv](https://img.shields.io/nuget/v/CK.AppIdentity.Monitoring.Metrics.Csv?label=CK.AppIdentity.Monitoring.Metrics.Csv)](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics.Csv)
[![CK.AppIdentity.Monitoring.Metrics.InfluxDb](https://img.shields.io/nuget/v/CK.AppIdentity.Monitoring.Metrics.InfluxDb?label=CK.AppIdentity.Monitoring.Metrics.InfluxDb)](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics.InfluxDb)
[![MIT License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Overview

CK-Metrics provides a different approach to metrics collection than [Microsoft.Extensions.Diagnostics.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions):

- **DI-free**: No dependency injection required. Configuration is static and thread-safe.
- **Global approach**: Like [DotNetEventSource](https://github.com/signature-opensource/CK-ActivityMonitor) support in CK-ActivityMonitor, metrics are application-wide global objects.
- **Automatic capture**: Uses a global `MeterListener` to capture all `System.Diagnostics.Metrics` instruments, including .NET built-in metrics.
- **Strict validation**: Enforces OpenTelemetry-compatible naming conventions for consistency and reliability.

**Prerequisites**: Familiarity with [CK-ActivityMonitor](https://github.com/signature-opensource/CK-ActivityMonitor) is recommended as metrics are emitted via `ActivityMonitor.StaticLogger`.

## Packages

| Package | Description |
|---------|-------------|
| [CK.Metrics](./CK.Metrics) | Core package with static API for metrics collection and configuration |
| [CK.Monitoring.Metrics](./CK.Monitoring.Metrics) | GrandOutput handler that writes metrics to FasterLog |
| [CK.AppIdentity.Monitoring.Metrics](./CK.AppIdentity.Monitoring.Metrics) | AppIdentity integration with FasterLog lifecycle and consumer base class |
| [CK.AppIdentity.Monitoring.Metrics.Csv](./CK.AppIdentity.Monitoring.Metrics.Csv) | CSV export consumer for metrics analysis |
| [CK.AppIdentity.Monitoring.Metrics.InfluxDb](./CK.AppIdentity.Monitoring.Metrics.InfluxDb) | InfluxDB v2.x consumer using Line Protocol over HTTP(S) |

Click on a package name to see its detailed documentation.

## Quick Start

### Installation

```bash
# Core package only (for producers)
dotnet add package CK.Metrics

# Full stack with InfluxDB export
dotnet add package CK.AppIdentity.Monitoring.Metrics.InfluxDb
```

### Producing Metrics

```csharp
using System.Diagnostics.Metrics;
using CK.Metrics;

// Create a Meter (namespace-like naming required)
using var meter = new Meter("MyApp.Orders", "1.0");

// Create and enable an instrument
var ordersCounter = meter.CreateCounter<int>("orders.completed", "orders")
    .DefaultConfigure(InstrumentConfiguration.BasicEnabled);

// Record measurements
ordersCounter.Add(1);
ordersCounter.Add(1, new KeyValuePair<string, object?>("region", "eu_west"));
```

### Configuration

```csharp
// Enable all instruments and collect observables every 50ms
var config = new MetricsConfiguration();
config.AutoObservableTimer = 50;
config.Configurations.Add((new InstrumentMatcher("*"), InstrumentConfiguration.BasicEnabled));
DotNetMetrics.ApplyConfiguration(config);
```

See [CK.Metrics](./CK.Metrics) for detailed producer documentation.

## Architecture

```
Producer (System.Diagnostics.Metrics)
    │
    ▼
DotNetMetrics (global MeterListener)              ─── CK.Metrics
    │
    ▼
ActivityMonitor.StaticLogger
    │
    ▼
GrandOutput → MetricsLogHandler                   ─── CK.Monitoring.Metrics
    │
    ▼
FasterLog (durable storage)                       ─── CK.AppIdentity.Monitoring.Metrics
    │
    ▼
IMetricsConsumer implementations
    ├── CsvMetricsConsumer                        ─── CK.AppIdentity.Monitoring.Metrics.Csv
    └── InfluxDbMetricsConsumer                   ─── CK.AppIdentity.Monitoring.Metrics.InfluxDb
```

## Building

```bash
# Build
dotnet build

# Run tests
dotnet test
```

## License

This project is licensed under the MIT License.
