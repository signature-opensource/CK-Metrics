using CK.Core;

namespace CK.AppIdentity.Monitoring.Metrics;

/// <summary>
/// The configuration of the "Metrics" feature of the root party (<see cref="MetricsFeatureDriver"/>): its FasterLog.
/// <code>
/// "Metrics": { "Path": "FasterLog/Metrics", "MemoryPageCount": 2, "TruncationIntervalMs": 60000, "HandlerWaitTimeoutMs": 30000 }
/// </code>
/// Its consumers are its options ("Csv", "InfluxDb"...: see <see cref="IMetricsConsumerOption"/>).
/// </summary>
[ConfigurationModel]
public sealed class MetricsConfiguration : IConfigurationModel
{
    /// <summary>
    /// Gets the path of the FasterLog, relative to the local file store of the party.
    /// </summary>
    public string Path { get; init; } = MetricsFeatureDriver.DefaultFasterLogPath;

    /// <summary>
    /// Gets the number of 4 MB pages of the FasterLog in memory.
    /// </summary>
    public int MemoryPageCount { get; init; } = 2;

    /// <summary>
    /// Gets the interval of the truncation of the log, in milliseconds (0 disables it).
    /// </summary>
    public int TruncationIntervalMs { get; init; } = MetricsFeatureDriver.DefaultTruncationIntervalMs;

    /// <summary>
    /// Gets how long the setup waits for the <c>MetricsLogHandler</c> of the GrandOutput, in milliseconds.
    /// </summary>
    public int HandlerWaitTimeoutMs { get; init; } = MetricsFeatureDriver.DefaultHandlerWaitTimeoutMs;

    /// <inheritdoc />
    public void Validate( IConfigurationValidationContext context )
    {
        if( string.IsNullOrWhiteSpace( Path ) ) context.Error( $"'{context.Path}:Path' must be a relative path." );
        if( MemoryPageCount <= 0 ) context.Error( $"'{context.Path}:MemoryPageCount' is {MemoryPageCount}: a positive integer is expected." );
        if( TruncationIntervalMs < 0 ) context.Error( $"'{context.Path}:TruncationIntervalMs' is {TruncationIntervalMs}: 0 or a positive integer is expected." );
        if( HandlerWaitTimeoutMs < 0 ) context.Error( $"'{context.Path}:HandlerWaitTimeoutMs' is {HandlerWaitTimeoutMs}: 0 or a positive integer is expected." );
    }
}
