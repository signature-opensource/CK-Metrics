using System.Buffers;
using CK.Core;
using FASTER.core;

namespace CK.AppIdentity.Monitoring.Metrics;

/// <summary>
/// Base class for metrics consumers with retry-on-failure semantics.
/// Uses FasterLog named iterators for persisted progress tracking.
/// </summary>
public abstract class MetricsConsumerBase : IMetricsConsumer
{
    readonly FasterLog _log;
    readonly string _name;
    readonly int _retryDelayMs;
    readonly long _batchThresholdBytes;
    readonly int _maxBatchAgeMs;
    readonly int _gracefulShutdownTimeoutMs;

    FasterLogScanIterator? _iterator;
    Task? _processorTask;
    CancellationTokenSource? _stopTokenSource;

    /// <summary>
    /// Initializes a new <see cref="MetricsConsumerBase"/>.
    /// </summary>
    /// <param name="log">The FasterLog instance to consume from.</param>
    /// <param name="name">The unique name for this consumer (max 20 characters).</param>
    /// <param name="retryDelayMs">Delay in milliseconds before retrying on failure. Defaults to 2000.</param>
    /// <param name="batchThresholdBytes">Size threshold in bytes for batching entries. Defaults to 2 MiB.</param>
    /// <param name="maxBatchAgeMs">Maximum age in milliseconds for a batch before it is sent. Defaults to 60000 (1 minute). Set to 0 for immediate sending.</param>
    /// <param name="gracefulShutdownTimeoutMs">Timeout in milliseconds for graceful shutdown flush. Defaults to 5000 (5 seconds). Set to 0 to skip graceful flush.</param>
    protected MetricsConsumerBase(
        FasterLog log,
        string name,
        int retryDelayMs = 2000,
        long batchThresholdBytes = 2 << 21,
        int maxBatchAgeMs = 60000,
        int gracefulShutdownTimeoutMs = 5000 )
    {
        Throw.CheckNotNullArgument( log );
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        Throw.CheckArgument( name.Length <= 20, "Consumer name must be 20 characters or less." );

        _log = log;
        _name = name;
        _retryDelayMs = retryDelayMs;
        _batchThresholdBytes = batchThresholdBytes;
        _maxBatchAgeMs = maxBatchAgeMs;
        _gracefulShutdownTimeoutMs = gracefulShutdownTimeoutMs;
    }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public long CompletedUntilAddress => _iterator?.CompletedUntilAddress ?? 0;

    /// <summary>
    /// Gets the FasterLog instance this consumer reads from.
    /// </summary>
    protected FasterLog Log => _log;

    /// <inheritdoc />
    public Task StartAsync( IActivityMonitor monitor, CancellationToken ct )
    {
        Throw.CheckState( _iterator == null, "Consumer is already started." );

        // Create a named iterator - automatically recovers position from the persisted state.
        _iterator = _log.Scan( 0, long.MaxValue, name: _name, recover: true );
        _stopTokenSource = CancellationTokenSource.CreateLinkedTokenSource( ct );
        _processorTask = Task.Run( () => RunAsync( monitor ), ct );

        return Task.CompletedTask;
    }

    async Task RunAsync( IActivityMonitor _ )
    {
        Throw.DebugAssert( _iterator != null );
        Throw.DebugAssert( _stopTokenSource != null );

        var ct = _stopTokenSource.Token;
        var memoryPool = MemoryPool<byte>.Shared;

        List<(IMemoryOwner<byte> Memory, int Length, long Address)> bufferList = new();
        long totalLength = 0;
        long lastAddress = 0;
        DateTime? batchStartTime = null;

        while( !ct.IsCancellationRequested )
        {
            // Collect entries up to a batch threshold.
            bool thresholdReached = false;
            while( _iterator.GetNext( memoryPool, out var entry, out var entryLength, out var currentAddress ) )
            {
                if( bufferList.Count == 0 )
                    batchStartTime = DateTime.UtcNow;

                bufferList.Add( (entry, entryLength, currentAddress) );
                totalLength += entryLength;
                lastAddress = currentAddress;

                if( totalLength >= _batchThresholdBytes )
                {
                    thresholdReached = true;
                    break;
                }
            }

            if( bufferList.Count == 0 )
            {
                // Wait for new entries.
                try
                {
                    if( !await _iterator.WaitAsync( ct ) )
                        break; // Log shutdown or end reached.
                }
                catch( OperationCanceledException )
                {
                    break;
                }
                continue;
            }

            // If batch is not full and maxBatchAge is configured, wait for more entries.
            if( !thresholdReached && _maxBatchAgeMs > 0 && batchStartTime.HasValue )
            {
                var elapsed = DateTime.UtcNow - batchStartTime.Value;
                var remaining = TimeSpan.FromMilliseconds( _maxBatchAgeMs ) - elapsed;

                if( remaining > TimeSpan.Zero )
                {
                    try
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource( ct );
                        timeoutCts.CancelAfter( remaining );

                        if( await _iterator.WaitAsync( timeoutCts.Token ) )
                            continue; // New entries available, collect them.
                    }
                    catch( OperationCanceledException ) when( !ct.IsCancellationRequested )
                    {
                        // Timeout - proceed to process the batch.
                    }
                    catch( OperationCanceledException )
                    {
                        // Actual cancellation requested.
                        break;
                    }
                }
            }

            // Process batch with retry-on-failure.
            var (success, throttleTime) = await ProcessBufferWithRetryAsync( bufferList, ct );

            if( success )
            {
                // Commit progress.
                _iterator.CompleteUntil( lastAddress );
                await _log.CommitAsync( ct );

                // Reset for next batch.
                totalLength = 0;
                lastAddress = 0;
                batchStartTime = null;

                if( throttleTime > TimeSpan.Zero )
                {
                    try
                    {
                        await Task.Delay( throttleTime, ct );
                    }
                    catch( OperationCanceledException )
                    {
                        break;
                    }
                }
            }
            // On failure (cancellation), don't commit - entries will be retried on restart.
        }

