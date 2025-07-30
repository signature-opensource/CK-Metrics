using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace CK.Metrics;

/// <summary>
/// Immutable basic description of a simple predicate that applies to <see cref="InstrumentInfo"/>.
/// </summary>
public sealed class InstrumentMatcher
{
    readonly string _namePattern;
    readonly Regex? _namePatternRegEx;
    readonly ImmutableArray<KeyValuePair<string, object?>> _includeTags;
    readonly ImmutableArray<KeyValuePair<string, object?>> _excludeTags;

    /// <summary>
    /// Gets the <see cref="InstrumentInfo.FullName"/> wildcard pattern to match.
    /// <para>
    /// This uses a very basic projection a regular expression:
    /// <list type="bullet">
    ///     <item><term>?</term><description>Maps to any single character ('.' in RegEx syntax).</description></item>
    ///     <item><term>*</term><description>Maps to the lazy '.*?' pattern.</description></item>
    ///     <item><term>**</term><description>Maps to the greedy '.*' pattern.</description></item>
    /// </list>
    /// "*" or "**" matches every instrument. 
    /// </para>
    /// </summary>
    public string NamePattern => _namePattern;

    /// <summary>
    /// Gets the required tags that must appear in <see cref="MeterInfo.Tags"/> or <see cref="InstrumentInfo.Tags"/>.
    /// Since a <c>null</c> is not expected in tag values, a <c>null</c> value matches any tag value.
    /// </summary>
    public ImmutableArray<KeyValuePair<string, object?>> IncludeTags => _includeTags;

    /// <summary>
    /// Gets the tags that must not appear in  <see cref="MeterInfo.Tags"/> or <see cref="InstrumentInfo.Tags"/>.
    /// See <see cref="IncludeTags"/>.
    /// </summary>
    public ImmutableArray<KeyValuePair<string, object?>> ExcludeTags => _excludeTags;

    /// <summary>
    /// Initializes a new <see cref="InstrumentMatcher"/>.
    /// </summary>
    /// <param name="namePattern">
    /// The <see cref="InstrumentInfo.FullName"/> wilcard pattern to match.
    /// "*" matches everything, this cannot be null or empty. See <see cref="NamePattern"/>.
    /// </param>
    /// <param name="includeTags">Required instrument's tags.</param>
    /// <param name="excludeTags">Excluded instrument's tags.</param>
    public InstrumentMatcher( string namePattern,
                              IEnumerable<KeyValuePair<string, object?>>? includeTags = null,
                              IEnumerable<KeyValuePair<string, object?>>? excludeTags = null )
    {
        _namePatternRegEx = WildcardToRegex( namePattern );
        _includeTags = ToTags( includeTags, nameof( includeTags ) );
        _excludeTags = ToTags( excludeTags, nameof( excludeTags ) );
        _namePattern = namePattern;
    }

    static ImmutableArray<KeyValuePair<string, object?>> ToTags( IEnumerable<KeyValuePair<string, object?>>? t, string argumentName )
    {
        if( t == null )
        {
            return ImmutableArray<KeyValuePair<string, object?>>.Empty;
        }
        var b = ImmutableArray.CreateBuilder<KeyValuePair<string, object?>>();
        b.AddRange( t );
        b.Sort( ( left, right ) => string.Compare( left.Key, right.Key, StringComparison.Ordinal ) );
        var tags = b.MoveToImmutable();
        // The match relies on non duplicate keys.
        // One day, there will be a TagValueMatcher and new overloads to build a InstrumentMatcher:
        // duplicate keys could then be unified into a composite OR TagValueMatcher.
        for( int i = 1; i < tags.Length; i++ )
        {
            if( tags[i - 1].Key == tags[i].Key )
            {
                Throw.ArgumentException( argumentName, $"Duplicate tag key '{tags[i].Key}'." );
            }
        }
        return tags;
    }

    /// <summary>
    /// Applies this matcher to a <see cref="FullInstrumentInfo"/>.
    /// </summary>
    /// <param name="instrument">The instrument.</param>
    /// <returns>True if this mateches the instrument, false otherwise.</returns>
    public bool Match( FullInstrumentInfo instrument )
    {
        if( _namePatternRegEx != null && !_namePatternRegEx.IsMatch( instrument.FullName ) )
        {
            return false;
        }
        if( _includeTags.Length > 0
            && MatchTags( true, _includeTags, instrument.MeterInfo.Tags )
            && MatchTags( true, _includeTags, instrument.Info.Tags ) )
        {
            return false;
        }
        if( _excludeTags.Length > 0
            && (MatchTags( false, _excludeTags, instrument.MeterInfo.Tags )
                || MatchTags( false, _excludeTags, instrument.Info.Tags )) )
        {
            return false;
        }
        return true;
    }

    static bool MatchTags( bool include,
                           ImmutableArray<KeyValuePair<string, object?>> matchers,
                           ImmutableArray<KeyValuePair<string, object?>> sortedTags )
    {
        if( sortedTags == null ) return !include;
        var mE = matchers.GetEnumerator();
        var tE = sortedTags.GetEnumerator();
        while( mE.MoveNext() )
        {
            var mKey = mE.Current.Key;
            for(; ; )
            {
                var tKey = tE.Current.Key;
                int cmp = StringComparer.Ordinal.Compare( mKey, tKey );
                if( cmp < 0 )
                {
                    if( !tE.MoveNext() ) return false;
                }
                else if( cmp > 0 )
                {
                    return false;
                }
                else
                {
                    Throw.DebugAssert( "Key match.", cmp == 0 );
                    // Here comes the future TagValueMatcher instead of basic value equality.
                    while( !MatchValue( mE.Current.Value, tE.Current.Value ) )
                    {
                        if( !tE.MoveNext() || tE.Current.Key != mKey )
                        {
                            return false;
                        }
                    }
                    break;
                }
            }
        }
        return true;

        static bool MatchValue( object? matcher, object? value )
        {
            if( matcher == null ) return true;
            if( value == null ) return false;
            return matcher.Equals( value );
        }
    }

    static Regex? WildcardToRegex( string namePattern )
    {
        Throw.CheckNotNullArgument( namePattern );
        namePattern = namePattern.Trim();
        return namePattern == "*" || namePattern == "**"
                ? null
                : new Regex( "^" + Regex.Escape( namePattern )
                                        .Replace( "\\?", "." )
                                        .Replace( "\\*\\*", ".*" )
                                        .Replace( "\\*", ".*?" )
                                 + "$",
                             RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );
    }
}
