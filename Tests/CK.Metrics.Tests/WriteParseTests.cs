using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;

namespace CK.Metrics.Tests.Metrics;

[TestFixture]
public class WriteParseTests
{
    [Test]
    public void write_parse_Meter()
    {
        // Creates a meter with an enabled instrument so its disposal
        // can be tracked.
        {
            using var m = new Meter( "Some.Name", "1.0" );
            m.CreateCounter<int>( "test.just_for_meter_registration" )
             .DefaultConfigure( InstrumentConfiguration.BasicEnabled );
            WriteAndReadMeter( m );
        }
        {
            using var m = new Meter( "Some.Name", "1.0.0",
                                     [
                                        new KeyValuePair<string,object?>("tag1", 42),
                                        new KeyValuePair<string,object?>("tag2", 3712.0),
                                        new KeyValuePair<string,object?>("tag3", "some \"value\"."),
                                        new KeyValuePair<string,object?>("tag4", true),
                                        new KeyValuePair<string,object?>("tag5", false),
                                        new KeyValuePair<string,object?>("tag.null", null)
                                     ] );
            m.CreateCounter<int>( "test.just_for_meter_registration" )
             .DefaultConfigure( InstrumentConfiguration.BasicEnabled );
            WriteAndReadMeter( m );
        }
        DotNetMetrics.GetConfiguration().Instruments.ShouldBeEmpty();
    }

    static void WriteAndReadMeter( Meter m )
    {
        var info = m.GetMeterInfo();
        info.IsMissing.ShouldBeFalse();
        var text = info.JsonDescription.AsSpan();
        MeterInfo.TryMatch( ref text, out var parsed ).ShouldBe( true );
        ShouldBeEqual( info, parsed );
    }

    static void ShouldBeEqual( MeterInfo info, MeterInfo? parsed )
    {
        parsed.ShouldNotBeNull();
        parsed.MeterId.ShouldBe( info.MeterId );
        parsed.Name.ShouldBe( info.Name );
        parsed.Version.ShouldBe( info.Version );
        parsed.TelemetrySchemaUrl.ShouldBe( info.TelemetrySchemaUrl );
        parsed.Tags.ShouldBe( info.Tags );
        parsed.JsonDescription.ShouldBe( info.JsonDescription );
    }

    [Test]
    public void write_parse_Instrument()
    {
        // Enabled the instrument so the Meter's disposal can be tracked.
        {
            using var m = new Meter( "Some.Name", "1.0" );
            m.CreateCounter<int>( "tested.values", "%", """
                I'm a counter in %...
                ... and this is "weird".
                """,
                [
                    new KeyValuePair<string,object?>("some.tag",
                    new string?[]{ """
                        Also
                        rather "weird"
                        'tag value' ✨ 
                        """,
                        "Value n°2 - There can be null in a tag value that is an array of string.",
                        null,
                        "Last value."
                    } ),
                    new KeyValuePair<string,object?>( "array_of_int.is_converted_to.array_of_long",
                                                      new int[]{ 0, 1, 2, 42, 3712 } ),
                    new KeyValuePair<string,object?>( "array_of_double",
                                                      new double[] { 0, 1, 2, 42, 3712 } ),
                    new KeyValuePair<string,object?>( "array_of_bool",
                                                      new bool[] { true, false, true } )
                ] )
             .DefaultConfigure( InstrumentConfiguration.BasicEnabled );

            WriteAndReadMeter( m );

            var instruments = DotNetMetrics.GetConfiguration().Instruments;
            instruments.Count.ShouldBe( 1 );

            WriteAndReadInstrument( instruments[0] );

        }
        DotNetMetrics.GetConfiguration().Instruments.ShouldBeEmpty();
    }

    static void WriteAndReadInstrument( FullInstrumentInfo full )
    {
        var text = full.Info.JsonDescription.AsSpan();
        InstrumentInfo.TryMatch( ref text, out var parsed ).ShouldBe( true );
        parsed.ShouldNotBeNull();
        ShouldBeEqual( full.Info, parsed );
    }

    static void ShouldBeEqual( InstrumentInfo info, InstrumentInfo parsed )
    {
        info.InstrumentId.ShouldBe( parsed.InstrumentId );
        info.MeterId.ShouldBe( parsed.MeterId );
        info.Name.ShouldBe( parsed.Name );
        info.Description.ShouldBe( parsed.Description );
        info.IsObservable.ShouldBe( parsed.IsObservable );
        info.JsonDescription.ShouldBe( parsed.JsonDescription );
        info.TypeName.ShouldBe( parsed.TypeName );
        info.Unit.ShouldBe( parsed.Unit );
        info.MeasureTypeName.ShouldBe( parsed.MeasureTypeName );
        ShouldBeEqual( info.Tags, parsed.Tags );
    }

    static void ShouldBeEqual( ImmutableArray<KeyValuePair<string, object?>> t1, ImmutableArray<KeyValuePair<string, object?>> t2 )
    {
        t1.Length.ShouldBe( t2.Length );
        for( int i = 0; i < t1.Length; i++ )
        {
            t1[i].Key.ShouldBe( t2[i].Key );
            t1[i].Value.ShouldBe( t2[i].Value );
        }
    }
}

