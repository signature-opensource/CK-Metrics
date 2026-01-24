using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Metrics.Tests;

[TestFixture]
public class InstrumentMatcherTests
{
    [SetUp]
    public void Setup()
    {
        // Ensure metrics are enabled for tests that need real instruments
        var c = new MetricsConfiguration();
        c.AutoObservableTimer = 50;
        c.Configurations.Add( (new InstrumentMatcher( "*" ), InstrumentConfiguration.BasicEnabled) );
        DotNetMetrics.ApplyConfiguration( c, waitForApplication: true );
    }

    [TearDown]
    public void TearDown()
    {
        // Disable metrics after tests
        var c = new MetricsConfiguration();
        c.AutoObservableTimer = 0;
        c.Configurations.Add( (new InstrumentMatcher( "*" ), InstrumentConfiguration.BasicDisabled) );
        DotNetMetrics.ApplyConfiguration( c, waitForApplication: true );
    }

    #region Wildcard Pattern Tests

    [Test]
    public void Pattern_star_matches_lazy()
    {
        using var meter = new Meter( "Test.Lazy" );
        var counter = meter.CreateCounter<int>( "my.counter" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "Test.Lazy/my.counter" );
        instrument.ShouldNotBeNull();

        // "*" should match lazily
        var matcher = new InstrumentMatcher( "Test.*/my.*" );
        matcher.Match( instrument ).ShouldBeTrue();
    }

    [Test]
    public void Pattern_double_star_matches_greedy()
    {
        using var meter = new Meter( "Test.Greedy" );
        var counter = meter.CreateCounter<int>( "deep.nested.counter" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "Test.Greedy/deep.nested.counter" );
        instrument.ShouldNotBeNull();

        // "**" should match greedily
        var matcher = new InstrumentMatcher( "Test.Greedy/**" );
        matcher.Match( instrument ).ShouldBeTrue();
    }

    [Test]
    public void Pattern_question_mark_matches_single_char()
    {
        using var meter = new Meter( "Test.Single" );
        var counter = meter.CreateCounter<int>( "abc" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "Test.Single/abc" );
        instrument.ShouldNotBeNull();

        // "?" should match exactly one character
        var matcher = new InstrumentMatcher( "Test.Single/a?c" );
        matcher.Match( instrument ).ShouldBeTrue();

        var matcherFail = new InstrumentMatcher( "Test.Single/a?" );
        matcherFail.Match( instrument ).ShouldBeFalse();
    }

    [Test]
    public void Pattern_mixed_wildcards()
    {
        using var meter = new Meter( "Mix.Wildcards" );
        var counter = meter.CreateCounter<int>( "prefix.middle.suffix" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "Mix.Wildcards/prefix.middle.suffix" );
        instrument.ShouldNotBeNull();

        // Mix of wildcards
        var matcher = new InstrumentMatcher( "Mix.?ildcards/prefix.*.suffix" );
        matcher.Match( instrument ).ShouldBeTrue();
    }

    [Test]
    public void Pattern_with_special_regex_chars_escaped()
    {
        using var meter = new Meter( "Test.Special" );
        var counter = meter.CreateCounter<int>( "name.with.dots" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "Test.Special/name.with.dots" );
        instrument.ShouldNotBeNull();

        // Dots should be escaped and match literally
        var matcher = new InstrumentMatcher( "Test.Special/name.with.dots" );
        matcher.Match( instrument ).ShouldBeTrue();

        // This should not match because the dot is literal
        var matcherFail = new InstrumentMatcher( "Test.Special/namexwithxdots" );
        matcherFail.Match( instrument ).ShouldBeFalse();
    }

    [Test]
    public void Universal_pattern_star_star_matches_everything()
    {
        using var meter = new Meter( "Test.Universal" );
        var counter = meter.CreateCounter<int>( "any.instrument" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "Test.Universal/any.instrument" );
        instrument.ShouldNotBeNull();

        var matcherSingleStar = new InstrumentMatcher( "*" );
        matcherSingleStar.Match( instrument ).ShouldBeTrue();

        var matcherDoubleStar = new InstrumentMatcher( "**" );
        matcherDoubleStar.Match( instrument ).ShouldBeTrue();
    }

    [Test]
    public void Universal_pattern_with_whitespace_trimmed()
    {
        using var meter = new Meter( "Test.Whitespace" );
        var counter = meter.CreateCounter<int>( "counter" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "Test.Whitespace/counter" );
        instrument.ShouldNotBeNull();

        var matcher = new InstrumentMatcher( "  *  " );
        matcher.Match( instrument ).ShouldBeTrue();
    }

    #endregion

    #region Match() Method Tests

    [Test]
    public void Match_name_pattern_only()
    {
        using var meter = new Meter( "NameOnly.Test" );
        var counter = meter.CreateCounter<int>( "my.counter" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "NameOnly.Test/my.counter" );
        instrument.ShouldNotBeNull();

        var matcher = new InstrumentMatcher( "NameOnly.Test/*" );
        matcher.Match( instrument ).ShouldBeTrue();
    }

    [Test]
    public void Match_fails_when_name_doesnt_match()
    {
        using var meter = new Meter( "NoMatch.Test" );
        var counter = meter.CreateCounter<int>( "counter" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "NoMatch.Test/counter" );
        instrument.ShouldNotBeNull();

        var matcher = new InstrumentMatcher( "Different.Name/*" );
        matcher.Match( instrument ).ShouldBeFalse();
    }

