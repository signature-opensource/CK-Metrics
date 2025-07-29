using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    internal sealed class InstrumentState<T> : InstrumentState where T : struct
    {
        ILocalAggregator<T> _localAggregator;

        internal InstrumentState( MeterState meter,
                                  Instrument instrument,
                                  int id,
                                  string typeName,
                                  string measureTypeName,
                                  StringBuilder b )
            : base( meter, instrument, id, typeName, measureTypeName, b )
        {
            Throw.DebugAssert( TypeExtensions.TypeAliases[typeof( T )] == measureTypeName );
            _localAggregator = ILocalAggregator<T>.Null;
        }

        public override ILocalAggregator LocalAggregator => _localAggregator;

        internal void HandleMeasure( T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags )
        {
            if( _localAggregator.HandleMeasure( measurement, tags ) )
            {
                var text = $"M:{_sInstrumentId}:{measurement}";
                if( !tags.IsEmpty )
                {
                    var b = new StringBuilder( text );
                    var w = new StringWriter( b );
                    b.Append( ":[" );
                    WriteTags( b, w, tags );
                    text = b.Append( ']' ).ToString();
                }
                SendMetricLog( text );
            }
        }
    }

}


