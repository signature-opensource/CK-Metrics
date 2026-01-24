using System.Globalization;
using System.Text;
using CK.Core;
using CK.Metrics;
using FASTER.core;

namespace CK.AppIdentity.Monitoring.Metrics.Csv;

/// <summary>
/// Metrics consumer that writes measurements to a CSV file.
/// <para>
/// This class demonstrates how to implement a metrics consumer by extending <see cref="MetricsConsumerBase"/>.
/// Use this as a reference when implementing your own consumers (e.g., InfluxDB, Prometheus, etc.).
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <strong>Consumer Implementation Pattern:</strong>
/// </para>
/// <list type="number">
///   <item>Extend <see cref="MetricsConsumerBase"/> which provides the consumption loop, batching, and retry logic.</item>
///   <item>Override <see cref="ProcessEntriesAsync"/> to handle batches of metrics entries.</item>
///   <item>Use <see cref="MetricsLogDispatcher"/> to parse the raw bytes into typed callbacks.</item>
///   <item>Override <see cref="DisposeAsync"/> to clean up resources (always call <c>base.DisposeAsync()</c> first).</item>
/// </list>
/// <para>
/// <strong>Entry Format:</strong>
/// </para>
/// <para>
/// Each entry from FasterLog contains:
/// <list type="bullet">
///   <item><strong>8 bytes</strong>: DateTime as binary (via <see cref="DateTime.ToBinary"/>)</item>
///   <item><strong>Remaining bytes</strong>: ASCII-encoded metrics text</item>
/// </list>
/// </para>
/// <para>
/// <strong>Retry Semantics:</strong>
/// </para>
/// <para>
/// If <see cref="ProcessEntriesAsync"/> throws an exception (other than <see cref="OperationCanceledException"/>),
/// the base class will retry the same batch after the configured delay. This ensures no data loss on transient failures.
/// </para>
/// </remarks>
public sealed class CsvMetricsConsumer : MetricsConsumerBase
{
    // =====================================================================================
    // STEP 1: Define consumer-specific fields
    // =====================================================================================
    // Each consumer will have its own output target (file, database connection, HTTP client, etc.)

    readonly string _filePath;

    // The dispatcher parses raw metrics text and invokes typed callbacks.
    // This is a nested class that handles the parsing logic.
    readonly CsvMetricsLogDispatcher _dispatcher;

    // The output writer. Initialized lazily on first use.
    StreamWriter? _writer;

    // =====================================================================================
    // STEP 2: Constructor - pass required parameters to base class
    // =====================================================================================

    /// <summary>
    /// Initializes a new <see cref="CsvMetricsConsumer"/>.
    /// </summary>
    /// <param name="log">
    /// The FasterLog instance to consume from. Obtained from <see cref="MetricsFeatureDriver.FasterLog"/>.
    /// </param>
    /// <param name="name">
    /// The unique name for this consumer (max 20 characters).
    /// This name is used for the FasterLog named iterator, which persists the consumer's position.
    /// <strong>Important:</strong> The name must be unique across all consumers and stable across restarts
    /// to enable position recovery.
    /// </param>
    /// <param name="filePath">The absolute path to the CSV file to write.</param>
    /// <param name="retryDelayMs">
    /// Delay in milliseconds before retrying after a processing failure.
    /// When <see cref="ProcessEntriesAsync"/> throws, the base class waits this long before retrying.
    /// </param>
    /// <param name="batchThresholdBytes">
    /// Size threshold in bytes for batching entries.
    /// The base class collects entries until this threshold is reached, then calls <see cref="ProcessEntriesAsync"/>.
    /// Larger batches improve throughput but increase memory usage and latency.
    /// </param>
    public CsvMetricsConsumer(
        FasterLog log,
        string name,
        string filePath,
        int retryDelayMs = 2000,
        long batchThresholdBytes = 2 << 21 )
        : base( log, name, retryDelayMs, batchThresholdBytes )
    {
        // Validate consumer-specific parameters.
        Throw.CheckNotNullOrWhiteSpaceArgument( filePath );
        _filePath = filePath;

        // Create the dispatcher that will parse metrics entries.
        // The dispatcher is a nested class that calls back to this consumer.
        _dispatcher = new CsvMetricsLogDispatcher( this );
    }

    // =====================================================================================
    // STEP 3: Override ProcessEntriesAsync - the main processing logic
    // =====================================================================================

