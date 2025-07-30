using CK.Core;
using System;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace CK.Metrics;

/// <summary>
/// Currently very simple instrument immutable configuration.
/// <para>
/// It is planned to extend this to support <see href="https://en.wikipedia.org/wiki/Aggregate_function#Decomposable_aggregate_functions">Decomposable aggregate functions</see>
/// as optional "cooler". 
/// </para>
/// </summary>
public sealed class InstrumentConfiguration : IEquatable<InstrumentConfiguration>
{
    readonly bool _enabled;
    string? _jsonDesc;

    /// <summary>
    /// Gets a default, disabled, instrument configuration.
    /// </summary>
    public static readonly InstrumentConfiguration Default = new InstrumentConfiguration( false, "false" );

    /// <summary>
    /// Gets a default enabled instrument configuration without any options.
    /// </summary>
    public static readonly InstrumentConfiguration BasicallyEnabled = new InstrumentConfiguration( true, "true" );

    /// <summary>
    /// Initializes a new configuration.
    /// </summary>
    /// <param name="enabled">Whether the instrument must be enabled.</param>
    public InstrumentConfiguration( bool enabled,
                                    string? jsonDesc = null )
    {
        _enabled = enabled;
    }

    /// <summary>
    /// Gets whether the instrument is enabled.
    /// </summary>
    public bool Enabled => _enabled;

    /// <summary>
    /// Gets the Json description.
    /// </summary>
    internal string JsonDescription => _jsonDesc ??= $"{(_enabled ? "true" : "false")}";

    public bool Equals( InstrumentConfiguration? other )
    {
        if ( other == null ) return false;
        return _enabled == other._enabled;
    }

    public override bool Equals( object? obj ) => Equals( obj as InstrumentConfiguration );

    public override int GetHashCode() => HashCode.Combine( _enabled );

    internal static bool DoTryParse( string text, int idxStart, out InstrumentConfiguration? instrumentConfiguration )
    {
        var head = text.AsSpan( idxStart );
        if( head.TryMatchBool( out var enabled ) )
        {
            instrumentConfiguration = enabled ? BasicallyEnabled : Default;
            return true;
        }
        instrumentConfiguration = null;
        return false;
    }
}

