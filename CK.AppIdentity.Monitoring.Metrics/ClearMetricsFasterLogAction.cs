using CK.Core;
using CK.Monitoring;

namespace CK.AppIdentity.Monitoring.Metrics;

/// <summary>
/// Action that clears the FasterLog instance from the <see cref="MetricsLogHandler"/>.
/// This is used by the <see cref="MetricsFeatureDriver"/> during shutdown.
/// </summary>
public sealed class ClearMetricsFasterLogAction : GrandOutputHandlersAction
{
    bool _found;

    /// <summary>
    /// Gets whether the <see cref="MetricsLogHandler"/> was found in the handler list.
    /// </summary>
    public bool HandlerFound => _found;

    /// <inheritdoc />
    protected override ValueTask RunAsync( IActivityMonitor monitor, DispatcherSink.HandlerList handlers )
    {
        var handler = handlers.Handlers.OfType<MetricsLogHandler>().FirstOrDefault();
        if( handler != null )
        {
            handler.ClearFasterLog();
            _found = true;
            monitor.Info( $"Cleared FasterLog from MetricsLogHandler." );
        }
        return ValueTask.CompletedTask;
    }
}
