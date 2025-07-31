using CK.Core;
using System;
using System.Diagnostics.Metrics;
using System.Threading;

namespace CK.Metrics;

/// <summary>
/// Links a <see cref="InstrumentInfo"/> to its <see cref="MeterInfo"/> and <see cref="Configuration"/>.
/// </summary>
public sealed class FullInstrumentInfo
{
    readonly MeterInfo _meterInfo;
    readonly InstrumentInfo _instrumentInfo;
    InstrumentConfiguration _configuration;
    string? _fullName;

    public FullInstrumentInfo( MeterInfo meterInfo,
                               InstrumentInfo instrumentInfo,
                               InstrumentConfiguration configuration )
        : this( meterInfo, instrumentInfo, configuration, null )
    {
    }

    FullInstrumentInfo( MeterInfo meterInfo,
                        InstrumentInfo instrumentInfo,
                        InstrumentConfiguration configuration,
                        string? fullName )
    {
        _meterInfo = meterInfo;
        _instrumentInfo = instrumentInfo;
        _configuration = configuration;
        _fullName = fullName;
    }

    /// <summary>
    /// Gets the meter that defines this instrument.
    /// </summary>
    public MeterInfo MeterInfo => _meterInfo;

    /// <summary>
    /// Gets the <see cref="InstrumentInfo"/>.
    /// </summary>
    public InstrumentInfo Info => _instrumentInfo;

    /// <summary>
    /// Gets the "<see cref="Meter.Name"/>/<see cref="Instrument.Name"/>".
    /// </summary>
    public string FullName => _fullName ??= _meterInfo.Name + '/' + _instrumentInfo.Name;

    /// <summary>
    /// Gets or sets the instrument's configuration.
    /// </summary>
    public InstrumentConfiguration Configuration
    {
        get => _configuration;
        set
        {
            Throw.CheckNotNullArgument( value );
            Interlocked.Exchange( ref _configuration, value );
        }
    }

    /// <summary>
    /// Clones this object to captures the current <see cref="Configuration"/>.
    /// </summary>
    /// <returns>A clone of this info.</returns>
    public FullInstrumentInfo Clone() => new FullInstrumentInfo( _meterInfo, _instrumentInfo, _configuration, _fullName );
}


