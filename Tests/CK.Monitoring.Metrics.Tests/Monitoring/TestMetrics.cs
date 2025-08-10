using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CK.Monitoring;

public class TestMetrics : MetricsHandlerBase
{
    public TestMetrics( TestMetricsConfiguration config, IdentityCard identityCard )
        : base( config, identityCard )
    {
    }

    public new TestMetricsConfiguration Configuration => (TestMetricsConfiguration) base.Configuration;

    public override Task<TimeSpan> ProcessEntriesAsync( IEnumerable<ReadOnlyMemory<byte>> entries )
    {
        if( Configuration.BlockEntries )
        {
            Configuration.OnEntriesBlocked();
            throw new Exception( "TestFasterLog is currently blocking log entries" );
        }

        foreach( var entry in entries ) Configuration.TestEntryQueue.Enqueue( entry.ToArray() ); // Full copy!

        return Task.FromResult( TimeSpan.Zero );
    }
}