    /// <summary>
    /// Processes a batch of metrics entries from FasterLog.
    /// </summary>
    /// <param name="entries">
    /// The batch of raw entries to process.
    /// Each entry is a <see cref="ReadOnlyMemory{T}"/> containing the raw bytes from FasterLog.
    /// </param>
    /// <returns>
    /// A <see cref="TimeSpan"/> indicating how long to wait before processing the next batch.
    /// Return <see cref="TimeSpan.Zero"/> for no throttling (process as fast as possible).
    /// Return a positive value to rate-limit processing (useful for external APIs with rate limits).
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Error Handling:</strong>
    /// </para>
    /// <list type="bullet">
    ///   <item>If this method throws an exception, the base class will retry the same batch after the configured delay.</item>
    ///   <item>The batch is NOT committed until this method returns successfully.</item>
    ///   <item>On successful return, the base class calls <c>CompleteUntil</c> and commits the position.</item>
    /// </list>
    /// <para>
    /// <strong>Entry Parsing:</strong>
    /// </para>
    /// <para>
    /// Each entry contains a DateTime prefix (8 bytes) followed by ASCII text.
    /// Use <see cref="MetricsLogDispatcher.Add"/> to parse the text and receive typed callbacks.
    /// </para>
    /// </remarks>
    protected override async Task<TimeSpan> ProcessEntriesAsync( IEnumerable<ReadOnlyMemory<byte>> entries )
    {
        // Ensure the output file is ready.
        EnsureWriterInitialized();

        // Create a monitor for the dispatcher. The dispatcher may log warnings for malformed entries.
        // Using a dedicated monitor keeps these logs separate from the main application flow.
        var monitor = new ActivityMonitor( "CsvMetricsConsumer" );

        // Process each entry in the batch.
        foreach( var entry in entries )
        {
            // -------------------------------------------------------------------------
            // Parse the entry format: DateTime (8 bytes) + ASCII text
            // -------------------------------------------------------------------------

            var buffer = entry.ToArray();

            // Skip malformed entries that are too short.
            if( buffer.Length < sizeof( long ) ) continue;

            // Extract the timestamp from the first 8 bytes.
            // The handler writes DateTime.ToBinary() which preserves UTC/Local kind.
            var dateTimeBinary = BitConverter.ToInt64( buffer, 0 );
            var dateTime = DateTime.FromBinary( dateTimeBinary );

            // Extract the metrics text from the remaining bytes.
            var text = Encoding.ASCII.GetString( buffer, sizeof( long ), buffer.Length - sizeof( long ) );

            // -------------------------------------------------------------------------
            // Use MetricsLogDispatcher to parse the text
            // -------------------------------------------------------------------------
            // The dispatcher maintains state about known meters and instruments.
            // It parses the text and calls the appropriate virtual method:
            // - OnNewMeter: when a new meter is declared
            // - OnNewInstrument: when a new instrument is declared
            // - OnMeasure: when a measurement is recorded
            // - OnDisposedMeter: when a meter is disposed
            _dispatcher.Add( monitor, dateTime, text );
        }

        // Flush the writer to ensure all data is written before we commit the position.
        // This is critical for durability: if we commit before flushing, we might lose data on crash.
        if( _writer != null )
        {
            await _writer.FlushAsync();
        }

        // Return TimeSpan.Zero to process the next batch immediately.
        // For rate-limited APIs, return a positive TimeSpan (e.g., TimeSpan.FromSeconds(1)).
        return TimeSpan.Zero;
    }

    // =====================================================================================
    // STEP 4: Implement consumer-specific output logic
    // =====================================================================================

    /// <summary>
    /// Ensures the CSV writer is initialized, creating the file and writing headers if needed.
    /// </summary>
    void EnsureWriterInitialized()
    {
        if( _writer != null ) return;

        // Create the directory structure if it doesn't exist.
        var directory = Path.GetDirectoryName( _filePath );
        if( !string.IsNullOrEmpty( directory ) )
        {
            Directory.CreateDirectory( directory );
        }

        // Open in append mode to preserve existing data across restarts.
        var fileExists = File.Exists( _filePath );
        _writer = new StreamWriter( _filePath, append: true, Encoding.UTF8 );

        // Write the CSV header only if the file is new or empty.
        // This ensures we don't duplicate headers on restart.
        if( !fileExists || new FileInfo( _filePath ).Length == 0 )
        {
            _writer.WriteLine( "Timestamp,MeterName,InstrumentName,InstrumentType,Value,Tags" );
        }
    }

