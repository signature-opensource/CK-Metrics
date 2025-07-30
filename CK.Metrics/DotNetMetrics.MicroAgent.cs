using CK.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    static class MicroAgent
    {
        readonly static Channel<object?> _channel;

        static MicroAgent()
        {
            _channel = Channel.CreateUnbounded<object?>( new UnboundedChannelOptions() { SingleReader = true } );
            AppDomain.CurrentDomain.DomainUnload += Stop;
            AppDomain.CurrentDomain.ProcessExit += Stop;
            _ = RunLoopAsync();
            static void Stop( object? sender, EventArgs e )
            {
                _channel.Writer.TryWrite( null );
            }
        }

        internal static void Push( object o ) => _channel.Writer.TryWrite( o );

        static async Task RunLoopAsync()
        {
            var monitor = new ActivityMonitor( "DotNetMetrics.MicroAgent" );
            object? e;
            while( (e = await _channel.Reader.ReadAsync()) != null )
            {
                switch( e )
                {
                    case MetricsConfiguration c:
                        Apply( monitor, c );
                        break;
                    case InstrumentState i:
                        // OnInstrumentPublished.
                        if( ApplyConfigurations( monitor, i ) )
                        {
                            var text = $"IConfig[{i.Info.Configuration.JsonDescription}]";
                            SendMetricLog( text );
                        }
                        break;
                }
            }
        }

    }
}

