using CK.Monitoring.Handlers;

namespace CK.Monitoring;

public class MetricsConfiguration : IHandlerConfiguration
{
    public MetricsConfiguration()
    {
    }

    public IHandlerConfiguration Clone()
    {
        var c = new MetricsConfiguration();
        return c;
    }
}
