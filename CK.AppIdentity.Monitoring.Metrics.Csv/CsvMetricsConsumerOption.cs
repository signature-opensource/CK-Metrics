using CK.Core;
using FASTER.core;

namespace CK.AppIdentity.Monitoring.Metrics.Csv;

/// <summary>
/// The "Csv" option of the "Metrics" feature: a <see cref="CsvMetricsConsumer"/> exports the metrics in a CSV file.
/// <code>
/// "Metrics": {
///   "Path": "FasterLog/Metrics",
///   "Csv": { "Name": "csv-export", "Path": "Exports/metrics.csv", "RetryDelayMs": 2000, "BatchThresholdBytes": 4194304 }
/// }
/// </code>
/// This is the reference of a consumer: an option of the "Metrics" feature (<see cref="IMetricsConsumerOption"/>) with
/// its configuration model. The <see cref="MetricsFeatureDriver"/> creates its consumer once the FasterLog is ready,
/// registers it (the truncation of the log follows the slowest consumer, an unregistered iterator is an orphan) and
/// starts it; its teardown disposes it.
/// </summary>
[PartyFeatureOption( "Csv", TypeActivation.Optional )]
[Configuration<CsvMetricsConsumerConfiguration>]
public sealed class CsvMetricsConsumerOption : IMetricsConsumerOption
{
    /// <summary>
    /// Initializes a new <see cref="CsvMetricsConsumerOption"/>.
    /// </summary>
    /// <param name="configuration">Its configuration.</param>
    public CsvMetricsConsumerOption( CsvMetricsConsumerConfiguration configuration )
    {
        Configuration = configuration;
    }

    /// <summary>
    /// Gets the configuration of the consumer.
    /// </summary>
    public CsvMetricsConsumerConfiguration Configuration { get; }

    /// <inheritdoc />
    public IMetricsConsumer CreateConsumer( IActivityMonitor monitor, FasterLog log, ApplicationIdentityService party )
    {
        var path = Path.Combine( party.LocalFileStore.FolderPath, Configuration.Path );
        monitor.Info( $"The CSV consumer '{Configuration.Name}' writes to '{path}'." );
        return new CsvMetricsConsumer( log, Configuration.Name, path, Configuration.RetryDelayMs, Configuration.BatchThresholdBytes );
    }
}

/// <summary>
/// The configuration of the "Csv" option of the "Metrics" feature (<see cref="CsvMetricsConsumerOption"/>).
/// </summary>
[ConfigurationModel]
public sealed class CsvMetricsConsumerConfiguration : IConfigurationModel
{
    /// <summary>
    /// The default name of the consumer.
    /// </summary>
    public const string DefaultConsumerName = "csv-export";

    /// <summary>
    /// The default path of the CSV file, relative to the local file store of the party.
    /// </summary>
    public const string DefaultCsvPath = "Exports/metrics.csv";

    /// <summary>
    /// Gets the name of the consumer: the name of its FasterLog iterator, unique among the consumers, at most 20
    /// characters, stable across restarts (its position is recovered by its name).
    /// </summary>
    public string Name { get; init; } = DefaultConsumerName;

    /// <summary>
    /// Gets the path of the CSV file, relative to the local file store of the party.
    /// </summary>
    public string Path { get; init; } = DefaultCsvPath;

    /// <summary>
    /// Gets how long the consumer waits after a processing failure, in milliseconds.
    /// </summary>
    public int RetryDelayMs { get; init; } = 2000;

    /// <summary>
    /// Gets how many bytes the consumer collects before processing them (4 MiB by default).
    /// </summary>
    public long BatchThresholdBytes { get; init; } = 2 << 21;

    /// <inheritdoc />
    public void Validate( IConfigurationValidationContext context )
    {
        if( string.IsNullOrWhiteSpace( Name ) || Name.Length > 20 ) context.Error( $"'{context.Path}:Name' is '{Name}': a name of at most 20 characters is expected." );
        if( string.IsNullOrWhiteSpace( Path ) ) context.Error( $"'{context.Path}:Path' must be a relative path." );
        if( RetryDelayMs < 0 ) context.Error( $"'{context.Path}:RetryDelayMs' is {RetryDelayMs}: 0 or a positive integer is expected." );
        if( BatchThresholdBytes <= 0 ) context.Error( $"'{context.Path}:BatchThresholdBytes' is {BatchThresholdBytes}: a positive integer is expected." );
    }
}
