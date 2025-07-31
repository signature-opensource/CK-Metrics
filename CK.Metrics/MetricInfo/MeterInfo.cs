using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;

namespace CK.Metrics;

public sealed class MeterInfo
{
    readonly string _name;
    readonly string? _version;
    readonly string? _telemetrySchemaUrl;
    readonly ImmutableArray<KeyValuePair<string, object?>> _tags;
    string? _jsonDesc;
    readonly int _meterId;

    internal MeterInfo( string name,
                        string? version,
                        string? telemetrySchemaUrl,
                        ImmutableArray<KeyValuePair<string, object?>> tags,
                        int meterId,
                        string? jsonDesc = null )
    {
        _name = name;
        _version = version;
        _telemetrySchemaUrl = telemetrySchemaUrl;
        _tags = tags;
        _meterId = meterId;
        _jsonDesc = jsonDesc;
    }

    public string Name => _name;

    public int MeterId => _meterId;

    public string? Version => _version;

    public ImmutableArray<KeyValuePair<string, object?>> Tags => _tags;

    public string JsonDescription => _jsonDesc ??= Write( new StringBuilder() ).ToString();

    public string? TelemetrySchemaUrl => _telemetrySchemaUrl;

    string Write( StringBuilder b )
    {
        Throw.DebugAssert( b.Length == 0 );
        var w = new StringWriter( b );
        b.Append( _meterId ).Append( ",\"" );
        JavaScriptEncoder.Default.Encode( w, _name );
        b.Append( "\",\"" ).Append( _version );
        b.Append( "\",\"" );
        if( _telemetrySchemaUrl != null )
        {
            JavaScriptEncoder.Default.Encode( w, _telemetrySchemaUrl );
        }
        b.Append( "\",[" );
        DotNetMetrics.WriteTags( b, ref w, _tags.AsSpan() );
        b.Append( ']' );
        return b.ToString();
    }

    /// <summary>
    /// Tries to match and forward the <paramref name="head"/> on success.
    /// </summary>
    /// <param name="head">The head to match.</param>
    /// <param name="meterInfo">The <see cref="MeterInfo"/> on success.</param>
    /// <returns>True on success (head has been forwarded), false otherwise.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, [NotNullWhen(true)]out MeterInfo? meterInfo )
    {
        var h = head;
        if( head.TryMatchInt32( out var meterId, minValue: 0 )
            && head.TryMatch( ',' )
            && head.TryMatchString( true, out var name ) && !string.IsNullOrWhiteSpace( name )
            && head.TryMatch( ',' )
            && head.TryMatchString( false, out var version )
            && head.TryMatch( ',' )
            && head.TryMatchString( true, out var telemetrySchemaUrl )
            && head.TryMatch( "," )
            && head.TryMatchTags( out var tags ) )
        {
            int descLen = h.Length - head.Length;
            meterInfo = new MeterInfo( name,
                                       version.Length == 0 ? null : version,
                                       telemetrySchemaUrl.Length == 0 ? null : telemetrySchemaUrl,
                                       tags,
                                       meterId,
                                       new string( h.Slice( 0, descLen ) ) );
            return true;
        }
        head = h;
        meterInfo = null;
        return false;
    }

    public override string ToString() => JsonDescription;
}
