using CK.Core;
using CK.Metrics;
using NUnit.Framework;
using System.Collections.Immutable;

namespace CK.AppIdentity.Monitoring.Metrics.InfluxDb.Tests;

[TestFixture]
public class LineProtocolBuilderTests
{
    [Test]
    public void appends_measurement_with_correct_format()
    {
        var builder = new LineProtocolBuilder( "TestDomain", "Production", "TestApp" );

        // Create test instrument info
        var meterInfo = CreateMeterInfo( 1, "test.meter" );
        var instrumentInfo = CreateInstrumentInfo( 1, 1, "requests", "Counter", "Int64" );
        var fullInstrument = new FullInstrumentInfo( meterInfo, instrumentInfo, InstrumentConfiguration.BasicDisabled );

        // Create a mock ParsedMeasureLog - we need to simulate the format "M:1,42.0,[]"
        var measureText = "M:1,42.0,[]";
        var measure = CreateParsedMeasureLog( measureText, 1 );

        var timestamp = new DateTime( 2024, 1, 15, 10, 30, 0, DateTimeKind.Utc );
        builder.AppendMeasurement( fullInstrument, measure, timestamp );

        var result = builder.ToString();

        // Verify line protocol format
        Assert.That( result, Does.Contain( "requests" ), "Should contain instrument name" );
        Assert.That( result, Does.Contain( "ck_domain=TestDomain" ), "Should contain domain tag" );
        Assert.That( result, Does.Contain( "ck_environment=Production" ), "Should contain environment tag" );
        Assert.That( result, Does.Contain( "ck_party=TestApp" ), "Should contain party tag" );
        Assert.That( result, Does.Contain( "meter=test.meter" ), "Should contain meter tag" );
        Assert.That( result, Does.Contain( "value=42.0" ), "Should contain value field" );
        Assert.That( result, Does.EndWith( "\n" ), "Should end with newline" );
    }

    [Test]
    public void appends_measurement_with_tags()
    {
        var builder = new LineProtocolBuilder( "Domain", "Env", "Party" );

        var meterInfo = CreateMeterInfo( 1, "http.client" );
        var instrumentInfo = CreateInstrumentInfo( 1, 1, "duration", "Histogram", "Double" );
        var fullInstrument = new FullInstrumentInfo( meterInfo, instrumentInfo, InstrumentConfiguration.BasicDisabled );

        // Measure with tags: M:1,150.5,["method","GET","status","200"]
        var measureText = "M:1,150.5,[\"method\",\"GET\",\"status\",\"200\"]";
        var measure = CreateParsedMeasureLog( measureText, 1 );

        var timestamp = new DateTime( 2024, 1, 15, 10, 30, 0, DateTimeKind.Utc );
        builder.AppendMeasurement( fullInstrument, measure, timestamp );

        var result = builder.ToString();

        Assert.That( result, Does.Contain( "method=GET" ), "Should contain method tag" );
        Assert.That( result, Does.Contain( "status=200" ), "Should contain status tag" );
        Assert.That( result, Does.Contain( "value=150.5" ), "Should contain value" );
    }

    [Test]
    public void escapes_special_characters_in_tags()
    {
        var builder = new LineProtocolBuilder( "My Domain", "Prod Env", "Test App" );

        var meterInfo = CreateMeterInfo( 1, "test.meter" );
        var instrumentInfo = CreateInstrumentInfo( 1, 1, "test,metric", "Counter", "Int64" );
        var fullInstrument = new FullInstrumentInfo( meterInfo, instrumentInfo, InstrumentConfiguration.BasicDisabled );

        var measureText = "M:1,1.0,[]";
        var measure = CreateParsedMeasureLog( measureText, 1 );

        var timestamp = DateTime.UtcNow;
        builder.AppendMeasurement( fullInstrument, measure, timestamp );

        var result = builder.ToString();

        // Spaces should be escaped with backslash
        Assert.That( result, Does.Contain( @"ck_domain=My\ Domain" ), "Should escape space in domain" );
        Assert.That( result, Does.Contain( @"test\,metric" ), "Should escape comma in measurement name" );
    }

    [Test]
    public void clears_buffer_correctly()
    {
        var builder = new LineProtocolBuilder( "Domain", "Env", "Party" );

        var meterInfo = CreateMeterInfo( 1, "test.meter" );
        var instrumentInfo = CreateInstrumentInfo( 1, 1, "test", "Counter", "Int64" );
        var fullInstrument = new FullInstrumentInfo( meterInfo, instrumentInfo, InstrumentConfiguration.BasicDisabled );

        var measureText = "M:1,1.0,[]";
        var measure = CreateParsedMeasureLog( measureText, 1 );

        builder.AppendMeasurement( fullInstrument, measure, DateTime.UtcNow );
        Assert.That( builder.Length, Is.GreaterThan( 0 ) );

        builder.Clear();
        Assert.That( builder.Length, Is.EqualTo( 0 ) );
        Assert.That( builder.ToString(), Is.Empty );
    }

    static MeterInfo CreateMeterInfo( int meterId, string name )
    {
        // Use reflection to create MeterInfo since the constructor is internal
        var type = typeof( MeterInfo );
        var ctor = type.GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new[] { typeof( string ), typeof( string ), typeof( string ),
                   typeof( ImmutableArray<KeyValuePair<string, object?>> ), typeof( int ), typeof( string ) },
            null );

        return (MeterInfo)ctor!.Invoke( new object?[] { name, null, null, ImmutableArray<KeyValuePair<string, object?>>.Empty, meterId, null } );
    }

    static InstrumentInfo CreateInstrumentInfo( int instrumentId, int meterId, string name, string typeName, string measureTypeName )
    {
        var type = typeof( InstrumentInfo );
        var ctor = type.GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new[] { typeof( int ), typeof( int ), typeof( string ), typeof( string ), typeof( string ),
                   typeof( string ), typeof( string ), typeof( ImmutableArray<KeyValuePair<string, object?>> ),
                   typeof( bool ), typeof( string ) },
            null );

        return (InstrumentInfo)ctor!.Invoke( new object?[] { instrumentId, meterId, name, null, null, typeName, measureTypeName,
            ImmutableArray<KeyValuePair<string, object?>>.Empty, false, null } );
    }

    static ParsedMeasureLog CreateParsedMeasureLog( string text, int instrumentId )
    {
        // ParsedMeasureLog format: M:{instrumentId},{value},[{tags}]
        // Find the positions of value and tags
        var colonIndex = text.IndexOf( ':' );
        var commaIndex = text.IndexOf( ',', colonIndex + 1 );
        var bracketIndex = text.IndexOf( '[', commaIndex + 1 );

        var mStart = commaIndex + 1;
        var mLength = bracketIndex - 1 - mStart;
        // tStart points AFTER '[' to match real parser behavior
        // (ParsedMeasureLog.Tags returns content without brackets)
        var tStart = bracketIndex + 1;

        var type = typeof( ParsedMeasureLog );
        var ctor = type.GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new[] { typeof( string ), typeof( int ), typeof( int ), typeof( int ), typeof( int ) },
            null );

        return (ParsedMeasureLog)ctor!.Invoke( new object[] { text, instrumentId, mStart, mLength, tStart } );
    }
}
