using CK.Core;
using FASTER.core;

namespace CK.AppIdentity.Monitoring.Metrics.InfluxDb;

/// <summary>
/// The "InfluxDb" option of the "Metrics" feature: an <see cref="InfluxDbMetricsConsumer"/> writes the metrics to an
/// InfluxDB server (its model: <see cref="InfluxDbConfiguration"/>).
/// <code>
/// "Metrics": {
///   "InfluxDb": {
///     "ServerUrl": "https://influxdb.example.com:8086",
///     "Org": "my-org",
///     "Bucket": "metrics",
///     "Token": { "$SECRET": "InfluxDbToken" },
///     "Tags": { "host": "%COMPUTERNAME%" }
///   }
/// }
/// </code>
/// The values of the <see cref="InfluxDbConfiguration.Tags"/> are expanded (<see cref="Environment.ExpandEnvironmentVariables(string)"/>);
/// the measurements are tagged with the domain, the environment and the party name of the root party.
/// </summary>
[PartyFeatureOption( "InfluxDb", TypeActivation.Optional )]
[Configuration<InfluxDbConfiguration>]
public sealed class InfluxDbMetricsConsumerOption : IMetricsConsumerOption
{
    /// <summary>
    /// Initializes a new <see cref="InfluxDbMetricsConsumerOption"/>.
    /// </summary>
    /// <param name="configuration">Its configuration.</param>
    public InfluxDbMetricsConsumerOption( InfluxDbConfiguration configuration )
    {
        Configuration = configuration;
    }

    /// <summary>
    /// Gets the configuration of the consumer.
    /// </summary>
    public InfluxDbConfiguration Configuration { get; }

    /// <inheritdoc />
    public IMetricsConsumer CreateConsumer( IActivityMonitor monitor, FasterLog log, ApplicationIdentityService party )
    {
        var c = Configuration;
        if( c.Tags != null )
        {
            c.Tags = c.Tags.ToDictionary( kv => kv.Key, kv => Environment.ExpandEnvironmentVariables( kv.Value ?? string.Empty ) );
        }
        monitor.Info( $"The InfluxDB consumer '{c.Name}' writes to '{c.ServerUrl}'." );
        return new InfluxDbMetricsConsumer( log, c.Name, c, party.DomainName, party.EnvironmentName, party.PartyName );
    }
}
