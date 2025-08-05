using System;
using CK.Core;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace CK.Metrics;

public static partial class DotNetMetrics // Extensions
{
    /// <summary>
    /// Gets the <see cref="MeterInfo"/> associated to this <see cref="Meter"/>.
    /// This returns <see cref="MeterInfo.Missing"/> if the Meter is not yet registered in
    /// <see cref="DotNetMetrics"/> because it has no instruments.
    /// </summary>
    /// <param name="meter">This meter.</param>
    /// <returns>The associated MeterInfo or <see cref="MeterInfo.Missing"/> if <paramref name="meter"/> is not yet registered.</returns>
    public static MeterInfo GetMeterInfo( this Meter meter )
    {
        MeterState? state;
        lock( _meters )
        {
            state = _meters.GetValueOrDefault( meter );
        }
        return state != null ? state.Info : MeterInfo.Missing;
    }

}


public static partial class DotNetMetrics // DefaultConfigure
{
    /// <summary>
    /// Configures an instrument if it has no existing matching configuration in last applied <see cref="MetricsConfiguration"/>.
    /// The <paramref name="configuration"/> must not be a <see cref="InstrumentConfiguration.BasicDisabled"/> configuration
    /// otherwise an <see cref="ArgumentException"/> is thrown: BasicDisabled is the default configuration.
    /// </summary>
    /// <param name="instrument">This instrument.</param>
    /// <param name="configuration">The configuration to apply if the instrument is not configured.</param>
    /// <returns>This instrument.</returns>
    public static Counter<T> DefaultConfigure<T>( this Counter<T> instrument, InstrumentConfiguration configuration ) where T : struct
    {
        return Unsafe.As<Counter<T>>( DoDefaultConfigure( instrument, configuration ) );
    }

    /// <inheritdoc cref="DefaultConfigure{T}(Counter{T}, InstrumentConfiguration)"/>
    public static ObservableCounter<T> DefaultConfigure<T>( this ObservableCounter<T> instrument, InstrumentConfiguration configuration ) where T : struct
    {
        return Unsafe.As<ObservableCounter<T>>( DoDefaultConfigure( instrument, configuration ) );
    }

    /// <inheritdoc cref="DefaultConfigure{T}(Counter{T}, InstrumentConfiguration)"/>
    public static UpDownCounter<T> DefaultConfigure<T>( this UpDownCounter<T> instrument, InstrumentConfiguration configuration ) where T : struct
    {
        return Unsafe.As<UpDownCounter<T>>( DoDefaultConfigure( instrument, configuration ) );
    }

    /// <inheritdoc cref="DefaultConfigure{T}(Counter{T}, InstrumentConfiguration)"/>
    public static ObservableUpDownCounter<T> DefaultConfigure<T>( this ObservableUpDownCounter<T> instrument, InstrumentConfiguration configuration ) where T : struct
    {
        return Unsafe.As<ObservableUpDownCounter<T>>( DoDefaultConfigure( instrument, configuration ) );
    }

    /// <inheritdoc cref="DefaultConfigure{T}(Counter{T}, InstrumentConfiguration)"/>
    public static Gauge<T> DefaultConfigure<T>( this Gauge<T> instrument, InstrumentConfiguration configuration ) where T : struct
    {
        return Unsafe.As<Gauge<T>>( DoDefaultConfigure( instrument, configuration ) );
    }

    /// <inheritdoc cref="DefaultConfigure{T}(Counter{T}, InstrumentConfiguration)"/>
    public static ObservableGauge<T> DefaultConfigure<T>( this ObservableGauge<T> instrument, InstrumentConfiguration configuration ) where T : struct
    {
        return Unsafe.As<ObservableGauge<T>>( DoDefaultConfigure( instrument, configuration ) );
    }

    /// <inheritdoc cref="DefaultConfigure{T}(Counter{T}, InstrumentConfiguration)"/>
    public static Histogram<T> DefaultConfigure<T>( this Histogram<T> instrument, InstrumentConfiguration configuration ) where T : struct
    {
        return Unsafe.As<Histogram<T>>( DoDefaultConfigure( instrument, configuration ) );
    }

    static Instrument DoDefaultConfigure( Instrument instrument, InstrumentConfiguration configuration )
    {
        Throw.CheckArgument( configuration is not null && configuration.Equals( InstrumentConfiguration.BasicDisabled ) is false );
        MicroAgent.Push( (instrument, configuration) );
        return instrument;
    }

}

