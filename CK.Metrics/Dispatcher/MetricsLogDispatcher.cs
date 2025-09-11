using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Metrics;

/// <summary>
/// Receives logs that should be emitted by the the <see cref="ActivityMonitor.StaticLogger"/>
/// and tagged with <see cref="DotNetMetrics.MetricsTag"/>.
/// <para>
/// Maintains a Meter/Instrument structure with associated external optional states and offers
/// <see cref="OnNewMeter(IActivityMonitor, MeterInfo)"/>, 
/// <see cref="OnNewInstrument(IActivityMonitor, FullInstrumentInfo, object?)"/>,
/// <see cref="OnMeasure(IActivityMonitor, FullInstrumentInfo, object?, DateTime, in ParsedMeasureLog)"/>
/// and <see cref="OnDisposedMeter(IActivityMonitor, MeterInfo, object?, IReadOnlyList{ValueTuple{FullInstrumentInfo?, object?}})"/>
/// abstract methods.
/// </para>
/// </summary>
public abstract class MetricsLogDispatcher
{
    readonly InstanceTracker<MeterInfo> _meterTracker;
    readonly InstanceTracker<FullInstrumentInfo> _instrumentTracker;

    /// <summary>
    /// Initializes a new <see cref="MetricsLogDispatcher"/>.
    /// </summary>
    /// <param name="maxExpectedMeterCount">Expected number of meters that will send measures.</param>
    /// <param name="maxExpectedInstrumentCount">Expected number of instruments that will send measures.</param>
    public MetricsLogDispatcher( int maxExpectedMeterCount = 100, int maxExpectedInstrumentCount = 200 )
    {
        _meterTracker = new InstanceTracker<MeterInfo>( maxExpectedMeterCount );
        _instrumentTracker = new InstanceTracker<FullInstrumentInfo>( maxExpectedInstrumentCount );
    }

    /// <summary>
    /// Adds a log line that should have been emitted by the <see cref="ActivityMonitor.StaticLogger"/>
    /// and be tagged with <see cref="DotNetMetrics.MetricsTag"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="logTime">The time of the log.</param>
    /// <param name="log">The log text.</param>
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
                        OnMeasure( monitor, i, state, logTime, m );
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
                        OnDisposedMeter( monitor, meter, meterState, removed ?? [] );
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
    /// <param name="monitor">The monitor.</param>
    /// <param name="meter">The disposed meter.</param>
    /// <param name="meterState">The associated meter state.</param>
    /// <param name="instruments">The meter's instruments (and their states) that have been unregistered.</param>
    protected abstract void OnDisposedMeter( IActivityMonitor monitor,
                                             MeterInfo meter,
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
    /// <param name="instrument">The instrument that emitted the measure.</param>
    /// <param name="instrumentState">The optional state associated to the instrument.</param>
    /// <param name="measureTime">Time of the measure.</param>
    /// <param name="measure">The measure.</param>
    protected abstract void OnMeasure( IActivityMonitor monitor,
                                       FullInstrumentInfo instrument,
                                       object? instrumentState,
                                       DateTime measureTime,
                                       in ParsedMeasureLog measure );
}


