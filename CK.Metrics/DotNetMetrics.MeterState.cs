using CK.Core;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Text;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    internal sealed partial class MeterState
    {
        static int _currentMeterId;

        readonly Meter _meter;
        internal InstrumentState? _first;
        readonly MeterInfo _info;

        MeterState( Meter meter, int id, ImmutableArray<KeyValuePair<string, object?>> tags, StringBuilder b )
        {
            _meter = meter;
            _info = new MeterInfo( meter.Name, meter.Version, telemetrySchemaUrl: null, tags, id );
        }

        public Meter Meter => _meter;

        public MeterInfo Info => _info;

        public IEnumerable<InstrumentState> InstrumentStates
        {
            get
            {
                var f = _first;
                while( f != null )
                {
                    yield return f;
                    f = f._next;
                }
            }
        }

        internal static MeterState Create( Meter meter, StringBuilder b )
        {
            Throw.DebugAssert( b.Length == 0 );
            StringBuilder? error = null;
            Validate( ref error, warning: b, meter, out var tags );
            if( b.Length != 0 )
            {
                ActivityMonitor.StaticLogger.UnfilteredLog( LogLevel.Warn | LogLevel.IsFiltered, _tag, b.ToString(), null, _filePath, 1 );
                b.Clear();
            }
            if( error != null )
            {
                throw new CKException( error.ToString() );
            }
            return new MeterState( meter, ++_currentMeterId, tags, b );

            static void Validate( ref StringBuilder? error,
                                  StringBuilder warning,
                                  Meter meter,
                                  out ImmutableArray<KeyValuePair<string,object?>> tags )
            {
                // Applying https://opentelemetry.io/docs/specs/semconv/general/naming/#recommendations-for-opentelemetry-authors
                CheckMeterName( ref error, warning, "Meter name", meter.Name );
                tags = meter.Tags != null
                        ? ValidateTags( ref error, warning, meter.Tags, () => $"Meter '{meter.Name}'" )
                        : [];

                static void CheckMeterName( ref StringBuilder? error, StringBuilder warning, string what, string text )
                {
                    if( string.IsNullOrWhiteSpace( text ) )
                    {
                        AddErrorOrWarning( ref error, $"{what} '{text}'", "Name must contain a-z, digits, underscores and can contain single dot separators." );
                    }
                    else
                    {
                        if( text.Length > MeterNameLengthLimit )
                        {
                            AddErrorOrWarning( ref error, $"{what} '{text}'", $"Name cannot be longer than MeterNameLengthLimit that is {MeterNameLengthLimit}." );
                        }
                        if( !MeterNameRegex().IsMatch( text ) )
                        {
                            AddErrorOrWarning( ref error, $"{what} '{text}'", "Name must be a simple namespace-like identifier." );
                        }
                    }
                }
            }
        }

        public override string ToString() => _info.ToString();

    }
     
}
