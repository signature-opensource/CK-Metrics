using CK.Core;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Text;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    static readonly MeterListener _listener;
    static CKTrait _tag;
    static string? ThisFile( [CallerFilePath] string? path = null ) => path;
    static Dictionary<Meter,MeterState> _meters;
    static ConcurrentDictionary<Instrument, InstrumentState> _instruments;
    static string _filePath;
    static bool _defaultCodeEnabled;

    /// <summary>
    /// Gets the "Metrics" tag.
    /// </summary>
    public static CKTrait MetricsTag => _tag;

    /// <summary>
    /// Defaults to 128. 
    /// From https://opentelemetry.io/docs/specs/otel/common/#attribute and https://opentelemetry.io/docs/specs/otel/common/#configurable-parameters
    /// ...but https://opentelemetry.io/docs/specs/otel/metrics/sdk/#attribute-limits: Metrics attributes should have no limit.
    /// <para>
    /// We currenlty decide to be strict here and to apply limits but this can be changed if needed.
    /// </para>
    /// </summary>
    static int MetricsAttributeCountLimit { get; set; }

    static DotNetMetrics()
    {
        _tag = ActivityMonitor.Tags.Context.FindOrCreate( "Metrics" );
        _filePath = ThisFile() ?? "DotNetMetrics.cs";
        _meters = new Dictionary<Meter, MeterState>();
        _instruments = new ConcurrentDictionary<Instrument, InstrumentState>();
        _listener = new MeterListener();
        _listener.SetMeasurementEventCallback<byte>( static ( i, m, t, s ) => ((InstrumentState<byte>)s!).HandleMeasure( m, t ) );
        _listener.SetMeasurementEventCallback<short>( static ( i, m, t, s ) => ((InstrumentState<short>)s!).HandleMeasure( m, t ) );
        _listener.SetMeasurementEventCallback<int>( static ( i, m, t, s ) => ((InstrumentState<int>)s!).HandleMeasure( m, t ) );
        _listener.SetMeasurementEventCallback<long>( static ( i, m, t, s ) => ((InstrumentState<long>)s!).HandleMeasure( m, t ) );
        _listener.SetMeasurementEventCallback<float>( static ( i, m, t, s ) => ((InstrumentState<float>)s!).HandleMeasure( m, t ) );
        _listener.SetMeasurementEventCallback<double>( static ( i, m, t, s ) => ((InstrumentState<double>)s!).HandleMeasure( m, t ) );
        _listener.SetMeasurementEventCallback<decimal>( static ( i, m, t, s ) => ((InstrumentState<decimal>)s!).HandleMeasure( m, t ) );
        _listener.InstrumentPublished = OnInstrumentPublished;
        _listener.MeasurementsCompleted = OnMeasurementsCompleted;
        _listener.Start();
    }

    /// <summary>
    /// Enables the instrument to send its measures.
    /// This is independent of any configuration.
    /// Measurements can be stopped by calling <see cref="DisableByCode(Instrument)"/>.
    /// </summary>
    /// <param name="instrument">The instrument.</param>
    /// <returns>True on success, false if the <see cref="Instrument.Meter"/> has been disposed.</returns>
    public static bool EnableByCode( Instrument instrument )
    {
        if( _instruments.TryGetValue( instrument, out var iState ) )
        {
            iState.CodeEnable( _listener );
            return true;
        }
        return false;
    }

    /// <summary>
    /// Cancels a previous call to <see cref="EnableByCode(Instrument)"/>.
    /// </summary>
    /// <param name="instrument">The instrument.</param>
    /// <returns>True on success, false if the <see cref="Instrument.Meter"/> has been disposed.</returns>
    public static bool DisableByCode( Instrument instrument )
    {
        if( _instruments.TryGetValue( instrument, out var iState ) )
        {
            return iState.CodeDisable( _listener );
        }
        return false;
    }

    static void OnInstrumentPublished( Instrument instrument, MeterListener listener )
    {
        Throw.DebugAssert( !_instruments.ContainsKey( instrument ) );
        Throw.DebugAssert( listener == _listener );

        StringBuilder b = new StringBuilder();
        var meter = instrument.Meter;

        bool defaultCodeEnabled;
        MeterState? mState = null;
        bool newMeter = false;
        lock( _meters )
        {
            defaultCodeEnabled = _defaultCodeEnabled;
            if( !_meters.TryGetValue( meter, out mState ) )
            {
                newMeter = true;
                mState = MeterState.Create( meter, b );
                _meters.Add( meter, mState );
            }
        }
        if( newMeter )
        {
            b.Clear().Append( "+Meter[" ).Append( mState.JsonDescription ).Append( ']' );
            SendMetricLog( b.ToString() );
            b.Clear();
        }
        var iState = InstrumentState.Create( mState, instrument, b );
        Throw.CheckState( _instruments.TryAdd( instrument, iState ) );
        b.Clear().Append( "+Instrument[" ).Append( iState.JsonDescription ).Append( ']' ); 
        SendMetricLog( b.ToString() ); ;
    }

    static void OnMeasurementsCompleted( Instrument instrument, object? state )
    {
        bool isExpectedCompletion = state is InstrumentState iState && iState.OnMeasurementsCompleted();
        if( !isExpectedCompletion )
        {
            // Meter has been disposed.
            MeterState? meter;
            lock( _meters )
            {
                if( _meters.TryGetValue( instrument.Meter, out meter ) )
                {
                   _meters.Remove( instrument.Meter );
                }
            }
            if( meter != null )
            {
                var b = new StringBuilder( "-Meter[" ).Append( meter.JsonDescription ).Append( ']' );
                SendMetricLog( b.ToString() );
            }
        }
    }

    static void SendMetricLog( string text )
    {
        var data = ActivityMonitor.StaticLogger.CreateActivityMonitorLogData( LogLevel.Info | LogLevel.IsFiltered,
                                                                              _tag,
                                                                              text,
                                                                              exception: null,
                                                                              fileName: _filePath,
                                                                              lineNumber: 1,
                                                                              isOpenGroup: false );
        ActivityMonitor.StaticLogger.UnfilteredLog( ref data );
    }

}
