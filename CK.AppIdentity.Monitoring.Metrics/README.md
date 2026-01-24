# CK.AppIdentity.Monitoring.Metrics

Integrates [CK.Monitoring.Metrics](https://www.nuget.org/packages/CK.Monitoring.Metrics) with
[CK.AppIdentity](https://www.nuget.org/packages/CK.AppIdentity) for automatic FasterLog management and consumer registration.

## Overview

This package provides:
- **MetricsFeatureDriver**: An `ApplicationIdentityFeatureDriver` that owns the shared FasterLog instance
- **IMetricsConsumer / MetricsConsumerBase**: Interfaces and base class for implementing metrics consumers
- **Automatic path resolution**: FasterLog storage uses the application's `LocalFileStore`

## Architecture

```
MetricsFeatureDriver (owns FasterLog)
    │
    ├── Creates FasterLog at LocalFileStore path
    ├── Injects FasterLog into MetricsLogHandler via action
    ├── Manages consumer registration
    ├── Handles truncation and orphan cleanup
    │
    └── Consumers (registered via RegisterConsumer)
        ├── CsvMetricsConsumer
        ├── (other consumers...)
        └── Each uses named FasterLog iterators
```

## Configuration

```json
{
  "CK-AppIdentity": {
    "FullName": "MyDomain/$MyApp/#Dev",
    "Local": {
      "Metrics": {
        "Path": "FasterLog/Metrics",
        "MemoryPageCount": 2,
        "TruncationIntervalMs": 60000,
        "HandlerWaitTimeoutMs": 30000
      }
    }
  }
}
```

### Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Path` | string | `FasterLog/Metrics` | Relative path within LocalFileStore for FasterLog storage |
| `MemoryPageCount` | int | 2 | Number of memory pages for FasterLog |
| `TruncationIntervalMs` | int | 60000 | Interval between log truncation checks (0 to disable) |
| `HandlerWaitTimeoutMs` | int | 30000 | Timeout waiting for MetricsLogHandler to be configured |

## Implementing a Consumer

Create a consumer by extending `MetricsConsumerBase`:

```csharp
public class MyMetricsConsumer : MetricsConsumerBase
{
    public MyMetricsConsumer(FasterLog log, string name)
        : base(log, name, retryDelayMs: 2000, batchThresholdBytes: 2 << 21)
    {
    }

    protected override Task<TimeSpan> ProcessEntriesAsync(
        IEnumerable<ReadOnlyMemory<byte>> entries)
    {
        foreach (var entry in entries)
        {
            // Entry format: DateTime (8 bytes) + ASCII text
            var buffer = entry.ToArray();
            var dateTime = DateTime.FromBinary(BitConverter.ToInt64(buffer, 0));
            var text = Encoding.ASCII.GetString(buffer, 8, buffer.Length - 8);

            // Process the metric...
        }

        return Task.FromResult(TimeSpan.Zero); // No throttling
    }
}
```

Create a feature driver to register the consumer:

```csharp
public class MyConsumerFeatureDriver : ApplicationIdentityFeatureDriver
{
    readonly MetricsFeatureDriver _metricsDriver;
    MyMetricsConsumer? _consumer;

    public MyConsumerFeatureDriver(
        ApplicationIdentityService s,
        MetricsFeatureDriver metricsDriver)
        : base(s, isAllowedByDefault: true)
    {
        _metricsDriver = metricsDriver; // DI ensures correct order
    }

    protected override async Task<bool> SetupAsync(FeatureLifetimeContext context)
    {
        if (_metricsDriver.FasterLog == null) return true;

        _consumer = new MyMetricsConsumer(_metricsDriver.FasterLog, "my-consumer");
        _metricsDriver.RegisterConsumer(_consumer);
        await _consumer.StartAsync(context.Monitor, CancellationToken.None);
        return true;
    }

    protected override async Task TeardownAsync(FeatureLifetimeContext context)
    {
        if (_consumer != null)
        {
            await _metricsDriver.RemoveConsumerAsync(_consumer.Name);
        }
    }
}
```

## Consumer Features

- **Named iterators**: Each consumer has a unique name (max 20 chars) used for FasterLog's persisted iterator
- **Recovery**: On restart, consumers resume from their last committed position
- **Retry-on-failure**: Exceptions in `ProcessEntriesAsync` trigger automatic retry after delay
- **Batching**: Entries are batched up to a configurable size threshold
- **Auto-truncation**: The driver periodically truncates the log up to the slowest consumer's position

## Orphan Cleanup

When a consumer is removed from configuration:
1. On next startup, its iterator address is recovered from FasterLog
2. During shutdown, orphaned iterators (recovered but never registered) are cleaned up
3. This prevents unbounded log growth from abandoned consumers

## Related Packages

- [CK.Metrics](https://www.nuget.org/packages/CK.Metrics) - Core metrics collection
- [CK.Monitoring.Metrics](https://www.nuget.org/packages/CK.Monitoring.Metrics) - GrandOutput handler
- [CK.AppIdentity.Monitoring.Metrics.Csv](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics.Csv) - CSV export consumer
