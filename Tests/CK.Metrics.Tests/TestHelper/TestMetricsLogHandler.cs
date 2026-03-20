using CK.Core;
using CK.Monitoring;
using System;
using System.Text;
using System.Threading.Tasks;

namespace CK.Metrics.Tests;

public sealed class TestMetricsLogHandler : IGrandOutputHandler
{
    readonly MetricsLogDispatcher _dispatcher;

    public TestMetricsLogHandler( MetricsLogDispatcher dispatcher )
    {
        _dispatcher = dispatcher;
    }

    public ValueTask<bool> ActivateAsync( IActivityMonitor monitor ) => ValueTask.FromResult( true );

    public ValueTask<bool> ApplyConfigurationAsync( IActivityMonitor monitor, IHandlerConfiguration c ) => ValueTask.FromResult( true );

    public ValueTask DeactivateAsync( IActivityMonitor monitor ) => default;

    public ValueTask HandleAsync( IActivityMonitor monitor, InputLogEntry logEvent )
    {
        if( logEvent.MonitorId == ActivityMonitor.StaticLogMonitorUniqueId
            && logEvent.Tags.Overlaps( DotNetMetrics.MetricsTag )
            && logEvent.Text is not null
            && Ascii.IsValid( logEvent.Text.AsSpan() ) )
        {
            _dispatcher.Add( monitor, logEvent.LogTime.TimeUtc, logEvent.Text );
        }
        return default;
    }

    public ValueTask OnTimerAsync( IActivityMonitor monitor, TimeSpan timerSpan ) => default;
}