    /// <summary>
    /// Writes a single measurement to the CSV file.
    /// Called by the dispatcher when a measurement is parsed.
    /// </summary>
    /// <param name="timestamp">The timestamp when the measurement was recorded.</param>
    /// <param name="instrument">
    /// The full instrument information including meter name, instrument name, type, unit, etc.
    /// </param>
    /// <param name="measure">
    /// The parsed measurement containing the value and optional tags.
    /// Passed by reference (<c>in</c>) for efficiency since it's a struct.
    /// </param>
    internal void WriteLine( DateTime timestamp, FullInstrumentInfo instrument, in ParsedMeasureLog measure )
    {
        if( _writer == null ) return;

        // Build the CSV line.
        // Format: Timestamp,MeterName,InstrumentName,InstrumentType,Value,Tags
        var sb = new StringBuilder();

        // ISO 8601 format for timestamp (e.g., "2024-01-15T10:30:00.0000000Z").
        sb.Append( timestamp.ToString( "O", CultureInfo.InvariantCulture ) );
        sb.Append( ',' );

        // Meter name (e.g., "System.Net.Http" or "myapp.metrics").
        sb.Append( EscapeCsvField( instrument.MeterInfo.Name ) );
        sb.Append( ',' );

        // Instrument name (e.g., "http.client.request.duration").
        sb.Append( EscapeCsvField( instrument.Info.Name ) );
        sb.Append( ',' );

        // Instrument type (e.g., "Counter", "Histogram", "ObservableGauge").
        sb.Append( instrument.Info.TypeName );
        sb.Append( ',' );

        // The measurement value (integer or floating-point).
        sb.Append( measure.Measure.ToString() );
        sb.Append( ',' );

        // Tags (key-value pairs) if present.
        if( measure.TagsLength > 0 )
        {
            sb.Append( EscapeCsvField( measure.Tags.ToString() ) );
        }

        _writer.WriteLine( sb.ToString() );
    }

