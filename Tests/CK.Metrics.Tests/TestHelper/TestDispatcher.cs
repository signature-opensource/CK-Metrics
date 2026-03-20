using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Metrics.Tests;

public class TestDispatcher : MetricsLogDispatcher
{
    public List<MeterInfo> NewMeters { get; } = new();
    public List<(FullInstrumentInfo Info, object? MeterState)> NewInstruments { get; } = new();
    public List<(FullInstrumentInfo Info, object? InstrumentState, DateTime Time, ParsedMeasureLog Measure)> Measures { get; } = new();
    public List<(MeterInfo Info, object? MeterState, IReadOnlyList<(FullInstrumentInfo?, object?)> Instruments)> DisposedMeters { get; } = new();

    protected override void OnDisposedMeter( IActivityMonitor monitor, MeterInfo meter, object? meterState, IReadOnlyList<(FullInstrumentInfo? Instrument, object? InstrumentState)> instruments )
    {
        DisposedMeters.Add( (meter, meterState, instruments) );
    }

    protected override void OnMeasure( IActivityMonitor monitor, FullInstrumentInfo instrument, object? instrumentState, DateTime measureTime, in ParsedMeasureLog measure )
    {
        Measures.Add( (instrument, instrumentState, measureTime, measure) );
    }

    protected override object? OnNewInstrument( IActivityMonitor monitor, FullInstrumentInfo instrument, object? meterState )
    {
        NewInstruments.Add( (instrument, meterState) );
        return null;
    }

    protected override object? OnNewMeter( IActivityMonitor monitor, MeterInfo info )
    {
        NewMeters.Add( info );
        return null;
    }
}
