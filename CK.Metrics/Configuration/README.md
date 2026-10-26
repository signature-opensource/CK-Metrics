# Metrics configuration

Meters and Instruments are created by code. The configuration enables or disables them: `InstrumentConfiguration`
only contains a `bool Enabled` property (`InstrumentConfiguration.BasicEnabled` and `InstrumentConfiguration.BasicDisabled`
are the two shared instances).

## MetricsConfiguration

The `MetricsConfiguration` provided to the static `DotNetMetrics.ApplyConfiguration` contains "rules" to select instruments
and the associated configuration to be applied as well as a potential update of the `AutoObservableTimer` timer delay.
```csharp
public class MetricsConfiguration
{
    public int? AutoObservableTimer { get; set; }

    /// <summary>
    /// Gets the ordered list of configurations to apply from most precise one to more general one.
    /// </summary>
    public IList<(InstrumentMatcher,InstrumentConfiguration)> Configurations => _configurations;
}
```
`AutoObservableTimer` is the delay (in ms) of the timer that collects the measures of the `ObservableInstrument<T>`:
- `null` (the default) leaves the current value unchanged.
- `0` or a negative value is normalized to `0`: automatic collection is disabled (this is the initial state).
- A positive value is clamped to at most 3_600_000 ms (one hour).

Applying a configuration replaces the current rules: each existing instrument is matched against the new rules and
instruments published later are matched against them. An instrument that matches no rule keeps its current configuration.

## InstrumentMatcher

The "rules" are the `InstrumentMatcher` that are very simple immutable objects that have a wildcard
`NamePattern` applied to the `FullInstrumentInfo.FullName` ("Meter's name/Instrument's name"). This uses a very basic
projection to a regular expression (anchored on both ends):
- `?` Maps to any single character (`'.'` in RegEx syntax).
- `*` Maps to the lazy `'.*?'` pattern.
- `**` Maps to the greedy `'.*'` pattern.

`"*"` or `"**"` matches every instrument. To enable all the instruments and collect the `ObservableInstrument`
each 50 ms:
```csharp
var c = new MetricsConfiguration();
c.AutoObservableTimer = 50;
c.Configurations.Add( (new InstrumentMatcher( "*" ), new InstrumentConfiguration( true )) );
DotNetMetrics.ApplyConfiguration( c );
```

### Configuration Priority

Rules are matched in order and the first matching rule wins. Put specific rules before general ones:

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

### Tags

For more complex configurations, the `IncludeTags` and `ExcludeTags` can be used.

```csharp
public sealed class InstrumentMatcher
{
    public InstrumentMatcher( string namePattern,
                              IEnumerable<KeyValuePair<string, object?>>? includeTags = null,
                              IEnumerable<KeyValuePair<string, object?>>? excludeTags = null );

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
}
```
The include tags must all be found in the meter's tags or all be found in the instrument's tags. A duplicate key in
`includeTags` or `excludeTags` throws an `ArgumentException`.
