using CK.Core;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    static readonly MeterListener _listener;
    static CKTrait _tag;
    static string? ThisFile( [CallerFilePath] string? path = null ) => path;
    static Dictionary<Meter,MeterState> _meters;
    static ConcurrentDictionary<Instrument, InstrumentState> _instruments;
    static string _filePath;
    static Timer _observableTimer;
    static int _currentTimerDueTime;
    static ImmutableArray<(InstrumentMatcher, InstrumentConfiguration)> _currentConfigurations;

    /// <summary>
    /// Gets the "Metrics" tag.
    /// </summary>
    public static CKTrait MetricsTag => _tag;

    /// <summary>
    /// Defaults to 255. 
    /// </summary>
    public static int MeterNameLengthLimit { get; set; }

    /// <summary>
    /// Defaults to 128. 
    /// From https://opentelemetry.io/docs/specs/otel/common/#attribute and https://opentelemetry.io/docs/specs/otel/common/#configurable-parameters
    /// ...but https://opentelemetry.io/docs/specs/otel/metrics/sdk/#attribute-limits: Metrics attributes should have no limit.
    /// <para>
    /// We currenlty decide to be strict here: a <see cref="CKException"/> is thrown if a <see cref="Meter.Tags"/>
    /// or <see cref="Instrument.Tags"/> has more attributes than this limit. This can be programatically changed if needed.
    /// </para>
    /// </summary>
    public static int AttributeCountLimit { get; set; }

    /// <summary>
    /// Applies to <see cref="KeyValuePair{TKey, TValue}"/>.Key length.
    /// It defaults to 255 characters.
    /// <para>
    /// A <see cref="CKException"/> is thrown if a <see cref="Meter.Tags"/>
    /// or <see cref="Instrument.Tags"/> has a key longer than this limit.
    /// This can be programatically changed if needed.
    /// </para>
    /// </summary>
    public static int AttributeNameLengthLimit { get; set; }

    /// <summary>
    /// This applies to string tag values and string items in array of strings.
    /// It defaults to 1024 characters.
    /// <para>
    /// A <see cref="CKException"/> is thrown if a <see cref="Meter.Tags"/>
    /// or <see cref="Instrument.Tags"/> has a string value longer than this limit.
    /// This can be programatically changed if needed.
    /// </para>
    /// </summary>
    public static int AttributeValueLengthLimit { get; set; }

    /// <summary>
    /// The maximal <see cref="InstrumentConfiguration.CoolerTimeSpan"/> is one hour.
    /// </summary>
    public const int MaxCoolerTimeSpan = 3_600_000;


    static DotNetMetrics()
    {
        MeterNameLengthLimit = 255;
        AttributeCountLimit = 128;
        AttributeNameLengthLimit = 255;
        AttributeValueLengthLimit = 1023;

        _tag = ActivityMonitor.Tags.Context.FindOrCreate( "Metrics" );
        _filePath = ThisFile() ?? "DotNetMetrics.cs";
        _meters = new Dictionary<Meter, MeterState>();
        _instruments = new ConcurrentDictionary<Instrument, InstrumentState>();
        _observableTimer = new Timer( OnObservableTimer, null, Timeout.Infinite, Timeout.Infinite );
        _currentConfigurations = [];
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

    static void OnObservableTimer( object? state ) => _listener.RecordObservableInstruments();

    /// <summary>
    /// Gets the currently available instruments and their configuration.
    /// <para>
    /// This is a thread-safe snapshot of the metrics regardless of any concurrent
    /// configurations being applied.
    /// Use <see cref="GetAvailableMetricsAsync"/> instead to obtain a fully configured state.
    /// </para>
    /// </summary>
    /// <returns>A <see cref="DotNetMetricsInfo"/>.</returns>
    public static DotNetMetricsInfo GetAvailableMetrics() => DoGetAvailableMetrics();

    static DotNetMetricsInfo DoGetAvailableMetrics()
    {
        var result = new List<FullInstrumentInfo>();
        MeterState[] meters;
        // Take a snapshot of the MeterState (see ApplyConfiguration).
        lock( _meters )
        {
            meters = _meters.Values.ToArray();
        }
        // New instruments may be published concurrently here and this is fine.
        return new DotNetMetricsInfo( _currentTimerDueTime,
                                      meters.SelectMany( m => m.InstrumentStates.Select( i => i.Info.Clone() ) ).ToList() );
    }

    /// <summary>
    /// Gets the currently available instruments and their configuration.
    /// <para>
    /// This captures a configured state: no configuration are concurrently being applied.
    /// This is typically useful in tests but in prodution, the synchronous <see cref="GetAvailableMetrics"/>
    /// can be called: a "half applied" configuration is a configuration...
    /// </para>
    /// </summary>
    /// <returns>A <see cref="DotNetMetricsInfo"/>.</returns>
    public static Task<DotNetMetricsInfo> GetAvailableMetricsAsync()
    {
        var tc = new TaskCompletionSource<DotNetMetricsInfo>( TaskCreationOptions.RunContinuationsAsynchronously );
        MicroAgent.Push( tc );
        return tc.Task;
    }

    /// <summary>
    /// Applies a <see cref="MetricsConfiguration"/>.
    /// </summary>
    /// <param name="configuration">The configuration to apply.</param>
    public static void ApplyConfiguration( MetricsConfiguration configuration ) => MicroAgent.Push( configuration );

    static void OnInstrumentPublished( Instrument instrument, MeterListener listener )
    {
        Throw.DebugAssert( !_instruments.ContainsKey( instrument ) );
        Throw.DebugAssert( listener == _listener );

        var meter = instrument.Meter;

        MeterState? mState = null;
        bool newMeter = false;
        lock( _meters )
        {
            if( !_meters.TryGetValue( meter, out mState ) )
            {
                newMeter = true;
                // Don't throw exception here: the poor MetricListener will be in trouble:
                // simply forget the meter and its instrument.
                mState = MeterState.Create( meter );
                if( mState == null ) return;
                _meters.Add( meter, mState );
            }
        }
        if( newMeter )
        {
            SendMetricLog( _newMeterPrefix + mState.Info.JsonDescription );
        }
        // Don't throw exception here: the poor MetricListener will be in trouble:
        // simply forget the instrument.
        var iState = InstrumentState.Create( mState, instrument );
        if( iState == null ) return;
        Throw.CheckState( _instruments.TryAdd( instrument, iState ) );
        SendMetricLog( _newInstrumentPrefix + iState.Info.Info.JsonDescription );
        // OnInstrumentPublished.
        MicroAgent.Push( iState );
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
                SendMetricLog( _disposedMeterPrefix + meter.Info.JsonDescription );
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

    // Called from the MicroAgent.
    static bool ApplyConfigurations( IActivityMonitor monitor, InstrumentState instrument )
    {
        foreach( var (m, c) in _currentConfigurations )
        {
            if( m.Match( instrument.Info ) )
            {
                return instrument.SetConfiguration( monitor, _listener, c, false );
            }
        }
        monitor.Debug( $"No matching configuration for {(instrument.IsEnabled ? "en" : "dis")}abled instrument '{instrument.Info.FullName}'." );
        return false;
    }

    // Called from the MicroAgent.
    static void Apply( ActivityMonitor monitor, MetricsConfiguration configuration )
    {
        using var _ = monitor.OpenTrace( "Applying metrics configuration." );
        // Take a snapshot of the MeterState and work on it. The InstrumentState list of each MeterState
        // is thread safe by design (append only single linked list). Instruments concurently published
        // after their first IntrumentState is considered are simply ignored.
        MeterState[] meters;
        lock( _meters )
        {
            meters = _meters.Values.ToArray();
        }
        // Also snapshots the instruments so we don't have to bother with concurently published instruments.
        var targets = meters.SelectMany( m => m.InstrumentStates ).ToList();
        if( meters.Length == 0 )
        {
            monitor.Trace( "No existing meters yet." );
        }
        else if( targets.Count == 0 )
        {
            monitor.Trace( $"No instrument published yet for existing {meters.Length} meters." );

        }
        else
        {
            monitor.Trace( $"Considering {meters.Length} meters with {targets.Count} instruments." );
        }
        _currentConfigurations = configuration.Configurations.ToImmutableArray();
        foreach( var instrument in targets )
        {
            ApplyConfigurations( monitor, instrument );
        }
        if( configuration.AutoObservableTimer.HasValue )
        {
            int t = configuration.AutoObservableTimer.Value;
            if( _currentTimerDueTime != t )
            {
                if( t == 0 ) _observableTimer.Change( Timeout.Infinite, Timeout.Infinite );
                else _observableTimer.Change( 0, t );
                _currentTimerDueTime = t;
            }
        }
    }
}

