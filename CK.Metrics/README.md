# CK-Metrics

Supports [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics) with
a different approach than other metrics collectors and handlers like the ones in [Microsoft.Extensions.Diagnostics.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions).

**Key features:**
- **DI-free**: No dependency injection required. Configuration is static and thread-safe.
- **Automatic capture**: Uses a global `MeterListener` to capture all `System.Diagnostics.Metrics` instruments.
- **Strict validation**: Enforces OpenTelemetry-compatible naming conventions.
- **CK.Monitoring integration**: Metrics flow through the GrandOutput pipeline for persistence and consumption.

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
var counter = meter.CreateCounter<int>("orders.completed")
    .DefaultConfigure(InstrumentConfiguration.BasicEnabled);

counter.Add(1); // Measurement is recorded
```

The `DefaultConfigure()` extension method is fluent and applies the configuration immediately
if no existing matching configuration exists. It works with all instrument types:
`Counter<T>`, `UpDownCounter<T>`, `Gauge<T>`, `Histogram<T>`, and their observable variants.

**Option 2: Global configuration (for external or multiple instruments)**

```csharp
var config = new MetricsConfiguration();
config.AutoObservableTimer = 50; // Collect observables every 50ms
config.Configurations.Add((new InstrumentMatcher("MyApp.*"), InstrumentConfiguration.BasicEnabled));
DotNetMetrics.ApplyConfiguration(config);
```

### Recording Measurements

Use the standard .NET APIs for recording:

```csharp
// Counters: Add values (cumulative)
counter.Add(1);
counter.Add(5, new KeyValuePair<string, object?>("region", "us_east"));

// Gauges: Record point-in-time values
gauge.Record(42);

// Histograms: Record distributions
histogram.Record(123.45);
```

### Observable Instruments

For instruments that pull values on demand:

```csharp
var memoryGauge = meter.CreateObservableGauge<long>(
    "process.memory.used",
    () => GC.GetTotalMemory(false),
    "bytes"
).DefaultConfigure(InstrumentConfiguration.BasicEnabled);

// Enable automatic periodic collection
var config = new MetricsConfiguration { AutoObservableTimer = 100 }; // every 100ms
DotNetMetrics.ApplyConfiguration(config);
```

## How It Works

CK.Metrics sets up a global `MeterListener` at static initialization that:

1. **Captures all Meters and Instruments** as they are created via `System.Diagnostics.Metrics`
2. **Validates naming** against strict OpenTelemetry-compatible patterns (see Restrictions)
3. **Logs metrics events** through `ActivityMonitor.StaticLogger` using two tags:
   - `MetricsTag` ("Metrics"): for actual metric entries (meter/instrument creation, measurements)
   - `MetricsInternalTag` ("MetricsInternal"): for internal/diagnostic logging with a default `LogFilter.Monitor` filter (groups: Trace, lines: Warn) to reduce noise
4. **Applies configurations** to enable/disable instruments based on pattern matching

### Metric Log Format

Metrics are logged as structured text entries with these prefixes:

| Prefix | Meaning |
|--------|---------|
| `+Meter:` | New meter created |
| `-Meter:` | Meter disposed |
| `+Instrument:` | Instrument published |
| `+IConfig:` | Configuration changed |
| `M:` | Measurement recorded |

These entries flow through the CK.Monitoring pipeline and can be processed by handlers like `MetricsLogHandler`.

## Configuration

The following static methods of the `DotNetMetrics` class are thread-safe and configure the metrics for the whole system:
```csharp
public static DotNetMetricsInfo GetConfiguration();
public static Task<DotNetMetricsInfo> GetConfigurationAsync();
public static void ApplyConfiguration( MetricsConfiguration configuration, bool waitForApplication = false );
public static Task<DotNetMetricsInfo> ApplyAndGetConfigurationAsync( MetricsConfiguration configuration );
```

## Getting the current System state and configurations

The `DotNetMetricsInfo` returned by `GetConfiguration` or `GetConfigurationAsync` exposes the Meters and Instruments and the
configuration of the timer that collects the measures of the `ObservableInstrument`:

```csharp
public sealed class DotNetMetricsInfo
{
    /// <summary>
    /// Gets the timer delay that collects the <see cref="ObservableInstrument{T}"/>
    /// measures.
    /// </summary>
    public int AutoObservableTimer => _autoObservableTimer;

