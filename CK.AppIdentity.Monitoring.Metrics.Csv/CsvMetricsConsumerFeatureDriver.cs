using CK.AppIdentity;
using CK.Core;
using CK.Metrics;
using Microsoft.Extensions.Configuration;

namespace CK.AppIdentity.Monitoring.Metrics.Csv;

/// <summary>
/// Feature driver that creates and manages a <see cref="CsvMetricsConsumer"/>.
/// <para>
/// This class demonstrates how to implement a consumer feature driver.
/// Use this as a reference when implementing your own consumer features.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <strong>Consumer Feature Driver Pattern:</strong>
/// </para>
/// <list type="number">
///   <item>Extend <see cref="ApplicationIdentityFeatureDriver"/>.</item>
///   <item>Inject <see cref="MetricsFeatureDriver"/> via constructor (this ensures correct initialization order).</item>
///   <item>In <see cref="SetupAsync"/>, read configuration, create the consumer, register it, and start it.</item>
///   <item>In <see cref="TeardownAsync"/>, remove the consumer from the driver (which disposes it).</item>
/// </list>
/// <para>
/// <strong>Why a Separate Feature Driver?</strong>
/// </para>
/// <para>
/// Each consumer type has its own feature driver because:
/// <list type="bullet">
///   <item>Configuration is consumer-specific (file paths, connection strings, API keys, etc.).</item>
///   <item>DI dependency on <see cref="MetricsFeatureDriver"/> ensures FasterLog is ready before consumers start.</item>
///   <item>Each consumer can be enabled/disabled independently via configuration.</item>
///   <item>Lifecycle is managed by the AppIdentity system (automatic setup/teardown).</item>
/// </list>
/// </para>
/// <para>
/// <strong>Initialization Order:</strong>
/// </para>
/// <para>
/// Because this driver depends on <see cref="MetricsFeatureDriver"/> via constructor injection,
/// the DI container ensures that <see cref="MetricsFeatureDriver.SetupAsync"/> runs before
/// this driver's <see cref="SetupAsync"/>. This guarantees that <see cref="MetricsFeatureDriver.FasterLog"/>
/// is available when we need it.
/// </para>
/// <para>
/// <strong>Configuration Example:</strong>
/// </para>
/// <code>
/// {
///   "CK-AppIdentity": {
///     "Local": {
///       "Metrics": {
///         "Path": "FasterLog/Metrics"
///       },
///       "MetricsCsv": {
///         "Name": "csv-export",
///         "Path": "Exports/metrics.csv",
///         "RetryDelayMs": 2000,
///         "BatchThresholdBytes": 4194304
///       }
///     }
///   }
/// }
/// </code>
/// </remarks>
public sealed class CsvMetricsConsumerFeatureDriver : ApplicationIdentityFeatureDriver
{
    // =====================================================================================
    // STEP 1: Define constants for default configuration values
    // =====================================================================================
    // These provide sensible defaults when configuration is not specified.

    /// <summary>
    /// Default consumer name.
    /// <para>
    /// This name is used for the FasterLog named iterator. It must be:
    /// <list type="bullet">
    ///   <item>Unique across all consumers in the application</item>
    ///   <item>At most 20 characters (FasterLog limitation)</item>
    ///   <item>Stable across restarts (to enable position recovery)</item>
    /// </list>
    /// </para>
    /// </summary>
    public const string DefaultConsumerName = "csv-export";

    /// <summary>
    /// Default relative path for CSV output.
    /// <para>
    /// This path is relative to <see cref="ILocalParty.LocalFileStore"/>.
    /// The actual file location depends on the application's identity configuration.
    /// </para>
    /// </summary>
    public const string DefaultCsvPath = "Exports/metrics.csv";

    // =====================================================================================
    // STEP 2: Define fields
    // =====================================================================================

    /// <summary>
    /// Reference to the MetricsFeatureDriver that owns the FasterLog.
    /// Injected via constructor to ensure correct initialization order.
    /// </summary>
    readonly MetricsFeatureDriver _metricsDriver;

    /// <summary>
    /// The consumer instance, created during SetupAsync.
    /// Null before setup or after teardown.
    /// </summary>
    CsvMetricsConsumer? _consumer;

    // =====================================================================================
    // STEP 3: Constructor - inject dependencies
    // =====================================================================================

