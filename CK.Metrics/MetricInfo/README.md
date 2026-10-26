# Metrics information

The types of this folder are immutable captures of the .NET `Meter` and `Instrument` objects and of their
configuration. They are what `DotNetMetrics.GetConfiguration()` returns and what the metrics log entries describe.

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
- `MeterInfo MeterInfo`: an immutable capture of the .Net `Meter` object.
- `InstrumentInfo Info`: an immutable capture of the .Net `Instrument` object.
- `InstrumentConfiguration Configuration`: the configuration that applies to the instrument (also immutable).
- `string FullName`: the "Meter's name/Instrument's name".

The `FullInstrumentInfo` objects of a `DotNetMetricsInfo` are clones: they capture the configuration at the time of the call.

## Metric Log Format

Metrics are logged as structured text entries with these prefixes:

| Prefix | Meaning | `MetricsLogKind` |
|--------|---------|------------------|
| `+Meter:` | New meter created | `NewMeter` |
| `-Meter:` | Meter disposed | `DisposedMeter` |
| `+Instrument:` | Instrument published | `NewInstrument` |
| `+IConfig:` | Configuration changed | `InstrumentConfiguration` |
| `M:` | Measurement recorded | `Measure` |

`MetricsLogParser.Create( text )` reads the `Kind` of an entry; its `TryParseNewMeter`, `TryParseDisposedMeter`,
`TryParseNewInstrument`, `TryParseInstrumentConfiguration` and `TryReadMeasure` methods read the corresponding
`MeterInfo`, `InstrumentInfo`, `InstrumentConfiguration` or `ParsedMeasureLog`.
