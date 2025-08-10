using System.Text;
using CK.Core;
using CK.Metrics;
using CK.Monitoring.Metrics;
using FASTER.core;

namespace CK.Monitoring;

/// <summary>
/// Base class for a <see cref="IGrandOutputHandler"/> that forwards metrics to an embedded
/// <see cref="FasterLog"/> instance.
/// </summary>
public abstract class MetricsHandlerBase : IGrandOutputHandler
{
    readonly IdentityCard _identity;
    MetricsConfigurationBase _configuration;
    int _countCommit;
    FasterLog? _log;
    FasterLogSettings? _logSettings;
    byte[] _buffer;
    CancellationTokenSource? _stopTokenSource;
    Task? _processorTask;
    FasterLogProcessor? _processor;

    protected MetricsConfigurationBase Configuration => _configuration;

    public MetricsHandlerBase( MetricsConfigurationBase configuration, IdentityCard identity )
    {
        _configuration = configuration;
        _identity = identity;
        _countCommit = _configuration.CommitRate;
        // FasterLog Setup is handled in Activate and Reconfigure.
        _buffer = new byte[64]; // May grow later.
    }

    /// <summary>
    /// Attempts to apply configuration if possible.
    /// If the FasterLog path has changed,
    /// the current FasterLog instance is destroyed and a new one is initialized.
    /// The handler must check the type of the given configuration and any key configuration
    /// before accepting it and reconfigures it (in this case, true must be returned).
    /// If this handler considers that this new configuration does not apply to itself, it must return false.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="c">Configuration to apply.</param>
    /// <returns>True if the configuration applied.</returns>
    public virtual async ValueTask<bool> ApplyConfigurationAsync( IActivityMonitor monitor, IHandlerConfiguration c )
    {
        if( c is not MetricsConfigurationBase newConfig ) return false;

        if(
            newConfig.FasterLogPath != _configuration.FasterLogPath
            || newConfig.LargePushThresholdSize != _configuration.LargePushThresholdSize
            || newConfig.PushRetryDelayMs != _configuration.PushRetryDelayMs
            || newConfig.MemoryPageCount != _configuration.MemoryPageCount
        )
        {
            await DestroyFasterLogAsync( monitor );
            InitializeFasterLog( newConfig );
        }

        _configuration = newConfig;
        if( _countCommit > _configuration.CommitRate ) _countCommit = _configuration.CommitRate;

        return true;
    }

    /// <summary>
    /// Prepares the handler to receive events by initializing the recycled stream, writer, and FasterLog instance.
    /// This is called before any event will be received.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error (this handler will not be added).</returns>
    public virtual ValueTask<bool> ActivateAsync( IActivityMonitor monitor )
    {
        InitializeFasterLog( _configuration );
        return ValueTask.FromResult( true );
    }

    /// <summary>
    /// Closes this handler by disposing of the FasterLog instance, recycled writer, and recycled stream.
    /// Ensures all pending data is committed before disposal.
    /// This is called after the handler has been removed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    public virtual async ValueTask DeactivateAsync( IActivityMonitor monitor )
    {
        await DestroyFasterLogAsync( monitor );
    }

