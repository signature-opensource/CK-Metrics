using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;
using CK.Metrics;
using CK.Monitoring.Handlers;
using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;

namespace CK.Monitoring.Metrics.Tests;

[TestFixture]
public class MetricsHandlerTests
{
    [Test, CancelAfter( 10000 )]
    public async Task MetricsHandlerBase_can_process_metrics_Async( CancellationToken ct )
    {
        var fasterLogPath = PrepareFasterLogDir();

        var testHandlerConfig = new TestMetricsConfiguration()
        {
            FasterLogPath = fasterLogPath,
            CommitRate = 1
        };

        await using var go = GrandOutput.EnsureActiveDefault( new GrandOutputConfiguration
        {
            MinimalFilter = LogFilter.Debug,
            Handlers =
            {
                testHandlerConfig,
                new TextFileConfiguration() {Path = "Text"}
            }
        } );

        // ApplyConfiguration sends the configuration to MicroAgent without waiting for it. :(
        // TODO: Find a way to wait for the MicroAgent's configuration.
        DotNetMetrics.ApplyConfiguration( new MetricsConfiguration
        {
            AutoObservableTimer = 50,
            Configurations =
            {
                (new InstrumentMatcher( "*" ), InstrumentConfiguration.BasicEnabled)
            }
        } );

        // In addition to the measures, there are 4 entries: +Meter, +Instrument, +IConfig, -Meter
        int measureCount = 11;
        int totalEntryCount = measureCount + 4;
        using( var m = new Meter( "test.meter", "1.0" ) )
        {
            var gauge = m.CreateGauge<int>( "test.instrument" );

            // Force-wait for MicroAgent by sending a GetAvailableMetrics command to it.
            // Remove the below line, and the Instruments won't have a listener by the time they start measuring,
            // and this test will time out. :(
            // TODO: Find a way to wait for the MicroAgent's configuration.
            // TODO: Race condition: InstrumentsPublished sends InstrumentState on MicroAgent and does not wait for it
            await DotNetMetrics.GetConfigurationAsync();

            for( int i = 0; i < measureCount; i++ )
            {
                gauge.Record( i, new KeyValuePair<string, object?>( "a", "b" + i ) );
            }
        }

        while( testHandlerConfig.TestEntryQueue.Count < totalEntryCount )
        {
            await Task.Delay( 500, ct );
            ct.ThrowIfCancellationRequested();
        }

        // Put entries in dispatcher
        var dispatcher = new TestMetricsLogDispatcher();
        var monitor = new ActivityMonitor( "TestMetricsLogDispatcher" );
        while( testHandlerConfig.TestEntryQueue.TryDequeue( out var entry ) )
        {
            var dateTime = DateTime.FromBinary( BitConverter.ToInt64( entry, 0 ) );
            var text = System.Text.Encoding.ASCII.GetString( entry.AsSpan( sizeof(long) ) );
            monitor.Info( $"Received after FasterLog: {text} @ {dateTime:O}" );
            dispatcher.Add( monitor, dateTime, text );
        }

        // Check entries
        dispatcher.NewMeters.Count.ShouldBe( 1 );
        dispatcher.Instruments.Count.ShouldBe( 1 );
        dispatcher.Measures.Count.ShouldBe( measureCount );

        for( int i = 0; i < measureCount; i++ )
        {
            var item = dispatcher.Measures[i];
            TestHelper.Monitor.Info( item.measure.Measure.ToString() );
            item.measure.Measure.ToString().ShouldBe( i.ToString() );
            item.measure.Tags.ToString().ShouldBe( @$"""a"",""b{i}""" );
        }

        dispatcher.DisposedMeters.Count.ShouldBe( 1 );
    }

    private string PrepareFasterLogDir()
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
