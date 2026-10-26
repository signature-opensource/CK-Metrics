# CK.Metrics

Supports [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics) with
a different approach than other metrics collectors and handlers like the ones in [Microsoft.Extensions.Diagnostics.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions).

**Key features:**
- **DI-free**: No dependency injection required. Configuration is static and thread-safe.
- **Automatic capture**: Uses a global `MeterListener` to capture all `System.Diagnostics.Metrics` instruments.
- **Strict validation**: Enforces OpenTelemetry-compatible naming conventions.
- **Log based**: Metrics events are emitted as structured log entries on `ActivityMonitor.StaticLogger`.

This library is on the collector side of the Telemetry world. It can handle Meters and Instruments defined in external assemblies
like the [.Net standard ones](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-diagnostics) but we
impose some constraints that are stricter than the OpenTelemetry framework suggests.

## Quick Start

### Creating Metrics

CK.Metrics automatically captures all `System.Diagnostics.Metrics` instruments via a global `MeterListener`.
You use the standard .NET Metrics API to create meters and instruments:

```csharp
using System.Diagnostics.Metrics;
using CK.Metrics;

// Create a Meter (namespace-like naming required, see Restrictions section)
using var meter = new Meter("MyApp.Orders", "1.0");

// Create instruments
var ordersCompleted = meter.CreateCounter<int>("orders.completed", "orders", "Number of completed orders");
var itemsInCart = meter.CreateGauge<int>("cart.items");
var orderDuration = meter.CreateHistogram<double>("order.duration", "ms");
```

### Enabling Instruments

Instruments are **disabled by default**. You can enable them in two ways:

**Option 1: Inline with `DefaultConfigure()` (recommended for producer-owned instruments)**

```csharp
var counter = meter.CreateCounter<int>( "orders.completed" )
                   .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

counter.Add( 1 ); // Measurement is recorded
```

The `DefaultConfigure()` extension method is fluent and applies the configuration immediately
if the instrument is currently disabled; an instrument already enabled keeps its configuration.
The configuration cannot be `InstrumentConfiguration.BasicDisabled` (an `ArgumentException` is thrown).
It works with all instrument types: `Counter<T>`, `UpDownCounter<T>`, `Gauge<T>`, `Histogram<T>`, and their
observable variants.

**Option 2: Global configuration (for external or multiple instruments)**

```csharp
var config = new MetricsConfiguration();
config.AutoObservableTimer = 50; // Collect observables every 50ms
config.Configurations.Add( (new InstrumentMatcher("MyApp.*"), InstrumentConfiguration.BasicEnabled) );
DotNetMetrics.ApplyConfiguration(config);
```

The rules and their priority are detailed in [Configuration](Configuration/README.md).

### Recording Measurements

Use the standard .NET APIs for recording:

```csharp
// Counters: Add values (cumulative)
ordersCompleted.Add( 1 );
ordersCompleted.Add( 5, new KeyValuePair<string, object?>("region", "us_east") );

// Gauges: Record point-in-time values
itemsInCart.Record( 42 );

// Histograms: Record distributions
orderDuration.Record( 123.45 );
```

### Observable Instruments

For instruments that pull values on demand:

```csharp
var memoryGauge = meter.CreateObservableGauge<long>( "process.memory.used",
                                                     () => GC.GetTotalMemory(false),
                                                     "bytes" )
                       .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

// Enable automatic periodic collection
var config = new MetricsConfiguration { AutoObservableTimer = 100 }; // every 100ms
DotNetMetrics.ApplyConfiguration( config );
```

## How It Works

CK.Metrics sets up a global `MeterListener` at static initialization that:

