namespace CK.Metrics;

/// <summary>
/// Generalizes <see cref="MeterInfo"/> and <see cref="InstrumentInfo"/>.
/// </summary>
public interface ITrackedMetricsInfo
{
    /// <summary>
    /// Get an incremented number that identifies this <see cref="MeterInfo"/> or <see cref="InstrumentInfo"/>
    /// in the collector process.
    /// </summary>
    int TrackeId { get; }

    /// <summary>
    /// Get the <see cref="MeterInfo.Name"/> or <see cref="InstrumentInfo.Name"/>.
    /// </summary>
    string Name { get; }
}
