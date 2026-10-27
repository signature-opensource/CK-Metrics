using NUnit.Framework;
using Shouldly;
using System.Diagnostics.Metrics;
using System.Linq;

namespace CK.Metrics.Tests;

[TestFixture]
public class DefaultConfigureTests
{
    [TearDown]
    public void TearDown()
    {
        DotNetMetrics.ApplyConfiguration( new MetricsConfiguration { AutoObservableTimer = 0 }, waitForApplication: true );
    }

    static InstrumentConfiguration ConfigurationOf( string fullName )
    {
        return DotNetMetrics.GetConfiguration().Instruments.Single( i => i.FullName == fullName ).Configuration;
    }

    [Test]
    public void an_explicit_disabled_configuration_beats_DefaultConfigure()
    {
        var c = new MetricsConfiguration();
        c.Configurations.Add( (new InstrumentMatcher( "Test.Explicit/*" ), InstrumentConfiguration.BasicDisabled) );
        DotNetMetrics.ApplyConfiguration( c, waitForApplication: true );

        using var meter = new Meter( "Test.Explicit" );
        meter.CreateCounter<int>( "counter" ).DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        ConfigurationOf( "Test.Explicit/counter" ).ShouldBe( InstrumentConfiguration.BasicDisabled );
    }

    [Test]
    public void DefaultConfigure_applies_when_no_configuration_matches()
    {
        var c = new MetricsConfiguration();
        c.Configurations.Add( (new InstrumentMatcher( "Test.Other/*" ), InstrumentConfiguration.BasicDisabled) );
        DotNetMetrics.ApplyConfiguration( c, waitForApplication: true );

        using var meter = new Meter( "Test.NotConfigured" );
        meter.CreateCounter<int>( "counter" ).DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        ConfigurationOf( "Test.NotConfigured/counter" ).ShouldBe( InstrumentConfiguration.BasicEnabled );
    }
}
