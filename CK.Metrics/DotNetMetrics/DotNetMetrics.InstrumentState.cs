using System.Diagnostics.Metrics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace CK.Core;

public static partial class DotNetMetrics
{
    static int _currentInstrumentId;

    internal partial class InstrumentState
    {
        readonly Instrument _instrument;
        readonly string _fullName;
        readonly string _jsonDesc;
        readonly MeterState _meter;
        InstrumentState? _next;
        readonly int _instrumentId;
        bool _codeEnabled;
        bool _configEnabled;
        bool _expectOnMeasurementsCompleted;

        public MeterState Meter => _meter;

        public int InstrumentId => _instrumentId;

        public string JsonDescription => _jsonDesc;

        public bool IsEnabled => _codeEnabled || _configEnabled;

        public string FullName => _fullName;

        public Instrument Instrument => _instrument;

        internal void CodeEnable( MeterListener listener )
        {
            if( !_codeEnabled )
            {
                bool mustEnable;
                lock( _jsonDesc )
                {
                    mustEnable = !IsEnabled;
                    _codeEnabled = true;
                }
                if( mustEnable )
                {
                    listener.EnableMeasurementEvents( _instrument, this );
                }
            }
        }

        internal bool CodeDisable( MeterListener listener )
        {
            if( _codeEnabled )
            {
                bool mustDisable;
                lock( _jsonDesc )
                {
                    _codeEnabled = false;
                    mustDisable = !IsEnabled;
                    if( mustDisable )
                    {
                        _expectOnMeasurementsCompleted = true;
                    }
                }
                if( mustDisable )
                {
                    object? previousState = listener.DisableMeasurementEvents( _instrument );
                    Throw.DebugAssert( previousState == null || previousState == this );
                    // Meter has been disposed.
                    return previousState != null;
                }
            }
            return true;
        }

        internal bool OnMeasurementsCompleted()
        {
            lock( _jsonDesc )
            {
                if( _expectOnMeasurementsCompleted )
                {
                    _expectOnMeasurementsCompleted = false;
                    return true;
                }
                return false;
            }
        }

        InstrumentState( MeterState meter, Instrument instrument, int id, string jsonDesc )
        {
            _meter = meter;
            _instrument = instrument;
            _instrumentId = id;
            _jsonDesc = jsonDesc;
            _fullName = meter.Meter.Name + '/' + instrument.Name;
            _next = meter._first;
            meter._first = this;
        }

        public static InstrumentState Create( MeterState meter, Instrument instrument, StringBuilder b )
        {
            Throw.DebugAssert( b.Length == 0 );
            StringBuilder? error = null;
            Validate( ref error, b, instrument, out var typeName, out var measureTypeName );
            if( b.Length > 0 )
            {
                ActivityMonitor.StaticLogger.UnfilteredLog( LogLevel.Warn | LogLevel.IsFiltered, _tag, b.ToString(), null, _filePath, 1 );
                b.Clear();
            }
            if( error != null )
            {
                throw new CKException( error.ToString() );
            }
            Write( b, instrument, typeName, measureTypeName );
            return new InstrumentState( meter, instrument, ++_currentInstrumentId, b.ToString() );

            static void Validate( ref StringBuilder? error,
                                  StringBuilder warning,
                                  Instrument instrument,
                                  out string typeName,
                                  out string measureTypeName )
            {
                if( instrument.Name.Length > 255 || !InstrumentNameRegex().IsMatch( instrument.Name ) )
                {
                    AddErrorOrWarning( ref error,
                              $"Instrument '{instrument.Name}'",
                              $"Name must follow https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-name-syntax." );
                }
                if( instrument.Tags != null )
                {
                    ValidateTags( ref error, warning, instrument.Tags, () => $"Instrument '{instrument.Name}'" );
                }
                var t = instrument.GetType();
                bool valid = t.IsGenericType && !t.IsGenericTypeDefinition && t.Namespace == "System.Diagnostics.Metrics";
                if( !valid )
                {
                    AddErrorOrWarning( ref error,
                              $"Instrument '{instrument.Name}'",
                              $"Invalid instrument type '{t}'." );
                    typeName = measureTypeName = "";
                }
                else
                {
                    typeName = t.Name.Substring( 0, t.Name.IndexOf( '`' ) );
                    Throw.CheckState( instrument.IsObservable == typeName.StartsWith( "Observable" ) );
                    if( instrument.IsObservable ) typeName = typeName.Substring( 10 );
                    measureTypeName = TypeExtensions.TypeAliases[t.GenericTypeArguments[0]];
                }
                // https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-unit
                if( instrument.Unit != null && instrument.Unit.Length > 63 )
                {
                    AddErrorOrWarning( ref error, $"Instrument '{instrument.Name}'", $"Units '{instrument.Unit}' cannot be longer than 63 characters." );
                }
            }

            static void Write( StringBuilder b, Instrument instrument, string typeName, string measureTypeName )
            {
                StringWriter? w = null;
                // Name and types are purely ascii.
                b.Append( '"' ).Append( instrument.Name )
                 .Append( "\",\"" ).Append( typeName ).Append( '/' ).Append( measureTypeName )
                 .Append( "\"," ).Append( instrument.IsObservable ).Append( ",\"" );
                if( instrument.Description != null )
                {
                    w = new StringWriter( b );
                    JavaScriptEncoder.Default.Encode( w, instrument.Description );
                }
                b.Append( "\",[" );
                if( instrument.Tags != null )
                {
                    w ??= new StringWriter( b );
                    WriteTags( b, w, instrument.Tags );
                }
                b.Append( "],[" );
#if NET10_0_OR_GREATER
        if( instrument.Advice?.HistogramBucketBoundaries != null )
        {
            bool atLeastOne = false;
            foreach( var b in instrument.Advice.HistogramBucketBoundaries )
            {
                if( atLeastOne ) b.Append( ',' );
                atLeastOne = true;
                b.Append( b );
            }
        }
#endif
                b.Append( ']' );
            }

        }

        // https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-name-syntax
        [GeneratedRegex( "^[a-z][-_\\./a-z0-9]*$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
        internal static partial Regex InstrumentNameRegex();

    }
}
