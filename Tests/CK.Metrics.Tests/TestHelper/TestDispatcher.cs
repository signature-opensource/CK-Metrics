using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Metrics.Tests;

/// <summary>
/// Test implementation of <see cref="MetricsLogDispatcher"/> that records all dispatched
/// events into public lists for assertion.
/// <para>
/// By default, <see cref="OnNewMeter"/> returns <c>"MeterState-{MeterId}"</c> and
/// <see cref="OnNewInstrument"/> returns <c>"InstrumentState-{InstrumentId}"</c>.
/// Set <see cref="MeterStateProvider"/> or <see cref="InstrumentStateProvider"/> to
/// override the state returned for each meter or instrument.
/// </para>
/// </summary>
public class TestDispatcher : MetricsLogDispatcher
{
    /// <summary>
    /// Gets the list of meters that have been registered, along with their associated state.
    /// </summary>
    public List<(MeterInfo Info, object? State)> NewMeters { get; } = new();

    /// <summary>
    /// Gets the list of instruments that have been registered, along with the meter state
    /// and instrument state.
    /// </summary>
    public List<(FullInstrumentInfo Info, object? MeterState, object? InstrumentState)> NewInstruments { get; } = new();

    /// <summary>
    /// Gets the list of recorded measures.
    /// </summary>
    public List<(FullInstrumentInfo Info, object? InstrumentState, DateTime Time, ParsedMeasureLog Measure)> Measures { get; } = new();

    /// <summary>
    /// Gets the list of disposed meters, along with their instruments at the time of disposal.
    /// </summary>
    public List<(MeterInfo Info, object? MeterState, IReadOnlyList<(FullInstrumentInfo?, object?)> Instruments)> DisposedMeters { get; } = new();

    /// <summary>
    /// Optional factory for meter state. When null (the default), <see cref="OnNewMeter"/>
    /// returns <c>"MeterState-{MeterId}"</c>.
    /// </summary>
    public Func<IActivityMonitor, MeterInfo, object?>? MeterStateProvider { get; set; }

    /// <summary>
    /// Optional factory for instrument state. When null (the default), <see cref="OnNewInstrument"/>
    /// returns <c>"InstrumentState-{InstrumentId}"</c>.
    /// </summary>
    public Func<IActivityMonitor, FullInstrumentInfo, object?, object?>? InstrumentStateProvider { get; set; }

    /// <summary>
    /// Initializes a new <see cref="TestDispatcher"/>.
    /// </summary>
    /// <param name="maxExpectedMeterCount">Initial capacity for the meter tracker.</param>
    /// <param name="maxExpectedInstrumentCount">Initial capacity for the instrument tracker.</param>
    public TestDispatcher( int maxExpectedMeterCount = 100, int maxExpectedInstrumentCount = 200 )
        : base( maxExpectedMeterCount, maxExpectedInstrumentCount )
    {
    }

    /// <inheritdoc />
    protected override object? OnNewMeter( IActivityMonitor monitor, MeterInfo info )
    {
        var state = MeterStateProvider?.Invoke( monitor, info ) ?? $"MeterState-{info.MeterId}";
        NewMeters.Add( (info, state) );
        return state;
    }

    /// <inheritdoc />
    protected override object? OnNewInstrument( IActivityMonitor monitor, FullInstrumentInfo instrument, object? meterState )
    {
        var state = InstrumentStateProvider?.Invoke( monitor, instrument, meterState ) ?? $"InstrumentState-{instrument.Info.InstrumentId}";
        NewInstruments.Add( (instrument, meterState, state) );
        return state;
    }

    /// <inheritdoc />
    protected override void OnMeasure( IActivityMonitor monitor, FullInstrumentInfo instrument, object? instrumentState, DateTime measureTime, in ParsedMeasureLog measure )
    {
        Measures.Add( (instrument, instrumentState, measureTime, measure) );
    }

    /// <inheritdoc />
    protected override void OnDisposedMeter( IActivityMonitor monitor, MeterInfo meter, object? meterState, IReadOnlyList<(FullInstrumentInfo? Instrument, object? InstrumentState)> instruments )
    {
        DisposedMeters.Add( (meter, meterState, instruments) );
    }

    /// <summary>
    /// Clears all recorded events.
    /// </summary>
    public void Clear()
    {
        NewMeters.Clear();
        NewInstruments.Clear();
        Measures.Clear();
        DisposedMeters.Clear();
    }
}
