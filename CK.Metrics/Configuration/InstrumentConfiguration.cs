using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading;

namespace CK.Metrics;

public class InstrumentConfiguration
{
    bool _enabled;
    int? _coolerTimeSpan;

    public InstrumentConfiguration()
    {
    }

    /// <summary>
    /// Gets or sets whether the instrument is enabled.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Gets or sets the cooler time span in milliseconds.
    /// Normalized between 0 (the default, no cooling) and <see cref="DotNetMetrics.MaxCoolerTimeSpan"/>.
    /// </summary>
    public int? CoolerTimeSpan
    {
        get => _coolerTimeSpan;
        set => _coolerTimeSpan = value.HasValue
                                    ? int.Clamp( value.Value, 0, DotNetMetrics.MaxCoolerTimeSpan )
                                    : value;
    }

    public InstrumentConfiguration Clone()
    {
        var c = new InstrumentConfiguration();
        c._enabled = _enabled;
        c._coolerTimeSpan = _coolerTimeSpan;
        return c;
    }

    internal protected virtual RunningConfiguration Create( UserMessageCollector messages, Instrument instrument )
    {
        var type = instrument.GetType();
        if( !type.IsGenericType || type.Namespace != "System.Diagnostics.Metrics" )
        {
            messages.Error( $"Default configuration doesn't apply to Instrument type '{type.ToCSharpName()}'." );
            return default;
        }
        switch( instrument )
        {
            case Counter<byte> c: return StandardAggregator.Create( messages, c, this );
            case Counter<int> c: return StandardAggregator.Create( messages, c, this );
            case Counter<short> c: return StandardAggregator.Create( messages, c, this );
            case Counter<long> c: return StandardAggregator.Create( messages, c, this );
            case Counter<double> c: return StandardAggregator.Create( messages, c, this );
            case Counter<decimal> c: return StandardAggregator.Create( messages, c, this );
        }
    }
}

sealed class CounterAggLong<T> : ILocalAggregator<T> where T : struct
{
    long _value;
    long _lastSent;

    public CounterAggLong()
    {
    }

    public void Flush()
    {
        throw new NotImplementedException();
    }

    public bool HandleMeasure( T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags )
    {
        throw new NotImplementedException();
    }
}

readonly record struct RunningConfiguration<T>( bool Enabled, ILocalAggregator<T>? aggregator = null ) where T : struct;


