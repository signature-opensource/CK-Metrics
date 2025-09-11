using CK.Monitoring.Handlers;
using FASTER.core;

namespace CK.Monitoring;

public abstract class MetricsConfigurationBase : IHandlerConfiguration
{
    /// <summary>
    /// Gets or sets the path given to <see cref="FasterLogSettings" />, used to store FasterLog segments.
    /// Defaults to ".fasterlog/metrics".
    /// </summary>
    /// <remarks>
    /// TODO: ApplicationIdentityService is in CK.AppIdentity which is not a dependency, and wouldn't be initialized early enough anyway - we cannot use ApplicationIdentityService.LocalFileStore.
    /// The path is resolved relative to the current directory
    /// and supports environment variable expansion (e.g. "%APPDATA%").
    /// </remarks>
    public string FasterLogPath { get; set; } = ".fasterlog/metrics";

    /// <summary>
    /// Gets or sets the number of FasterLog pages to keep in-memory.
    /// Defaults to 2.
    /// </summary>
    public int MemoryPageCount { get; set; } = 2;

    /// <summary>
    /// Gets or sets the rate of FasterLog Commits.
    /// This is a multiple of <see cref="GrandOutputConfiguration.TimerDuration" />,
    /// and defaults to 1 (which is every 500 ms, with the default
    /// <see cref="GrandOutputConfiguration.TimerDuration" /> of 500 ms).
    /// </summary>
    public int CommitRate { get; set; } = 1;

    /// <summary>
    /// Gets or sets the delay in milliseconds to wait before retrying a push of the same log entry batch.
    /// Defaults to 2000 (2 seconds).
    /// </summary>
    public int PushRetryDelayMs { get; set; } = 2000;

    /// <summary>
    /// The size threshold, in bytes, above which log entries should be pushed, for large entry batches,
    /// or when recovering from a persisted commit.
    /// Note that entries can be pushed below the threshold size.
    /// Defaults to 2^21: 2 097 152 bytes (2 MiB).
    /// </summary>
    public long LargePushThresholdSize { get; set; } = 2 << 21;

    /// <summary>
    /// Instantiates a new <see cref="MetricsConfigurationBase"/> with the default values.
    /// </summary>
    public MetricsConfigurationBase()
    {
    }

    /// <summary>
    /// Instantiates a new <see cref="MetricsConfigurationBase"/>, copying properties from another instance.
    /// </summary>
    public MetricsConfigurationBase( MetricsConfigurationBase source )
    {
        FasterLogPath = source.FasterLogPath;
        MemoryPageCount = source.MemoryPageCount;
        CommitRate = source.CommitRate;
        PushRetryDelayMs = source.PushRetryDelayMs;
        LargePushThresholdSize = source.LargePushThresholdSize;
    }

    /// <inheritdoc />
    public abstract IHandlerConfiguration Clone();
}
