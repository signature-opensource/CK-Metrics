using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace CK.Metrics;

public class MetricsConfiguration
{
    readonly List<(InstrumentMatcher, InstrumentConfiguration)> _configurations;
    int? _autoObservableTimer;

    public MetricsConfiguration()
    {
        _configurations = new List<(InstrumentMatcher, InstrumentConfiguration)>();        
    }

    /// <summary>
    /// Gets or sets the timer delay that collects the <see cref="ObservableInstrument{T}"/>
    /// measures. Defaults to null (leaves the current value unchaged).
    /// <para>
    /// When not null, this is clamped between 0 and 3_600_000 ms (one hour): a negative value
    /// becomes 0, that disables the auto collection (the default).
    /// </para>
    /// </summary>
    public int? AutoObservableTimer
    {
        get => _autoObservableTimer;
        set
        {
            _autoObservableTimer = value.HasValue
                                        ? value.Value <= 0 ? 0 : int.Clamp( value.Value, 0, 3_600_000 )
                                        : value;
        }
    }

    /// <summary>
    /// Gets the ordered list of configurations to apply from most precise one to more general one.
    /// </summary>
    public IList<(InstrumentMatcher,InstrumentConfiguration)> Configurations => _configurations;

}
