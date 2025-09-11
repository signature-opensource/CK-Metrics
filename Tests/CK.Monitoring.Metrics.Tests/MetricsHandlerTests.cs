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

        DotNetMetrics.ApplyConfiguration( new MetricsConfiguration
        {
            AutoObservableTimer = 50,
            Configurations =
            {
                (new InstrumentMatcher( "*" ), InstrumentConfiguration.BasicEnabled)
            }
        }, waitForApplication: true );

        // In addition to the measures, there are 4 entries:
        // +Meter,
        // +Instrument,
        // +IConfig,
        // ... (measures come here)
        // -Meter
        const int meterCount = 21;
        const int measureCount = 21;
        int totalEntryCount = meterCount * ( measureCount + 4 );

        for( int i = 0; i < meterCount; i++ )
        {
            using var m = new Meter( $"test.meter{i}", "1.0" );
            var gauge = m.CreateGauge<int>( $"test.instrument{i}" );
            for( int j = 0; j < measureCount; j++ )
            {
                gauge.Record( j, new KeyValuePair<string, object?>( "a", "b" + j ) );
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
        dispatcher.NewMeters.Count.ShouldBe( meterCount );
        dispatcher.Instruments.Count.ShouldBe( meterCount );
        dispatcher.Measures.Count.ShouldBe( meterCount * measureCount );
        dispatcher.DisposedMeters.Count.ShouldBe( meterCount );

        for( int i = 0; i < meterCount; i++ )
        {
            for( int j = 0; j < measureCount; j++ )
            {
                int idx = i * measureCount + j;
                var item = dispatcher.Measures[idx];
                item.instrument.FullName.ShouldBe( $"test.meter{i}/test.instrument{i}" );
                item.measure.Measure.ToString().ShouldBe( j.ToString() );
                item.measure.Tags.ToString().ShouldBe( @$"""a"",""b{j}""" );
            }
        }

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
