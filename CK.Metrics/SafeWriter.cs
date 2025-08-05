using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;

namespace CK.Metrics;

/// <summary>
/// This Writer is a specification. Its real implementation has yet to be done...
/// - It must ensure that number formatting uses the <see cref="CultureInfo.InvariantCulture"/>
/// (that StringBuilder doesn't guaranty).
/// - It must efficently encode a string.
/// - It must be able to write double values with a . (or a Exponent) so that its text differ
///   from a long with the same value.
///   This is used in tag values to restore type consistently.
/// </summary>
ref struct SafeWriter
{
    StringBuilder _b;

    public SafeWriter()
    {
        _b = new StringBuilder();
    }

    public void Append( bool v ) => _b.Append( v ? "true" : "false" );
    public void Append( byte v ) => _b.Append( v );
    public void Append( short v ) => _b.Append( v );
    public void Append( int v ) => _b.Append( v );
    public void Append( long v ) => _b.Append( v );
    public void Append( float v ) => _b.Append( v );
    public void Append( double v ) => _b.Append( v );
    public void Append( decimal v ) => _b.Append( v );

    public void Append( char c ) => _b.Append( c );
    public void Append( string raw ) => _b.Append( raw );

    public void AppendJsonRawString( string? v )
    {
        _b.Append( '"' );
        if( v != null ) _b.Append( v );
        _b.Append( '"' );
    }

    public void AppendEncodedJsonString( string? v, bool useNullToken )
    {
        if( v == null )
            _b.Append( useNullToken ? "null" : "\"\"" );
        else
        {
            _b.Append( '"' );
            _b.Append( JavaScriptEncoder.Default.Encode( v ) );
            _b.Append( '"' );
        }
    }

    public void AppendExplicitDouble( double v )
    {
        var s = v.ToString( "G17", CultureInfo.InvariantCulture );
        if( !s.Contains( '.' ) && !s.Contains( 'E' ) )
        {
            s += ".0";
        }
        _b.Append( s );
    }

    public void AppendArrayOfExplicitDouble( double[] a )
    {
        _b.Append( '[' );
        bool atLeastOne = false;
        foreach( var d in a )
        {
            if( atLeastOne ) _b.Append( ',' );
            atLeastOne = true;
            AppendExplicitDouble( d );
        }
        _b.Append( ']' );
    }

    public void AppendArrayOfEncodedJsonString( string?[] a )
    {
        _b.Append( '[' );
        bool atLeastOne = false;
        foreach( var s in a )
        {
            if( atLeastOne ) _b.Append( ',' );
            atLeastOne = true;
            AppendEncodedJsonString( s, useNullToken: true );
        }
        _b.Append( ']' );
    }

    public void AppendArray( long[] a )
    {
        _b.Append( '[' );
        bool atLeastOne = false;
        foreach( var d in a )
        {
            if( atLeastOne ) _b.Append( ',' );
            atLeastOne = true;
            _b.Append( d );
        }

        _b.Append( ']' );
    }

    public void AppendArray( bool[] a )
    {
        _b.Append( '[' );
        bool atLeastOne = false;
        foreach( var d in a )
        {
            if( atLeastOne ) _b.Append( ',' );
            atLeastOne = true;
            _b.Append( d ? "true" : "false" );
        }

        _b.Append( ']' );
    }

    public override string ToString() => _b.ToString();
}
