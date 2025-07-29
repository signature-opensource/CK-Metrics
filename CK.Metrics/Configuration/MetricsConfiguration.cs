using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Metrics;

public class MetricsConfiguration
{
    readonly List<(InstrumentMatcher, InstrumentConfiguration)> _configurations;
    int _autoObservableTimer;

    public MetricsConfiguration()
    {
        _configurations = new List<(InstrumentMatcher, InstrumentConfiguration)>();        
    }

    /// <summary>
    /// Gets or sets the timer delay that collects the <see cref="ObservableInstrument{T}"/>
    /// measures.
    /// <para>
    /// This is normalized to 0 (the default, auto collection is disabled by default) or to a
    /// value between 50 ms and 3_600_000 ms (one hour).
    /// </para>
    /// </summary>
    public int AutoObservableTimer
    {
        get => _autoObservableTimer;
        set => _autoObservableTimer = value <= 0 ? 0 : int.Clamp( value, 0, 3_600_000 );
    }

    /// <summary>
    /// Gets the ordered list of configurations to apply from most precise one to more general one.
    /// </summary>
    public IList<(InstrumentMatcher,InstrumentConfiguration)> Configurations => _configurations;

    public MetricsConfiguration Clone()
    {
        var clone = new MetricsConfiguration();
        clone._autoObservableTimer = _autoObservableTimer;
        foreach( var (m,c) in _configurations )
        {
            clone._configurations.Add( (m, c.Clone()) );
        }
        return clone;
    }
}
