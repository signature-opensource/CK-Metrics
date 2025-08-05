using CK.Core;
using CK.Metrics;
using CK.Monitoring.Handlers;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Monitoring.Metrics;

public class GrandOutputMetricsHandler : IGrandOutputHandler
{
    readonly IdentityCard _identity;
    GrandOutputMetricsHandlerConfiguration _configuration;

    public GrandOutputMetricsHandler( GrandOutputMetricsHandlerConfiguration configuration, IdentityCard identity )
    {
        _configuration = configuration;
        _identity = identity;
    }

    public ValueTask<bool> ApplyConfigurationAsync( IActivityMonitor monitor, IHandlerConfiguration c )
    {
        if( c is GrandOutputMetricsHandlerConfiguration configuration )
        {
            return ValueTask.FromResult( true );
        }
        return ValueTask.FromResult( false );
    }

    public ValueTask<bool> ActivateAsync( IActivityMonitor monitor )
    {
        return ValueTask.FromResult( true );
    }

    public ValueTask DeactivateAsync( IActivityMonitor monitor )
    {
        return default;
    }

    public ValueTask HandleAsync( IActivityMonitor monitor, InputLogEntry logEvent )
    {
        if( logEvent.MonitorId == ActivityMonitor.StaticLogMonitorUniqueId
            && logEvent.Tags.Overlaps( DotNetMetrics.MetricsTag ) )
        {
            
        }
        return default;
    }

    public ValueTask OnTimerAsync( IActivityMonitor monitor, TimeSpan timerSpan )
    {
        return default;
    }

}
