// ReSharper disable once CheckNamespace
// Namespace is fixed to "CK.Monitoring" for all GrandOutputHandlers and their configuration classes.
namespace CK.Monitoring;

/// <summary>
/// Configuration for <see cref="MetricsLogHandler"/>.
/// This configuration is serializable - it does not contain the FasterLog instance.
/// The FasterLog is injected at runtime via e.g., SetMetricsFasterLogAction in CK.AppIdentity.Monitoring.Metrics.
/// </summary>
public sealed class MetricsLogHandlerConfiguration : IHandlerConfiguration
{
    /// <summary>
    /// Gets or sets the rate of FasterLog Commits.
    /// This is a multiple of <see cref="GrandOutputConfiguration.TimerDuration"/>,
    /// and defaults to 1 (which is every 500 ms, with the default
    /// <see cref="GrandOutputConfiguration.TimerDuration"/> of 500 ms).
    /// </summary>
    // ReSharper disable once PropertyCanBeMadeInitOnly.Global
    public int CommitRate { get; set; } = 1;

    /// <inheritdoc />
    public IHandlerConfiguration Clone() => new MetricsLogHandlerConfiguration
    {
        CommitRate = CommitRate
    };
}
