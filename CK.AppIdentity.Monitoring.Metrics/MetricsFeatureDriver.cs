using CK.Core;
using CK.Monitoring;
using FASTER.core;
using Microsoft.Extensions.Configuration;

namespace CK.AppIdentity.Monitoring.Metrics;

/// <summary>
/// Feature driver that owns the shared FasterLog instance for metrics.
/// <para>
/// This driver creates and manages the FasterLog, injects it into the <see cref="MetricsLogHandler"/>
/// via <see cref="SetMetricsFasterLogAction"/>, and provides consumer registration.
/// </para>
/// <para>
/// Consumer features (like CSV export) should depend on this driver via DI and call
/// <see cref="RegisterConsumer"/> during their setup.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Configuration example:
/// </para>
/// <code>
/// {
///   "CK-AppIdentity": {
///     "FullName": "MyDomain/$MyApp/#Dev",
///     "Local": {
///       "Metrics": {
///         "Path": "FasterLog/Metrics",
///         "MemoryPageCount": 2,
///         "TruncationIntervalMs": 60000,
///         "HandlerWaitTimeoutMs": 30000
///       }
///     }
///   }
/// }
/// </code>
/// <para>
/// The handler must be configured separately in GrandOutput configuration:
/// </para>
/// <code>
/// {
///   "CK-Monitoring": {
///     "GrandOutput": {
///       "Handlers": {
///         "MetricsLogHandler, CK.Monitoring.Metrics": {
///           "CommitRate": 1
///         }
///       }
///     }
///   }
/// }
/// </code>
/// </remarks>
public sealed class MetricsFeatureDriver : ApplicationIdentityFeatureDriver
{
    /// <summary>
    /// Default relative path for FasterLog storage.
    /// </summary>
    // ReSharper disable once MemberCanBePrivate.Global
    public const string DefaultFasterLogPath = "FasterLog/Metrics";

    /// <summary>
    /// Default truncation interval in milliseconds (1 minute).
    /// </summary>
    // ReSharper disable once MemberCanBePrivate.Global
    public const int DefaultTruncationIntervalMs = 60000;

    /// <summary>
    /// Default timeout in milliseconds for waiting for the handler (30 seconds).
    /// </summary>
    // ReSharper disable once MemberCanBePrivate.Global
    public const int DefaultHandlerWaitTimeoutMs = 30000;

    FasterLog? _log;
    FasterLogSettings? _logSettings;
    IDevice? _device;
    GrandOutput? _targetGrandOutput;
    readonly List<IMetricsConsumer> _consumers = new();
    HashSet<string> _recoveredConsumerNames = new();
    readonly HashSet<string> _registeredConsumerNames = new();
    Timer? _truncationTimer;
    int _truncationIntervalMs;

    /// <summary>
    /// Initializes a new <see cref="MetricsFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    public MetricsFeatureDriver( ApplicationIdentityService s )
        : base( s, isAllowedByDefault: true )
    {
    }

    /// <summary>
    /// Gets the FasterLog instance managed by this driver.
    /// This is null before the setup completes or if the setup fails.
    /// </summary>
    public FasterLog? FasterLog => _log;

    /// <summary>
    /// Gets the list of registered consumers.
    /// </summary>
    public IReadOnlyList<IMetricsConsumer> Consumers => _consumers;

    /// <summary>
    /// Registers a consumer with this driver.
    /// The consumer's name is tracked for orphan detection on shutdown.
    /// </summary>
    /// <param name="consumer">The consumer to register.</param>
    public void RegisterConsumer( IMetricsConsumer consumer )
    {
        Throw.CheckNotNullArgument( consumer );
        Throw.CheckState( _log != null, "FasterLog is not initialized." );

        _consumers.Add( consumer );
        _registeredConsumerNames.Add( consumer.Name );
    }

