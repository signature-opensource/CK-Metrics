using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace CK.Metrics;

public interface ILocalAggregator
{
    /// <summary>
    /// Flushes any pending data.
    /// </summary>
    void Flush();
}

/// <summary>
/// For .Net <see cref="Instrument"/>, this supports the cooler feature.
/// </summary>
/// <typeparam name="T">The type of the measure.</typeparam>
public interface ILocalAggregator<T> : ILocalAggregator where T : struct
{
    /// <summary>
    /// Handles the measure: returns true to emit it, false to skip.
    /// </summary>
    /// <param name="measurement">The measured value.</param>
    /// <param name="tags">The optional tags.</param>
    /// <returns></returns>
    bool HandleMeasure( T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags );

    /// <summary>
    /// Gets a null aggregator.
    /// </summary>
    public static readonly ILocalAggregator<T> Null = new NullLocalAggregator();

    sealed class NullLocalAggregator : ILocalAggregator<T>
    {
        public NullLocalAggregator()
        {
        }

        public void Flush() { }

        public bool HandleMeasure( T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags ) => true;

    }
}