    /// <summary>
    /// Initializes a new <see cref="CsvMetricsConsumerFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service (required by base class).</param>
    /// <param name="metricsDriver">
    /// The metrics feature driver that owns the FasterLog.
    /// <para>
    /// <strong>Important:</strong> This dependency injection ensures that
    /// <see cref="MetricsFeatureDriver.SetupAsync"/> runs before this driver's
    /// <see cref="SetupAsync"/>. Without this dependency, there would be no
    /// guarantee about initialization order.
    /// </para>
    /// </param>
    /// <remarks>
    /// The <c>isAllowedByDefault: true</c> parameter means this feature driver
    /// will be activated unless explicitly disabled in configuration.
    /// Set to <c>false</c> if the consumer should only run when explicitly enabled.
    /// </remarks>
    public CsvMetricsConsumerFeatureDriver( ApplicationIdentityService s, MetricsFeatureDriver metricsDriver )
        : base( s, isAllowedByDefault: true )
    {
        _metricsDriver = metricsDriver;
    }

    // =====================================================================================
    // STEP 4: Override SetupAsync - create and start the consumer
    // =====================================================================================

    /// <summary>
    /// Sets up the CSV consumer based on configuration.
    /// </summary>
    /// <param name="context">
    /// The feature lifetime context providing access to the monitor and cancellation.
    /// </param>
    /// <returns>
    /// <c>true</c> if setup succeeded (or was skipped due to missing configuration),
    /// <c>false</c> if setup failed and the application should not start.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Setup Flow:</strong>
    /// </para>
    /// <list type="number">
    ///   <item>Check if MetricsFeatureDriver has a FasterLog (it may have skipped initialization).</item>
    ///   <item>Read configuration from the Local:MetricsCsv section.</item>
    ///   <item>If no configuration exists, return true (consumer is optional).</item>
    ///   <item>Parse configuration values with defaults.</item>
    ///   <item>Create the consumer instance.</item>
    ///   <item>Register it with the MetricsFeatureDriver (for truncation tracking).</item>
    ///   <item>Start the consumer's processing loop.</item>
    /// </list>
    /// <para>
    /// <strong>Error Handling:</strong>
    /// </para>
    /// <para>
    /// If configuration is invalid or the consumer cannot be created, you can either:
    /// <list type="bullet">
    ///   <item>Return <c>false</c> to prevent the application from starting.</item>
    ///   <item>Log a warning and return <c>true</c> to continue without the consumer.</item>
    /// </list>
    /// This implementation chooses the latter for optional configuration.
    /// </para>
    /// </remarks>
    protected override async Task<bool> SetupAsync( FeatureLifetimeContext context )
    {
        using var _ = context.Monitor.TemporarilySetAutoTags( DotNetMetrics.MetricsInternalTag );

        // -------------------------------------------------------------------------
        // Check if MetricsFeatureDriver has a FasterLog
        // -------------------------------------------------------------------------
        // The MetricsFeatureDriver may have skipped initialization if:
        // - No Metrics configuration was present
        // - The MetricsLogHandler was not found in GrandOutput
        // In these cases, we should also skip consumer initialization.

        if( _metricsDriver.FasterLog == null )
        {
            context.Monitor.Trace( "CsvMetricsConsumerFeatureDriver: MetricsFeatureDriver has no FasterLog. CSV consumer not started." );
            return true; // Not an error - just nothing to consume.
        }

        // -------------------------------------------------------------------------
        // Read configuration from Local:MetricsCsv section
        // -------------------------------------------------------------------------
        // Configuration is read from the LocalConfiguration, which is scoped to
        // the application's local identity (domain/app/environment).

        var csvSection = ApplicationIdentityService.LocalConfiguration.Configuration.GetSection( "MetricsCsv" );
        if( !csvSection.Exists() )
        {
            context.Monitor.Trace( "CsvMetricsConsumerFeatureDriver: No Local:MetricsCsv configuration found. CSV consumer not started." );
            return true; // Configuration is optional - return success.
        }

        // -------------------------------------------------------------------------
        // Parse configuration with defaults
        // -------------------------------------------------------------------------

        // Consumer name - must be unique and stable across restarts.
        var name = csvSection["Name"] ?? DefaultConsumerName;

        // File path - relative to LocalFileStore.
        var relativePath = csvSection["Path"] ?? DefaultCsvPath;
        var basePath = ApplicationIdentityService.LocalFileStore.FolderPath;
        var resolvedPath = Path.Combine( basePath, relativePath );

        // Retry delay - how long to wait after a processing failure.
        int retryDelayMs = 2000;
        if( int.TryParse( csvSection["RetryDelayMs"], out var rdm ) )
            retryDelayMs = rdm;

        // Batch threshold - how many bytes to collect before processing.
        long batchThresholdBytes = 2 << 21; // 4 MiB default
        if( long.TryParse( csvSection["BatchThresholdBytes"], out var btb ) )
            batchThresholdBytes = btb;

        // -------------------------------------------------------------------------
        // Create, register, and start the consumer
        // -------------------------------------------------------------------------

        // Create the consumer with the parsed configuration.
        _consumer = new CsvMetricsConsumer(
            _metricsDriver.FasterLog,
            name,
            resolvedPath,
            retryDelayMs,
            batchThresholdBytes );

        // Register with the MetricsFeatureDriver.
        // This is important for:
        // - Truncation: The driver tracks all consumers' positions to know what can be truncated.
        // - Orphan detection: On shutdown, unregistered iterators are cleaned up.
        _metricsDriver.RegisterConsumer( _consumer );

        // Start the consumer's processing loop.
        // This creates the named iterator and begins consuming entries.
        // The CancellationToken.None is used because the consumer has its own
        // internal cancellation via DisposeAsync.
        await _consumer.StartAsync( context.Monitor, CancellationToken.None );

        context.Monitor.Info( $"CsvMetricsConsumerFeatureDriver: Started CSV consumer '{name}' writing to '{resolvedPath}'." );

        return true;
    }

