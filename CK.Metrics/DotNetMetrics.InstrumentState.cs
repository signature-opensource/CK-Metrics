using CK.Core;
using System;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    static int _currentInstrumentId;

    internal abstract partial class InstrumentState
    {
        readonly Instrument _instrument;
        readonly string _fullName;
        readonly string _jsonDesc;
        readonly MeterState _meter;
        internal InstrumentState? _next;
        InstrumentConfiguration? _configuration;
        readonly int _instrumentId;
        protected string _sInstrumentId;
        bool _expectOnMeasurementsCompleted;

        public MeterState Meter => _meter;

        public int InstrumentId => _instrumentId;

        public string JsonDescription => _jsonDesc;

        public bool IsEnabled => _configuration?.Enabled ?? false;

        public string FullName => _fullName;

        public Instrument Instrument => _instrument;

        public abstract ILocalAggregator LocalAggregator { get; }

        internal void SetConfiguration( MeterListener listener,
                                        UserMessageCollector messages,
                                        InstrumentConfiguration configuration )
        {
            bool? mustEnable = null;
            lock( _jsonDesc )
            {
                bool newEnable = configuration.Enabled;
                bool oldEnabled = IsEnabled;
                if( oldEnabled != newEnable )
                {
                    if( newEnable )
                    {
                        mustEnable = true;
                    }
                    else
                    {
                        mustEnable = false;
                        _expectOnMeasurementsCompleted = true;
                    }
                }
                _configuration = configuration;
                if( oldEnabled )
                {
                    OnHotSetConfiguration( messages );
                }
            }
            if( mustEnable.HasValue )
            {
                if( mustEnable.Value )
                {
                    listener.EnableMeasurementEvents( _instrument, this );
                }
                else
                {
                    listener.DisableMeasurementEvents( _instrument );
                }
            }
        }

        private void OnHotSetConfiguration( UserMessageCollector messages )
        {
            throw new NotImplementedException();
        }

        internal bool OnMeasurementsCompleted()
        {
            LocalAggregator.Flush();
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

        protected InstrumentState( MeterState meter,
                                   Instrument instrument,
                                   int id,
                                   string typeName,
                                   string measureTypeName,
                                   StringBuilder b )
        {
            _meter = meter;
            _instrument = instrument;
            _instrumentId = id;
            _sInstrumentId = id.ToString( CultureInfo.InvariantCulture );
            _jsonDesc = Write( b, meter, instrument, _sInstrumentId, typeName, measureTypeName );
            _fullName = meter.Meter.Name + '/' + instrument.Name;
            _next = meter._first;
            meter._first = this;

            static string Write( StringBuilder b, MeterState meter, Instrument instrument, string id, string typeName, string measureTypeName )
            {
                Throw.DebugAssert( b.Length == 0 );
                StringWriter? w = null;
                // Name and types are purely ascii.
                b.Append( id ).Append( ",\"").Append( meter.MeterId )
                 .Append( ",\"" ).Append( instrument.Name )
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
                return b.ToString();
            }

        }

        public static InstrumentState Create( MeterState meter, Instrument instrument, StringBuilder b )
        {
            Throw.DebugAssert( b.Length == 0 );
            StringBuilder? error = null;
            Validate( ref error, b, instrument, out var typeName, out var measureType );
            if( b.Length > 0 )
            {
                ActivityMonitor.StaticLogger.UnfilteredLog( LogLevel.Warn | LogLevel.IsFiltered, _tag, b.ToString(), null, _filePath, 1 );
                b.Clear();
            }
            if( error != null )
            {
                throw new CKException( error.ToString() );
            }
            if( measureType == typeof( int ) )
            {
                return new InstrumentState<int>( meter, instrument, ++_currentInstrumentId, typeName, "int", b );
            }
            if( measureType == typeof( double ) )
            {
                return new InstrumentState<double>( meter, instrument, ++_currentInstrumentId, typeName, "double", b );
            }
            if( measureType == typeof( long ) )
            {
                return new InstrumentState<long>( meter, instrument, ++_currentInstrumentId, typeName, "long", b );
            }
            if( measureType == typeof( float ) )
            {
                return new InstrumentState<float>( meter, instrument, ++_currentInstrumentId, typeName, "float", b );
            }
            if( measureType == typeof( byte ) )
            {
                return new InstrumentState<byte>( meter, instrument, ++_currentInstrumentId, typeName, "byte", b );
            }
            if( measureType == typeof( short ) )
            {
                return new InstrumentState<short>( meter, instrument, ++_currentInstrumentId, typeName, "short", b );
            }
            Throw.DebugAssert( measureType == typeof( decimal ) );
            return new InstrumentState<decimal>( meter, instrument, ++_currentInstrumentId, typeName, "decimal", b );

            static void Validate( ref StringBuilder? error,
                                  StringBuilder warning,
                                  Instrument instrument,
                                  out string typeName,
                                  out Type measureType )
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

        }

        public override string ToString() => _jsonDesc;

        // https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-name-syntax
        [GeneratedRegex( "^[a-z][-_\\./a-z0-9]*$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
        private static partial Regex InstrumentNameRegex();

    }
}
