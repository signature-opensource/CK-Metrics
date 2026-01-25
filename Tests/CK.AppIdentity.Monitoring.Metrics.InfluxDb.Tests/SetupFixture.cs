using CK.Core;
using CK.Metrics;
using NUnit.Framework;

namespace CK.AppIdentity.Monitoring.Metrics.InfluxDb.Tests;

[SetUpFixture]
public class SetupFixture
{
    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        ActivityMonitor.Tags.AddFilter( DotNetMetrics.MetricsTag, new LogClamper( LogFilter.Debug, true ) );
    }

    [OneTimeTearDown]
    public void RunAfterAnyTests()
    {
        ActivityMonitor.Tags.RemoveFilter( DotNetMetrics.MetricsTag );
    }
}
