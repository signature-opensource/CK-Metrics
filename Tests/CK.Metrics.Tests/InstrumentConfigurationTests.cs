using NUnit.Framework;
using Shouldly;
using System;

namespace CK.Metrics.Tests;

[TestFixture]
public class InstrumentConfigurationTests
{
    [Test]
    public void Constructor_enabled_true()
    {
        var config = new InstrumentConfiguration( true );
        config.Enabled.ShouldBeTrue();
    }

    [Test]
    public void Constructor_enabled_false()
    {
        var config = new InstrumentConfiguration( false );
        config.Enabled.ShouldBeFalse();
    }

    [Test]
    public void BasicEnabled_is_singleton()
    {
        InstrumentConfiguration.BasicEnabled.ShouldBeSameAs( InstrumentConfiguration.BasicEnabled );
        InstrumentConfiguration.BasicEnabled.Enabled.ShouldBeTrue();
    }

    [Test]
    public void BasicDisabled_is_singleton()
    {
        InstrumentConfiguration.BasicDisabled.ShouldBeSameAs( InstrumentConfiguration.BasicDisabled );
        InstrumentConfiguration.BasicDisabled.Enabled.ShouldBeFalse();
    }

    [Test]
    public void TryMatch_true_succeeds()
    {
        var span = "true".AsSpan();
        InstrumentConfiguration.TryMatch( ref span, out var config ).ShouldBeTrue();
        config.ShouldNotBeNull();
        config.Enabled.ShouldBeTrue();
        config.ShouldBeSameAs( InstrumentConfiguration.BasicEnabled );
        span.Length.ShouldBe( 0 );
    }

    [Test]
    public void TryMatch_false_succeeds()
    {
        var span = "false".AsSpan();
        InstrumentConfiguration.TryMatch( ref span, out var config ).ShouldBeTrue();
        config.ShouldNotBeNull();
        config.Enabled.ShouldBeFalse();
        config.ShouldBeSameAs( InstrumentConfiguration.BasicDisabled );
        span.Length.ShouldBe( 0 );
    }

    [Test]
    public void TryMatch_invalid_text_fails()
    {
        var span = "invalid".AsSpan();
        InstrumentConfiguration.TryMatch( ref span, out var config ).ShouldBeFalse();
        config.ShouldBeNull();
        span.ToString().ShouldBe( "invalid" );
    }

    [Test]
    public void TryMatch_empty_span_fails()
    {
        var span = "".AsSpan();
        InstrumentConfiguration.TryMatch( ref span, out var config ).ShouldBeFalse();
        config.ShouldBeNull();
    }

    [Test]
    public void TryMatch_with_trailing_content()
    {
        var span = "true,extra".AsSpan();
        InstrumentConfiguration.TryMatch( ref span, out var config ).ShouldBeTrue();
        config.ShouldNotBeNull();
        config.Enabled.ShouldBeTrue();
        span.ToString().ShouldBe( ",extra" );
    }

    [Test]
    public void TryMatch_restores_span_on_failure()
    {
        var original = "notabool";
        var span = original.AsSpan();
        InstrumentConfiguration.TryMatch( ref span, out var config ).ShouldBeFalse();
        config.ShouldBeNull();
        span.ToString().ShouldBe( original );
    }

    [Test]
    public void Equals_same_enabled_returns_true()
    {
        var config1 = new InstrumentConfiguration( true );
        var config2 = new InstrumentConfiguration( true );
        config1.Equals( config2 ).ShouldBeTrue();
    }

    [Test]
    public void Equals_different_enabled_returns_false()
    {
        var config1 = new InstrumentConfiguration( true );
        var config2 = new InstrumentConfiguration( false );
        config1.Equals( config2 ).ShouldBeFalse();
    }

    [Test]
    public void Equals_null_returns_false()
    {
        var config = new InstrumentConfiguration( true );
        config.Equals( null ).ShouldBeFalse();
    }

    [Test]
    public void Equals_object_overload()
    {
        var config1 = new InstrumentConfiguration( true );
        object config2 = new InstrumentConfiguration( true );
        config1.Equals( config2 ).ShouldBeTrue();

        object notConfig = "not a config";
        config1.Equals( notConfig ).ShouldBeFalse();
    }

    [Test]
    public void GetHashCode_consistency()
    {
        var config1 = new InstrumentConfiguration( true );
        var config2 = new InstrumentConfiguration( true );
        config1.GetHashCode().ShouldBe( config2.GetHashCode() );

        var config3 = new InstrumentConfiguration( false );
        var config4 = new InstrumentConfiguration( false );
        config3.GetHashCode().ShouldBe( config4.GetHashCode() );
    }
}
