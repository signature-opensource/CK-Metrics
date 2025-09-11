using System.Buffers;
using CK.Core;
using FASTER.core;

namespace CK.Monitoring.Metrics;

/// <summary>
///     Background processor for log entries enqueued inside a <see cref="FasterLog" /> instance.
///     Belongs to a parent <see cref="MetricsHandlerBase" />.
/// </summary>
internal class FasterLogProcessor
{
    readonly CancellationToken _deactivateToken;
    readonly FASTER.core.FasterLog _fasterLog;
    readonly long _largePushThresholdSize;
    readonly MetricsHandlerBase _parentHandler;
    readonly int _pushRetryDelayMs;

    /// <summary>
    ///     Represents a background processor responsible for handling log entries
    ///     enqueued within a specified <see cref="FasterLog" /> instance
    ///     This processor operates as part of a parent <see cref="MetricsHandlerBase" /> handler.
    /// </summary>
    /// <param name="parentHandler">The parent handler.</param>
    /// <param name="fasterLog">The FasterLog instance to iterate on.</param>
    /// <param name="pushRetryDelayMs">Delay in milliseconds to wait before retrying a push of the same log entry batch.</param>
    /// <param name="deactivateToken">The token called just before destroying the FasterLog instance.</param>
    /// <param name="largePushThresholdSize">
    ///     Size threshold, in bytes, above which log entries should be pushed, for large
    ///     entry batches
    /// </param>
    public FasterLogProcessor(
        MetricsHandlerBase parentHandler,
        FASTER.core.FasterLog fasterLog,
        long largePushThresholdSize,
        int pushRetryDelayMs,
        CancellationToken deactivateToken
    )
    {
        _parentHandler = parentHandler;
        _fasterLog = fasterLog;
        _largePushThresholdSize = largePushThresholdSize;
        _pushRetryDelayMs = pushRetryDelayMs;
        _deactivateToken = deactivateToken;
    }

    public async Task RunAsync()
    {
        if( _deactivateToken.IsCancellationRequested ) return;

        using var iter = _fasterLog.Scan( 0, long.MaxValue );
        var memoryPool = MemoryPool<byte>.Shared;
        var lengthThreshold = _largePushThresholdSize;

        List<(IMemoryOwner<byte>, int)> bufferList = new();
        var totalLength = 0;
        long lastAddress = 0;

        while( !_deactivateToken.IsCancellationRequested )
        {
            while( iter.GetNext( memoryPool, out var entry, out var entryLength, out var currentAddress ) )
            {
                bufferList.Add( (entry, entryLength) );
                totalLength += entryLength;
                lastAddress = currentAddress;

                if( totalLength >= lengthThreshold )
                    break;
            }

            if( bufferList.Count == 0 )
            {
                if( !await iter.WaitAsync( _deactivateToken ) )
                    // WaitAsync returned false: log has been shutdown / iterator has reached endAddress
                    break;
            }
            else
            {
                // In case we stop in the middle, success will be False here.
                var (success, throttleTime) = await ProcessBufferAsync( bufferList, _deactivateToken );

                if( success )
                {
                    iter.CompleteUntil( lastAddress );
                    _fasterLog.TruncateUntilPageStart( lastAddress );
                    await _fasterLog.CommitAsync( _deactivateToken ); // Commit the buffer

                    totalLength = 0; // Reset, since the buffer was cleared
                    lastAddress = 0; // Reset, since the buffer was cleared

                    if( throttleTime > TimeSpan.Zero )
                        await Task.Delay( throttleTime, _deactivateToken );
                }
                // Don't Commit/Advance on failure, because it means we're shutting down.
            }
        }
    }


    async Task<(bool, TimeSpan)> ProcessBufferAsync( List<(IMemoryOwner<byte>, int)> entries,
        CancellationToken stopToken )
    {
        var ts = TimeSpan.Zero;
        var success = false;
        while( !stopToken.IsCancellationRequested && !success )
            try
            {
                ts = await _parentHandler.ProcessEntriesAsync( entries.Select( e =>
                    (ReadOnlyMemory<byte>) e.Item1.Memory.Slice( 0, e.Item2 ) ) );
                success = true;
            }
            catch( Exception ex )
            {
                ActivityMonitor.StaticLogger.Error(
                    ActivityMonitor.Tags.SecurityCritical,
                    $"Error while processing a log entry batch in {_parentHandler.GetType().Name}. Will retry in {_pushRetryDelayMs} ms.",
                    ex
                );
                success = false;
                await Task.Delay( _pushRetryDelayMs, stopToken ); // Wait before retry
            }

        foreach( var entry in entries ) entry.Item1.Dispose();

        entries.Clear();
        return (success, ts);
    }
}
