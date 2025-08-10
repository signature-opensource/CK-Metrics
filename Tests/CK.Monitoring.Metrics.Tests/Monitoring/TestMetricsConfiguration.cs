using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Monitoring;

public class TestMetricsConfiguration : MetricsConfigurationBase
{
    private readonly SemaphoreSlim _entriesBlockedSemaphore = new(0, int.MaxValue);

    public TestMetricsConfiguration()
    {
        TestEntryQueue = new ConcurrentQueue<byte[]>();
    }

    private TestMetricsConfiguration( TestMetricsConfiguration source ) : base( source )
    {
        // Keep the same queue for tests
        TestEntryQueue = source.TestEntryQueue;
        BlockEntries = source.BlockEntries;
        _entriesBlockedSemaphore = source._entriesBlockedSemaphore;
    }

    /// <summary>
    /// Collected entries.
    /// </summary>
    public ConcurrentQueue<byte[]> TestEntryQueue { get; }

    /// <summary>
    /// True to block entries with an exception.
    /// </summary>
    public bool BlockEntries { get; set; }

    public override IHandlerConfiguration Clone()
    {
        return new TestMetricsConfiguration( this );
    }

    public async Task WaitForEntriesBlockedAsync( CancellationToken cancellationToken = default )
    {
        await _entriesBlockedSemaphore.WaitAsync( cancellationToken );
    }

    public void OnEntriesBlocked()
    {
        _entriesBlockedSemaphore.Release();
    }
}
