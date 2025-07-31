using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    [GeneratedRegex( @"^[a-zA-Z][_a-zA-Z0-9]*(\.[_a-zA-Z0-9]*)*$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex MeterNameRegex();

    // https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-name-syntax
    [GeneratedRegex( @"^[a-zA-Z][-_\./a-zA-Z0-9]*$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex InstrumentNameRegex();

    // https://opentelemetry.io/docs/specs/semconv/general/naming/#recommendations-for-opentelemetry
    [GeneratedRegex( @"^[a-z][a-z0-9]((\.|_)([a-z][a-z0-9])*)*$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex AttributeNameRegex();


    static void AddErrorOrWarning( [NotNull] ref StringBuilder? b, string what, string msg )
    {
        if( b == null ) b = new StringBuilder( $"Invalid {what}:" );
        else b.AppendLine();
        b.Append( msg );
    }

    static ImmutableArray<KeyValuePair<string,object?>> ValidateTags( ref StringBuilder? error,
                                                                      StringBuilder warning,
                                                                      IEnumerable<KeyValuePair<string, object?>> tags,
                                                                      Func<string> what )
    {
        var a = ImmutableArray.CreateBuilder<KeyValuePair<string, object?>>();
        foreach( var tag in tags )
        {
            a.Add( tag );
            if( string.IsNullOrWhiteSpace( tag.Key ) )
            {
                AddErrorOrWarning( ref error, what(), $"Invalid null or empty tag value for key '{tag.Key}'." );
            }
            else
            {
                if( tag.Key.Length > AttributeNameLengthLimit )
                {
                    AddErrorOrWarning( ref error, what(), $"Tag key '{tag.Key}' cannot be longer than AttributeNameLengthLimit that is {AttributeNameLengthLimit}." );
                }
                if( !AttributeNameRegex().IsMatch( tag.Key ) )
                {
                    AddErrorOrWarning( ref error, what(), $"Tag key '{tag.Key}' must follow https://opentelemetry.io/docs/specs/semconv/general/naming/#recommendations-for-opentelemetry-authors." );
                }
            }
            // https://opentelemetry.io/docs/specs/otel/common/#attribute
            switch( tag.Value )
            {
                case null:
                    AddErrorOrWarning( ref warning, what(), $"Invalid null tag value for '{tag.Key}'." );
                    break;
                case string s:
                    if( s.Length > AttributeValueLengthLimit )
                    {
                        AddErrorOrWarning( ref error, what(), $"Tag '{tag.Key}:{tag.Value}': value is longer than AttributeValueLengthLimit that is {AttributeValueLengthLimit}." );
                    }
                    break;
                case double:
                case bool:
                case long:
                case long[]:
                case double[]:
                case bool[]:
                    break;
                case string[] strings:
                    if( strings.Any( s => s is null ) )
                    {
                        AddErrorOrWarning( ref warning, what(), $"Potential invalid null tag value in '{tag.Key}' array." );
                    }
                    var firstTooLong = strings.FirstOrDefault( s => s.Length > AttributeValueLengthLimit );
                    if( firstTooLong != null )
                    {
                        AddErrorOrWarning( ref error, what(), $"Tag key '{tag.Key}': has a string value longer than AttributeValueLengthLimit that is {AttributeValueLengthLimit}." );
                    }
                    break;
                default:
                    AddErrorOrWarning( ref error, what(), $"""
                                    Invalid tag value for '{tag.Key}'. Type '{tag.Value.GetType().Name}' is forbidden.
                                    Only string, long, double, bool and array of them are allowed. 
                                    """ );
                    break;
            }
        }
        if( a.Count > AttributeCountLimit )
        {
            AddErrorOrWarning( ref error, what(), $"""
                                    Attributes count {a.Count} exceeds MetricsAttributeCountLimit that is {AttributeCountLimit}.
                                    """ );
        }
        return a.DrainToImmutable();
    }
}
