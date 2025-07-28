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
    /// This must be 0 (the default, auto collection is disbaled by default) or greater than 50 ms
    /// and lower than 60000 ms (one hour).
    /// </para>
    /// </summary>
    public int AutoObservableTimer { get; set; }

    /// <summary>
    /// Gets the ordered list of configurations to apply.
    /// </summary>
    public IList<(InstrumentMatcher,InstrumentConfiguration)> Configurations => _configurations;
}
