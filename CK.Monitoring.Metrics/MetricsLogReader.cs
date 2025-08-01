using CK.Core;
using CK.Metrics;
using System.Diagnostics.Metrics;

namespace CK.Monitoring.Metrics;

public abstract class MetricsLogReader
{
    readonly MeterTracker<MeterInfo> _meterTracker;
    readonly MeterTracker<FullInstrumentInfo> _instrumentTracker;

    public MetricsLogReader( int maxExpectedMeterCount = 100, int maxExpectedInstrumentCount = 200 )
    {
        _meterTracker = new MeterTracker<MeterInfo>( maxExpectedMeterCount );
        _instrumentTracker = new MeterTracker<FullInstrumentInfo>( maxExpectedInstrumentCount );
    }

    public void Add( IActivityMonitor monitor, string log )
    {
        var p = MetricsLogParser.Create( log );
        switch( p.Kind )
        {
            case MetricsLogKind.Measure:

                break;
            case MetricsLogKind.NewInstrument:
                if( p.TryParseNewInstrument( out var newInstrument ) )
                {
                    var meter = _meterTracker.Find( newInstrument.MeterId );
                    if( meter != null )
                    {
                        var i = new FullInstrumentInfo( meter, newInstrument, InstrumentConfiguration.BasicDisabled );
                        if( !_instrumentTracker.TryAdd( i, out var exists ) )
                        {
                            monitor.Error( $"""
                                Duplicate new Instrument metrics log received.
                                Received: {log}
                                Already registered: {exists}
                                """ );
                        }
                    }
                    else
                    {
                        monitor.Error( $"New Instrument metrics log received n°{newInstrument.InstrumentId}: the meter n°{newInstrument.MeterId} is not registered." );
                    }
                }
                else
                {
                    monitor.Error( $"Unable to parse new Instrument metrics log: {log}" );
                }
                break;
            case MetricsLogKind.NewMeter:
                if( p.TryParseNewMeter( out var newMeter ) )
                {
                    if( !_meterTracker.TryAdd( newMeter, out var exists ) )
                    {
                        monitor.Error( $"""
                            Duplicate new Meter metrics log received:
                            Received: {log}
                            Already registered: {exists}
                            """ );
                    }
                }
                else
                {
                    monitor.Error( $"Unable to parse new Meter metrics log: {log}" );
                }
                break;
            case MetricsLogKind.DisposedMeter:
                if( p.TryParseDisposedMeter( out var disposedMeter ) )
                {
                    if( !_meterTracker.TryRemove( disposedMeter.MeterId ) )
                    {
                        monitor.Error( $"""
                            Unable to find Meter n°{disposedMeter.MeterId}.
                            Received: {log}
                            """ );
                    }
                }
                else
                {
                    monitor.Error( $"Unable to parse disposed Meter metrics log: {log}" );
                }
                break;
        }
    }

    protected abstract void HandleMeasure<T>( FullInstrumentInfo instrument, Measurement<T> measure ) where T : struct;
}


