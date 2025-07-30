using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace CK.Metrics;

/// <summary>
/// Captures the currently available instruments:
/// <see cref="FullInstrumentInfo"/> provides their configuration and <see cref="MeterInfo"/>.
/// </summary>
public sealed class DotNetMetricsInfo
{
    readonly int _autoObservableTimer;
    readonly IReadOnlyList<FullInstrumentInfo> _instruments;

    internal DotNetMetricsInfo( int autoObservableTimer, IReadOnlyList<FullInstrumentInfo> instruments )
    {
        _autoObservableTimer = autoObservableTimer;
        _instruments = instruments;
    }

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