    /// <summary>
    /// Gets the instruments and their configuration grouped by their <see cref="FullInstrumentInfo.MeterInfo"/>.
    /// </summary>
    public IReadOnlyList<FullInstrumentInfo> Instruments => _instruments;
}
```
The `FullInstrumentInfo` exposes:
- The `MeterInfo` that is an immutable capture of the .Net `Meter` object.
- The `InstrumentInfo` that is an immutable capture of the .Net `Instrument` object.
- The `InstrumentConfiguration` currently applies to the instrument (also immutable).
- It also exposes a `string FullName` that is the "Meter's name/Instrument's name".

Currently `InstrumentConfiguration` only contains a `bool Enabled` property but it is planned to extend it.

## Applying configurations

Meters and Instruments are created by code. The configuration can only enable or disable them (at least for now).
The `MetricsConfiguration` provided to the the static `ApplyConfiguration` contains "rules" to select instruments
and the associated configuration to be applied as well as a potential update of the `AutoObservableTimer` timer delay.
```csharp
public class MetricsConfiguration
{
    /// <summary>
    /// Gets or sets the timer delay that collects the <see cref="ObservableInstrument{T}"/>
    /// measures. Defaults to null (leaves the current value unchaged).
    /// <para>
    /// When not null, this is normalized to 0 (the default, auto collection is disabled by default)
    /// or to a value between 50 ms and 3_600_000 ms (one hour).
    /// </para>
    /// </summary>
    public int? AutoObservableTimer { get; set; }

    /// <summary>
    /// Gets the ordered list of configurations to apply from most precise one to more general one.
    /// </summary>
    public IList<(InstrumentMatcher,InstrumentConfiguration)> Configurations => _configurations;
}
```
The "rules" are the `InstrumentMatcher` that are very simple immutable objects that have a wildcard
`NamePattern` applied to the `FullInstrumentInfo.FullName`. This uses a very basic projection to
a regular expression:
- `?` Maps to any single character (`'.'` in RegEx syntax).
- `*` Maps to the lazy `'.*?'` pattern.
- `**` Maps to the greedy `'.*'` pattern.

Because `"*"` or `"**"` matches every instrument. To enable all the instruments and collect the `ObservableInstrument`
each 50 ms:
```csharp
var c = new MetricsConfiguration();
c.AutoObservableTimer = 50;
c.Configurations.Add( (new InstrumentMatcher( "*" ), new InstrumentConfiguration( true )) );
DotNetMetrics.ApplyConfiguration( c );
```

### Configuration Priority

Rules are matched in order. Put specific rules before general ones:

```csharp
var config = new MetricsConfiguration();
// Disable .NET internal metrics
config.Configurations.Add((new InstrumentMatcher("System.**"), InstrumentConfiguration.BasicDisabled));
// Disable debug metrics
config.Configurations.Add((new InstrumentMatcher("MyApp.Debug.*"), InstrumentConfiguration.BasicDisabled));
// Enable all other app metrics
config.Configurations.Add((new InstrumentMatcher("MyApp.*"), InstrumentConfiguration.BasicEnabled));
DotNetMetrics.ApplyConfiguration(config);
```

For more complex configurations, the `IncludeTags` and `ExcludeTags` can be used.

```csharp
public sealed class InstrumentMatcher
{
    public string NamePattern { get; }

    /// <summary>
    /// Gets the required tags that must appear in <see cref="MeterInfo.Tags"/> or <see cref="InstrumentInfo.Tags"/>.
    /// Since a <c>null</c> is not welcome (but accepted) in tag values, a <c>null</c> value matches any tag value.
    /// </summary>
    public ImmutableArray<KeyValuePair<string, object?>> IncludeTags { get; }

