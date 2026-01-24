using NUnit.Framework;

namespace CK.Metrics.Tests;

[TestFixture]
public class MetricsLogParserTests
{
    [Test]
    public void TryReadMeasure_parses_tags_without_brackets()
    {
        var parser = MetricsLogParser.Create( "M:1,42.0,[\"method\",\"GET\"]" );
        Assert.That( parser.Kind, Is.EqualTo( MetricsLogKind.Measure ) );
        Assert.That( parser.TryReadMeasure( out var m ), Is.True );
        Assert.That( m.InstrumentId, Is.EqualTo( 1 ) );
        Assert.That( m.Measure.ToString(), Is.EqualTo( "42.0" ) );
        // Tags should NOT include brackets - matches existing test expectation in MetricsLogDispatcherTests
        Assert.That( m.Tags.ToString(), Is.EqualTo( "\"method\",\"GET\"" ) );
    }

    [Test]
    public void TryReadMeasure_parses_multiple_tag_pairs()
    {
        var parser = MetricsLogParser.Create( "M:5,100.5,[\"method\",\"POST\",\"status\",\"201\"]" );
        Assert.That( parser.Kind, Is.EqualTo( MetricsLogKind.Measure ) );
        Assert.That( parser.TryReadMeasure( out var m ), Is.True );
        Assert.That( m.InstrumentId, Is.EqualTo( 5 ) );
        Assert.That( m.Measure.ToString(), Is.EqualTo( "100.5" ) );
        Assert.That( m.Tags.ToString(), Is.EqualTo( "\"method\",\"POST\",\"status\",\"201\"" ) );
    }

    [Test]
    public void TryReadMeasure_handles_empty_tags()
    {
        var parser = MetricsLogParser.Create( "M:1,42.0,[]" );
        Assert.That( parser.Kind, Is.EqualTo( MetricsLogKind.Measure ) );
        Assert.That( parser.TryReadMeasure( out var m ), Is.True );
        Assert.That( m.InstrumentId, Is.EqualTo( 1 ) );
        Assert.That( m.Measure.ToString(), Is.EqualTo( "42.0" ) );
        Assert.That( m.TagsLength, Is.EqualTo( 0 ) );
    }

    [Test]
    public void TryReadMeasure_handles_no_tags()
    {
        var parser = MetricsLogParser.Create( "M:1,42.0" );
        Assert.That( parser.Kind, Is.EqualTo( MetricsLogKind.Measure ) );
        Assert.That( parser.TryReadMeasure( out var m ), Is.True );
        Assert.That( m.InstrumentId, Is.EqualTo( 1 ) );
        Assert.That( m.Measure.ToString(), Is.EqualTo( "42.0" ) );
        Assert.That( m.TagsLength, Is.EqualTo( 0 ) );
    }

    [Test]
    public void TryReadMeasure_parses_integer_value()
    {
        var parser = MetricsLogParser.Create( "M:10,999,[\"key\",\"value\"]" );
        Assert.That( parser.TryReadMeasure( out var m ), Is.True );
        Assert.That( m.InstrumentId, Is.EqualTo( 10 ) );
        Assert.That( m.Measure.ToString(), Is.EqualTo( "999" ) );
        Assert.That( m.Tags.ToString(), Is.EqualTo( "\"key\",\"value\"" ) );
    }

    [Test]
    public void TryReadMeasure_parses_negative_value()
    {
        var parser = MetricsLogParser.Create( "M:1,-123.456,[\"tag\",\"val\"]" );
        Assert.That( parser.TryReadMeasure( out var m ), Is.True );
        Assert.That( m.Measure.ToString(), Is.EqualTo( "-123.456" ) );
        Assert.That( m.Tags.ToString(), Is.EqualTo( "\"tag\",\"val\"" ) );
    }
}
