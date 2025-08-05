using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Metrics.Tests.Metrics;


[TestFixture]
public class MeterLifeCycleTests
{
    List<string> _logs = new List<string>();

    [OneTimeSetUp]
    public void Setup()
    {
        ActivityMonitor.OnStaticLog += ActivityMonitor_OnStaticLog;
    }

    [OneTimeTearDown]
    public void Teardown()
    {
        ActivityMonitor.OnStaticLog -= ActivityMonitor_OnStaticLog;
        AllEnableOrDisable( false );
        _logs.Clear();
    }

    private static void AllEnableOrDisable( bool enable )
    {
        var c = new MetricsConfiguration();
        c.AutoObservableTimer = enable ? 50 : 0;
        c.Configurations.Add( (new InstrumentMatcher( "*" ), enable
                                                              ? InstrumentConfiguration.BasicEnabled
                                                              : InstrumentConfiguration.BasicDisabled) );
        DotNetMetrics.ApplyConfiguration( c );
    }

    void ActivityMonitor_OnStaticLog( ref ActivityMonitorLogData data )
    {
        lock( _logs )
        {
            _logs.Add( data.Text );
        }
    }

    [Test]
    public void disposed_Meter_are_detected_only_when_enabled()
    {
        DotNetMetrics.MetricsTag.ToString().ShouldBe( "Metrics" );
        AllEnableOrDisable( true );
        var threads = Enumerable.Range( 0, 8 )
                        .Select( i => new Thread( () => CreateAndDestroyMeters( $"T{i}", 5 ) ) )
                        .ToArray();
        foreach( var t in threads ) t.Start();
        foreach( var t in threads ) t.Join();
        Thread.Sleep( 200 );
        var metricsLogs = _logs.Select( MetricsLogParser.Create ).ToArray();
        int createdCount = metricsLogs.Count( e => e.Kind == MetricsLogKind.NewMeter );
        int disposedCount = metricsLogs.Count( e => e.Kind == MetricsLogKind.DisposedMeter );
        createdCount.ShouldBe( disposedCount );
    }

    [Test]
    public async Task disposed_Meter_are_detected_when_measuring_Async()
    {
        var meter = new Meter( "Some" );
        try
        {
            var inst = meter.CreateCounter<int>( "hat_sold" )
                            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

            var metrics = await DotNetMetrics.GetAvailableMetricsAsync();
            metrics.Instruments.Single().Configuration.Enabled.ShouldBeTrue();

            var disposer = Task.Run( async () =>
            {
                await Task.Delay( 100 );
                meter.Dispose();
                await Task.Delay( 50 );
            } );

            int i = 0;
            while( !disposer.IsCompleted )
            {
                inst.Add( i++ );
                Thread.Sleep( 10 );
            }
#pragma warning disable VSTHRD103 // Call async methods when in an async method
            metrics = DotNetMetrics.GetAvailableMetrics();
#pragma warning restore VSTHRD103 
            metrics.Instruments.ShouldBeEmpty();
        }
        finally
        {
            // Don't leave a meter on failure.
            meter.Dispose();
        }

    }

    static void CreateAndDestroyMeters( string name, int count )
    {
        var r = new Random();
        Meter?[] meters = new Meter[count];
        for( int i = 0; i < 3*meters.Length; i++ )
        {
            int idx = r.Next( 0, count );
            ref var m = ref meters[idx];
            if( m == null )
            {
                meters[idx] = CreateMeter( $"{name}.{i}", r.Next( 1, 5 ) );
            }
            else
            {
                if( r.Next( 0, 2 ) == 0 )
                {
                    m.Dispose();
                    m = null;
                }
            }
            Thread.Sleep( r.Next( 20, 50 ) );
        }
        for( int i = 0; i < meters.Length; i++ )
        {
            meters[i]?.Dispose();
        }
    }

    static Meter CreateMeter( string name, int instrumentCount )
    {
        var m = new Meter( name );
        for( int i = 0; i < instrumentCount; i++ )
        {
            m.CreateCounter<int>( $"{name}.{i}" );
        }
        return m;
    }
}

