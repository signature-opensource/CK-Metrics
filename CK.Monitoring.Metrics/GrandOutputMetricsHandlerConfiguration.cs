using CK.Monitoring.Handlers;

namespace CK.Monitoring.Metrics;

public class GrandOutputMetricsHandlerConfiguration : IHandlerConfiguration
{
    public GrandOutputMetricsHandlerConfiguration()
    {
    }

    public IHandlerConfiguration Clone()
    {
        var c = new GrandOutputMetricsHandlerConfiguration();
        return c;
    }
}
