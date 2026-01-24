using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CK.Core;
using FASTER.core;
using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Monitoring.Metrics.InfluxDb.Tests;

[TestFixture]
public class InfluxDbMetricsConsumerTests
{
    [Test]
    public void configuration_builds_correct_write_url()
    {
        var config = new InfluxDbConfiguration
        {
            ServerUrl = "https://influxdb.example.com:8086",
            Org = "my-org",
            Bucket = "my-bucket"
        };

        var expectedUrl = "https://influxdb.example.com:8086/api/v2/write?org=my-org&bucket=my-bucket&precision=ns";
        Assert.That( config.WriteUrl, Is.EqualTo( expectedUrl ) );
    }

    [Test]
    public void configuration_escapes_special_characters_in_url()
    {
        var config = new InfluxDbConfiguration
        {
            ServerUrl = "https://influxdb.example.com:8086/",
            Org = "my org",
            Bucket = "my/bucket"
        };

        Assert.That( config.WriteUrl, Does.Contain( "org=my%20org" ) );
        Assert.That( config.WriteUrl, Does.Contain( "bucket=my%2Fbucket" ) );
    }

    [Test]
    public void line_protocol_builder_creates_measurement()
    {
        var builder = new LineProtocolBuilder( "TestDomain", "Production", "TestApp" );
        Assert.That( builder.Length, Is.EqualTo( 0 ) );
        builder.Clear();
        Assert.That( builder.ToString(), Is.Empty );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task mock_server_can_receive_http_requests_Async( CancellationToken cancellationToken )
    {
        using var mockServer = new MockInfluxDbServer();
        await Task.Delay( 50, cancellationToken );

        using var httpClient = new HttpClient();
        var content = new StringContent( "test_metric value=1 1234567890000000000\n", Encoding.UTF8, "text/plain" );

        var response = await httpClient.PostAsync(
            mockServer.Url + "/api/v2/write?org=test&bucket=test&precision=ns",
            content,
            cancellationToken );

        Assert.That( response.StatusCode, Is.EqualTo( HttpStatusCode.NoContent ) );

        await Task.Delay( 100, cancellationToken );

        Assert.That( mockServer.ReceivedRequests.Count, Is.GreaterThan( 0 ) );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task consumer_can_be_created_and_disposed_Async( CancellationToken cancellationToken )
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( consumer_can_be_created_and_disposed_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var logPath = Path.Combine( testDir, "faster-log" );

        using var device = Devices.CreateLogDevice( Path.Combine( logPath, "metrics.log" ), preallocateFile: false );
        var logSettings = new FasterLogSettings { LogDevice = device };
        using var log = new FasterLog( logSettings );

        var config = new InfluxDbConfiguration
        {
            ServerUrl = "http://localhost:9999",
            Org = "test",
            Bucket = "test",
            Token = "test",
            FlushIntervalMs = 100,
            RetryDelayMs = 100
        };

        var consumer = new InfluxDbMetricsConsumer(
            log,
            "test-dispose",
            config,
            "Domain",
            "Env",
            "Party" );

        using var cts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        await consumer.StartAsync( TestHelper.Monitor, cts.Token );

        await Task.Delay( 50, cancellationToken );

        await cts.CancelAsync();

        using var disposeCts = new CancellationTokenSource( TimeSpan.FromSeconds( 5 ) );
        var disposeTask = consumer.DisposeAsync().AsTask();
        var completedTask = await Task.WhenAny( disposeTask, Task.Delay( -1, disposeCts.Token ) );

        if( completedTask != disposeTask )
        {
            Assert.Fail( "Consumer.DisposeAsync did not complete within 5 seconds" );
        }

        await disposeTask;
        Assert.Pass( "Consumer was created and disposed without hanging" );
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task consumer_processes_entries_like_csv_test_Async( CancellationToken cancellationToken )
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( consumer_processes_entries_like_csv_test_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var logPath = Path.Combine( testDir, "faster-log" );

        using var device = Devices.CreateLogDevice( Path.Combine( logPath, "metrics.log" ), preallocateFile: false );
        var logSettings = new FasterLogSettings { LogDevice = device };
        using var log = new FasterLog( logSettings );

        using var mockServer = new MockInfluxDbServer();
        await Task.Delay( 50, cancellationToken );

        var config = new InfluxDbConfiguration
        {
            ServerUrl = mockServer.Url,
            Org = mockServer.Org,
            Bucket = mockServer.Bucket,
            Token = mockServer.Token,
            UseGzip = false,
            FlushIntervalMs = 50,
            RetryDelayMs = 100
        };

        EnqueueMetricsEntry( log, "+Meter:1,\"test.meter\",\"1.0\",\"\",[]" );
        EnqueueMetricsEntry( log, "+Instrument:1,1,\"requests\",\"Counter\",\"Int64\",false,\"Total requests\",\"\",[]" );
        EnqueueMetricsEntry( log, "M:1,42.0,[\"method\",\"GET\"]" );
        await log.CommitAsync( cancellationToken );

        var cts = new CancellationTokenSource();
        var consumer = new InfluxDbMetricsConsumer( log, "test-consumer", config, "Domain", "Env", "Party" );
        await consumer.StartAsync( TestHelper.Monitor, cts.Token );

        // Wait for processing with polling (like other tests in this file)
        for( int i = 0; i < 10 && consumer.CompletedUntilAddress == 0; i++ )
        {
            await Task.Delay( 200, cancellationToken );
        }

        var address = consumer.CompletedUntilAddress;

        await cts.CancelAsync();
        await consumer.DisposeAsync();

        Assert.That( address, Is.GreaterThan( 0 ), "Consumer should have processed entries" );
        Assert.That( mockServer.ReceivedRequests.Count, Is.GreaterThan( 0 ), "Mock server should have received at least one request" );
    }

    [Test]
    [CancelAfter( 5000 )]
    public async Task consumer_processes_entries_without_hanging_Async( CancellationToken cancellationToken )
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( consumer_processes_entries_without_hanging_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var logPath = Path.Combine( testDir, "faster-log" );

        using var device = Devices.CreateLogDevice( Path.Combine( logPath, "metrics.log" ), preallocateFile: false );
        var logSettings = new FasterLogSettings { LogDevice = device };
        using var log = new FasterLog( logSettings );

        var config = new InfluxDbConfiguration
        {
            ServerUrl = "http://localhost:59999",
            Org = "test",
            Bucket = "test",
            Token = "test",
            UseGzip = false,
            FlushIntervalMs = 50,
            RetryDelayMs = 100
        };

        EnqueueMetricsEntry( log, "+Meter:1,\"test.meter\",\"1.0\",\"\",[]" );
        EnqueueMetricsEntry( log, "+Instrument:1,1,\"requests\",\"Counter\",\"Int64\",false,\"Total requests\",\"\",[]" );
        EnqueueMetricsEntry( log, "M:1,42.0,[\"method\",\"GET\"]" );
        await log.CommitAsync( cancellationToken );

        var consumer = new InfluxDbMetricsConsumer(
            log,
            "test-process",
            config,
            "TestDomain",
            "Production",
            "TestApp" );

        var cts = new CancellationTokenSource();
        await consumer.StartAsync( TestHelper.Monitor, cts.Token );

        await Task.Delay( 500, cancellationToken );

        await cts.CancelAsync();
        await consumer.DisposeAsync();

        Assert.Pass( "Consumer processed entries without hanging" );
    }

    [Test]
    [CancelAfter( 10000 )]
    public async Task consumer_sends_metrics_to_mock_server_Async( CancellationToken cancellationToken )
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( consumer_sends_metrics_to_mock_server_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var logPath = Path.Combine( testDir, "faster-log" );

        using var device = Devices.CreateLogDevice( Path.Combine( logPath, "metrics.log" ), preallocateFile: false );
        var logSettings = new FasterLogSettings { LogDevice = device };
        using var log = new FasterLog( logSettings );

        using var mockServer = new MockInfluxDbServer();
        await Task.Delay( 100, cancellationToken );

        var config = new InfluxDbConfiguration
        {
            ServerUrl = mockServer.Url,
            Org = mockServer.Org,
            Bucket = mockServer.Bucket,
            Token = mockServer.Token,
            UseGzip = false,
            FlushIntervalMs = 50,
            RetryDelayMs = 100
        };

        EnqueueMetricsEntry( log, "+Meter:1,\"test.meter\",\"1.0\",\"\",[]" );
        EnqueueMetricsEntry( log, "+Instrument:1,1,\"requests\",\"Counter\",\"Int64\",false,\"Total requests\",\"\",[]" );
        EnqueueMetricsEntry( log, "M:1,42.0,[\"method\",\"GET\"]" );
        await log.CommitAsync( cancellationToken );

        var consumer = new InfluxDbMetricsConsumer(
            log,
            "test-send",
            config,
            "TestDomain",
            "Production",
            "TestApp" );

        var cts = new CancellationTokenSource();
        await consumer.StartAsync( TestHelper.Monitor, cts.Token );

        // Wait for processing
        for( int i = 0; i < 10 && consumer.CompletedUntilAddress == 0; i++ )
        {
            await Task.Delay( 200, cancellationToken );
        }

        var writeRequests = mockServer.ReceivedRequests.Where( r => r.Path.StartsWith( "/api/v2/write" ) ).ToList();

        await cts.CancelAsync();
        await consumer.DisposeAsync();

        Assert.That( writeRequests.Count, Is.GreaterThan( 0 ), "Mock server should have received write request" );

        var request = writeRequests[0];
        Assert.That( request.Method, Is.EqualTo( "POST" ) );
        Assert.That( request.Body, Does.Contain( "requests" ) );
        Assert.That( request.Body, Does.Contain( "value=42" ) );
    }

    [Test]
    [CancelAfter( 5000 )]
    public void direct_dispatcher_test( CancellationToken cancellationToken )
    {
        var builder = new LineProtocolBuilder( "TestDomain", "Production", "TestApp" );
        var dispatcher = new TestInfluxDbDispatcher( builder );

        var monitor = TestHelper.Monitor;
        var time = DateTime.UtcNow;

        dispatcher.Add( monitor, time, "+Meter:1,\"test.meter\",\"1.0\",\"\",[]" );
        Assert.That( dispatcher.MeterCount, Is.EqualTo( 1 ) );

        dispatcher.Add( monitor, time, "+Instrument:1,1,\"requests\",\"Counter`1\",\"Int64\",false,\"Total requests\",\"\",[]" );
        Assert.That( dispatcher.InstrumentCount, Is.EqualTo( 1 ) );

        dispatcher.Add( monitor, time, "M:1,42.0,[]" );
        Assert.That( dispatcher.MeasureCount, Is.EqualTo( 1 ) );

        var output = builder.ToString();
        Assert.That( output, Does.Contain( "requests" ) );
    }

    static void EnqueueMetricsEntry( FasterLog log, string text )
    {
        var dateTime = DateTime.UtcNow;
        var buffer = new byte[sizeof( long ) + text.Length];
        BitConverter.TryWriteBytes( buffer.AsSpan(), dateTime.ToBinary() );
        Encoding.ASCII.GetBytes( text, buffer.AsSpan()[sizeof( long )..] );
        log.Enqueue( buffer );
    }

    /// <summary>
    /// Integration test that runs against a real InfluxDB when environment variables are set.
    /// Set CK_METRICS_INFLUXDB_TEST_URL, CK_METRICS_INFLUXDB_TEST_ORG, CK_METRICS_INFLUXDB_TEST_BUCKET,
    /// and CK_METRICS_INFLUXDB_TEST_TOKEN to enable.
    /// </summary>
    [Test]
    [CancelAfter( 30000 )]
    public async Task writes_metrics_to_real_influxdb_when_configured_Async( CancellationToken cancellationToken )
    {
        var url = Environment.GetEnvironmentVariable( "CK_METRICS_INFLUXDB_TEST_URL" );
        var org = Environment.GetEnvironmentVariable( "CK_METRICS_INFLUXDB_TEST_ORG" );
        var bucket = Environment.GetEnvironmentVariable( "CK_METRICS_INFLUXDB_TEST_BUCKET" );
        var token = Environment.GetEnvironmentVariable( "CK_METRICS_INFLUXDB_TEST_TOKEN" );

        if( string.IsNullOrEmpty( url ) )
        {
            Assert.Ignore( "Skipped: CK_METRICS_INFLUXDB_TEST_URL not set" );
        }

        Assume.That( org, Is.Not.Null.And.Not.Empty, "CK_METRICS_INFLUXDB_TEST_ORG must be set" );
        Assume.That( bucket, Is.Not.Null.And.Not.Empty, "CK_METRICS_INFLUXDB_TEST_BUCKET must be set" );
        Assume.That( token, Is.Not.Null.And.Not.Empty, "CK_METRICS_INFLUXDB_TEST_TOKEN must be set" );

        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( writes_metrics_to_real_influxdb_when_configured_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var logPath = Path.Combine( testDir, "faster-log" );

        using var device = Devices.CreateLogDevice( Path.Combine( logPath, "metrics.log" ), preallocateFile: false );
        var logSettings = new FasterLogSettings { LogDevice = device };
        using var log = new FasterLog( logSettings );

        var config = new InfluxDbConfiguration
        {
            ServerUrl = url,
            Org = org!,
            Bucket = bucket!,
            Token = token,
            UseGzip = false,
            FlushIntervalMs = 50,
            RetryDelayMs = 1000
        };

        // Generate unique test run ID for verification
        var testRunId = Guid.NewGuid().ToString();

        EnqueueMetricsEntry( log, "+Meter:1,\"test.meter\",\"1.0\",\"\",[]" );
        EnqueueMetricsEntry( log, "+Instrument:1,1,\"integration_test_requests\",\"Counter\",\"Int64\",false,\"Integration test metric\",\"\",[]" );
        // Include test_run_id tag to verify measurement-level tags are written
        EnqueueMetricsEntry( log, $"M:1,42.0,[\"test_run_id\",\"{testRunId}\"]" );
        await log.CommitAsync( cancellationToken );

        var consumer = new InfluxDbMetricsConsumer(
            log,
            "integration-test",
            config,
            "TestDomain",
            "Test",
            "IntegrationTest" );

        var cts = new CancellationTokenSource();
        await consumer.StartAsync( TestHelper.Monitor, cts.Token );

        // Wait for processing
        long address = 0;
        for( int i = 0; i < 20; i++ )
        {
            await Task.Delay( 200, cancellationToken );
            address = consumer.CompletedUntilAddress;
            if( address > 0 )
                break;
        }

        await cts.CancelAsync();
        await consumer.DisposeAsync();

        Assert.That( address, Is.GreaterThan( 0 ), "Consumer should have processed entries" );

        // Query InfluxDB to verify the metric was written correctly
        // Filter by the unique test_run_id tag to get only our test's data
        var fluxQuery = $"""
            from(bucket: "{bucket}")
              |> range(start: -1m)
              |> filter(fn: (r) => r._measurement == "integration_test_requests")
              |> filter(fn: (r) => r.test_run_id == "{testRunId}")
            """;

        var queryResult = await QueryInfluxDbAsync( url, org!, token!, fluxQuery, cancellationToken );

        // Verify the query result contains expected data
        Assert.That( queryResult, Does.Contain( "integration_test_requests" ), "Measurement name should be in result" );
        Assert.That( queryResult, Does.Contain( "42" ), "Value should be in result" );
        Assert.That( queryResult, Does.Contain( "ck_domain" ), "Domain tag should be in result" );
        Assert.That( queryResult, Does.Contain( "TestDomain" ), "Domain value should be in result" );
        Assert.That( queryResult, Does.Contain( "ck_environment" ), "Environment tag should be in result" );
        Assert.That( queryResult, Does.Contain( "Test" ), "Environment value should be in result" );
        Assert.That( queryResult, Does.Contain( "ck_party" ), "Party tag should be in result" );
        Assert.That( queryResult, Does.Contain( "IntegrationTest" ), "Party value should be in result" );
        Assert.That( queryResult, Does.Contain( "meter" ), "Meter tag should be in result" );
        Assert.That( queryResult, Does.Contain( "test.meter" ), "Meter value should be in result" );
        // Verify measurement-level tag was written
        Assert.That( queryResult, Does.Contain( "test_run_id" ), "Measurement tag key should be in result" );
        Assert.That( queryResult, Does.Contain( testRunId ), "Measurement tag value should be in result" );
    }

    static async Task<string> QueryInfluxDbAsync(
        string url,
        string org,
        string token,
        string fluxQuery,
        CancellationToken cancellationToken )
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Token", token );

        var queryUrl = $"{url.TrimEnd( '/' )}/api/v2/query?org={Uri.EscapeDataString( org )}";
        var request = new HttpRequestMessage( HttpMethod.Post, queryUrl );
        request.Content = new StringContent( fluxQuery, Encoding.UTF8, "application/vnd.flux" );
        request.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/csv" ) );

        var response = await client.SendAsync( request, cancellationToken );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync( cancellationToken );
    }

    sealed class TestInfluxDbDispatcher : CK.Metrics.MetricsLogDispatcher
    {
        readonly LineProtocolBuilder _builder;

        public int MeterCount { get; private set; }
        public int InstrumentCount { get; private set; }
        public int MeasureCount { get; private set; }

        public TestInfluxDbDispatcher( LineProtocolBuilder builder )
        {
            _builder = builder;
        }

        protected override object? OnNewMeter( IActivityMonitor monitor, CK.Metrics.MeterInfo info )
        {
            MeterCount++;
            return null;
        }

        protected override object? OnNewInstrument( IActivityMonitor monitor, CK.Metrics.FullInstrumentInfo instrument, object? meterState )
        {
            InstrumentCount++;
            return null;
        }

        protected override void OnDisposedMeter( IActivityMonitor monitor, CK.Metrics.MeterInfo meter, object? meterState, IReadOnlyList<(CK.Metrics.FullInstrumentInfo? Instrument, object? InstrumentState)> instruments )
        {
        }

        protected override void OnMeasure( IActivityMonitor monitor, CK.Metrics.FullInstrumentInfo instrument, object? instrumentState, DateTime measureTime, in CK.Metrics.ParsedMeasureLog measure )
        {
            MeasureCount++;
            _builder.AppendMeasurement( instrument, measure, measureTime );
        }
    }
}
