using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;

namespace CK.Metrics;

public readonly struct MetricsLogParser
{
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
            var head = Text.AsSpan( DotNetMetrics._newMeterPrefix.Length );
            return MeterInfo.TryMatch( ref head, out meterInfo );
        }
        meterInfo = null;
        return false;
    }

    public bool TryParseDisposedMeter( [NotNullWhen( true )] out MeterInfo? meterInfo )
    {
        if( Kind == MetricsLogKind.DisposedMeter )
        {
            var head = Text.AsSpan( DotNetMetrics._disposedMeterPrefix.Length );
            return MeterInfo.TryMatch( ref head, out meterInfo );
        }
        meterInfo = null;
        return false;
    }

    public bool TryParseNewInstrument( [NotNullWhen( true )] out InstrumentInfo? instrumentInfo )
    {
        if( Kind == MetricsLogKind.NewInstrument )
        {
            var head = Text.AsSpan( DotNetMetrics._newInstrumentPrefix.Length );
            return InstrumentInfo.TryMatch( ref head, out instrumentInfo );
        }
        instrumentInfo = null;
        return false;
    }

    public bool TryParseInstrumentConfiguration( [NotNullWhen( true )] out InstrumentConfiguration? instrumentConfiguration )
    {
        if( Kind == MetricsLogKind.InstrumentConfiguration )
        {
            var head = Text.AsSpan( DotNetMetrics._instrumentConfigurationPrefix.Length );
            return InstrumentConfiguration.TryMatch( ref head, out instrumentConfiguration );
        }
        instrumentConfiguration = null;
        return false;
    }

    public bool TryReadMeasure( out ParsedMeasureLog m )
    {
        if( Kind == MetricsLogKind.Measure )
        {
            var head = Text.AsSpan( DotNetMetrics._measurePrefix.Length );
            if( head.TryMatchInt32( out var instrumentId, 0 ) && head.TryMatch( ',' ) )
            {
                var mH = head;
                if( head.TrySkipDouble() )
                {
                    int mStart = mH.Length + DotNetMetrics._measurePrefix.Length;
                    int mLength = head.Length - mH.Length;
                    if( head.Length == 0 )
                    {
                        m = new ParsedMeasureLog( Text, instrumentId, mStart, mLength, 0 );
                        return true;
                    }
                    if( head.TryMatch( ",[" ) && head[^1] == ']' )
                    {
                        if( head.Length == 1 )
                        {
                            m = new ParsedMeasureLog( Text, instrumentId, mStart, mLength, 0 );
                            return true;
                        }
                        m = new ParsedMeasureLog( Text, instrumentId, mStart, mLength, head.Length - 1 + DotNetMetrics._measurePrefix.Length );
                        return true;
                    }
                }
            }
        }
        m = default;
        return false;
    }


    public static MetricsLogParser Create( string text )
    {
        Throw.CheckNotNullArgument( text );
        if( text.Length >= 3 )
        {
            var s = text.AsSpan();
            if( s.StartsWith( DotNetMetrics._newMeterPrefix, StringComparison.Ordinal ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.NewMeter );
            }
            if( s.StartsWith( DotNetMetrics._disposedMeterPrefix ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.DisposedMeter );
            }
            if( s.StartsWith( DotNetMetrics._newInstrumentPrefix ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.NewInstrument );
            }
            if( s.StartsWith( DotNetMetrics._instrumentConfigurationPrefix ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.NewInstrument );
            }
            if( s.StartsWith( DotNetMetrics._measurePrefix ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.Measure );
            }
        }
        return new MetricsLogParser( text, MetricsLogKind.None );

    }
}

