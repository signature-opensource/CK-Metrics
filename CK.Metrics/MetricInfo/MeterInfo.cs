using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;

namespace CK.Metrics;

/// <summary>
/// Immutable capture of a <see cref="System.Diagnostics.Metrics.Meter"/>.
/// Its <see cref="JsonDescription"/> can be parsed back by <see cref="TryMatch(ref ReadOnlySpan{char}, out CK.Metrics.MeterInfo?)"/>.
/// </summary>
public sealed class MeterInfo : ITrackedMetricsInfo
{
    /// <summary>
    /// Null object pattern: an invalid missing singleton instance. <see cref="MeterId"/> is -1, <see cref="Name"/> and <see cref="JsonDescription"/>
    /// are "Missing".
    /// </summary>
    public static readonly MeterInfo Missing = new MeterInfo( "Missing", null, null, [], -1, "\"Missing\"" );

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

    int ITrackedMetricsInfo.TrackeId => _meterId;

    public string? Version => _version;

    public bool IsMissing => _meterId == -1;

    public ImmutableArray<KeyValuePair<string, object?>> Tags => _tags;

    public string JsonDescription => _jsonDesc ??= Write();

    public string? TelemetrySchemaUrl => _telemetrySchemaUrl;

    string Write()
    {
        SafeWriter w = new SafeWriter();
        w.Append( _meterId );
        w.Append( ',' );
        w.AppendJsonRawString( _name );
        w.Append( ',' );
        w.AppendJsonRawString( _version );
        w.Append( ',' );
        w.AppendEncodedJsonString( _telemetrySchemaUrl, useNullToken: false );
        w.Append( ',' );
        DotNetMetrics.WriteTags( ref w, _tags.AsSpan() );
        return w.ToString();
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
        if( head.TryMatchInteger( out int meterId ) && meterId >= -1
            && head.TryMatch( ',' )
            && head.TryMatchJsonQuotedString( out var name ) && !string.IsNullOrWhiteSpace( name )
            && head.TryMatch( ',' )
            && head.TryMatchJsonQuotedString( out var version )
            && head.TryMatch( ',' )
            && head.TryMatchJsonQuotedString( out var telemetrySchemaUrl )
            && head.TryMatch( ',' )
            && head.TryMatchTags( out var tags ) )
        {
            if( meterId == -1 )
            {
                meterInfo = Missing;
                return true;
            }
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
