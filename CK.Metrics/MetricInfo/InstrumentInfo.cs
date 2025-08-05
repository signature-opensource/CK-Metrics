using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;

namespace CK.Metrics;

public sealed class InstrumentInfo
{
    readonly string _name;
    readonly string? _description;
    readonly string? _unit;
    readonly string _typeName;
    readonly string _measureTypeName;
    readonly ImmutableArray<KeyValuePair<string, object?>> _tags;
    string? _jsonDesc;
    readonly int _instrumentId;
    readonly int _meterId;
    readonly bool _isObservable;

    internal InstrumentInfo( int instrumentId,
                             int meterId,
                             string name,
                             string? description,
                             string? unit,
                             string typeName,
                             string measureTypeName,
                             ImmutableArray<KeyValuePair<string, object?>> tags,
                             bool isObservable,
                             string? jsonDesc = null )
    {
        _name = name;
        _description = description;
        _unit = unit;
        _typeName = typeName;
        _measureTypeName = measureTypeName;
        _tags = tags;
        _instrumentId = instrumentId;
        _meterId = meterId;
        _isObservable = isObservable;
        _jsonDesc = jsonDesc;
    }

    public string Name => _name;

    public string? Description => _description;

    public string? Unit => _unit;

    public string TypeName => _typeName;

    public string MeasureTypeName => _measureTypeName;

    public ImmutableArray<KeyValuePair<string, object?>> Tags => _tags;

    public int InstrumentId => _instrumentId;

    public int MeterId => _meterId;

    public bool IsObservable => _isObservable;

    public string JsonDescription => _jsonDesc ??= Write();

    public override string ToString() => JsonDescription;

    internal string Write()
    {
        SafeWriter w = new SafeWriter();
        // Name and types are purely ascii.
        w.Append( _instrumentId );
        w.Append( ',' );
        w.Append( _meterId );
        w.Append( ',' );
        w.AppendJsonRawString( _name );
        w.Append( ',' );
        w.AppendJsonRawString( _typeName );
        w.Append( ',' );
        w.AppendJsonRawString( _measureTypeName );
        w.Append( ',' );
        w.Append( _isObservable );
        w.Append( ',' );
        w.AppendEncodedJsonString( _description, useNullToken: false );
        w.Append( ',' );
        w.AppendEncodedJsonString( _unit, useNullToken: false );
        w.Append( ',' );
        DotNetMetrics.WriteTags( ref w, _tags.AsSpan() );
        return w.ToString();
    }

    /// <summary>
    /// Tries to match and forward the <paramref name="head"/> on success.
    /// </summary>
    /// <param name="head">The head to match.</param>
    /// <param name="instrumentInfo">The <see cref="InstrumentInfo"/> on success.</param>
    /// <returns>True on success (head has been forwarded), false otherwise.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, [NotNullWhen(true)]out InstrumentInfo? instrumentInfo )
    {
        var h = head;
        if( head.TryMatchInteger( out int instrumentId ) && instrumentId >= 0
            && head.TryMatch( ',' )
            && head.TryMatchInteger( out int meterId ) && meterId >= -1
            && head.TryMatch( ',' )
            && head.TryMatchJsonQuotedString( out var name ) && !string.IsNullOrWhiteSpace( name )
            && head.TryMatch( ',' )
            && head.TryMatchJsonQuotedString( out var typeName ) && !string.IsNullOrWhiteSpace( typeName )
            && head.TryMatch( ',' )
            && head.TryMatchJsonQuotedString( out var measureTypeName ) && !string.IsNullOrWhiteSpace( measureTypeName )
            && head.TryMatch( ',' )
            && head.TryMatchBool( out var isObservable )
            && head.TryMatch( ',' )
            && head.TryMatchJsonQuotedString( out var description )
            && head.TryMatch( ',' )
            && head.TryMatchJsonQuotedString( out var unit )
            && head.TryMatch( ',' )
            && head.TryMatchTags( out var tags ) )
        {
            int descLen = h.Length - head.Length;
            instrumentInfo = new InstrumentInfo( instrumentId,
                                                 meterId,
                                                 name,
                                                 description.Length == 0 ? null : description,
                                                 unit.Length == 0 ? null : unit,
                                                 typeName,
                                                 measureTypeName,
                                                 tags,
                                                 isObservable,
                                                 new string( h.Slice( 0, descLen ) ) );
            return true;
        }
        head = h;
        instrumentInfo = null;
        return false;
    }

}


