using CK.Core;
using CommunityToolkit.HighPerformance;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Diagnostics.SymbolStore;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    internal sealed partial class MeterState
    {
        static int _currentMeterId;

        readonly Meter _meter;
        internal InstrumentState? _first;
        readonly MeterInfo _info;

        MeterState( Meter meter, int id, ImmutableArray<KeyValuePair<string, object?>> tags )
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

        internal static MeterState? Create( Meter meter )
        {
            StringBuilder? error = null;
            StringBuilder? warning = null;
            Validate( ref error, ref warning, meter, out var tags );
            if( warning != null )
            {
                ActivityMonitor.StaticLogger.UnfilteredLog( LogLevel.Warn | LogLevel.IsFiltered, _tag, warning.ToString(), null, _filePath, 1 );
            }
            if( error != null )
            {
                ActivityMonitor.StaticLogger.UnfilteredLog( LogLevel.Error | LogLevel.IsFiltered, _tag, error.ToString(), null, _filePath, 1 );
                return null;
            }
            return new MeterState( meter, ++_currentMeterId, tags );
        }

        public override string ToString() => _info.ToString();

    }
     
}
