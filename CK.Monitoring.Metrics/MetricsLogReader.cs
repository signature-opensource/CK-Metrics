using CK.Core;
using CK.Metrics;
using System.Diagnostics.Metrics;

namespace CK.Monitoring.Metrics;

public abstract class MetricsLogReader
{
    readonly InstanceTracker<MeterInfo> _meterTracker;
    readonly InstanceTracker<FullInstrumentInfo> _instrumentTracker;

    public MetricsLogReader( int maxExpectedMeterCount = 100, int maxExpectedInstrumentCount = 200 )
    {
        _meterTracker = new InstanceTracker<MeterInfo>( maxExpectedMeterCount );
        _instrumentTracker = new InstanceTracker<FullInstrumentInfo>( maxExpectedInstrumentCount );
    }

    public void Add( IActivityMonitor monitor, DateTime logTime, string log )
    {
        var p = MetricsLogParser.Create( log );
        switch( p.Kind )
        {
            case MetricsLogKind.Measure:
                if( p.TryReadMeasure( out ParsedMeasureLog m ) )
                {
                    var (i,state) = _instrumentTracker.Find( m.InstrumentId );
                    if( i != null )
                    {
                        HandleMeasure( monitor, i, state, logTime, m );
                    }
                    else
                    {
                        monitor.Error( DotNetMetrics.MetricsTag, $"""
                            Measure log received for unregistered instrument n°{m.InstrumentId}
                            Received: {log}
                            """ );
                    }
                }
                else
                {
                    monitor.Error( DotNetMetrics.MetricsTag, $"""
                        Unable to parse measure log.
                        Received: {log}
                        """ );
                }

                break;
            case MetricsLogKind.NewInstrument:
                if( p.TryParseNewInstrument( out var newInstrument ) )
                {
                    var (meter,meterState) = _meterTracker.Find( newInstrument.MeterId );
                    if( meter != null )
                    {
                        var i = new FullInstrumentInfo( meter, newInstrument, InstrumentConfiguration.BasicDisabled );
                        if( _instrumentTracker.TryAdd( monitor,
                                                       i,
                                                       out var exists,
                                                       (monitor,i) => OnNewInstrument( monitor, i, meterState ) ) )
                        {
                            monitor.Trace( DotNetMetrics.MetricsTag, $"New Instrument '{i.FullName}'." );
                        }
                        else
                        {
                            monitor.Error( DotNetMetrics.MetricsTag, $"""
                                Duplicate new Instrument log received.
                                Received: {log}
                                Already registered: {exists}
                                """ );
                        }
                    }
                    else
                    {
                        monitor.Error( DotNetMetrics.MetricsTag, $"""
                            New Instrument log received n°{newInstrument.InstrumentId}: unregistered meter n°{newInstrument.MeterId}.
                            Received: {log}
                            """ );
                    }
                }
                else
                {
                    monitor.Error( DotNetMetrics.MetricsTag, $"""
                        Unable to parse new Instrument log.
                        Received: {log}
                        """ );
                        
                }
                break;
            case MetricsLogKind.NewMeter:
                if( p.TryParseNewMeter( out var newMeter ) )
                {
                    if( !_meterTracker.TryAdd( monitor, newMeter, out var exists, OnNewMeter ) )
                    {
                        monitor.Error( DotNetMetrics.MetricsTag, $"""
                            Duplicate new Meter log received:
                            Received: {log}
                            Already registered: {exists}
                            """ );
                    }
                }
                else
                {
                    monitor.Error( DotNetMetrics.MetricsTag, $"""
                        Unable to parse new Meter log.
                        Received: {log}
                        """ );
                        
                }
                break;
            case MetricsLogKind.DisposedMeter:
                if( p.TryParseDisposedMeter( out var disposedMeter ) )
                {
                    if( _meterTracker.TryRemove( disposedMeter.MeterId, out var meter, out var meterState ) )
                    {
                        var removed = _instrumentTracker.Cleanup( monitor, ( monitor, i, state ) => i.MeterInfo.MeterId == disposedMeter.MeterId );
                        OnDisposedMeter( meter, meterState, removed ?? [] );
                    }
                    else
                    {
                        monitor.Error( DotNetMetrics.MetricsTag, $"""
                            Disposed unregistered Meter n°{disposedMeter.MeterId}.
                            Received: {log}
                            """ );
                    }
                }
                else
                {
                    monitor.Error( DotNetMetrics.MetricsTag, $"""
                        Unable to parse disposed Meter log.
                        Received: {log}
                        """ );
                        
                }
                break;
        }
    }

    /// <summary>
    /// Called on new Meter.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="info">The new <see cref="MeterInfo"/>.</param>
    /// <returns>The state to associate to this new meter.</returns>
    protected abstract object? OnNewMeter( IActivityMonitor monitor, MeterInfo info );

    /// <summary>
    /// Called on disposed meter.
    /// </summary>
    /// <param name="meter">The disposed meter.</param>
    /// <param name="meterState">The associated meter state.</param>
    /// <param name="instruments">The meter's instruments (and their states) that have been unregistered.</param>
    protected abstract void OnDisposedMeter( MeterInfo meter,
                                             object? meterState,
                                             IReadOnlyList<(FullInstrumentInfo? Instrument, object? InstrumentState)> instruments );

    /// <summary>
    /// Called on new instrument.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="instrument">The new instrument.</param>
    /// <param name="meterState">The optional state associated to the instrument's meter.</param>
    /// <returns>The state to associate to this new instrument.</returns>
    protected abstract object? OnNewInstrument( IActivityMonitor monitor, FullInstrumentInfo instrument, object? meterState );

    /// <summary>
    /// Called on each measure.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="i">The instrument that emitted the measure.</param>
    /// <param name="instrumentState">The optional state associated to the instrument.</param>
    /// <param name="measureTime">Time of the measure.</param>
    /// <param name="measure">The measure.</param>
    protected abstract void HandleMeasure( IActivityMonitor monitor,
                                           FullInstrumentInfo i,
                                           object? instrumentState,
                                           DateTime measureTime,
                                           ParsedMeasureLog measure );
}


