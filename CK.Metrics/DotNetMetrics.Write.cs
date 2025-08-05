using CK.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
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

    internal static void WriteTags( ref SafeWriter w, ReadOnlySpan<KeyValuePair<string, object?>> tags )
    {
        w.Append( '[' );
        bool atLeastOne = false;
        foreach( var tag in tags )
        {
            if( atLeastOne ) w.Append( ',' );
            atLeastOne = true;
            WriteTag( ref w, tag );
        }
        w.Append( ']' );
    }

    static void WriteTag( ref SafeWriter w, KeyValuePair<string, object?> tag )
    {
        Throw.DebugAssert( tag.Key != null );
        w.AppendEncodedJsonString( tag.Key, useNullToken: false );
        switch( tag.Value )
        {
            case null: w.Append( ",null" ); break;
            case true: w.Append( ",true" ); break;
            case false: w.Append( ",false" ); break;
            case string s:
                w.Append( ',' );
                w.AppendEncodedJsonString( s, useNullToken: true );
                break;
            case double i:
                w.Append( ',' );
                w.AppendExplicitDouble( i );
                break;
            case long i:
                w.Append( ',' );
                w.Append( i );
                break;
            case double[] a:
                w.Append( ',' );
                w.AppendArrayOfExplicitDouble( a );
                break;
            case long[] a:
                w.Append( ',' );
                w.AppendArray( a );
                break;
            case bool[] a:
                w.Append( ',' );
                w.AppendArray( a );
                break;
            case string[] a:
                w.Append( ',' );
                w.AppendArrayOfEncodedJsonString( a );
                break;
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

    static void AppendLongArray<T>( StringBuilder b, long[] a ) where T : struct
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
