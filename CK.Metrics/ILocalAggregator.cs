using System.Diagnostics.Metrics;

namespace CK.Metrics;

/// <summary>
/// For .Net <see cref="Instrument"/>, this supports the cooler feature.
/// </summary>
/// <typeparam name="T">The type of the measure.</typeparam>
public interface ILocalAggregator<T> where T : struct
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
    public static readonly ILocalAggregator<T> Null = new NullLocalAggrgator<T>();

    sealed class NullLocalAggrgator<T> : ILocalAggregator<T> where T : struct
    {
        public NullLocalAggrgator()
        {
        }

        public bool HandleMeasure( T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags ) => true;

    }
}
