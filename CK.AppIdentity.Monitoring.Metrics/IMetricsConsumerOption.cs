using CK.Core;
using FASTER.core;

namespace CK.AppIdentity.Monitoring.Metrics;

/// <summary>
/// A consumer of the metrics: an option of the "Metrics" feature (<see cref="MetricsFeatureDriver"/>), declared in its
/// section (<c>"Metrics": { "Csv": { ... } }</c>). A concrete option is decorated with
/// <c>[PartyFeatureOption( "Name", TypeActivation.Optional )]</c> and its <c>[Configuration&lt;TModel&gt;]</c>: its
/// constructor receives its model. Once the FasterLog is ready, the driver creates the consumer of each enabled option,
/// registers it (<see cref="MetricsFeatureDriver.RegisterConsumer(IMetricsConsumer)"/>) and starts it; its teardown
/// disposes them.
/// </summary>
public interface IMetricsConsumerOption : IPartyComponentOption<MetricsFeatureDriver>
{
    /// <summary>
    /// Creates the consumer (not started).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="log">The FasterLog of the metrics.</param>
    /// <param name="party">The root party (its local file store, its names).</param>
    /// <returns>The consumer.</returns>
    IMetricsConsumer CreateConsumer( IActivityMonitor monitor, FasterLog log, ApplicationIdentityService party );
}
