# CK-Metrics

Supports [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics) with
a different approach than other metrics collectors and handlers like the ones in [Microsoft.Extensions.Diagnostics.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions).

The approach is simpler as there are no DI involved. Just like the [DotNetEventEventSource](https://github.com/signature-opensource/CK-ActivityMonitor/tree/develop/CK.ActivityMonitor/DotNetEventSource)
support, metrics are global to an application (technically the AppDomain but we don't play with multiple AppDomains anyway).

This library is on the collector side of the Telemetry world. It can handle Meters and Instruments defined in external assemblies
like the [.Net standard ones](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-diagnostics) but we
impose some constraints that are stricter than the OpenTelemetry framework suggests.

## Configuration

The 2 following static methods of the `DotNetMetrics` class are thread-safe and configures the metrics for the whole system:
```csharp
public static Task<DotNetMetricsInfo> GetAvailableMetricsAsync();
public static void ApplyConfiguration( MetricsConfiguration configuration );
```

## Getting the current System state and configurations

The `DotNetMetricsInfo` returned by `GetAvailableMetricsAsync` exposes the Meters and Instruments and the
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


