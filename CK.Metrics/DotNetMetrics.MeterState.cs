using CK.Core;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    internal sealed partial class MeterState
    {
        static int _currentMeterId;

        readonly Meter _meter;
        internal InstrumentState? _first;
        readonly int _meterId;
        readonly string _jsonDesc;

        MeterState( Meter meter, int id, StringBuilder b )
        {
            _meter = meter;
            _meterId = id;
            _jsonDesc = Write( b, id, meter );

            static string Write( StringBuilder b, int id, Meter meter )
            {
                Throw.DebugAssert( b.Length == 0 );
                var w = new StringWriter( b );
                b.Append( id ).Append( ",\"" );
                JavaScriptEncoder.Default.Encode( w, meter.Name );
                b.Append( "\",\"" ).Append( meter.Version );
                b.Append( "\",\"" );
#if NET10_0_OR_GREATER
                if( meter.TelemetrySchemaUrl != null )
                {
                    JavaScriptEncoder.Default.Encode( w, meter.TelemetrySchemaUrl );
                }
#endif
                b.Append( "\",[" );
                if( meter.Tags != null ) WriteTags( b, w, meter.Tags );
                b.Append( ']' );
                return b.ToString();
            }

        }

        public Meter Meter => _meter;

        public int MeterId => _meterId;

        public string JsonDescription => _jsonDesc;

        internal static MeterState Create( Meter meter, StringBuilder b )
        {
            Throw.DebugAssert( b.Length == 0 );
            StringBuilder? error = null;
            Validate( ref error, warning: b, meter );
            if( b.Length != 0 )
            {
                ActivityMonitor.StaticLogger.UnfilteredLog( LogLevel.Warn | LogLevel.IsFiltered, _tag, b.ToString(), null, _filePath, 1 );
                b.Clear();
            }
            if( error != null )
            {
                throw new CKException( error.ToString() );
            }
            return new MeterState( meter, ++_currentMeterId, b );

            static void Validate( ref StringBuilder? error, StringBuilder warning, Meter meter )
            {
                // Applying https://opentelemetry.io/docs/specs/semconv/general/naming/#recommendations-for-opentelemetry-authors
                CheckMeterName( ref error, warning, "Meter name", meter.Name );
                if( meter.Tags != null )
                {
                    ValidateTags( ref error, warning, meter.Tags, () => $"Meter '{meter.Name}'" );
                }

                static void CheckMeterName( ref StringBuilder? error, StringBuilder warning, string what, string text )
                {
                    if( string.IsNullOrWhiteSpace( text ) )
                    {
                        AddErrorOrWarning( ref error, $"{what} '{text}'", "Name must contain a-z, digits, underscores and can contain single dot separators." );
                    }
                    else if( !MeterNameRegex().IsMatch( text ) )
                    {
                        AddErrorOrWarning( ref warning, $"{what} '{text}'", "Name must contain a-z, digits, underscores and can contain single dot separators." );
                    }
                }
            }
        }

        public override string ToString() => _jsonDesc;

        [GeneratedRegex( "^[a-z][_a-z0-9]*(\\.[_a-z0-9]*)*$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
        private static partial Regex MeterNameRegex();

    }
     
}
