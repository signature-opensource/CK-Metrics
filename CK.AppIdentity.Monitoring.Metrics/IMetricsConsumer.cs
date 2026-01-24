using CK.Core;

namespace CK.AppIdentity.Monitoring.Metrics;

/// <summary>
/// Interface for a metrics consumer that reads from a shared FasterLog.
/// Each consumer uses a named iterator for independent progress tracking.
/// </summary>
public interface IMetricsConsumer : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique name for this consumer.
    /// This is used as the FasterLog named iterator name (max 20 characters).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the current completed until address (progress).
    /// This is the address up to which entries have been successfully processed and committed.
    /// </summary>
    long CompletedUntilAddress { get; }

    /// <summary>
    /// Starts the consumer's background processing loop.
    /// </summary>
    /// <param name="monitor">The monitor to use for logging.</param>
    /// <param name="ct">Cancellation token to stop the consumer.</param>
    /// <returns>A task that completes when the consumer has started.</returns>
    Task StartAsync( IActivityMonitor monitor, CancellationToken ct );
}
