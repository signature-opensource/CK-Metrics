using System.Text;
using CK.Core;
using CK.Metrics;
using FASTER.core;

// ReSharper disable once CheckNamespace
// Namespace is fixed to "CK.Monitoring" for all GrandOutputHandlers and their configuration classes.
namespace CK.Monitoring;

/// <summary>
/// Sealed <see cref="IGrandOutputHandler"/> that writes metrics entries to a shared FasterLog instance.
/// This handler only writes entries - it does not consume them.
/// <para>
/// The FasterLog instance is injected at runtime via <see cref="SetFasterLog"/> method,
/// typically called by a feature driver (e.g., SetMetricsFasterLogAction in CK.AppIdentity.Monitoring.Metrics).
/// </para>
/// </summary>
public sealed class MetricsLogHandler : IGrandOutputHandler
{
    FasterLog? _log;
    int _commitRate;
    int _countCommit;
    byte[] _buffer;

    /// <summary>
    /// Initializes a new <see cref="MetricsLogHandler"/>.
    /// </summary>
    /// <param name="configuration">The handler configuration.</param>
    public MetricsLogHandler( MetricsLogHandlerConfiguration configuration )
    {
        _commitRate = configuration.CommitRate;
        _countCommit = _commitRate;
        _buffer = new byte[64];
    }

    /// <summary>
    /// Gets whether this handler has a FasterLog instance set.
    /// </summary>
    public bool HasFasterLog => _log != null;

    /// <summary>
    /// Sets the FasterLog instance for this handler.
    /// Called by the feature driver (via e.g., SetMetricsFasterLogAction in CK.AppIdentity.Monitoring.Metrics).
    /// </summary>
    /// <param name="log">The FasterLog instance to use for writing metrics.</param>
    /// <exception cref="InvalidOperationException">Thrown if FasterLog is already set.</exception>
    public void SetFasterLog( FasterLog log )
    {
        Throw.CheckState( _log == null, "FasterLog is already set." );
        Throw.CheckNotNullArgument( log );
        _log = log;
    }

    /// <summary>
    /// Clears the FasterLog instance from this handler.
    /// Called by the feature driver (via e.g., ClearMetricsFasterLogAction in CK.AppIdentity.Monitoring.Metrics) during shutdown.
    /// </summary>
    public void ClearFasterLog()
    {
        _log = null;
    }

    /// <inheritdoc />
    public ValueTask<bool> ActivateAsync( IActivityMonitor monitor )
    {
        // Nothing to initialize - FasterLog is set via action later.
        return ValueTask.FromResult( true );
    }

    /// <inheritdoc />
    public ValueTask HandleAsync( IActivityMonitor monitor, InputLogEntry logEvent )
    {
        // Skip if FasterLog not yet configured.
        if( _log == null ) return ValueTask.CompletedTask;

        // Skip entries with the "SecurityCritical" tag, no matter what.
        if( logEvent.Tags.Overlaps( ActivityMonitor.Tags.SecurityCritical ) )
            return ValueTask.CompletedTask;

        // Only process metrics entries from StaticLogger with the Metrics tag.
        // While CK-Metrics writes text as pinky-promise ASCII, there is absolutely nothing
        // preventing anybody else from just sending whatever string they want
        // on the StaticLogger with the Metrics tag.
        if( logEvent.MonitorId == ActivityMonitor.StaticLogMonitorUniqueId
            && logEvent.Tags.Overlaps( DotNetMetrics.MetricsTag )
            && logEvent.Text is not null
            && Ascii.IsValid( logEvent.Text.AsSpan() ) )
        {
            DateTime dt = logEvent.LogTime.TimeUtc;
            string text = logEvent.Text;

            // DateTime is 8 bytes, Text is 1 byte per character (ASCII).
            int requiredSize = sizeof(long) + text.Length;

            if( requiredSize > (1 << 16) - sizeof(long) )
            {
                monitor.Warn( DotNetMetrics.MetricsTag, $"Metrics entry too large: {text}" );
                return ValueTask.CompletedTask;
            }

            while( _buffer.Length < requiredSize )
            {
                // Grow buffer.
                Array.Resize( ref _buffer, _buffer.Length * 2 );
            }

            var span = _buffer.AsSpan();

            // Write DateTime (8 bytes).
            BitConverter.TryWriteBytes( span, dt.ToBinary() );
            // Write Text (1 byte per character).
            Encoding.ASCII.GetBytes( text, span[sizeof(long)..] );

            // Ship it to FasterLog.
            _log.Enqueue( span[..requiredSize] );
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask OnTimerAsync( IActivityMonitor monitor, TimeSpan timerSpan )
    {
        // Skip if FasterLog not yet configured.
        if( _log == null ) return;

        if( --_countCommit == 0 )
        {
            await _log.CommitAsync();
            _countCommit = _commitRate;
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> ApplyConfigurationAsync( IActivityMonitor monitor, IHandlerConfiguration c )
    {
        if( c is not MetricsLogHandlerConfiguration newConfig ) return ValueTask.FromResult( false );

        _commitRate = newConfig.CommitRate;
        if( _countCommit > _commitRate ) _countCommit = _commitRate;

        return ValueTask.FromResult( true );
    }

    /// <inheritdoc />
    public ValueTask DeactivateAsync( IActivityMonitor monitor )
    {
        // Do NOT dispose FasterLog - it is owned by the feature driver.
        return ValueTask.CompletedTask;
    }
}
