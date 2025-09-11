using System;
using System.Diagnostics.Metrics;

namespace CK.Metrics;

/// <summary>
/// Applies only to <see cref="Instrument{T}"/> (push), not to <see cref="ObservableInstrument{T}"/> (pull).
/// <para>
/// Currently unused.
/// </para>
/// </summary>
public enum LocalAggregateKind
{
    /// <summary>
    /// No local aggregation is done: the type of the measure is the type of the <see cref="Instrument{T}"/>.
    /// </summary>
    None = 0,

    /// <summary>
    /// Applies to <see cref="Counter{T}"/> and <see cref="UpDownCounter{T}"/> where T is byte, short, int.
    /// This is accepted also for long but with a warning: aggregating multiple long in a long can easily overflow
    /// (at least easier than with byte...) but this is allowed and up to the developper/sys-op to control this.
    /// <para>
    /// Note that overflow is clamped to the <see cref="long.MaxValue"/>.
    /// </para>
    /// </summary>
    LongSum,

    /// <summary>
    /// Applies to <see cref="Counter{T}"/>, <see cref="UpDownCounter{T}"/> and <see cref="Gauge{T}"/> where T is byte, short, int.
    /// This is accepted also for long but with a warning: aggregating multiple long in a long can easily overflow
    /// (at least easier than with byte...) but this is allowed and up to the developper/sys-op to control this.
    /// <para>
    /// The measured type is a <see cref="ValueTuple{T1, T2}"/> <c>(long Sum, int Count)</c>.
    /// </para>
    /// <para>
    /// Note that Sum overflow is clamped to the <see cref="long.MaxValue"/> and Count to <see cref="int.MaxValue"/>.
    /// </para>
    /// </summary>
    LongSumCount
}