        // Graceful shutdown: flush any pending entries.
        if( bufferList.Count > 0 && _gracefulShutdownTimeoutMs > 0 )
        {
            try
            {
                using var gracefulCts = new CancellationTokenSource( _gracefulShutdownTimeoutMs );
                var (success, _) = await ProcessBufferWithRetryAsync( bufferList, gracefulCts.Token );

                if( success )
                {
                    _iterator.CompleteUntil( lastAddress );
                    await _log.CommitAsync( gracefulCts.Token );
                    ActivityMonitor.StaticLogger.Info(
                        $"Consumer '{_name}' flushed pending entries during graceful shutdown." );
                }
            }
            catch( OperationCanceledException )
            {
                // Graceful shutdown timeout - entries will be processed on next startup.
                ActivityMonitor.StaticLogger.Warn(
                    ActivityMonitor.Tags.ToBeInvestigated,
                    $"Graceful shutdown timeout ({_gracefulShutdownTimeoutMs}ms) reached for consumer '{_name}'. " +
                    "Pending entries will be retried on next startup." );
            }
            catch( Exception ex )
            {
                ActivityMonitor.StaticLogger.Error(
                    ActivityMonitor.Tags.ToBeInvestigated,
                    $"Error during graceful shutdown flush for consumer '{_name}'.",
                    ex );
            }
        }
    }

    async Task<(bool Success, TimeSpan ThrottleTime)> ProcessBufferWithRetryAsync(
        List<(IMemoryOwner<byte> Memory, int Length, long Address)> entries,
        CancellationToken ct )
    {
        var throttleTime = TimeSpan.Zero;
        var success = false;

        while( !ct.IsCancellationRequested && !success )
        {
            try
            {
                throttleTime = await ProcessEntriesAsync(
                    entries.Select( e => (ReadOnlyMemory<byte>)e.Memory.Memory[..e.Length] ) );
                success = true;
            }
            catch( OperationCanceledException )
            {
                throw;
            }
            catch( Exception ex )
            {
                ActivityMonitor.StaticLogger.Error(
                    ActivityMonitor.Tags.SecurityCritical,
                    $"Error processing metrics batch in consumer '{_name}'. Will retry in {_retryDelayMs} ms.",
                    ex );

                try
                {
                    await Task.Delay( _retryDelayMs, ct );
                }
                catch( OperationCanceledException )
                {
                    break;
                }
            }
        }

        // Release memory.
        foreach( var entry in entries )
            entry.Memory.Dispose();
        entries.Clear();

        return (success, throttleTime);
    }

    /// <summary>
    /// Processes a batch of metrics entries.
    /// Implementations should throw an exception to trigger retry behavior.
    /// </summary>
    /// <param name="entries">The batch of entries to process. Each entry contains DateTime (8 bytes) + ASCII text.</param>
    /// <returns>
    /// A TimeSpan indicating how long to wait before processing the next batch.
    /// Return <see cref="TimeSpan.Zero"/> for no throttling.
    /// </returns>
    protected abstract Task<TimeSpan> ProcessEntriesAsync( IEnumerable<ReadOnlyMemory<byte>> entries );

    /// <inheritdoc />
    public virtual async ValueTask DisposeAsync()
    {
        if( _stopTokenSource != null )
        {
            await _stopTokenSource.CancelAsync();

            if( _processorTask != null )
            {
                try
                {
                    // Wait for the processor task which includes graceful shutdown logic.
                    await _processorTask;
                }
                catch( OperationCanceledException )
                {
                    // Expected.
                }
                catch( Exception )
                {
                    // Ignore errors during shutdown.
                }
            }

            _stopTokenSource.Dispose();
            _stopTokenSource = null;
        }

        // Disposing the iterator removes it from FasterLog.PersistedIterators.
        _iterator?.Dispose();
        _iterator = null;
    }
}
