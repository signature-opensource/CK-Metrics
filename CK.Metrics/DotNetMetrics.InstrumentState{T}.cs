using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Text;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    internal sealed class InstrumentState<T> : InstrumentState where T : struct
    {
        internal InstrumentState( MeterState meter,
                                  Instrument instrument,
                                  int id,
                                  string typeName,
                                  string measureTypeName,
                                  System.Collections.Immutable.ImmutableArray<KeyValuePair<string, object?>> tags,
                                  StringBuilder b )
            : base( meter, instrument, id, typeName, measureTypeName, tags, b )
        {
            Throw.DebugAssert( TypeExtensions.TypeAliases[typeof( T )] == measureTypeName );
        }

        protected override void OnConfigurationChanged( IActivityMonitor monitor, InstrumentConfiguration configuration )
        {
            
        }

        internal void HandleMeasure( T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags )
        {
            var text = $"M:{_sInstrumentId}:{measurement}";
            if( !tags.IsEmpty )
            {
                var b = new StringBuilder( text );
                StringWriter? w = null;
                b.Append( ":[" );
                WriteTags( b, ref w, tags );
                text = b.Append( ']' ).ToString();
            }
            SendMetricLog( text );
        }
    }

}


