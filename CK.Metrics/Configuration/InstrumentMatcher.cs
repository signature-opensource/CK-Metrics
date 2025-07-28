using CK.Core;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace CK.Metrics;

public sealed class InstrumentMatcher
{
    readonly Regex? _namePattern;
    readonly ImmutableArray<KeyValuePair<string, object?>> _includeTags;
    readonly ImmutableArray<KeyValuePair<string, object?>> _excludeTags;

    /// <summary>
    /// Initializes a new <see cref="InstrumentMatcher"/>.
    /// </summary>
    /// <param name="namePattern">
    /// The meter/instrument's name wilcard pattern to match.
    /// "*" matches everything, this cannot be null or empty.
    /// </param>
    /// <param name="includeTags">Required instrument's tags.</param>
    /// <param name="excludeTags">Excluded instrument's tags.</param>
    public InstrumentMatcher( string namePattern,
                              IEnumerable<KeyValuePair<string, object?>>? includeTags = null,
                              IEnumerable<KeyValuePair<string, object?>>? excludeTags = null )
    {
        _namePattern = WildcardToRegex( namePattern );
        _includeTags = ToTags( includeTags, nameof( includeTags ) );
        _excludeTags = ToTags( excludeTags, nameof( excludeTags ) );
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

    internal bool Match( DotNetMetrics.InstrumentState instrument )
    {
        if( _namePattern != null && !_namePattern.IsMatch( instrument.FullName ) )
        {
            return false;
        }
        if( _includeTags.Length > 0
            && MatchTags( true, _includeTags, instrument.Meter.Meter.Tags )
            && MatchTags( true, _includeTags, instrument.Instrument.Tags ) )
        {
            return false;
        }
        if( _excludeTags.Length > 0
            && (MatchTags( false, _excludeTags, instrument.Meter.Meter.Tags )
                || MatchTags( false, _excludeTags, instrument.Instrument.Tags )) )
        {
            return false;
        }
        return true;
    }

    static bool MatchTags( bool include,
                           ImmutableArray<KeyValuePair<string, object?>> matchers,
                           IEnumerable<KeyValuePair<string, object?>>? sortedTags )
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
                    while( tE.Current.Value != mE.Current.Value )
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
