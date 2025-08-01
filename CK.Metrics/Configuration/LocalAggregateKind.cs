using System;
using System.Diagnostics.Metrics;

namespace CK.Metrics;

public enum LocalAggregateKind
{
    /// <summary>
    /// No local aggregation is done: the type of the measure is the type of the <see cref="Instrument{T}"/>
    /// and <see cref="ObservableInstrument{T}"/>.
    /// </summary>
    None = 0,

    /// <summary>
    /// Applies to <see cref="Counter{T}"/> and <see cref="ObservableCounter{T}"/> where T is byte, short, int.
    /// This is accepted also for long but with a warning: aggregating multiple long in a long can easily overflow
    /// (at least easier than with byte...) but this is allowed and up to the developper/sysop to control this.
    /// <para>
    /// Note that overflow is clamped to the <see cref="long.MaxValue"/>.
    /// </para>
    /// </summary>
    LongSum,

    /// <summary>
    /// Same as <see cref="LongSum"/> but with a int count of measures. The measured type is a <see cref="ValueTuple{T1, T2}"/>
    /// <c>(long Sum, int Count)</c>.
    /// </summary>
    LongSumCount
}

