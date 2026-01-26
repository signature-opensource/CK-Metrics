using System.Text;
using CK.Core;
using CK.Monitoring;
using FASTER.core;
using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.Monitoring.Metrics.Csv.Tests;

[TestFixture]
public class CsvMetricsConsumerTests
{
    [Test]
    public async Task consumer_creates_csv_file_with_header_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( consumer_creates_csv_file_with_header_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var csvPath = Path.Combine( testDir, "metrics.csv" );
        var logPath = Path.Combine( testDir, "faster-log" );

        using var device = Devices.CreateLogDevice( Path.Combine( logPath, "metrics.log" ), preallocateFile: false );
        var logSettings = new FasterLogSettings { LogDevice = device };
        using var log = new FasterLog( logSettings );

        // Enqueue a simple metrics entry (Meter registration).
        // Format: +Meter:{meterId},"{name}","{version}","{telemetrySchemaUrl}",{tags}
        EnqueueMetricsEntry( log, "+Meter:1,\"test.meter\",\"1.0\",\"\",[]" );
        await log.CommitAsync();

        var cts = new CancellationTokenSource();
        // Use maxBatchAgeMs: 0 for immediate processing in tests.
        var consumer = new CsvMetricsConsumer( log, "test-consumer", csvPath, maxBatchAgeMs: 0 );
        await consumer.StartAsync( TestHelper.Monitor, cts.Token );

        // Wait for consumer to process.
        await Task.Delay( 500 );

        // Cancel and dispose consumer.
        await cts.CancelAsync();
        await consumer.DisposeAsync();

        // Verify CSV file exists with header.
        Assert.That( File.Exists( csvPath ), Is.True );
        var content = await File.ReadAllTextAsync( csvPath );
        Assert.That( content, Does.StartWith( "Timestamp,MeterName,InstrumentName,InstrumentType,Value,Tags" ) );
    }

    [Test]
    public async Task consumer_writes_measurements_to_csv_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( consumer_writes_measurements_to_csv_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var csvPath = Path.Combine( testDir, "metrics.csv" );
        var logPath = Path.Combine( testDir, "faster-log" );

        using var device = Devices.CreateLogDevice( Path.Combine( logPath, "metrics.log" ), preallocateFile: false );
        var logSettings = new FasterLogSettings { LogDevice = device };
        using var log = new FasterLog( logSettings );

        // Enqueue metrics using correct format: meter, instrument, and measurement.
        // Format: +Meter:{meterId},"{name}","{version}","{telemetrySchemaUrl}",{tags}
        EnqueueMetricsEntry( log, "+Meter:1,\"test.meter\",\"1.0\",\"\",[]" );
        // Format: +Instrument:{instrumentId},{meterId},"{name}","{typeName}","{measureTypeName}",{isObservable},"{description}","{unit}",{tags}
        EnqueueMetricsEntry( log, "+Instrument:1,1,\"requests\",\"Counter\",\"Int64\",false,\"Total requests\",\"\",[]" );
        // Format: M:{instrumentId},{value},[{tags}]
        EnqueueMetricsEntry( log, "M:1,42.0,[\"method\",\"GET\"]" );
        await log.CommitAsync();

        var cts = new CancellationTokenSource();
        // Use maxBatchAgeMs: 0 for immediate processing in tests.
        var consumer = new CsvMetricsConsumer( log, "test-consumer", csvPath, maxBatchAgeMs: 0 );
        await consumer.StartAsync( TestHelper.Monitor, cts.Token );

        // Wait for consumer to process.
        await Task.Delay( 500 );

        // Cancel and dispose consumer.
        await cts.CancelAsync();
        await consumer.DisposeAsync();

        // Verify CSV file contains measurement.
        var lines = await File.ReadAllLinesAsync( csvPath );
        Assert.That( lines.Length, Is.GreaterThanOrEqualTo( 2 ), "Expected header + at least 1 measurement" );
        Assert.That( lines[0], Does.StartWith( "Timestamp,MeterName" ) );

        if( lines.Length > 1 )
        {
            Assert.That( lines[1], Does.Contain( "test.meter" ) );
            Assert.That( lines[1], Does.Contain( "requests" ) );
        }
    }

    [Test]
    public async Task consumer_processes_entries_from_FasterLog_Async()
    {
        var testDir = Path.Combine( TestHelper.TestProjectFolder, "Logs", nameof( consumer_processes_entries_from_FasterLog_Async ) );
        if( Directory.Exists( testDir ) ) Directory.Delete( testDir, true );
        Directory.CreateDirectory( testDir );

        var csvPath = Path.Combine( testDir, "metrics.csv" );
        var logPath = Path.Combine( testDir, "faster-log" );

        using var device = Devices.CreateLogDevice( Path.Combine( logPath, "metrics.log" ), preallocateFile: false );
        var logSettings = new FasterLogSettings { LogDevice = device };
        using var log = new FasterLog( logSettings );

        // Enqueue multiple entries.
        for( int i = 0; i < 10; i++ )
        {
            EnqueueMetricsEntry( log, $"+Meter:{i},\"meter{i}\",\"1.0\",\"\",[]" );
        }
        await log.CommitAsync();

        var cts = new CancellationTokenSource();
        // Use maxBatchAgeMs: 0 for immediate processing in tests.
        var consumer = new CsvMetricsConsumer( log, "test-consumer", csvPath, maxBatchAgeMs: 0 );
        await consumer.StartAsync( TestHelper.Monitor, cts.Token );

        // Wait for consumer to process.
        await Task.Delay( 500 );

        // Verify consumer has processed entries.
        Assert.That( consumer.CompletedUntilAddress, Is.GreaterThan( 0 ) );

        // Cancel and dispose consumer.
        await cts.CancelAsync();
        await consumer.DisposeAsync();
    }

    static void EnqueueMetricsEntry( FasterLog log, string text )
    {
        var dateTime = DateTime.UtcNow;
        var buffer = new byte[sizeof( long ) + text.Length];
        BitConverter.TryWriteBytes( buffer.AsSpan(), dateTime.ToBinary() );
        Encoding.ASCII.GetBytes( text, buffer.AsSpan()[sizeof( long )..] );
        log.Enqueue( buffer );
    }
}
