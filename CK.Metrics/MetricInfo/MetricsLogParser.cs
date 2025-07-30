using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;

namespace CK.Metrics;

public readonly struct MetricsLogParser
{
    const string _newMeterPrefix = "+Meter[";
    const string _disposedMeterPrefix = "-Meter[";
    const string _newInstrumentPrefix = "+Instrument[";
    const string _instrumentConfigurationPrefix = "+IConfig[";

    public readonly string Text;

    public readonly MetricsLogKind Kind;

    MetricsLogParser( string text, MetricsLogKind kind )
    {
        Text = text;
        Kind = kind;
    }

    public bool TryParseNewMeter( [NotNullWhen( true )] out MeterInfo? meterInfo )
    {
        if( Kind == MetricsLogKind.NewMeter )
        {
            return MeterInfo.DoTryParse( Text, _newMeterPrefix.Length, out meterInfo );
        }
        meterInfo = null;
        return false;
    }

    public bool TryParseDisposedMeter( [NotNullWhen( true )] out MeterInfo? meterInfo )
    {
        if( Kind == MetricsLogKind.DisposedMeter )
        {
            return MeterInfo.DoTryParse( Text, _disposedMeterPrefix.Length, out meterInfo );
        }
        meterInfo = null;
        return false;
    }

    public bool TryParseNewInstrument( [NotNullWhen( true )] out InstrumentInfo? instrumentInfo )
    {
        if( Kind == MetricsLogKind.NewInstrument )
        {
            return InstrumentInfo.DoTryParse( Text, _newInstrumentPrefix.Length, out instrumentInfo );
        }
        instrumentInfo = null;
        return false;
    }

    public bool TryParseInstrumentConfiguration( [NotNullWhen( true )] out InstrumentConfiguration? instrumentConfiguration )
    {
        if( Kind == MetricsLogKind.InstrumentConfiguration )
        {
            return InstrumentConfiguration.DoTryParse( Text, _instrumentConfigurationPrefix.Length, out instrumentConfiguration );
        }
        instrumentConfiguration = null;
        return false;
    }


    public static MetricsLogParser Create( string text )
    {
        Throw.CheckNotNullArgument( text );
        if( text.Length >= 3 )
        {
            var s = text.AsSpan();
            if( s.StartsWith( _newMeterPrefix, StringComparison.Ordinal ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.NewMeter );
            }
            if( s.StartsWith( _disposedMeterPrefix ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.DisposedMeter );
            }
            if( s.StartsWith( _newInstrumentPrefix ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.NewInstrument );
            }
            if( s.StartsWith( _instrumentConfigurationPrefix ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.NewInstrument );
            }
            if( s.StartsWith( "M:" ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.Measure );
            }
        }
        return new MetricsLogParser( text, MetricsLogKind.None );

    }

}

