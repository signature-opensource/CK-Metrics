using CK.Core;
using System;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CK.Metrics;

public static partial class DotNetMetrics
{
    sealed class Lock { }

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

        internal static void SyncWait()
        {
            var l = new Lock();
            lock( l )
            {
                if( _channel.Writer.TryWrite( l ) )
                {
                    System.Threading.Monitor.Wait( l );
                }
            }
        }

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
                            SendInstrumentConfig( i.Info );
                        }
                        break;
                    case ValueTuple<Instrument, InstrumentConfiguration> defaultConfig:
                        // DefaultConfigure
                        if( _instruments.TryGetValue( defaultConfig.Item1, out var iState ) )
                        {
                            if( iState.Info.Configuration.Equals( InstrumentConfiguration.BasicDisabled ) )
                            {
                                bool changed = iState.SetConfiguration( monitor, _listener, defaultConfig.Item2, true );
                                Throw.DebugAssert( changed );
                                SendInstrumentConfig( iState.Info );
                            }
                            else
                            {
                                monitor.Debug( _tag, $"Skipped DefaultConfigure for instrument '{iState.Info.FullName}' as it is already configured." );
                            }
                        }
                        else
                        {
                            monitor.Warn( ActivityMonitor.Tags.ToBeInvestigated | _tag,
                                          $"'{nameof( DefaultConfigure )}' method has been called on a non registered instrument." );
                        }
                        break;
                    case TaskCompletionSource<DotNetMetricsInfo> tc:
                        tc.SetResult( DoGetConfiguration() );
                        break;
                    case Lock:
                        lock( e )
                        {
                            System.Threading.Monitor.Pulse( e );
                        }
                        break;
                }
            }

            static void SendInstrumentConfig( FullInstrumentInfo info )
            {
                var text = $"{_instrumentConfigurationPrefix}{info.Info.InstrumentId},{info.Configuration.JsonDescription}";
                SendMetricLog( text );
            }
        }

    }
}

