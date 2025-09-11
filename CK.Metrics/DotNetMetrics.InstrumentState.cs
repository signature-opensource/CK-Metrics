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
                monitor.Debug( _tag, $"Unchanged configuration for {(_enabled ? "en" : "dis")}abled instrument '{_info.FullName}'." );
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
                            monitor.Trace( _tag, $"Instrument '{_info.FullName}' is enabled and configured by DefaultConfigure." );
                        }
                        else
                        {
                            monitor.Trace( _tag, $"Enabling and reconfiguring instrument '{_info.FullName}'." );
                        }
                        mustEnable = true;
                    }
                    else
                    {
                        mustEnable = false;
                        _expectOnMeasurementsCompleted = true;
                        Throw.DebugAssert( "This is called only if the current configuration is BasicDisabled",
                                           !isDefaultConfigure );
                        monitor.Trace( _tag, $"Disabling and reconfiguring instrument '{_info.FullName}'." );
                    }
                    _enabled = newEnable;
                }
                else
                {
                    if( isDefaultConfigure )
                    {
                        Throw.DebugAssert( !_enabled );
                        monitor.Trace( _tag, $"Applied DefaultConfigure to disabled instrument '{_info.FullName}'." );
                    }
                    else
                    {
                        monitor.Debug( _tag, $"Reconfiguring {(_enabled ? "en" : "dis")}abled instrument '{_info.FullName}'." );
                    }
                }
                _info.Configuration = configuration;
                OnConfigurationChanged( monitor, configuration  );
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

        protected abstract void OnConfigurationChanged( IActivityMonitor monitor, InstrumentConfiguration configuration );

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
                                   ImmutableArray<KeyValuePair<string, object?>> tags )
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

        public static InstrumentState Create( MeterState meter, Instrument instrument )
        {
            StringBuilder? error = null;
            StringBuilder? warning = null;
            Validate( ref error, ref warning, instrument, out var typeName, out var measureType, out var tags );
            if( warning != null )
            {
                ActivityMonitor.StaticLogger.UnfilteredLog( LogLevel.Warn | LogLevel.IsFiltered, _tag, warning.ToString(), null, _filePath, 1 );
            }
            if( error != null )
            {
                throw new CKException( error.ToString() );
            }
            if( measureType == typeof( int ) )
            {
                return new InstrumentState<int>( meter, instrument, ++_currentInstrumentId, typeName, "int", tags );
            }
            if( measureType == typeof( double ) )
            {
                return new InstrumentState<double>( meter, instrument, ++_currentInstrumentId, typeName, "double", tags );
            }
            if( measureType == typeof( long ) )
            {
                return new InstrumentState<long>( meter, instrument, ++_currentInstrumentId, typeName, "long", tags );
            }
            if( measureType == typeof( float ) )
            {
                return new InstrumentState<float>( meter, instrument, ++_currentInstrumentId, typeName, "float", tags );
            }
            if( measureType == typeof( byte ) )
            {
                return new InstrumentState<byte>( meter, instrument, ++_currentInstrumentId, typeName, "byte", tags );
            }
            if( measureType == typeof( short ) )
            {
                return new InstrumentState<short>( meter, instrument, ++_currentInstrumentId, typeName, "short", tags );
            }
            Throw.DebugAssert( measureType == typeof( decimal ) );
            return new InstrumentState<decimal>( meter, instrument, ++_currentInstrumentId, typeName, "decimal", tags );

        }

        public override string ToString() => _info.FullName;

    }
}
