using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    static int _currentInstrumentId;

    internal abstract partial class InstrumentState
    {
        readonly Instrument _instrument;
        readonly MeterState _meter;
        readonly FullInstrumentInfo _info;
        readonly object _lock;
        internal InstrumentState? _next;
        protected string _sInstrumentId;
        bool _enabled;
        bool _expectOnMeasurementsCompleted;

        public MeterState Meter => _meter;

        public Instrument Instrument => _instrument;

        public FullInstrumentInfo Info => _info;

        public bool IsEnabled => _enabled;

        internal bool SetConfiguration( IActivityMonitor monitor,
                                        MeterListener listener,
                                        InstrumentConfiguration configuration,
                                        bool isDefaultConfigure )
        {
            if( _info.Configuration.Equals( configuration ) )
            {
                Throw.DebugAssert( "DefaultConfigure cannot be BasicDisabled and this is called only if the current configuration is BasicDisabled",
                                   !isDefaultConfigure );
                monitor.Debug( $"Unchanged configuration for {(_enabled ? "en" : "dis")}abled instrument '{_info.FullName}'." );
                return false;
            }
            bool? mustEnable = null;
            lock( _lock )
            {
                bool newEnable = configuration.Enabled;
                if( _enabled != newEnable )
                {
                    if( newEnable )
                    {
                        if( isDefaultConfigure )
                        {
                            monitor.Trace( $"Instrument '{_info.FullName}' is enabled and configured by DefaultConfigure." );
                        }
                        else
                        {
                            monitor.Trace( $"Enabling and reconfiguring instrument '{_info.FullName}'." );
                        }
                        mustEnable = true;
                    }
                    else
                    {
                        mustEnable = false;
                        _expectOnMeasurementsCompleted = true;
                        Throw.DebugAssert( "This is called only if the current configuration is BasicDisabled",
                                           !isDefaultConfigure );
                        monitor.Trace( $"Disabling and reconfiguring instrument '{_info.FullName}'." );
                    }
                    _enabled = newEnable;
                }
                else
                {
                    if( isDefaultConfigure )
                    {
                        Throw.DebugAssert( !_enabled );
                        monitor.Trace( $"Applied DefaultConfigure to disabled instrument '{_info.FullName}'." );
                    }
                    else
                    {
                        monitor.Debug( $"Reconfiguring {(_enabled ? "en" : "dis")}abled instrument '{_info.FullName}'." );
                    }
                }
                _info.Configuration = configuration;
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
            return true;
        }

        internal bool OnMeasurementsCompleted()
        {
            lock( _lock )
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
                                   ImmutableArray<KeyValuePair<string, object?>> tags,
                                   StringBuilder b )
        {
            _lock = new object();
            _meter = meter;
            _instrument = instrument;
            _sInstrumentId = id.ToString( CultureInfo.InvariantCulture );
            _info = new FullInstrumentInfo( meter.Info,
                                            new InstrumentInfo( id,
                                                                meter.Info.MeterId,
                                                                instrument.Name,
                                                                instrument.Description,
                                                                instrument.Unit,
                                                                typeName,
                                                                measureTypeName,
                                                                tags,
                                                                instrument.IsObservable ),
                                            InstrumentConfiguration.BasicDisabled );
            _next = meter._first;
            Interlocked.Exchange( ref meter._first, this );
        }

        public static InstrumentState Create( MeterState meter, Instrument instrument, StringBuilder b )
        {
            Throw.DebugAssert( b.Length == 0 );
            StringBuilder? error = null;
            Validate( ref error, b, instrument, out var typeName, out var measureType, out var tags );
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
                return new InstrumentState<int>( meter, instrument, ++_currentInstrumentId, typeName, "int", tags, b );
            }
            if( measureType == typeof( double ) )
            {
                return new InstrumentState<double>( meter, instrument, ++_currentInstrumentId, typeName, "double", tags, b );
            }
            if( measureType == typeof( long ) )
            {
                return new InstrumentState<long>( meter, instrument, ++_currentInstrumentId, typeName, "long", tags, b );
            }
            if( measureType == typeof( float ) )
            {
                return new InstrumentState<float>( meter, instrument, ++_currentInstrumentId, typeName, "float", tags, b );
            }
            if( measureType == typeof( byte ) )
            {
                return new InstrumentState<byte>( meter, instrument, ++_currentInstrumentId, typeName, "byte", tags, b );
            }
            if( measureType == typeof( short ) )
            {
                return new InstrumentState<short>( meter, instrument, ++_currentInstrumentId, typeName, "short", tags, b );
            }
            Throw.DebugAssert( measureType == typeof( decimal ) );
            return new InstrumentState<decimal>( meter, instrument, ++_currentInstrumentId, typeName, "decimal", tags, b );

            static void Validate( ref StringBuilder? error,
                                  StringBuilder warning,
                                  Instrument instrument,
                                  out string typeName,
                                  out Type measureType,
                                  out ImmutableArray<KeyValuePair<string,object?>> tags )
            {
                if( instrument.Name.Length > 255
                    || !InstrumentNameRegex().IsMatch( instrument.Name ) )
                {
                    AddErrorOrWarning( ref error,
                              $"Instrument '{instrument.Name}'",
                              $"Name must follow https://opentelemetry.io/docs/specs/otel/metrics/api/#instrument-name-syntax." );
                }
                tags = instrument.Tags != null
                            ? ValidateTags( ref error, warning, instrument.Tags, () => $"Instrument '{instrument.Name}'" )
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

        }

        public override string ToString() => _info.FullName;

    }
}
