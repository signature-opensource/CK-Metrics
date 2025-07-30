using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
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
            // https://opentelemetry.io/docs/specs/otel/common/#attribute
            switch( tag.Value )
            {
                case null:
                    AddErrorOrWarning( ref warning, what(), $"Invalid null tag value for '{tag.Key}'." );
                    break;
                case string:
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
                    break;
                default:
                    AddErrorOrWarning( ref error, what(), $"""
                                    Invalid tag value for '{tag.Key}'. Type '{tag.Value.GetType().Name}' is forbidden.
                                    Only string, long, double, bool and array of them are allowed. 
                                    """ );
                    break;
            }
        }
        if( a.Count > MetricsAttributeCountLimit )
        {
            AddErrorOrWarning( ref error, what(), $"""
                                    Attributes count {a.Count} exceeds MetricsAttributeCountLimit that is {MetricsAttributeCountLimit}.
                                    """ );
        }
        return a.DrainToImmutable();
    }
}
