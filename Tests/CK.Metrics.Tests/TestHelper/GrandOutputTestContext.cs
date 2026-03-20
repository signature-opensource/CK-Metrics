using CK.Monitoring;
using System;

namespace CK.Metrics.Tests;

/// <summary>
/// Disposable test context that wires the full metrics pipeline:
/// real <c>System.Diagnostics.Metrics</c> instruments → <see cref="DotNetMetrics"/> →
/// <see cref="GrandOutput"/> → <see cref="TestMetricsLogHandler"/> → <see cref="TestDispatcher"/>.
/// <para>
/// On construction the handler is registered with <see cref="GrandOutput.Default"/>.
/// On disposal the handler is removed. Use with a <c>using</c> statement.
/// </para>
/// </summary>
public sealed class GrandOutputTestContext : IDisposable
{
    readonly TestMetricsLogHandler _handler;

    /// <summary>
    /// Gets the <see cref="TestDispatcher"/> that collects all dispatched metrics events.
    /// </summary>
    public TestDispatcher Dispatcher { get; }

    /// <summary>
    /// Initializes a new context with a default <see cref="TestDispatcher"/>.
    /// </summary>
    public GrandOutputTestContext()
        : this( new TestDispatcher() )
    {
    }

    /// <summary>
    /// Initializes a new context with a pre-configured <see cref="TestDispatcher"/>
    /// (e.g. with custom <see cref="TestDispatcher.MeterStateProvider"/>).
    /// </summary>
    /// <param name="dispatcher">The dispatcher to use.</param>
    public GrandOutputTestContext( TestDispatcher dispatcher )
    {
        Dispatcher = dispatcher;
        _handler = new TestMetricsLogHandler( dispatcher );
        GrandOutput.Default!.Sink.SubmitAddHandler( _handler );
        GrandOutput.Default.Sink.SyncWait();
    }

    /// <summary>
    /// Flushes the <see cref="GrandOutput"/> sink so that all pending log entries
    /// have been processed by the handler before assertions.
    /// </summary>
    public void SyncWait() => GrandOutput.Default!.Sink.SyncWait();

    /// <summary>
    /// Removes the handler from <see cref="GrandOutput.Default"/>.
    /// </summary>
    public void Dispose()
    {
        GrandOutput.Default?.Sink.SubmitRemoveHandler( _handler );
    }
}
