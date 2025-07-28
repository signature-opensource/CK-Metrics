using CK.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;

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
    }

    void ActivityMonitor_OnStaticLog( ref ActivityMonitorLogData data )
    {
        lock( _logs )
        {
            _logs.Add( data.Text );
        }
    }

    [Test]
    public void disposed_Meter_are_detected()
    {
        DotNetMetrics.MetricsTag.ToString().ShouldBe( "Metrics" );
        var threads = Enumerable.Range( 0, 2 )
                        .Select( i => new Thread( () => CreateAndDestroyMeters( $"T{i}", 3 ) ) )
                        .ToArray();
        foreach( var t in threads ) t.Start();
        foreach( var t in threads ) t.Join();
        Thread.Sleep( 200 );
        var metricsLogs = _logs.Select( ParseMetricsLog ).ToArray();
        int createdCount = metricsLogs.Count( e => e.Kind == MetricsLogKind.NewMeter );
        int disposedCount = metricsLogs.Count( e => e.Kind == MetricsLogKind.DisposedMeter );
        createdCount.ShouldBe( disposedCount );
    }

    public enum MetricsLogKind
    {
        None = 0,
        NewMeter,
        DisposedMeter,
        NewInstrument,
        Measure
    }

    public struct MetricsLogParser
    {
        public readonly string Text;

        public readonly MetricsLogKind Kind;

        public MetricsLogParser( string text, MetricsLogKind kind )
        {
            Text = text;
            Kind = kind;
        }
    }

    public static MetricsLogParser ParseMetricsLog( string text )
    {
        Throw.CheckNotNullArgument( text );
        if( text.Length >= 3 )
        {
            if( text.StartsWith( "+Meter[" ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.NewMeter );
            }
            if( text.StartsWith( "-Meter[" ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.DisposedMeter );
            }
            if( text.StartsWith( "+Instrument[" ) )
            {
                return new MetricsLogParser( text, MetricsLogKind.NewInstrument );
            }
            if( text.Contains(':') )
            {
                return new MetricsLogParser( text, MetricsLogKind.Measure );
            }
        }
        return new MetricsLogParser( text, MetricsLogKind.None );
        
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