    /// <summary>
    /// Escapes a string value for safe inclusion in a CSV field.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped value, quoted if necessary.</returns>
    static string EscapeCsvField( string value )
    {
        // If the value contains special characters, wrap in quotes and escape existing quotes.
        if( value.Contains( ',' ) || value.Contains( '"' ) || value.Contains( '\n' ) || value.Contains( '\r' ) )
        {
            return $"\"{value.Replace( "\"", "\"\"" )}\"";
        }
        return value;
    }

    // =====================================================================================
    // STEP 5: Override DisposeAsync to clean up resources
    // =====================================================================================

    /// <summary>
    /// Disposes the consumer and closes the CSV file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Important:</strong> Always call <c>base.DisposeAsync()</c> first.
    /// </para>
    /// <para>
    /// The base class handles:
    /// <list type="bullet">
    ///   <item>Cancelling the processing loop</item>
    ///   <item>Waiting for pending operations to complete</item>
    ///   <item>Disposing the FasterLog iterator (which removes it from PersistedIterators)</item>
    /// </list>
    /// </para>
    /// <para>
    /// After calling the base, clean up consumer-specific resources (file handles, connections, etc.).
    /// </para>
    /// </remarks>
    public override async ValueTask DisposeAsync()
    {
        // ALWAYS call base.DisposeAsync() first.
        // This stops the processing loop and disposes the iterator.
        await base.DisposeAsync();

        // Clean up consumer-specific resources.
        if( _writer != null )
        {
            await _writer.DisposeAsync();
            _writer = null;
        }
    }

    // =====================================================================================
    // STEP 6: Implement MetricsLogDispatcher to parse metrics entries
    // =====================================================================================

    /// <summary>
    /// Nested dispatcher that parses metrics log entries and writes measurements to CSV.
    /// <para>
    /// <see cref="MetricsLogDispatcher"/> maintains state about known meters and instruments,
    /// and invokes virtual methods when events occur.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>How the dispatcher works:</strong>
    /// </para>
    /// <para>
    /// The metrics log contains different types of entries:
    /// <list type="bullet">
    ///   <item><c>+Meter:</c> - A new meter was created</item>
    ///   <item><c>+Instrument:</c> - A new instrument was created</item>
    ///   <item><c>-Meter:</c> - A meter was disposed</item>
    ///   <item><c>M:</c> - A measurement was recorded</item>
    /// </list>
    /// </para>
    /// <para>
    /// The dispatcher parses these entries and maintains an internal mapping of meter/instrument IDs.
    /// When a measurement arrives, it looks up the instrument and calls <see cref="OnMeasure"/>.
    /// </para>
    /// <para>
    /// <strong>State Management:</strong>
    /// </para>
    /// <para>
    /// The <see cref="OnNewMeter"/> and <see cref="OnNewInstrument"/> methods can return state objects
    /// that will be passed to subsequent callbacks. This is useful for maintaining per-meter or
    /// per-instrument state (e.g., database handles, aggregation state, etc.).
    /// </para>
    /// <para>
    /// For this CSV consumer, we don't need any state, so we return <c>null</c> from these methods.
    /// </para>
    /// </remarks>
    sealed class CsvMetricsLogDispatcher : MetricsLogDispatcher
    {
        readonly CsvMetricsConsumer _consumer;

        public CsvMetricsLogDispatcher( CsvMetricsConsumer consumer )
        {
            _consumer = consumer;
        }

        /// <summary>
        /// Called when a new meter is declared in the log.
        /// </summary>
        /// <param name="monitor">The activity monitor for logging.</param>
        /// <param name="info">Information about the meter (name, version, tags, etc.).</param>
        /// <returns>
        /// An optional state object that will be passed to <see cref="OnNewInstrument"/>
        /// and <see cref="OnDisposedMeter"/> for this meter. Return <c>null</c> if no state is needed.
        /// </returns>
        /// <remarks>
        /// Use this to initialize per-meter resources (e.g., create a database table, open a connection).
        /// For CSV output, we don't need any per-meter state.
        /// </remarks>
        protected override object? OnNewMeter( IActivityMonitor monitor, MeterInfo info ) => null;

        /// <summary>
        /// Called when a new instrument is declared in the log.
        /// </summary>
        /// <param name="monitor">The activity monitor for logging.</param>
        /// <param name="instrument">Full information about the instrument including its meter.</param>
        /// <param name="meterState">The state object returned by <see cref="OnNewMeter"/> for this meter.</param>
        /// <returns>
        /// An optional state object that will be passed to <see cref="OnMeasure"/>
        /// for measurements from this instrument. Return <c>null</c> if no state is needed.
        /// </returns>
        /// <remarks>
        /// Use this to initialize per-instrument resources (e.g., create a time series, register with backend).
        /// For CSV output, we don't need any per-instrument state.
        /// </remarks>
        protected override object? OnNewInstrument( IActivityMonitor monitor, FullInstrumentInfo instrument, object? meterState ) => null;

        /// <summary>
        /// Called when a meter is disposed.
        /// </summary>
        /// <param name="monitor">The activity monitor for logging.</param>
        /// <param name="meter">Information about the disposed meter.</param>
        /// <param name="meterState">The state object returned by <see cref="OnNewMeter"/> for this meter.</param>
        /// <param name="instruments">
        /// List of instruments that belonged to this meter, with their state objects.
        /// The instrument may be <c>null</c> if it was never fully initialized.
        /// </param>
        /// <remarks>
        /// Use this to clean up per-meter resources.
        /// For CSV output, we don't have any cleanup to do.
        /// </remarks>
        protected override void OnDisposedMeter(
            IActivityMonitor monitor,
            MeterInfo meter,
            object? meterState,
            IReadOnlyList<(FullInstrumentInfo? Instrument, object? InstrumentState)> instruments )
        {
            // Nothing to do for CSV output.
            // For other consumers, you might close connections or flush buffers here.
        }

        /// <summary>
        /// Called when a measurement is recorded.
        /// </summary>
        /// <param name="monitor">The activity monitor for logging.</param>
        /// <param name="instrument">Full information about the instrument that recorded the measurement.</param>
        /// <param name="instrumentState">The state object returned by <see cref="OnNewInstrument"/> for this instrument.</param>
        /// <param name="measureTime">The timestamp when the measurement was recorded.</param>
        /// <param name="measure">
        /// The parsed measurement containing the value and tags.
        /// Passed by reference (<c>in</c>) since it's a struct.
        /// </param>
        /// <remarks>
        /// This is the main method where you handle measurements.
        /// For CSV output, we write a line to the file.
        /// For database consumers, you would insert/update records here.
        /// For HTTP consumers, you would queue the measurement for batched sending.
        /// </remarks>
        protected override void OnMeasure(
            IActivityMonitor monitor,
            FullInstrumentInfo instrument,
            object? instrumentState,
            DateTime measureTime,
            in ParsedMeasureLog measure )
        {
            _consumer.WriteLine( measureTime, instrument, measure );
        }
    }
}
