using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace CK.Metrics;

// Internal matchers. Must NOT be generalized: this relies on the fact that:
// - strings are written with the JavaScriptEncoder.Default.
// - Only long, double, bool and string can appear in tag values.
// - Doubles are always written with a fractional '.' (so we can recognize them).
public static partial class DotNetMetrics
{
    internal static bool TryMatchTags( this ref ReadOnlySpan<char> s, out ImmutableArray<KeyValuePair<string, object?>> t )
    {
        t = [];
        ImmutableArray<KeyValuePair<string, object?>>.Builder? tags = null;
        s.TryMatch( '[' );
        while( !s.TryMatch( ']' ) )
        {
            if( !TryMatchString( ref s, true, out var key )
                || !s.TryMatch( ',' )
                || s.Length < 2 )
            {
                return false;
            }
            switch( s[0] )
            {
                case '"':
                    tags ??= ImmutableArray.CreateBuilder<KeyValuePair<string, object?>>();
                    if( !TryMatchString( ref s, true, out var value ) )
                    {
                        return false;
                    }
                    tags.Add( new KeyValuePair<string, object?>( key, value ) );
                    break;
                case 't':
                    if( !s.TryMatch( "true" ) )
                    {
                        return false;
                    }
                    tags ??= ImmutableArray.CreateBuilder<KeyValuePair<string, object?>>();
                    tags.Add( new KeyValuePair<string, object?>( key, true ) );
                    break;
                case 'f':
                    if( !s.TryMatch( "false" ) )
                    {
                        return false;
                    }
                    tags ??= ImmutableArray.CreateBuilder<KeyValuePair<string, object?>>();
                    tags.Add( new KeyValuePair<string, object?>( key, false ) );
                    break;
                case 'n':
                    // Handle null for InstrumentMatcher.Include/ExcludeTags.
                    // Null are not allowed in regular tags (note that we validate meter
                    // and instrument tags but not measures' tags).
                    if( !s.TryMatch( "null" ) )
                    {
                        return false;
                    }
                    tags ??= ImmutableArray.CreateBuilder<KeyValuePair<string, object?>>();
                    tags.Add( new KeyValuePair<string, object?>( key, null ) );
                    break;
                default:
                    if( !TryMatchLongXOrDouble( ref s, out var num ) )
                    {
                        return false;
                    }
                    tags ??= ImmutableArray.CreateBuilder<KeyValuePair<string, object?>>();
                    tags.Add( new KeyValuePair<string, object?>( key, num ) );
                    break;
            }
        }
        if( tags != null ) t = tags.DrainToImmutable();
        return true;
    }

    internal static bool TryMatchString( this ref ReadOnlySpan<char> head, bool decode, [NotNullWhen( true )] out string? s )
    {
        s = null;
        var h = head;
        if( !head.TrySkipJSONQuotedString() ) return false;
        // Ignores leading and trailing ".
        var len = h.Length - head.Length - 2;
        if( len == 0 )
        {
            s = string.Empty;
        }
        else if( !decode )
        {
            s = new string( h.Slice( 1, len ) );
        }
        else
        {
            s = new string( h.Slice( 1, h.Length - head.Length - 2 ) );
        }
        return true;
    }

    internal static bool TryMatchLongXOrDouble( this ref ReadOnlySpan<char> head, [NotNullWhen( true )] out object? v )
    {
        v = null;
        var h = head;
        // Silently skips leading - is any.
        head.TryMatch( '-' );
        if( head.TryMatchDigits( out var digits ) )
        {
            if( head.Length == 0 || head[0] != '.' )
            {
                // We are on not on a floating number.
                if( !long.TryParse( h, out var longResult ) )
                {
                    return false;
                }
                v = longResult;
                return true;
            }
        }
        if( !h.TryMatchDouble( out var doubleResult ) )
        {
            return false;
        }
        v = doubleResult;
        return true;
    }

}