    [Test]
    public void Match_with_include_tags()
    {
        using var meter = new Meter( "Include.Tags", "1.0",
            [new KeyValuePair<string, object?>( "env", "production" )] );
        var counter = meter.CreateCounter<int>( "counter" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "Include.Tags/counter" );
        instrument.ShouldNotBeNull();

        var matcher = new InstrumentMatcher( "*",
            includeTags: [new KeyValuePair<string, object?>( "env", "production" )] );
        matcher.Match( instrument ).ShouldBeTrue();
    }

    [Test]
    public void Match_with_exclude_tags()
    {
        using var meter = new Meter( "Exclude.Tags", "1.0",
            [new KeyValuePair<string, object?>( "env", "test" )] );
        var counter = meter.CreateCounter<int>( "counter" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "Exclude.Tags/counter" );
        instrument.ShouldNotBeNull();

        var matcher = new InstrumentMatcher( "*",
            excludeTags: [new KeyValuePair<string, object?>( "env", "test" )] );
        matcher.Match( instrument ).ShouldBeFalse();
    }

    [Test]
    public void Match_with_both_include_and_exclude_tags()
    {
        using var meter = new Meter( "Both.Tags", "1.0",
            [
                new KeyValuePair<string, object?>( "env", "production" ),
                new KeyValuePair<string, object?>( "region", "us-east" )
            ] );
        var counter = meter.CreateCounter<int>( "counter" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "Both.Tags/counter" );
        instrument.ShouldNotBeNull();

        // Should match: has include tag, doesn't have exclude tag
        var matcher1 = new InstrumentMatcher( "*",
            includeTags: [new KeyValuePair<string, object?>( "env", "production" )],
            excludeTags: [new KeyValuePair<string, object?>( "feature", "beta" )] );
        matcher1.Match( instrument ).ShouldBeTrue();

        // Should not match: has include tag but also has exclude tag
        var matcher2 = new InstrumentMatcher( "*",
            includeTags: [new KeyValuePair<string, object?>( "env", "production" )],
            excludeTags: [new KeyValuePair<string, object?>( "region", "us-east" )] );
        matcher2.Match( instrument ).ShouldBeFalse();
    }

    [Test]
    public void Match_fails_when_include_tags_not_found()
    {
        using var meter = new Meter( "NoInclude.Tags" );
        var counter = meter.CreateCounter<int>( "counter" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "NoInclude.Tags/counter" );
        instrument.ShouldNotBeNull();

        var matcher = new InstrumentMatcher( "*",
            includeTags: [new KeyValuePair<string, object?>( "required", "value" )] );
        matcher.Match( instrument ).ShouldBeFalse();
    }

    [Test]
    public void Match_with_null_tag_value_matches_any()
    {
        using var meter = new Meter( "NullTag.Matcher", "1.0",
            [new KeyValuePair<string, object?>( "env", "production" )] );
        var counter = meter.CreateCounter<int>( "counter" )
            .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

        var instruments = DotNetMetrics.GetConfiguration().Instruments;
        var instrument = instruments.FirstOrDefault( i => i.FullName == "NullTag.Matcher/counter" );
        instrument.ShouldNotBeNull();

        // Null value in matcher should match any value with that key
        var matcher = new InstrumentMatcher( "*",
            includeTags: [new KeyValuePair<string, object?>( "env", null )] );
        matcher.Match( instrument ).ShouldBeTrue();
    }

    #endregion

    #region Constructor and Property Tests

    [Test]
    public void NamePattern_property_returns_original_pattern()
    {
        var matcher = new InstrumentMatcher( "My.Pattern.*" );
        matcher.NamePattern.ShouldBe( "My.Pattern.*" );
    }

    [Test]
    public void IncludeTags_property_returns_sorted_tags()
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new( "zebra", "z" ),
            new( "alpha", "a" ),
            new( "middle", "m" )
        };
        var matcher = new InstrumentMatcher( "*", includeTags: tags );

        matcher.IncludeTags.Length.ShouldBe( 3 );
        matcher.IncludeTags[0].Key.ShouldBe( "alpha" );
        matcher.IncludeTags[1].Key.ShouldBe( "middle" );
        matcher.IncludeTags[2].Key.ShouldBe( "zebra" );
    }

    [Test]
    public void ExcludeTags_property_returns_sorted_tags()
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new( "zebra", "z" ),
            new( "alpha", "a" )
        };
        var matcher = new InstrumentMatcher( "*", excludeTags: tags );

        matcher.ExcludeTags.Length.ShouldBe( 2 );
        matcher.ExcludeTags[0].Key.ShouldBe( "alpha" );
        matcher.ExcludeTags[1].Key.ShouldBe( "zebra" );
    }

    [Test]
    public void Empty_tags_returns_empty_array()
    {
        var matcher = new InstrumentMatcher( "*" );

        matcher.IncludeTags.Length.ShouldBe( 0 );
        matcher.ExcludeTags.Length.ShouldBe( 0 );
    }

    [Test]
    public void Duplicate_tag_keys_throws_exception()
    {
        var duplicateTags = new List<KeyValuePair<string, object?>>
        {
            new( "key", "value1" ),
            new( "key", "value2" )
        };

        Should.Throw<ArgumentException>( () => new InstrumentMatcher( "*", includeTags: duplicateTags ) );
        Should.Throw<ArgumentException>( () => new InstrumentMatcher( "*", excludeTags: duplicateTags ) );
    }

    [Test]
    public void Null_pattern_throws_exception()
    {
        Should.Throw<ArgumentNullException>( () => new InstrumentMatcher( null! ) );
    }

    #endregion
}
