using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using CK.Core;
using CK.Metrics;
using FASTER.core;

namespace CK.AppIdentity.Monitoring.Metrics.InfluxDb;

/// <summary>
/// Metrics consumer that posts measurements to InfluxDB v2.x using the line protocol format.
/// </summary>
public sealed class InfluxDbMetricsConsumer : MetricsConsumerBase
{
    readonly InfluxDbConfiguration _config;
    readonly HttpClient _httpClient;
    readonly LineProtocolBuilder _lineProtocolBuilder;
    readonly InfluxDbMetricsLogDispatcher _dispatcher;
    readonly TimeSpan _flushInterval;

    /// <summary>
    /// Initializes a new <see cref="InfluxDbMetricsConsumer"/>.
    /// </summary>
    public InfluxDbMetricsConsumer(
        FasterLog log,
        string name,
        InfluxDbConfiguration config,
        string domain,
        string environment,
        string party,
        HttpClient? httpClient = null )
        : base( log, name, config.RetryDelayMs, config.BatchThresholdBytes )
    {
        Throw.CheckNotNullArgument( config );
        Throw.CheckNotNullOrWhiteSpaceArgument( config.ServerUrl );
        Throw.CheckNotNullOrWhiteSpaceArgument( config.Org );
        Throw.CheckNotNullOrWhiteSpaceArgument( config.Bucket );

        // Ensure DotNetMetrics static constructor runs on the main thread before
        // the consumer starts processing on a background thread.
        // This avoids a potential deadlock where MeterListener.Start() blocks
        // on MicroAgent.SyncWait() during static initialization.
        _ = DotNetMetrics.GetConfiguration();

        _config = config;
        _httpClient = httpClient ?? new HttpClient();
        _lineProtocolBuilder = new LineProtocolBuilder( domain, environment, party );
        _dispatcher = new InfluxDbMetricsLogDispatcher( this );
        _flushInterval = TimeSpan.FromMilliseconds( config.FlushIntervalMs );

        ConfigureHttpClient();
    }

    void ConfigureHttpClient()
    {
        if( !string.IsNullOrEmpty( _config.Token ) )
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue( "Token", _config.Token );
        }
        else if( !string.IsNullOrEmpty( _config.Username ) && !string.IsNullOrEmpty( _config.Password ) )
        {
            var credentials = Convert.ToBase64String( Encoding.UTF8.GetBytes( $"{_config.Username}:{_config.Password}" ) );
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue( "Basic", credentials );
        }

        _httpClient.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json" ) );
    }

    /// <inheritdoc />
    protected override async Task<TimeSpan> ProcessEntriesAsync( IEnumerable<ReadOnlyMemory<byte>> entries )
    {
        // Create a monitor for the dispatcher (same as CsvMetricsConsumer)
        var monitor = new ActivityMonitor( "InfluxDbMetricsConsumer" );

        try
        {
            // Clear the line protocol buffer
            _lineProtocolBuilder.Clear();

            // Count entries for logging
            var entryCount = 0;

            // Process each entry in the batch
            foreach( var entry in entries )
            {
                entryCount++;
                var buffer = entry.ToArray();
                if( buffer.Length < sizeof( long ) ) continue;

                var dateTimeBinary = BitConverter.ToInt64( buffer, 0 );
                var dateTime = DateTime.FromBinary( dateTimeBinary );
                var text = Encoding.ASCII.GetString( buffer, sizeof( long ), buffer.Length - sizeof( long ) );

                _dispatcher.Add( monitor, dateTime, text );
            }

            monitor.Info( $"Processed {entryCount} entries, line protocol length: {_lineProtocolBuilder.Length}" );

            // If we have any data, send it to InfluxDB
            if( _lineProtocolBuilder.Length > 0 )
            {
                await SendToInfluxDbAsync( monitor );
            }
            else
            {
                monitor.Info( "No measurements to send (line protocol buffer is empty)" );
            }

            return _flushInterval;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Error in ProcessEntriesAsync: {ex.GetType().Name}: {ex.Message}", ex );
            throw;
        }
    }

    internal void AddMeasurement( FullInstrumentInfo instrument, in ParsedMeasureLog measure, DateTime measureTime )
    {
        _lineProtocolBuilder.AppendMeasurement( instrument, measure, measureTime );
    }

    async Task SendToInfluxDbAsync( IActivityMonitor monitor )
    {
        var content = _lineProtocolBuilder.ToString();
        HttpContent httpContent;

        if( _config.UseGzip )
        {
            var compressedBytes = CompressWithGzip( Encoding.UTF8.GetBytes( content ) );
            httpContent = new ByteArrayContent( compressedBytes );
            httpContent.Headers.ContentType = new MediaTypeHeaderValue( "text/plain" ) { CharSet = "utf-8" };
            httpContent.Headers.ContentEncoding.Add( "gzip" );
        }
        else
        {
            httpContent = new StringContent( content, Encoding.UTF8, "text/plain" );
        }

        monitor.Trace( $"Sending {content.Length} bytes to {_config.WriteUrl}" );

        try
        {
            var response = await _httpClient.PostAsync( _config.WriteUrl, httpContent );

            if( !response.IsSuccessStatusCode )
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                var errorMessage = $"InfluxDB write failed with status {(int)response.StatusCode} ({response.StatusCode}): {responseBody}";
                monitor.Error( errorMessage );
                throw new HttpRequestException( errorMessage );
            }

            monitor.Info( $"Successfully wrote {_lineProtocolBuilder.Length} bytes to InfluxDB." );
        }
        catch( Exception ex ) when( ex is not HttpRequestException )
        {
            monitor.Error( $"HTTP request failed: {ex.Message}", ex );
            throw;
        }
    }

    static byte[] CompressWithGzip( byte[] data )
    {
        using var output = new MemoryStream();
        using( var gzip = new GZipStream( output, CompressionLevel.Optimal, leaveOpen: true ) )
        {
            gzip.Write( data, 0, data.Length );
        }
        return output.ToArray();
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _httpClient.Dispose();
    }

    sealed class InfluxDbMetricsLogDispatcher : MetricsLogDispatcher
    {
        readonly InfluxDbMetricsConsumer _consumer;

        public InfluxDbMetricsLogDispatcher( InfluxDbMetricsConsumer consumer )
        {
            _consumer = consumer;
        }

        protected override object? OnNewMeter( IActivityMonitor monitor, MeterInfo info ) => null;

        protected override object? OnNewInstrument( IActivityMonitor monitor, FullInstrumentInfo instrument, object? meterState ) => null;

        protected override void OnDisposedMeter(
            IActivityMonitor monitor,
            MeterInfo meter,
            object? meterState,
            IReadOnlyList<(FullInstrumentInfo? Instrument, object? InstrumentState)> instruments )
        {
        }

        protected override void OnMeasure(
            IActivityMonitor monitor,
            FullInstrumentInfo instrument,
            object? instrumentState,
            DateTime measureTime,
            in ParsedMeasureLog measure )
        {
            _consumer.AddMeasurement( instrument, measure, measureTime );
        }
    }
}
