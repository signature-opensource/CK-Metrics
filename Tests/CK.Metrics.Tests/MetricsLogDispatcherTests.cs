using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using static CK.Testing.MonitorTestHelper;

namespace CK.Metrics.Tests;

[TestFixture]
public class MetricsLogDispatcherTests
{
    sealed class TestMetricsLogDispatcher : MetricsLogDispatcher
    {
        public List<(MeterInfo Info, object? State)> NewMeters { get; } = new();
        public List<(FullInstrumentInfo Info, object? MeterState, object? InstrumentState)> NewInstruments { get; } = new();
        public List<(FullInstrumentInfo Info, object? InstrumentState, DateTime Time, ParsedMeasureLog Measure)> Measures { get; } = new();
        public List<(MeterInfo Info, object? MeterState, IReadOnlyList<(FullInstrumentInfo?, object?)> Instruments)> DisposedMeters { get; } = new();

        public Func<IActivityMonitor, MeterInfo, object?>? MeterStateProvider { get; set; }
        public Func<IActivityMonitor, FullInstrumentInfo, object?, object?>? InstrumentStateProvider { get; set; }

        public TestMetricsLogDispatcher( int maxExpectedMeterCount = 100, int maxExpectedInstrumentCount = 200 )
            : base( maxExpectedMeterCount, maxExpectedInstrumentCount )
        {
        }

        protected override object? OnNewMeter( IActivityMonitor monitor, MeterInfo info )
        {
            var state = MeterStateProvider?.Invoke( monitor, info ) ?? $"MeterState-{info.MeterId}";
            NewMeters.Add( (info, state) );
            return state;
        }

        protected override object? OnNewInstrument( IActivityMonitor monitor, FullInstrumentInfo instrument, object? meterState )
        {
            var state = InstrumentStateProvider?.Invoke( monitor, instrument, meterState ) ?? $"InstrumentState-{instrument.Info.InstrumentId}";
            NewInstruments.Add( (instrument, meterState, state) );
            return state;
        }

        protected override void OnMeasure( IActivityMonitor monitor, FullInstrumentInfo instrument, object? instrumentState, DateTime measureTime, in ParsedMeasureLog measure )
        {
            Measures.Add( (instrument, instrumentState, measureTime, measure) );
        }

        protected override void OnDisposedMeter( IActivityMonitor monitor, MeterInfo meter, object? meterState, IReadOnlyList<(FullInstrumentInfo? Instrument, object? InstrumentState)> instruments )
        {
            DisposedMeters.Add( (meter, meterState, instruments) );
        }

        public void Clear()
        {
            NewMeters.Clear();
            NewInstruments.Clear();
            Measures.Clear();
            DisposedMeters.Clear();
        }
    }

    // Create log strings in the exact format the parser expects.
    // Format: meterId,"name","version","telemetrySchemaUrl",[tags]
    static string CreateNewMeterLog( int meterId, string name, string? version = null )
    {
        var versionStr = version != null ? $"\"{version}\"" : "\"\"";
        return $"+Meter:{meterId},\"{name}\",{versionStr},\"\",[]";
    }

    // Format: instrumentId,meterId,"name","typeName","measureTypeName",isObservable,"description","unit",[tags]
    static string CreateNewInstrumentLog( int instrumentId, int meterId, string name )
    {
        return $"+Instrument:{instrumentId},{meterId},\"{name}\",\"Counter`1\",\"Int32\",false,\"\",\"\",[]";
    }

    static string CreateMeasureLog( int instrumentId, double value )
    {
        return $"M:{instrumentId},{value}";
    }

    static string CreateDisposedMeterLog( int meterId, string name, string? version = null )
    {
        var versionStr = version != null ? $"\"{version}\"" : "\"\"";
        return $"-Meter:{meterId},\"{name}\",{versionStr},\"\",[]";
    }

    #region NewMeter Tests

    [Test]
    public void Add_NewMeter_calls_OnNewMeter()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var log = CreateNewMeterLog( 1, "Test.Meter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, log );

        dispatcher.NewMeters.Count.ShouldBe( 1 );
        dispatcher.NewMeters[0].Info.Name.ShouldBe( "Test.Meter" );
        dispatcher.NewMeters[0].Info.MeterId.ShouldBe( 1 );
        dispatcher.NewMeters[0].State.ShouldBe( "MeterState-1" );
    }

    [Test]
    public void Add_NewMeter_duplicate_logs_error()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var log = CreateNewMeterLog( 1, "Test.Meter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, log );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, log );

        dispatcher.NewMeters.Count.ShouldBe( 1 );
    }