1. **Captures all Meters and Instruments** as they are created via `System.Diagnostics.Metrics`
2. **Validates naming** against strict OpenTelemetry-compatible patterns (see [Restrictions](#restrictions))
3. **Logs metrics events** through `ActivityMonitor.StaticLogger` using two tags:
   - `MetricsTag` ("Metrics"): for actual metric entries (meter/instrument creation, measurements).
   - `MetricsInternalTag` ("MetricsInternal"): for internal/diagnostic logging with a default `LogFilter.Monitor` filter (groups: Trace, lines: Warn) to reduce noise.
4. **Applies configurations** to enable/disable instruments based on pattern matching

These entries are described in [Metrics information](MetricInfo/README.md#metric-log-format). The abstract
`MetricsLogDispatcher` (in the `Dispatcher` folder) is a base class for the code that processes them: its `Add` method
receives the log lines and calls the `OnNewMeter`, `OnNewInstrument`, `OnMeasure` and `OnDisposedMeter` methods.

## Configuration

The following static methods of the `DotNetMetrics` class are thread-safe and configure the metrics for the whole system:
```csharp
public static DotNetMetricsInfo GetConfiguration();
public static Task<DotNetMetricsInfo> GetConfigurationAsync();
public static void ApplyConfiguration( MetricsConfiguration configuration, bool waitForApplication = false );
public static Task<DotNetMetricsInfo> ApplyAndGetConfigurationAsync( MetricsConfiguration configuration );
```

- [Configuration](Configuration/README.md): the `MetricsConfiguration`, its `InstrumentMatcher` rules and their priority.
- [Metrics information](MetricInfo/README.md): the `DotNetMetricsInfo` and `FullInstrumentInfo` returned by
  `GetConfiguration`, and the metrics log format.

## Restrictions

A fundamental restriction is the type of the measures that can be collected by the .Net [`Instrument<T>`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.instrument-1)
and [`ObservableInstrument<T>`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.observableinstrument-1):
a measure can be `byte`, `short`, `int`, `long`, `float`, `double` and `decimal`.

### Meter

The meter's name must be a namespace-like simple identifier that cannot be longer than the
static `int DotNetMetrics.MeterNameLengthLimit` that defaults to 255 characters.
This can be programmatically changed if needed.
It is checked by `^[a-zA-Z][_a-zA-Z0-9]*(\.[_a-zA-Z0-9]*)*$` regular expression.

Its version, when not null or empty must be a non negative integer or a standard 2, 3 or 4
parts [Version](https://learn.microsoft.com/en-us/dotnet/api/system.version).

The meter's description can be any string (of any length).

### Instrument

The instrument's name follows https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-name-syntax.
It cannot be longer than 255 characters.
It is checked by `^[a-zA-Z][-_\./a-zA-Z0-9]*$` regular expression.

The instrument's unit (when defined) must be at most 63 characters and can contain any Unicode characters.
See https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-unit

### Tags (Attributes)

By default, a maximum of 128 tags per meter or instrument is allowed.
This can be programmatically changed by the static `int DotNetMetrics.AttributeCountLimit` property.

For tag's key ([attribute names](https://opentelemetry.io/docs/specs/semconv/general/naming/) for OpenTelemetry) we
enforce the [OpenTelemetry Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/general/naming/#recommendations-for-opentelemetry-authors)
and restricts keys to not be longer than the static `int DotNetMetrics.AttributeNameLengthLimit` that defaults to 255 characters.
This can be programmatically changed if needed.
It is checked by `^[a-z][a-z0-9]*((\.|_)[a-z][a-z0-9]*)*$` regular expression.

Only `long`, `double`, `bool` and `string` (or array of them) are allowed in Tag values (see OpenTelemetry's attributes).
However, strings cannot be longer than the static `int DotNetMetrics.AttributeValueLengthLimit` that defaults to 1023 characters.
This can be programmatically changed if needed.

Notes:
- A `null` is allowed as a tag's value and in an array of strings but a warning is
  emitted when a null value is found.
- Tags' values of type `int` (and `int[]`) are automatically converted to `long` (and `long[]`): the
  `MeterInfo.Tags` and `InstrumentInfo.Tags` contain the converted values.

## Persisting the metrics

This package only emits the metrics log entries: it does not store anything. As an exception to the dependency order,
to persist these logs, see [CK.Monitoring.Metrics](../CK.Monitoring.Metrics/README.md).
