using CK.Core;
using CK.Metrics;
using NUnit.Framework;

namespace CK.AppIdentity.Monitoring.Metrics.Csv.Tests;

[SetUpFixture]
public class SetupFixture
{
    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        ActivityMonitor.Tags.AddFilter( DotNetMetrics.MetricsInternalTag, new LogClamper( LogFilter.Debug, true ) );
    }

    [OneTimeTearDown]
    public void RunAfterAnyTests()
    {
        ActivityMonitor.Tags.RemoveFilter( DotNetMetrics.MetricsInternalTag );
    }
}
