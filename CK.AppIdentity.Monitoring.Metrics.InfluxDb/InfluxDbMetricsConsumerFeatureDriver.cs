using CK.AppIdentity;
using CK.Core;
using CK.Metrics;
using Microsoft.Extensions.Configuration;

namespace CK.AppIdentity.Monitoring.Metrics.InfluxDb;

/// <summary>
/// Feature driver that creates and manages an <see cref="InfluxDbMetricsConsumer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Configuration is read from the <c>Local:MetricsInfluxDb</c> section.
/// </para>
/// <code>
/// {
///   "CK-AppIdentity": {
///     "Local": {
///       "MetricsInfluxDb": {
///         "Name": "influxdb",
///         "ServerUrl": "https://influxdb.example.com:8086",
///         "Org": "my-org",
///         "Bucket": "metrics",
///         "Token": "my-api-token"
///       }
///     }
///   }
/// }
/// </code>
/// </remarks>
public sealed class InfluxDbMetricsConsumerFeatureDriver : ApplicationIdentityFeatureDriver
{
    /// <summary>
    /// Default consumer name.
    /// </summary>
    public const string DefaultConsumerName = "influxdb-export";

    readonly MetricsFeatureDriver _metricsDriver;
    InfluxDbMetricsConsumer? _consumer;

    /// <summary>
    /// Initializes a new <see cref="InfluxDbMetricsConsumerFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="metricsDriver">
    /// The metrics feature driver that owns the FasterLog.
    /// This dependency ensures correct initialization order.
    /// </param>
    public InfluxDbMetricsConsumerFeatureDriver( ApplicationIdentityService s, MetricsFeatureDriver metricsDriver )
        : base( s, isAllowedByDefault: true )
    {
        _metricsDriver = metricsDriver;
    }

    /// <inheritdoc />
    protected override async Task<bool> SetupAsync( FeatureLifetimeContext context )
    {
        using var _ = context.Monitor.TemporarilySetAutoTags( DotNetMetrics.MetricsInternalTag );

        // Check if MetricsFeatureDriver has a FasterLog
        if( _metricsDriver.FasterLog == null )
        {
            context.Monitor.Trace( "InfluxDbMetricsConsumerFeatureDriver: MetricsFeatureDriver has no FasterLog. InfluxDB consumer not started." );
            return true;
        }

        // Read configuration from Local:MetricsInfluxDb section
        var influxSection = ApplicationIdentityService.LocalConfiguration.Configuration.GetSection( "MetricsInfluxDb" );
        if( !influxSection.Exists() )
        {
            context.Monitor.Trace( "InfluxDbMetricsConsumerFeatureDriver: No Local:MetricsInfluxDb configuration found. InfluxDB consumer not started." );
            return true;
        }

        // Parse configuration
        var config = new InfluxDbConfiguration();

        config.ServerUrl = influxSection["ServerUrl"] ?? string.Empty;
        config.Org = influxSection["Org"] ?? string.Empty;
        config.Bucket = influxSection["Bucket"] ?? string.Empty;
        config.Token = influxSection["Token"];
        config.Username = influxSection["Username"];
        config.Password = influxSection["Password"];

        // Validate required fields
        if( string.IsNullOrWhiteSpace( config.ServerUrl ) )
        {
            context.Monitor.Error( "InfluxDbMetricsConsumerFeatureDriver: ServerUrl is required." );
            return false;
        }
        if( string.IsNullOrWhiteSpace( config.Org ) )
        {
            context.Monitor.Error( "InfluxDbMetricsConsumerFeatureDriver: Org is required." );
            return false;
        }
        if( string.IsNullOrWhiteSpace( config.Bucket ) )
        {
            context.Monitor.Error( "InfluxDbMetricsConsumerFeatureDriver: Bucket is required." );
            return false;
        }

        // Parse optional settings
        if( bool.TryParse( influxSection["UseGzip"], out var useGzip ) )
            config.UseGzip = useGzip;

        if( int.TryParse( influxSection["FlushIntervalMs"], out var flushIntervalMs ) )
            config.FlushIntervalMs = flushIntervalMs;

        if( int.TryParse( influxSection["RetryDelayMs"], out var retryDelayMs ) )
            config.RetryDelayMs = retryDelayMs;

        if( long.TryParse( influxSection["BatchThresholdBytes"], out var batchThresholdBytes ) )
            config.BatchThresholdBytes = batchThresholdBytes;

        // Parse optional static tags (with environment variable expansion)
        var tagsSection = influxSection.GetSection( "Tags" );
        if( tagsSection.Exists() )
        {
            config.Tags = tagsSection.GetChildren()
                .ToDictionary(
                    x => x.Key,
                    x => Environment.ExpandEnvironmentVariables( x.Value ?? string.Empty ) );
        }

        // Get consumer name
        var name = influxSection["Name"] ?? DefaultConsumerName;

        // Get AppIdentity context
        var domain = ApplicationIdentityService.DomainName;
        var environment = ApplicationIdentityService.EnvironmentName;
        var party = ApplicationIdentityService.PartyName;

        // Create the consumer
        _consumer = new InfluxDbMetricsConsumer(
            _metricsDriver.FasterLog,
            name,
            config,
            domain,
            environment,
            party );

        // Register with the MetricsFeatureDriver
        _metricsDriver.RegisterConsumer( _consumer );

        // Start the consumer's processing loop
        await _consumer.StartAsync( context.Monitor, CancellationToken.None );

        context.Monitor.Info( $"InfluxDbMetricsConsumerFeatureDriver: Started InfluxDB consumer '{name}' writing to '{config.ServerUrl}'." );

        return true;
    }

    /// <inheritdoc />
    protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        return Task.FromResult( true );
    }

    /// <inheritdoc />
    protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync( FeatureLifetimeContext context )
    {
        using var _ = context.Monitor.TemporarilySetAutoTags( DotNetMetrics.MetricsInternalTag );

        if( _consumer != null )
        {
            await _metricsDriver.RemoveConsumerAsync( _consumer.Name );
            _consumer = null;
        }
    }
}
