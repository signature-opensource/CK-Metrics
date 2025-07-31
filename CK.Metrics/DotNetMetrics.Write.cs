using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    internal const string _newMeterPrefix = "+Meter:";
    internal const string _disposedMeterPrefix = "-Meter:";
    internal const string _newInstrumentPrefix = "+Instrument:";
    internal const string _instrumentConfigurationPrefix = "+IConfig:";
    internal const string _measurePrefix = "M:";

    internal static void WriteTags( StringBuilder b, ref StringWriter? w, ReadOnlySpan<KeyValuePair<string, object?>> tags )
    {
        foreach( var tag in tags )
        {
            WriteTag( b, ref w, tag );
        }
    }

    static void WriteTag( StringBuilder b, ref StringWriter? w, KeyValuePair<string, object?> tag )
    {
        w ??= new StringWriter( b );
        b.Append( '"' );
        JavaScriptEncoder.Default.Encode( w, tag.Key );
        b.Append( '"' );
        switch( tag.Value )
        {
            case null: b.Append( ",null" ); break;
            case true: b.Append( ",true" ); break;
            case false: b.Append( ",false" ); break;
            case string s:
                b.Append( ",\"" );
                JavaScriptEncoder.Default.Encode( w, s );
                b.Append( '"' );
                break;
            case int i: b.Append( ',' ).Append( i ); break;
            case double i: b.Append( ',' ).Append( i ); break;
            case long i: b.Append( ',' ).Append( i ); break;
            case int[] a: AppendArray( b, a ); break;
            case double[] a: AppendArray( b, a ); break;
            case long[] a: AppendArray( b, a ); break;
            case string[] a: AppendStringArray( b, w, a ); break;
            default: throw new CKException( $"Invalid attribute value type '{tag.Value.GetType()}'." );
        }
    }

    static void AppendArray<T>( StringBuilder b, T[] a ) where T : struct
    {
        b.Append( ",[" );
        bool atLeastOne = false;
        foreach( var v in a )
        {
            if( atLeastOne ) b.Append( ',' );
            atLeastOne = true;
            b.Append( v );
        }
        b.Append( ']' );
    }

    static void AppendStringArray( StringBuilder b, TextWriter w, string[] a )
    {
        b.Append( ",[" );
        bool atLeastOne = false;
        foreach( var v in a )
        {
            if( atLeastOne ) b.Append( ',' );
            atLeastOne = true;
            if( v == null ) b.Append( "null" );
            else JavaScriptEncoder.Default.Encode( w, v );
        }
        b.Append( ']' );
    }

}
