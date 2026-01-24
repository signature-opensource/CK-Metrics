using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CK.AppIdentity.Monitoring.Metrics;
using CK.Core;
using CK.Metrics;
using CK.Monitoring.Handlers;
using FASTER.core;
using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;

namespace CK.Monitoring.Metrics.Tests;

[TestFixture]
public class MetricsHandlerTests
{
    [Test, CancelAfter( 10000 )]
    public async Task MetricsLogHandler_writes_metrics_to_FasterLog_Async( CancellationToken ct )
    {
        var fasterLogPath = PrepareFasterLogDir();

        // Create FasterLog.
        using var device = Devices.CreateLogDevice( Path.Combine( fasterLogPath, "metrics.log" ), preallocateFile: false );
        var logSettings = new FasterLogSettings { LogDevice = device };
        using var log = new FasterLog( logSettings );

        // Create handler configuration.
        var handlerConfig = new MetricsLogHandlerConfiguration { CommitRate = 1 };

        await using var go = GrandOutput.EnsureActiveDefault( new GrandOutputConfiguration
        {
            MinimalFilter = LogFilter.Debug,
            Handlers =
            {
                handlerConfig,
                new TextFileConfiguration() { Path = "Text" }
            }
        } );

        // Inject FasterLog into handler via action.
        var setAction = new SetMetricsFasterLogAction( log );
        go.Sink.Submit( setAction );
        await setAction.Completion;
        Assert.That( setAction.HandlerFound, Is.True );

        DotNetMetrics.ApplyConfiguration( new MetricsConfiguration
        {
            AutoObservableTimer = 50,
            Configurations =
            {
                (new InstrumentMatcher( "*" ), InstrumentConfiguration.BasicEnabled)
            }
        }, waitForApplication: true );

        // Create meters and record measurements.
        const int meterCount = 5;
        const int measureCount = 10;

        for( int i = 0; i < meterCount; i++ )
        {
            using var m = new Meter( $"test.meter{i}", "1.0" );
            var gauge = m.CreateGauge<int>( $"test.instrument{i}" );
            for( int j = 0; j < measureCount; j++ )
            {
                gauge.Record( j, new KeyValuePair<string, object?>( "a", "b" + j ) );
            }
        }

        // Wait for entries to be written to FasterLog.
        await Task.Delay( 1000, ct );
        await log.CommitAsync( ct );

        // Read entries from FasterLog.
        var entries = new List<(DateTime Time, string Text)>();
        using var iter = log.Scan( 0, long.MaxValue );
        while( iter.GetNext( out var data, out var length, out _ ) )
        {
            var dateTime = DateTime.FromBinary( BitConverter.ToInt64( data, 0 ) );
            var text = Encoding.ASCII.GetString( data, sizeof( long ), length - sizeof( long ) );
            entries.Add( (dateTime, text) );
        }

        // Verify entries were written.
        Assert.That( entries.Count, Is.GreaterThan( 0 ) );

        // Parse entries using dispatcher.
        var dispatcher = new TestMetricsLogDispatcher();
        var monitor = new ActivityMonitor( "TestMetricsLogDispatcher" );
        foreach( var entry in entries )
        {
            dispatcher.Add( monitor, entry.Time, entry.Text );
        }

        // Verify dispatched entries.
        Assert.That( dispatcher.NewMeters.Count, Is.EqualTo( meterCount ) );
        Assert.That( dispatcher.Instruments.Count, Is.EqualTo( meterCount ) );
        Assert.That( dispatcher.Measures.Count, Is.EqualTo( meterCount * measureCount ) );
        Assert.That( dispatcher.DisposedMeters.Count, Is.EqualTo( meterCount ) );
    }

    [Test]
    public void SetFasterLog_throws_if_already_set()
    {
        var fasterLogPath = PrepareFasterLogDir();

        using var device = Devices.CreateLogDevice( Path.Combine( fasterLogPath, "metrics.log" ), preallocateFile: false );
        var logSettings = new FasterLogSettings { LogDevice = device };
        using var log = new FasterLog( logSettings );

        var handler = new MetricsLogHandler( new MetricsLogHandlerConfiguration() );
        handler.SetFasterLog( log );

        Assert.Throws<InvalidOperationException>( () => handler.SetFasterLog( log ) );
    }

    [Test]
    public async Task ApplyConfigurationAsync_accepts_same_handler_type_Async()
    {
        var handler = new MetricsLogHandler( new MetricsLogHandlerConfiguration { CommitRate = 1 } );
        await handler.ActivateAsync( TestHelper.Monitor );

        var newConfig = new MetricsLogHandlerConfiguration { CommitRate = 5 };
        var result = await handler.ApplyConfigurationAsync( TestHelper.Monitor, newConfig );

        Assert.That( result, Is.True );
    }

    [Test]
    public async Task ApplyConfigurationAsync_rejects_different_handler_type_Async()
    {
        var handler = new MetricsLogHandler( new MetricsLogHandlerConfiguration() );
        await handler.ActivateAsync( TestHelper.Monitor );

        var otherConfig = new TextFileConfiguration { Path = "test" };
        var result = await handler.ApplyConfigurationAsync( TestHelper.Monitor, otherConfig );

        Assert.That( result, Is.False );
    }

    string PrepareFasterLogDir()
    {
        var path = Path.Combine( TestHelper.TestProjectFolder, "Logs", TestContext.CurrentContext.Test.Name,
            "FasterLog" );
        if( Directory.Exists( path ) ) Directory.Delete( path, true );
        Directory.CreateDirectory( path );
        return path;
    }

    record struct MeasureInfo( FullInstrumentInfo instrument, DateTime measureTime, ParsedMeasureLog measure );

    class TestMetricsLogDispatcher : MetricsLogDispatcher
    {
        public List<MeterInfo> NewMeters { get; } = new List<MeterInfo>();
        public List<MeterInfo> DisposedMeters { get; } = new List<MeterInfo>();
        public List<FullInstrumentInfo> Instruments { get; } = new List<FullInstrumentInfo>();
        public List<MeasureInfo> Measures { get; } = new List<MeasureInfo>();

        protected override object? OnNewMeter( IActivityMonitor monitor, MeterInfo info )
        {
            NewMeters.Add( info );
            return this;
        }

        protected override void OnDisposedMeter( IActivityMonitor monitor, MeterInfo meter, object? meterState,
            IReadOnlyList<(FullInstrumentInfo? Instrument, object? InstrumentState)> instruments )
        {
            DisposedMeters.Add( meter );
        }

        protected override object? OnNewInstrument( IActivityMonitor monitor, FullInstrumentInfo instrument,
            object? meterState )
        {
            Instruments.Add( instrument );
            return this;
        }

        protected override void OnMeasure( IActivityMonitor monitor, FullInstrumentInfo instrument,
            object? instrumentState, DateTime measureTime,
            in ParsedMeasureLog measure )
        {
            Measures.Add( new MeasureInfo( instrument, measureTime, measure ) );
        }
    }
}
