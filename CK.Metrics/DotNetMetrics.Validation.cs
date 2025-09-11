using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
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
    [GeneratedRegex( @"^[a-z][a-z0-9]*((\.|_)[a-z][a-z0-9]*)*$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex AttributeNameRegex();

    static void Validate( ref StringBuilder? error,
                          ref StringBuilder? warning,
                          Meter meter,
                          out ImmutableArray<KeyValuePair<string, object?>> tags )
    {
        // Applying https://opentelemetry.io/docs/specs/semconv/general/naming/#recommendations-for-opentelemetry-authors
        ValidateMeterName( ref error, meter.Name );
        var sVersion = meter.Version.AsSpan();
        if( sVersion.Length > 0
            && !Version.TryParse( sVersion, out _ )
            && !(int.TryParse( sVersion, out var v ) && v >= 0) )
        {
            AddErrorOrWarning( ref error, $"Meter '{meter.Name}'", $"Version '{sVersion}' must be a valid version number." );
        }
        tags = meter.Tags != null
                ? ValidateTags( ref error, ref warning, meter.Tags, () => $"Meter '{meter.Name}'" )
                : [];

        static void ValidateMeterName( ref StringBuilder? error, string name )
        {
            if( string.IsNullOrWhiteSpace( name ) )
            {
                AddErrorOrWarning( ref error, $"Meter '{name}'", "Invalid empty meter name." );
            }
            else
            {
                if( name.Length > MeterNameLengthLimit )
                {
                    AddErrorOrWarning( ref error, $"Meter '{name}'", $"Name cannot be longer than MeterNameLengthLimit that is {MeterNameLengthLimit}." );
                }
                if( !MeterNameRegex().IsMatch( name ) )
                {
                    AddErrorOrWarning( ref error, $"Meter '{name}'", "Name must be a simple namespace-like identifier." );
                }
            }
        }
    }


    static void Validate( ref StringBuilder? error,
                          ref StringBuilder? warning,
                          Instrument instrument,
                          out string typeName,
                          out Type measureType,
                          out ImmutableArray<KeyValuePair<string, object?>> tags )
    {
        if( instrument.Name.Length > 255
            || !InstrumentNameRegex().IsMatch( instrument.Name ) )
        {
            AddErrorOrWarning( ref error,
                      $"Instrument '{instrument.Name}'",
                      $"Name must follow https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-name-syntax." );
        }
        tags = instrument.Tags != null
                    ? ValidateTags( ref error, ref warning, instrument.Tags, () => $"Instrument '{instrument.Name}'" )
                    : [];
        var t = instrument.GetType();
        bool valid = t.IsGenericType && !t.IsGenericTypeDefinition && t.Namespace == "System.Diagnostics.Metrics";
        if( !valid )
        {
            AddErrorOrWarning( ref error,
                      $"Instrument '{instrument.Name}'",
                      $"Invalid instrument type '{t}'." );
            typeName = "";
            measureType = typeof( void );
        }
        else
        {
            typeName = t.Name.Substring( 0, t.Name.IndexOf( '`' ) );
            Throw.CheckState( instrument.IsObservable == typeName.StartsWith( "Observable" ) );
            if( instrument.IsObservable ) typeName = typeName.Substring( 10 );
            measureType = t.GenericTypeArguments[0];
        }
        // https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-unit
        if( instrument.Unit != null && instrument.Unit.Length > 63 )
        {
            AddErrorOrWarning( ref error, $"Instrument '{instrument.Name}'", $"Units '{instrument.Unit}' cannot be longer than 63 characters." );
        }
    }


    static void AddErrorOrWarning( [NotNull] ref StringBuilder? b, string what, string msg )
    {
        if( b == null ) b = new StringBuilder( $"Invalid {what}:" );
        else b.AppendLine();
        b.Append( msg );
    }

    static ImmutableArray<KeyValuePair<string,object?>> ValidateTags( ref StringBuilder? error,
                                                                      ref StringBuilder? warning,
                                                                      IEnumerable<KeyValuePair<string, object?>> tags,
                                                                      Func<string> what )
    {
        var a = ImmutableArray.CreateBuilder<KeyValuePair<string, object?>>();
        foreach( var tag in tags )
        {
            bool replaced = false;
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
                    AddErrorOrWarning( ref warning, what(), $"Potential invalid null tag value for '{tag.Key}'." );
                    break;
                case string s:
                    if( s.Length > AttributeValueLengthLimit )
                    {
                        AddErrorOrWarning( ref error, what(), $"Tag '{tag.Key}:{tag.Value}': value is longer than AttributeValueLengthLimit that is {AttributeValueLengthLimit}." );
                    }
                    break;
                case int i:
                    a.Add( new KeyValuePair<string, object?>( tag.Key, (long)i ) );
                    replaced = true;
                    break;
                case int[] intAray:
                    var longArray = new long[intAray.Length];
                    for( int i = 0; i < intAray.Length; i++ )
                    {
                        longArray[i] = intAray[i];
                    }
                    a.Add( new KeyValuePair<string, object?>( tag.Key, longArray ) );
                    replaced = true;
                    break;
                case double:
                case bool:
                case long:
                case long[]:
                case double[]:
                case bool[]:
                    break;
                case string[] strings:
                    bool hasNull = false;
                    List<string>? tooLong = null;
                    foreach( var s in strings )
                    {
                        if( s == null ) hasNull = true;
                        else if( s.Length > AttributeValueLengthLimit )
                        {
                            tooLong ??= new List<string>();
                            tooLong.Add( s );
                        }
                    }
                    if( hasNull )
                    {
                        AddErrorOrWarning( ref warning, what(), $"Potential invalid null tag value in '{tag.Key}' string array." );
                    }
                    if( tooLong != null )
                    {
                        AddErrorOrWarning( ref error, what(), $"""
                            Tag key '{tag.Key}': string values cannot be longer than AttributeValueLengthLimit that is {AttributeValueLengthLimit}:
                            {tooLong.Concatenate( Environment.NewLine )}
                            """ );
                    }
                    break;
                default:
                    AddErrorOrWarning( ref error, what(), $"""
                                    Invalid tag value for '{tag.Key}'. Type '{tag.Value.GetType().ToCSharpName()}' is forbidden.
                                    Only string, long, double, bool and array of them are allowed. 
                                    """ );
                    break;
            }
            if( !replaced )
            {
                a.Add( tag );
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
