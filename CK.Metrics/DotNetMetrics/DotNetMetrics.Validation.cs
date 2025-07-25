using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace CK.Core;

public static partial class DotNetMetrics
{
    static void AddErrorOrWarning( [NotNull]ref StringBuilder? b, string what, string msg )
    {
        if( b == null ) b = new StringBuilder( $"Invalid {what}:" );
        else b.AppendLine();
        b.Append( msg );
    }

    static void ValidateTags( ref StringBuilder? error,
                              StringBuilder warning,
                              IEnumerable<KeyValuePair<string, object?>> tags,
                              Func<string> what )
    {
        int count = 0;
        foreach( var tag in tags )
        {
            ++count;
            if( string.IsNullOrWhiteSpace( tag.Key ) )
            {
                AddErrorOrWarning( ref error, what(), $"Invalid null tag key '{tag.Key}'." );
            }
            switch( tag.Value )
            {
                case null:
                    AddErrorOrWarning( ref warning, what(), $"Invalid null tag value for '{tag.Key}'." );
                    break;
                case string:
                case int:
                case double:
                case bool:
                case long:
                case int[]:
                case long[]:
                case double[]:
                case bool[]:
                    break;
                case string[] strings:
                    if( strings.Any( s => s is null ) )
                    {
                        AddErrorOrWarning( ref warning, what(), $"Invalid null tag value in '{tag.Key}' array." );
                    }
                    break;
                default:
                    AddErrorOrWarning( ref error, what(), $"""
                                    Invalid tag value for '{tag.Key}'. Type '{tag.Value.GetType().Name}' is forbidden.
                                    Only string, int, long, double, bool and array of them are allowed. 
                                    """ );
                    break;
            }
        }
        if( count > MetricsAttributeCountLimit )
        {
            AddErrorOrWarning( ref error, what(), $"""
                                    Attributes count {count} exceeds MetricsAttributeCountLimit that is {MetricsAttributeCountLimit}.
                                    """ );
        }
    }
}