    // =====================================================================================
    // STEP 5: Override SetupDynamicRemoteAsync and TeardownDynamicRemoteAsync
    // =====================================================================================
    // These methods handle per-party setup/teardown for remote parties.
    // Most consumers don't need per-party logic, so these return success/complete.

    /// <summary>
    /// Called when a new remote party is added dynamically.
    /// </summary>
    /// <param name="context">The feature lifetime context.</param>
    /// <param name="party">The remote party being added.</param>
    /// <returns><c>true</c> to allow the party, <c>false</c> to reject it.</returns>
    /// <remarks>
    /// Override this if your consumer needs to do something when remote parties
    /// are added (e.g., create a separate output file per party).
    /// For CSV output, we use a single file for all parties.
    /// </remarks>
    protected override Task<bool> SetupDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        // No per-party setup needed for CSV output.
        return Task.FromResult( true );
    }

    /// <summary>
    /// Called when a remote party is removed dynamically.
    /// </summary>
    /// <param name="context">The feature lifetime context.</param>
    /// <param name="party">The remote party being removed.</param>
    /// <returns>A task that completes when teardown is done.</returns>
    /// <remarks>
    /// Override this if your consumer needs to do something when remote parties
    /// are removed (e.g., flush and close a per-party output file).
    /// For CSV output, we use a single file for all parties.
    /// </remarks>
    protected override Task TeardownDynamicRemoteAsync( FeatureLifetimeContext context, IOwnedParty party )
    {
        // No per-party teardown needed for CSV output.
        return Task.CompletedTask;
    }

    // =====================================================================================
    // STEP 6: Override TeardownAsync - clean up the consumer
    // =====================================================================================

    /// <summary>
    /// Tears down the CSV consumer.
    /// </summary>
    /// <param name="context">The feature lifetime context.</param>
    /// <returns>A task that completes when teardown is done.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Teardown Flow:</strong>
    /// </para>
    /// <list type="number">
    ///   <item>Call <see cref="MetricsFeatureDriver.RemoveConsumerAsync"/> which:
    ///     <list type="bullet">
    ///       <item>Removes the consumer from the driver's list.</item>
    ///       <item>Calls <see cref="IAsyncDisposable.DisposeAsync"/> on the consumer.</item>
    ///     </list>
    ///   </item>
    ///   <item>The consumer's DisposeAsync:
    ///     <list type="bullet">
    ///       <item>Cancels the processing loop.</item>
    ///       <item>Waits for pending operations to complete.</item>
    ///       <item>Disposes the FasterLog iterator (removing it from PersistedIterators).</item>
    ///       <item>Closes the CSV file.</item>
    ///     </list>
    ///   </item>
    /// </list>
    /// <para>
    /// <strong>Ordering:</strong>
    /// </para>
    /// <para>
    /// Feature drivers are torn down in reverse order of their dependencies.
    /// Since this driver depends on <see cref="MetricsFeatureDriver"/>, this
    /// TeardownAsync runs BEFORE MetricsFeatureDriver.TeardownAsync.
    /// This ensures the FasterLog is still available during our teardown.
    /// </para>
    /// </remarks>
    protected override async Task TeardownAsync( FeatureLifetimeContext context )
    {
        using var _ = context.Monitor.TemporarilySetAutoTags( DotNetMetrics.MetricsInternalTag );

        if( _consumer != null )
        {
            // RemoveConsumerAsync removes from the driver's list and disposes the consumer.
            // This ensures proper cleanup including:
            // - Stopping the processing loop
            // - Committing final position
            // - Closing the CSV file
            // - Removing the iterator from FasterLog.PersistedIterators
            await _metricsDriver.RemoveConsumerAsync( _consumer.Name );
            _consumer = null;
        }
    }
}
