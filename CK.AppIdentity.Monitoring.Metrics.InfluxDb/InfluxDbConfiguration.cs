namespace CK.AppIdentity.Monitoring.Metrics.InfluxDb;

/// <summary>
/// Configuration for the InfluxDB metrics consumer.
/// </summary>
public sealed class InfluxDbConfiguration
{
    /// <summary>
    /// Gets or sets the InfluxDB server URL.
    /// Example: "https://influxdb.example.com:8086"
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the InfluxDB organization name.
    /// </summary>
    public string Org { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the InfluxDB bucket name.
    /// </summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API token for token-based authentication.
    /// When set, the consumer uses "Authorization: Token {token}" header.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the username for basic authentication.
    /// Use together with <see cref="Password"/> as an alternative to <see cref="Token"/>.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the password for basic authentication.
    /// Use together with <see cref="Username"/> as an alternative to <see cref="Token"/>.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets whether to compress HTTP requests with gzip.
    /// Defaults to true.
    /// </summary>
    public bool UseGzip { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum delay in milliseconds between flushes to InfluxDB.
    /// Defaults to 1000 ms (1 second).
    /// </summary>
    public int FlushIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the delay in milliseconds before retrying after a failure.
    /// Defaults to 2000 ms (2 seconds).
    /// </summary>
    public int RetryDelayMs { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the size threshold in bytes for batching entries.
    /// When the accumulated line protocol data exceeds this threshold, a flush is triggered.
    /// Defaults to 4 MiB.
    /// </summary>
    public long BatchThresholdBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum age in milliseconds for a batch before it is sent.
    /// The batch is sent when either this time elapses since the first entry was collected,
    /// OR the <see cref="BatchThresholdBytes"/> is reached, whichever comes first.
    /// Defaults to 60000 ms (1 minute). Set to 0 for immediate sending.
    /// </summary>
    public int MaxBatchAgeMs { get; set; } = 60000;

    /// <summary>
    /// Gets or sets the timeout in milliseconds for graceful shutdown.
    /// During shutdown, the consumer will attempt to flush pending entries within this timeout.
    /// Defaults to 5000 ms (5 seconds). Set to 0 to skip graceful flush.
    /// </summary>
    public int GracefulShutdownTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Gets or sets optional static tags to include in every measurement.
    /// These tags are added alongside the automatic ck_domain, ck_environment, ck_party, and meter tags.
    /// <para>
    /// Tag values support environment variable expansion (e.g., "%COMPUTERNAME%", "%USERNAME%")
    /// when parsed by the feature driver.
    /// </para>
    /// </summary>
    public Dictionary<string, string>? Tags { get; set; }

    /// <summary>
    /// Gets the write API endpoint URL.
    /// </summary>
    public string WriteUrl => $"{ServerUrl.TrimEnd( '/' )}/api/v2/write?org={Uri.EscapeDataString( Org )}&bucket={Uri.EscapeDataString( Bucket )}&precision=ns";
}