    [Test]
    public void Add_NewMeter_invalid_parse_logs_error()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var log = "+Meter:invalid_meter_data";

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, log );

        dispatcher.NewMeters.Count.ShouldBe( 0 );
    }

    [Test]
    public void Add_NewMeter_with_version()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var log = CreateNewMeterLog( 2, "Test.Versioned", "1.2.3" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, log );

        dispatcher.NewMeters.Count.ShouldBe( 1 );
        dispatcher.NewMeters[0].Info.Version.ShouldBe( "1.2.3" );
    }

    #endregion

    #region NewInstrument Tests

    [Test]
    public void Add_NewInstrument_calls_OnNewInstrument()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var meterLog = CreateNewMeterLog( 1, "Test.Meter" );
        var instrumentLog = CreateNewInstrumentLog( 10, 1, "my.counter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, meterLog );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, instrumentLog );

        dispatcher.NewInstruments.Count.ShouldBe( 1 );
        dispatcher.NewInstruments[0].Info.Info.Name.ShouldBe( "my.counter" );
        dispatcher.NewInstruments[0].Info.Info.InstrumentId.ShouldBe( 10 );
        dispatcher.NewInstruments[0].MeterState.ShouldBe( "MeterState-1" );
        dispatcher.NewInstruments[0].InstrumentState.ShouldBe( "InstrumentState-10" );
    }

    [Test]
    public void Add_NewInstrument_duplicate_logs_error()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var meterLog = CreateNewMeterLog( 1, "Test.Meter" );
        var instrumentLog = CreateNewInstrumentLog( 10, 1, "my.counter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, meterLog );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, instrumentLog );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, instrumentLog );

        dispatcher.NewInstruments.Count.ShouldBe( 1 );
    }

    [Test]
    public void Add_NewInstrument_unknown_meter_logs_error()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var instrumentLog = CreateNewInstrumentLog( 10, 999, "my.counter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, instrumentLog );

        dispatcher.NewInstruments.Count.ShouldBe( 0 );
    }

    [Test]
    public void Add_NewInstrument_invalid_parse_logs_error()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var log = "+Instrument:invalid_instrument_data";

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, log );

        dispatcher.NewInstruments.Count.ShouldBe( 0 );
    }

    [Test]
    public void Add_NewInstrument_receives_meter_state()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var customMeterState = new { Custom = "MeterData" };
        dispatcher.MeterStateProvider = ( m, info ) => customMeterState;

        var meterLog = CreateNewMeterLog( 1, "Test.Meter" );
        var instrumentLog = CreateNewInstrumentLog( 10, 1, "my.counter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, meterLog );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, instrumentLog );

        dispatcher.NewInstruments[0].MeterState.ShouldBeSameAs( customMeterState );
    }

    #endregion

    #region Measure Tests

    [Test]
    public void Add_Measure_calls_OnMeasure()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var meterLog = CreateNewMeterLog( 1, "Test.Meter" );
        var instrumentLog = CreateNewInstrumentLog( 10, 1, "my.counter" );
        var measureLog = CreateMeasureLog( 10, 42.5 );
        var measureTime = DateTime.UtcNow;

        dispatcher.Add( TestHelper.Monitor, measureTime, meterLog );
        dispatcher.Add( TestHelper.Monitor, measureTime, instrumentLog );
        dispatcher.Add( TestHelper.Monitor, measureTime, measureLog );

        dispatcher.Measures.Count.ShouldBe( 1 );
        dispatcher.Measures[0].Info.Info.InstrumentId.ShouldBe( 10 );
        dispatcher.Measures[0].Time.ShouldBe( measureTime );
        dispatcher.Measures[0].Measure.InstrumentId.ShouldBe( 10 );
        dispatcher.Measures[0].InstrumentState.ShouldBe( "InstrumentState-10" );
    }

    [Test]
    public void Add_Measure_unknown_instrument_logs_error()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var measureLog = CreateMeasureLog( 999, 42.5 );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, measureLog );

        dispatcher.Measures.Count.ShouldBe( 0 );
    }

    [Test]
    public void Add_Measure_invalid_parse_logs_error()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var log = "M:invalid_measure";

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, log );

        dispatcher.Measures.Count.ShouldBe( 0 );
    }

    [Test]
    public void Add_Measure_with_tags()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var meterLog = CreateNewMeterLog( 1, "Test.Meter" );
        var instrumentLog = CreateNewInstrumentLog( 10, 1, "my.counter" );
        var measureLog = "M:10,100.5,[\"key\",\"value\"]";

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, meterLog );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, instrumentLog );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, measureLog );

        dispatcher.Measures.Count.ShouldBe( 1 );
        dispatcher.Measures[0].Measure.Tags.ToString().ShouldBe( "\"key\",\"value\"" );
    }

    [Test]
    public void Add_Multiple_measures_for_same_instrument()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var meterLog = CreateNewMeterLog( 1, "Test.Meter" );
        var instrumentLog = CreateNewInstrumentLog( 10, 1, "my.counter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, meterLog );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, instrumentLog );

        for( int i = 0; i < 5; i++ )
        {
            dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, CreateMeasureLog( 10, i * 10 ) );
        }

        dispatcher.Measures.Count.ShouldBe( 5 );
    }

    #endregion

    #region DisposedMeter Tests

    [Test]
    public void Add_DisposedMeter_calls_OnDisposedMeter()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var meterLog = CreateNewMeterLog( 1, "Test.Meter" );
        var disposedLog = CreateDisposedMeterLog( 1, "Test.Meter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, meterLog );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, disposedLog );

        dispatcher.DisposedMeters.Count.ShouldBe( 1 );
        dispatcher.DisposedMeters[0].Info.Name.ShouldBe( "Test.Meter" );
        dispatcher.DisposedMeters[0].MeterState.ShouldBe( "MeterState-1" );
    }

    [Test]
    public void Add_DisposedMeter_cleans_up_instruments()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var meterLog = CreateNewMeterLog( 1, "Test.Meter" );
        var instrumentLog1 = CreateNewInstrumentLog( 10, 1, "counter1" );
        var instrumentLog2 = CreateNewInstrumentLog( 11, 1, "counter2" );
        var disposedLog = CreateDisposedMeterLog( 1, "Test.Meter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, meterLog );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, instrumentLog1 );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, instrumentLog2 );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, disposedLog );

        dispatcher.DisposedMeters.Count.ShouldBe( 1 );
        dispatcher.DisposedMeters[0].Instruments.Count.ShouldBe( 2 );
    }

    [Test]
    public void Add_DisposedMeter_unknown_meter_logs_error()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var disposedLog = CreateDisposedMeterLog( 999, "Unknown.Meter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, disposedLog );

        dispatcher.DisposedMeters.Count.ShouldBe( 0 );
    }

    [Test]
    public void Add_DisposedMeter_returns_empty_list_when_no_instruments()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var meterLog = CreateNewMeterLog( 1, "Test.Meter" );
        var disposedLog = CreateDisposedMeterLog( 1, "Test.Meter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, meterLog );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, disposedLog );

        dispatcher.DisposedMeters[0].Instruments.Count.ShouldBe( 0 );
    }

    [Test]
    public void Add_DisposedMeter_invalid_parse_logs_error()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var log = "-Meter:invalid_meter_data";

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, log );

        dispatcher.DisposedMeters.Count.ShouldBe( 0 );
    }

    #endregion

    #region Lifecycle Tests

    [Test]
    public void Full_meter_lifecycle_create_measure_dispose()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        var meterLog = CreateNewMeterLog( 1, "Test.Meter" );
        var instrumentLog = CreateNewInstrumentLog( 10, 1, "my.counter" );
        var measureLog = CreateMeasureLog( 10, 42.0 );
        var disposedLog = CreateDisposedMeterLog( 1, "Test.Meter" );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, meterLog );
        dispatcher.NewMeters.Count.ShouldBe( 1 );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, instrumentLog );
        dispatcher.NewInstruments.Count.ShouldBe( 1 );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, measureLog );
        dispatcher.Measures.Count.ShouldBe( 1 );

        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, disposedLog );
        dispatcher.DisposedMeters.Count.ShouldBe( 1 );
        dispatcher.DisposedMeters[0].Instruments.Count.ShouldBe( 1 );

        // After disposal, measures for this instrument should fail
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, measureLog );
        dispatcher.Measures.Count.ShouldBe( 1 ); // Still 1, no new measure added
    }

    [Test]
    public void Multiple_meters_independent_lifecycle()
    {
        var dispatcher = new TestMetricsLogDispatcher();

        // Create two meters
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, CreateNewMeterLog( 1, "Meter.One" ) );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, CreateNewMeterLog( 2, "Meter.Two" ) );

        // Create instruments for each
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, CreateNewInstrumentLog( 10, 1, "counter.one" ) );
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, CreateNewInstrumentLog( 20, 2, "counter.two" ) );

        dispatcher.NewMeters.Count.ShouldBe( 2 );
        dispatcher.NewInstruments.Count.ShouldBe( 2 );

        // Dispose first meter
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, CreateDisposedMeterLog( 1, "Meter.One" ) );
        dispatcher.DisposedMeters.Count.ShouldBe( 1 );

        // Second meter's instrument should still work
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, CreateMeasureLog( 20, 100.0 ) );
        dispatcher.Measures.Count.ShouldBe( 1 );

        // First meter's instrument should not work
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, CreateMeasureLog( 10, 50.0 ) );
        dispatcher.Measures.Count.ShouldBe( 1 ); // Still 1

        // Dispose second meter
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, CreateDisposedMeterLog( 2, "Meter.Two" ) );
        dispatcher.DisposedMeters.Count.ShouldBe( 2 );
    }

    [Test]
    public void Unrecognized_log_kind_is_ignored()
    {
        var dispatcher = new TestMetricsLogDispatcher();
        dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, "Random text that is not a metric log" );

        dispatcher.NewMeters.Count.ShouldBe( 0 );
        dispatcher.NewInstruments.Count.ShouldBe( 0 );
        dispatcher.Measures.Count.ShouldBe( 0 );
        dispatcher.DisposedMeters.Count.ShouldBe( 0 );
    }

    [Test]
    public void Constructor_with_custom_counts()
    {
        var dispatcher = new TestMetricsLogDispatcher( maxExpectedMeterCount: 5, maxExpectedInstrumentCount: 10 );

        // Add more than expected to test overflow to remainders
        for( int i = 0; i < 10; i++ )
        {
            dispatcher.Add( TestHelper.Monitor, DateTime.UtcNow, CreateNewMeterLog( i, $"Meter.{i}" ) );
        }

        dispatcher.NewMeters.Count.ShouldBe( 10 );
    }

    #endregion
}