    /// <summary>
    /// Removes and disposes a consumer by name.
    /// </summary>
    /// <param name="name">The name of the consumer to remove.</param>
    public async Task RemoveConsumerAsync( string name )
    {
        var consumer = _consumers.FirstOrDefault( c => c.Name == name );
        if( consumer != null )
        {
            _consumers.Remove( consumer );
            await consumer.DisposeAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task<bool> SetupAsync( FeatureLifetimeContext context )
    {
        // Read configuration from the Local:Metrics section.
        var metricsSection = ApplicationIdentityService.LocalConfiguration.Configuration.GetSection( "Metrics" );
        if( !metricsSection.Exists() )
        {
            context.Monitor.Trace( "MetricsFeatureDriver: No Local:Metrics configuration found. Metrics not enabled." );
            return true;
        }

        // Parse configuration.
        var relativePath = metricsSection["Path"] ?? DefaultFasterLogPath;
        var basePath = ApplicationIdentityService.LocalFileStore.FolderPath;
        var resolvedPath = basePath.Combine( relativePath );

        int memoryPageCount = 2;
        if( int.TryParse( metricsSection["MemoryPageCount"], out var mpc ) )
            memoryPageCount = mpc;

        _truncationIntervalMs = DefaultTruncationIntervalMs;
        if( int.TryParse( metricsSection["TruncationIntervalMs"], out var tim ) )
            _truncationIntervalMs = tim;

        int handlerWaitTimeoutMs = DefaultHandlerWaitTimeoutMs;
        if( int.TryParse( metricsSection["HandlerWaitTimeoutMs"], out var hwt ) )
            handlerWaitTimeoutMs = hwt;

        // Create the FasterLog directory if needed.
        Directory.CreateDirectory( resolvedPath );

        // Create the FasterLog with recovery.
        _device = Devices.CreateLogDevice( Path.Combine( resolvedPath, "metrics.log" ), preallocateFile: false );
        _logSettings = new FasterLogSettings
        {
            LogDevice = _device,
            PageSizeBits = 22, // 4 MB pages
            MemorySizeBits = 22 + (int)Math.Ceiling( Math.Log2( memoryPageCount ) )
        };
        _log = new FasterLog( _logSettings );

        // Save recovered iterator names for orphan detection.
        _recoveredConsumerNames = _log.RecoveredIterators?.Keys.ToHashSet() ?? new HashSet<string>();
        if( _recoveredConsumerNames.Count > 0 )
        {
            context.Monitor.Info(
                $"MetricsFeatureDriver: Recovered {_recoveredConsumerNames.Count} consumer iterator(s): {string.Join( ", ", _recoveredConsumerNames )}." );
        }

        // Get target GrandOutput.
        _targetGrandOutput = GrandOutput.Default ?? GrandOutput.EnsureActiveDefault();

        // Send FasterLog to the handler via action (with retry if handler not yet configured).
        var action = new SetMetricsFasterLogAction( _log );
        _targetGrandOutput.Sink.Submit( action );
        await action.Completion;

        if( !action.HandlerFound )
        {
            // Handler may be configured later - retry with timeout.
            var start = DateTime.UtcNow;
            while( !action.HandlerFound && (DateTime.UtcNow - start).TotalMilliseconds < handlerWaitTimeoutMs )
            {
                await Task.Delay( 100 );
                action = new SetMetricsFasterLogAction( _log );
                _targetGrandOutput.Sink.Submit( action );
                await action.Completion;
            }

            if( !action.HandlerFound )
            {
                context.Monitor.Error( "MetricsFeatureDriver: MetricsLogHandler not found in GrandOutput. " +
                                       "Ensure CK-Monitoring:GrandOutput:Handlers includes MetricsLogHandlerConfiguration." );
                // Cleanup.
                _log.Dispose();
                _device.Dispose();
                _log = null;
                _device = null;
                return false;
            }
        }

        context.Monitor.Info( $"MetricsFeatureDriver: Configured FasterLog at '{resolvedPath}'." );

        // Start the truncation timer.
        if( _truncationIntervalMs > 0 )
        {
            _truncationTimer = new Timer( OnTruncationTimer, null, _truncationIntervalMs, _truncationIntervalMs );
        }

        return true;
    }

    void OnTruncationTimer( object? state )
    {
        if( _log == null ) return;

        try
        {
            var addresses = new List<long>();

            // Add active consumer addresses.
            foreach( var consumer in _consumers )
            {
                addresses.Add( consumer.CompletedUntilAddress );
            }

            // Add orphan addresses (recovered but not registered).
            var orphanNames = _recoveredConsumerNames.Except( _registeredConsumerNames );
            foreach( var orphanName in orphanNames )
            {
                if( _log.RecoveredIterators != null &&
                    _log.RecoveredIterators.TryGetValue( orphanName, out var orphanAddr ) )
                {
                    addresses.Add( orphanAddr );
                }
            }

            if( addresses.Count == 0 ) return;

            var minAddress = addresses.Min();
            if( minAddress <= _log.BeginAddress ) return;

            // Truncate up to that point (page-aligned).
            _log.TruncateUntilPageStart( minAddress );
            _log.Commit( true );
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( ActivityMonitor.Tags.ToBeInvestigated,
                "MetricsFeatureDriver: Error during truncation.", ex );
        }
    }

    /// <inheritdoc />
    protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        // No per-party setup needed for metrics.
        return Task.FromResult( true );
    }

    /// <inheritdoc />
    protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        // No per-party teardown needed.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync( FeatureLifetimeContext context )
    {
        if( _log == null ) return;

        // Stop the truncation timer.
        if( _truncationTimer != null )
        {
            await _truncationTimer.DisposeAsync();
            _truncationTimer = null;
        }

        // Dispose all registered consumers.
        foreach( var consumer in _consumers )
        {
            try
            {
                await consumer.DisposeAsync();
            }
            catch( Exception ex )
            {
                context.Monitor.Warn( $"Error disposing consumer '{consumer.Name}'.", ex );
            }
        }

        _consumers.Clear();

        // Find orphans: recovered but never registered during this run.
        var orphanedConsumers = _recoveredConsumerNames
            .Except( _registeredConsumerNames )
            .ToList();

        // Clear orphaned consumers by creating and disposing iterators.
        foreach( var orphanName in orphanedConsumers )
        {
            context.Monitor.Info( $"MetricsFeatureDriver: Removing orphaned consumer '{orphanName}'." );
            try
            {
                using var orphanIter = _log.Scan( 0, long.MaxValue, name: orphanName, recover: true );
                // Disposing removes from PersistedIterators.
            }
            catch( Exception ex )
            {
                context.Monitor.Warn( $"Error cleaning up orphan '{orphanName}'.", ex );
            }
        }

        // Clear FasterLog from the handler.
        if( _targetGrandOutput != null )
        {
            var clearAction = new ClearMetricsFasterLogAction();
            _targetGrandOutput.Sink.Submit( clearAction );
            await clearAction.Completion;

            if( !clearAction.HandlerFound )
            {
                context.Monitor.Warn( "MetricsFeatureDriver: MetricsLogHandler not found during teardown." );
            }
        }

        // Commit and dispose FasterLog.
        try
        {
            await _log.CommitAsync();
        }
        catch( Exception ex )
        {
            context.Monitor.Warn( "Error during final FasterLog commit.", ex );
        }

        _log.Dispose();
        _device?.Dispose();
        _log = null;
        _device = null;
        _targetGrandOutput = null;
    }
}
