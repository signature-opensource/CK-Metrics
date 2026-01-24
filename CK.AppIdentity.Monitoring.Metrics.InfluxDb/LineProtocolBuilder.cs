using System.Text;
using CK.Metrics;

namespace CK.AppIdentity.Monitoring.Metrics.InfluxDb;

/// <summary>
/// Builds InfluxDB line protocol formatted strings from metrics.
/// </summary>
/// <remarks>
/// Line protocol format:
/// <code>
/// &lt;measurement&gt;,&lt;tag_key&gt;=&lt;tag_value&gt;,... &lt;field_key&gt;=&lt;field_value&gt; &lt;timestamp_ns&gt;
/// </code>
/// </remarks>
public sealed class LineProtocolBuilder
{
    readonly StringBuilder _buffer = new();

    readonly string _domain;
    readonly string _environment;
    readonly string _party;

    /// <summary>
    /// Initializes a new <see cref="LineProtocolBuilder"/> with AppIdentity context.
    /// </summary>
    /// <param name="domain">The AppIdentity domain name.</param>
    /// <param name="environment">The AppIdentity environment name.</param>
    /// <param name="party">The AppIdentity party name.</param>
    public LineProtocolBuilder( string domain, string environment, string party )
    {
        _domain = domain;
        _environment = environment;
        _party = party;
    }

    /// <summary>
    /// Gets the current length of the buffered line protocol data.
    /// </summary>
    public int Length => _buffer.Length;

    /// <summary>
    /// Clears the internal buffer.
    /// </summary>
    public void Clear() => _buffer.Clear();

    /// <summary>
    /// Gets the buffered line protocol data as a string.
    /// </summary>
    public override string ToString() => _buffer.ToString();

    /// <summary>
    /// Appends a measurement to the buffer.
    /// </summary>
    /// <param name="instrument">The instrument information.</param>
    /// <param name="measure">The measurement data.</param>
    /// <param name="timestampUtc">The UTC timestamp of the measurement.</param>
    public void AppendMeasurement( FullInstrumentInfo instrument, in ParsedMeasureLog measure, DateTime timestampUtc )
    {
        // Measurement name (instrument name)
        EscapeMeasurement( _buffer, instrument.Info.Name );

        // Fixed tags: ck_domain, ck_environment, ck_party, meter
        _buffer.Append( ",ck_domain=" );
        EscapeTagValue( _buffer, _domain );
        _buffer.Append( ",ck_environment=" );
        EscapeTagValue( _buffer, _environment );
        _buffer.Append( ",ck_party=" );
        EscapeTagValue( _buffer, _party );
        _buffer.Append( ",meter=" );
        EscapeTagValue( _buffer, instrument.MeterInfo.Name );

        // Measurement tags
        if( measure.TagsLength > 0 )
        {
            AppendMeasureTags( measure.Tags );
        }

        // Field: value
        _buffer.Append( " value=" );
        _buffer.Append( measure.Measure );

        // Timestamp in nanoseconds since Unix epoch
        _buffer.Append( ' ' );
        _buffer.Append( ToUnixNanoseconds( timestampUtc ) );

        // Line ending
        _buffer.Append( '\n' );
    }

    /// <summary>
    /// Parses and appends measurement tags from the JSON array format.
    /// The Tags span from ParsedMeasureLog contains content WITHOUT brackets: "key1","value1","key2","value2",...
    /// (TagsLength excludes the trailing ']', and _tStart points after the leading '[')
    /// </summary>
    void AppendMeasureTags( ReadOnlySpan<char> tags )
    {
        // Tags span contains content without brackets, e.g.: "method","GET","status","200"
        while( !tags.IsEmpty )
        {
            // Parse key
            if( !TryParseJsonString( ref tags, out var key ) )
                break;

            // Skip comma
            tags = tags.TrimStart();
            if( tags.IsEmpty || tags[0] != ',' )
                break;
            tags = tags[1..].TrimStart();

            // Parse value
            if( !TryParseJsonString( ref tags, out var value ) )
                break;

            // Append tag
            _buffer.Append( ',' );
            EscapeTagKey( _buffer, key );
            _buffer.Append( '=' );
            EscapeTagValue( _buffer, value );

            // Skip comma for next pair
            tags = tags.TrimStart();
            if( !tags.IsEmpty && tags[0] == ',' )
            {
                tags = tags[1..].TrimStart();
            }
        }
    }

    /// <summary>
    /// Parses a JSON quoted string and advances the span.
    /// </summary>
    static bool TryParseJsonString( ref ReadOnlySpan<char> span, out string result )
    {
        result = string.Empty;
        span = span.TrimStart();

        if( span.IsEmpty || span[0] != '"' )
            return false;

        span = span[1..];
        var sb = new StringBuilder();

        while( !span.IsEmpty )
        {
            var c = span[0];
            if( c == '"' )
            {
                span = span[1..];
                result = sb.ToString();
                return true;
            }
            if( c == '\\' && span.Length > 1 )
            {
                span = span[1..];
                c = span[0];
                switch( c )
                {
                    case 'n': sb.Append( '\n' ); break;
                    case 'r': sb.Append( '\r' ); break;
                    case 't': sb.Append( '\t' ); break;
                    case '\\': sb.Append( '\\' ); break;
                    case '"': sb.Append( '"' ); break;
                    default: sb.Append( c ); break;
                }
            }
            else
            {
                sb.Append( c );
            }
            span = span[1..];
        }

        return false;
    }

    /// <summary>
    /// Escapes a measurement name.
    /// Must escape: comma, space, backslash.
    /// </summary>
    static void EscapeMeasurement( StringBuilder sb, string value )
    {
        foreach( var c in value )
        {
            switch( c )
            {
                case ',':
                case ' ':
                case '\\':
                    sb.Append( '\\' );
                    break;
            }
            sb.Append( c );
        }
    }

    /// <summary>
    /// Escapes a tag key.
    /// Must escape: comma, equals, space, backslash.
    /// </summary>
    static void EscapeTagKey( StringBuilder sb, string value )
    {
        foreach( var c in value )
        {
            switch( c )
            {
                case ',':
                case '=':
                case ' ':
                case '\\':
                    sb.Append( '\\' );
                    break;
            }
            sb.Append( c );
        }
    }

    /// <summary>
    /// Escapes a tag value.
    /// Must escape: comma, equals, space, backslash.
    /// </summary>
    static void EscapeTagValue( StringBuilder sb, string value )
    {
        foreach( var c in value )
        {
            switch( c )
            {
                case ',':
                case '=':
                case ' ':
                case '\\':
                    sb.Append( '\\' );
                    break;
            }
            sb.Append( c );
        }
    }

    /// <summary>
    /// Converts a UTC DateTime to Unix nanoseconds.
    /// </summary>
    static long ToUnixNanoseconds( DateTime utc )
    {
        var unixEpoch = new DateTime( 1970, 1, 1, 0, 0, 0, DateTimeKind.Utc );
        var ticks = (utc - unixEpoch).Ticks;
        return ticks * 100; // 1 tick = 100 nanoseconds
    }
}