    /// <summary>
    /// Gets the tags that must not appear in <see cref="MeterInfo.Tags"/> or <see cref="InstrumentInfo.Tags"/>.
    /// See <see cref="IncludeTags"/>.
    /// </summary>
    public ImmutableArray<KeyValuePair<string, object?>> ExcludeTags { get; }
```

## Restrictions

A fundamental restriction is the type of the measures that can be collected by the .Net [`Instrument<T>`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.instrument-1)
and [`ObservableInstrument<T>`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.observableinstrument-1):
a measure can be `byte`, `short`, `int`, `long`, `float`, `double` and `decimal`.

### Meter

The meter's name must be a namespace-like simple identifier that cannot be longer than the
static `int DotNetMetrics.MeterNameLengthLimit` that defaults to 255 charaters.
This can be programmatically changed if needed.
It is checked by `^[a-zA-Z][_a-zA-Z0-9]*(\.[_a-zA-Z0-9]*)*$` regular expression.

Its version, when not null or empty must be a non negative integer or a standard 2, 3 or 4
parts [Version](https://learn.microsoft.com/en-us/dotnet/api/system.version).

The meter's description can be any string (of any length).

### Instrument

The instrument's name follows https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-name-syntax.
It cannot be longer than 255 charaters.
It is checked by `^[a-zA-Z][-_\./a-zA-Z0-9]*$` regular expression.

The instrument's unit (when defined) must be at most 63 characters and can contain any Unicode characters.
See https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-unit

### Tags (Attributes)

By defaut, a maximum of 128 tags per meter or instrument is allowed.
This can be programmatically changed by the static `int DotNetMetrics.AttributeCountLimit` property.

For tag's key ([attribute names](https://opentelemetry.io/docs/specs/semconv/general/naming/) for OpenTelemetry) we
enforce the [OpenTelemetry Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/general/naming/#recommendations-for-opentelemetry-authors)
and restricts keys to not be longer than the static `int DotNetMetrics.AttributeNameLengthLimit` that defaults to 255 charaters.
This can be programmatically changed if needed.
It is checked by `^[a-z][a-z0-9]*((\.|_)[a-z][a-z0-9]*)*$` regular expression.

Only `long`, `double`, `bool` and `string` (or array of them) are allowed in Tag values (see [OpenTelemetry's attributes]()).
However, strings cannot be longer than the static `int DotNetMetrics.AttributeValueLengthLimit` that defaults to 1023 charaters.
This can be programmatically changed if needed.

Notes:
- A `null` is allowed as a tag's value and in an array of strings but a warning is
  emitted when a null value is found.
- Tags' value of type `int` are automatically converted to `long` (in the [MeterInfo](MetricInfo/MeterInfo.cs)).

## Monitoring Integration

CK.Metrics logs metrics through `ActivityMonitor.StaticLogger`. To persist and process these metrics,
use the companion packages:

- **CK.Monitoring.Metrics**: Provides `MetricsLogHandler` for the GrandOutput pipeline that writes metrics to FasterLog.
- **CK.AppIdentity.Monitoring.Metrics**: Full integration with FasterLog lifecycle management and `IMetricsConsumer` interface.

### Full Stack Example

```csharp
// 1. Configure GrandOutput with metrics handler
await using var go = GrandOutput.EnsureActiveDefault(new GrandOutputConfiguration
{
    Handlers = { new MetricsLogHandlerConfiguration() }
});

// 2. Enable metrics collection
DotNetMetrics.ApplyConfiguration(new MetricsConfiguration
{
    AutoObservableTimer = 50,
    Configurations = { (new InstrumentMatcher("*"), InstrumentConfiguration.BasicEnabled) }
});

// 3. Create and use metrics
using var meter = new Meter("MyApp.Service", "1.0");
var counter = meter.CreateCounter<int>("requests.total")
    .DefaultConfigure(InstrumentConfiguration.BasicEnabled);
counter.Add(1);
// Metrics flow through GrandOutput -> MetricsLogHandler -> FasterLog
```

## Related Packages

| Package | Description |
|---------|-------------|
| [CK.Monitoring.Metrics](https://www.nuget.org/packages/CK.Monitoring.Metrics) | GrandOutput handler that writes metrics to FasterLog |
| [CK.AppIdentity.Monitoring.Metrics](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics) | AppIdentity integration with FasterLog management and `MetricsConsumerBase` |
| [CK.AppIdentity.Monitoring.Metrics.Csv](https://www.nuget.org/packages/CK.AppIdentity.Monitoring.Metrics.Csv) | CSV export consumer for metrics analysis |
