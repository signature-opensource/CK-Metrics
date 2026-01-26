using CK.Core;
using CK.Metrics;
using CK.Monitoring;
using FASTER.core;

namespace CK.AppIdentity.Monitoring.Metrics;

/// <summary>
/// Action that injects a FasterLog instance into the <see cref="MetricsLogHandler"/>.
/// This is used by the <see cref="MetricsFeatureDriver"/> to provide the shared FasterLog.
/// </summary>
public sealed class SetMetricsFasterLogAction : GrandOutputHandlersAction
{
    readonly FasterLog _log;
    bool _found;

    /// <summary>
    /// Initializes a new <see cref="SetMetricsFasterLogAction"/>.
    /// </summary>
    /// <param name="log">The FasterLog instance to inject into the handler.</param>
    public SetMetricsFasterLogAction( FasterLog log )
    {
        Throw.CheckNotNullArgument( log );
        _log = log;
    }

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
            handler.SetFasterLog( _log );
            _found = true;
            monitor.Info( DotNetMetrics.MetricsInternalTag, $"Injected FasterLog into MetricsLogHandler." );
        }
        return ValueTask.CompletedTask;
    }
}
