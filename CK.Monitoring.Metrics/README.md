# CK.Monitoring.Metrics

Integrates [CK.Metrics](https://www.nuget.org/packages/CK.Metrics) with [CK.Monitoring](https://www.nuget.org/packages/CK.Monitoring)
by providing a `GrandOutputHandler` that writes metrics to a high-performance [FasterLog](https://microsoft.github.io/FASTER/docs/fasterlog/).

> **Important:** `MetricsLogHandler` does not create its FasterLog. The instance must be injected at
> runtime, and until it is, the handler has nowhere to write. Owning that lifecycle - creating the log,
> injecting it, disposing it - is deliberately left to whatever hosts the application; the packages
> listed at the end of this file include one that does it.

## Overview

This package provides:
- **MetricsLogHandler**: A sealed `IGrandOutputHandler` that receives metrics from the GrandOutput pipeline and writes them to FasterLog

The handler acts as a **producer only** - it writes metrics entries to FasterLog but does not consume them.
Consumers are implemented separately, outside this package.

## Configuration

Add the handler to GrandOutput configuration:

```json
{
  "CK-Monitoring": {
    "GrandOutput": {
      "Handlers": {
        "CK.Monitoring.MetricsLogHandler, CK.Monitoring.Metrics": {
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
        "CK.Monitoring.MetricsLogHandler, CK.Monitoring.Metrics": true
      }
    }
  }
}
```

### Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CommitRate` | int | 1 | Multiplier of `GrandOutputConfiguration.TimerDuration` (default 500ms) between FasterLog commits. A value of 1 commits every 500ms, 2 every 1 second, etc. |

## How It Works

1. The handler receives log entries tagged with `DotNetMetrics.MetricsTag` from the GrandOutput pipeline
2. Four filters then drop an entry rather than write it: a `SecurityCritical` tag, an entry that did
   not come from the static logger, a null or non-ASCII `Text`, and an entry whose payload would exceed
   `(1 << 16) - sizeof(long)` - that last one is logged as a `Warn`, the others silently
3. What survives is written to FasterLog with a timestamp prefix (8 bytes DateTime binary + ASCII text)
4. FasterLog provides durability and supports multiple named consumers via persisted iterators

## FasterLog Injection

The handler does not create or own the FasterLog instance. It must be injected at runtime by calling
`MetricsLogHandler.SetFasterLog( FasterLog )`, and until it is, the handler silently ignores every
metrics entry.

Note the failure mode: no injection is not an error, it is silence. Whatever owns the FasterLog has to
own four things - creating it, injecting it, registering consumers and truncating the log, and cleaning
up orphaned consumers on shutdown.

The injection has to reach a live handler instance, which only exists inside the sink. That is what
`GrandOutputHandlersAction` is for: it comes from `CK.Monitoring`, runs on the sink's own thread, and
receives the handler list, so an implementation walks it for a `MetricsLogHandler` and calls
`SetFasterLog` on it. No example is shown here, deliberately: the implementation that ships in this
stack belongs to a package downstream of this one, and referencing this package does not give it to
you. What this package guarantees is the three members that make one writable:

| Member | Contract |
|--------|----------|
| `SetFasterLog( FasterLog )` | once-only. A second call throws `InvalidOperationException` - the message reads `Invalid state: 'FasterLog is already set.' should be true.`, the guard being `Throw.CheckState( _log == null, "FasterLog is already set." )` bound to the `[CallerArgumentExpression]` overload rather than the message-carrying one |
| `HasFasterLog` | whether one is set - readable from inside any `GrandOutputHandlersAction`, and the way to detect the silent case |
| `ClearFasterLog()` | the way back, for shutdown |

Whatever submits the action should assert it found the handler before trusting that metrics are being
written: a configuration without a `MetricsLogHandler` is the exact situation in which entries are
dropped without a word, and `HasFasterLog` is the only signal that distinguishes it.

This design allows:
- The handler to be configured via standard GrandOutput configuration
- The FasterLog lifecycle to be managed externally
- Multiple consumers to share the same FasterLog instance

## Entry Format

Each FasterLog entry contains:
- **8 bytes**: DateTime as binary (via `DateTime.ToBinary()`)
- **Remaining bytes**: ASCII-encoded metrics text

The text format follows the CK.Metrics log format for meters, instruments, and measurements. An entry
whose total size would exceed `(1 << 16) - sizeof(long)` is dropped with a warning rather than
truncated.

## Requires.

- `CK.Monitoring` for the handler contract and `GrandOutputHandlersAction`, `CK.Metrics` for the
  metrics themselves, and `Microsoft.FASTER.Core` for `FasterLog`.
