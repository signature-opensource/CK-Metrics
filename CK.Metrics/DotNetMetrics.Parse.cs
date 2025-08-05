using CK.Core;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static CK.Core.ActivityMonitor;

namespace CK.Metrics;

// Internal matchers. Must NOT be generalized: this relies on the fact that:
// - strings are written with the JavaScriptEncoder.Default.
// - Only long, double, bool and string can appear in tag values.
// - Doubles are always written with a fractional '.' (so we can recognize them).
public static partial class DotNetMetrics
{
    internal static bool TryMatchTags( this ref ReadOnlySpan<char> s, out ImmutableArray<KeyValuePair<string, object?>> t )
    {
        s.TryMatch( '[' );
        if( s.TryMatch(']') )
        {
            t = [];
            return true;
        }
        if( ReadKeyValueTag( ref s, out var tag ) )
        {
            if( s.TryMatch( ']' ) )
            {
                t = [ tag ];
                return true;
            }
            var tags = ImmutableArray.CreateBuilder<KeyValuePair<string, object?>>( 4 );
            tags.Add( tag );
            do
            {
                if( !s.TryMatch( ',' ) || !ReadKeyValueTag( ref s, out tag ) )
                {
                    goto error;
                }
                tags.Add( tag );
            }
            while( !s.TryMatch( ']' ) );
            t = tags.DrainToImmutable();
            return true;
        }
        error:
        t = [];
        return false;
    }

    static bool ReadKeyValueTag( ref ReadOnlySpan<char> s, out KeyValuePair<string,object?> tag )
    {
        if( !s.TryMatchJsonQuotedString( out var key )
            || !s.TryMatch( ',' )
            || s.Length < 2
            || !ReadTagValue( ref s, out object? value, allowArray: true ) )
        {
            tag = default;
            return false;
        }
        Throw.DebugAssert( value is null or string or bool or long or double
                                         or string[] or bool[] or long[] or double[] );
        tag = new KeyValuePair<string, object?>( key, value );
        return true;
    }

    static bool ReadTagValue( ref ReadOnlySpan<char> head, out object? value, bool allowArray )
    {
        value = default;
        switch( head[0] )
        {
            case '"':
                if( !head.TryMatchJsonQuotedString( out string? s ) )
                {
                    return false;
                }
                value = s;
                break;
            case 't':
                if( !head.TryMatch( "true" ) )
                {
                    return false;
                }
                value = true;
                break;
            case 'f':
                if( !head.TryMatch( "false" ) )
                {
                    return false;
                }
                value = false;
                break;
            case '[':
                head = head.Slice( 1 );
                if( !allowArray || head.Length == 0 )
                {
                    return false;
                }
                if( head.TryMatch( ']' ) )
                {
                    value = Array.Empty<string>();
                    return true;
                }
                return TryMatchArray( ref head, out value );
            case 'n':
                return head.TryMatch( "null" );
            default:
                return TryMatchLongXOrDouble( ref head, out value );
        }
        return true;
    }

    static bool TryMatchArray( ref ReadOnlySpan<char> head, out object? array )
    {
        Throw.DebugAssert( head[0] != '[' && head[0] != ']' );
        if( !ReadTagValue( ref head, out object? first, allowArray: false ) )
        {
            array = default;
            return false;
        }
        Throw.DebugAssert( first is null or string or bool or long or double );
        if( first is null or string )
        {
            // Only strings can be null: this is an array of string.
            // Single string or null?
            if( head.TryMatch( ']' ) )
            {
                array = new string?[] { (string?)first };
                return true;
            }
            var strings = new List<string?>() { (string?)first };
            while( head.TryMatch( ',' ) )
            {
                if( !head.TryMatchJsonQuotedString( out var s ) && !head.TryMatch( "null" ) )
                {
                    array = default;
                    return false;
                }
                strings.Add( s );
            }
            array = strings.ToArray();
        }
        else if( first is long firstLong )
        {
            // Single long?
            if( head.TryMatch( ']' ) )
            {
                array = new long[] { firstLong };
                return true;
            }
            var longs = new List<long>() { firstLong };
            while( head.TryMatch( ',' ) )
            {
                if( !head.TryMatchInteger( out long l ) )
                {
                    array = default;
                    return false;
                }
                longs.Add( l );
            }
            array = longs.ToArray();
        }
        else if( first is double firstDouble )
        {
            // Single double?
            if( head.TryMatch( ']' ) )
            {
                array = new double[] { firstDouble };
                return true;
            }
            var doubles = new List<double>() { firstDouble };
            while( head.TryMatch( ',' ) )
            {
                if( !head.TryMatchFloatingNumber( out double d ) )
                {
                    array = default;
                    return false;
                }
                doubles.Add( d );
            }
            array = doubles.ToArray();
        }
        else 
        {
            // Single bool?
            if( head.TryMatch( ']' ) )
            {
                array = new bool[] { (bool)first };
                return true;
            }
            var bools = new List<bool>() { (bool)first };
            while( head.TryMatch( ',' ) )
            {
                if( !head.TryMatchBool( out var b ) )
                {
                    array = default;
                    return false;
                }
                bools.Add( b );
            }
            array = bools.ToArray();
        }
        if( head.TryMatch( ']' ) )
        {
            return true;
        }
        array = default;
        return false;
    }

    static bool TryMatchLongXOrDouble( this ref ReadOnlySpan<char> head, [NotNullWhen( true )] out object? v )
    {
        v = null;
        var h = head;
        // Silently skips leading '-' if any.
        h.TryMatch( '-' );
        if( h.TrySkipDigits() )
        {
            if( h.Length == 0 || (h[0] != '.' && h[0] != 'E') )
            {
                // We are on NOT on a double.
                if( !head.TryMatchInteger( out long longResult ) )
                {
                    return false;
                }
                v = longResult;
                return true;
            }
        }
        if( !head.TryMatchFloatingNumber( out double doubleResult ) )
        {
            return false;
        }
        v = doubleResult;
        return true;
    }

}
