using CK.Core;
using System.Diagnostics.Metrics;

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
    {
        _meterInfo = meterInfo;
        _instrumentInfo = instrumentInfo;
        _configuration = configuration;
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
            _configuration = value;
        }
    }
}