    /// <summary>
    /// Handles a log event by writing it to the recycled stream and enqueueing it to the FasterLog.
    /// Entries tagged with "SecurityCritical" are skipped for security reasons.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="logEvent">The log event.</param>
    public ValueTask HandleAsync( IActivityMonitor monitor, InputLogEntry logEvent )
    {
        Throw.DebugAssert( _log != null );

        // Skip entries with "SecurityCritical", no matter what
        if( logEvent.Tags.Overlaps( ActivityMonitor.Tags.SecurityCritical ) )
            return ValueTask.CompletedTask;

        // While CK-Metrics writes text as pinky-promise ASCII, there is absolutely nothing
        // preventing anybody else from just sending whatever string they want
        // on the StaticLogger with the Metrics tag.
        if( logEvent.MonitorId == ActivityMonitor.StaticLogMonitorUniqueId
            && logEvent.Tags.Overlaps( DotNetMetrics.MetricsTag )
            && logEvent.Text is not null
            && Ascii.IsValid( logEvent.Text.AsSpan() )
          )
        {
            DateTime dt = logEvent.LogTime.TimeUtc;
            string text = logEvent.Text;

            // DateTime is 8 bytes, Text is 1 byte per character (ASCII).
            int requiredSize = sizeof(long) + text.Length;

            if( requiredSize > (1 << 16) - sizeof(long) )
            {
                monitor.Warn( $"Metrics entry too large: {text}" );
                return ValueTask.CompletedTask;
            }

            while( _buffer.Length < requiredSize )
            {
                // Grow buffer.
                Array.Resize( ref _buffer, _buffer.Length * 2 );
            }

            var span = _buffer.AsSpan();

            // Write DateTime (8 bytes)
            BitConverter.TryWriteBytes( span, dt.ToBinary() );
            // Write Text (1 byte per character)
            Encoding.ASCII.GetBytes( text, span[sizeof(long)..] );

            // Ship it to FasterLog.
            _log.Enqueue( span[..requiredSize] );
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Called regularly to commit the FasterLog when the commit count reaches zero.
    /// Enables this handler to do any required housekeeping.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="timerSpan">Indicative timer duration.</param>
    public virtual async ValueTask OnTimerAsync( IActivityMonitor monitor, TimeSpan timerSpan )
    {
        Throw.DebugAssert( _log != null );
        if( --_countCommit == 0 )
        {
            await _log.CommitAsync();
            _countCommit = _configuration.CommitRate;
        }
    }

    /// <summary>
    /// Initializes a new FasterLog instance using the configured path
    /// and launches the processor in a background task.
    /// The path is resolved relative to the current directory and supports environment variable expansion.
    /// </summary>
    void InitializeFasterLog( MetricsConfigurationBase newConfig )
    {
        Throw.DebugAssert( _logSettings is null );
        Throw.DebugAssert( _log is null );

        var path = Path.Combine( Environment.CurrentDirectory,
            Environment.ExpandEnvironmentVariables( newConfig.FasterLogPath ) );
        _logSettings = new FasterLogSettings( path );
        // Hard-code page size at 64 KiB
        _logSettings.PageSize = 1 << 16;
        // 128 KiB with a MemoryPageCount of 2.
        _logSettings.MemorySize = _logSettings.PageSize * newConfig.MemoryPageCount;
        // 16 MiB with a MemoryPageCount of 2.
        _logSettings.SegmentSize = _logSettings.MemorySize * 128;
        _log = new FasterLog( _logSettings );
        _stopTokenSource = new CancellationTokenSource();
        _processor = new FasterLogProcessor( this, _log, newConfig.LargePushThresholdSize,
            newConfig.PushRetryDelayMs, _stopTokenSource.Token );
        _processorTask = Task.Run( () => _processor.RunAsync() );
    }

    /// <summary>
    /// Destroys the current FasterLog instance by committing any pending data and disposing of resources
    /// and waits for the background processor task to return.
    /// </summary>
    /// <returns>A <see cref="Task" /> that represents the asynchronous destroy operation.</returns>
    async Task DestroyFasterLogAsync( IActivityMonitor monitor )
    {
        Throw.DebugAssert( _processorTask != null );
        Throw.DebugAssert( _processor != null );
        Throw.DebugAssert( _stopTokenSource != null );
        Throw.DebugAssert( _logSettings != null );
        Throw.DebugAssert( _log != null );

        await _log.CommitAsync();
        await _stopTokenSource.CancelAsync();
        try
        {
            await _processorTask;
        }
        catch( Exception exception )
        {
            monitor.Warn( "Ignoring Exception while destroying FasterLog.", exception );
        }

        _processorTask = null;
        _processor = null;
        _stopTokenSource.Dispose();
        _stopTokenSource = null;
        _log.Dispose();
        _log = null;
        _logSettings.Dispose();
        _logSettings = null;
    }

    /// <summary>
    /// Process a batch of metrics entries, serialized to binary.
    /// Be careful: all given entries will be released once returning successfully from this method.
    /// </summary>
    /// <remarks>
    /// <para>This method will be called repeatedly with the same metrics entries in case it throws.</para>
    /// <para>Format: 1 int64 (datetime), then ASCII characters.</para>
    /// </remarks>
    /// <param name="entries">The entries to process</param>
    /// <returns>A TimeSpan containing the time to wait before the next batch if you need to throttle your processing.</returns>
    public abstract Task<TimeSpan> ProcessEntriesAsync( IEnumerable<ReadOnlyMemory<byte>> entries );
}
