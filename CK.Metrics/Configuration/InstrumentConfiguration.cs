using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;

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
    //readonly LocalAggregateKind _aggregateKind;
    string? _jsonDesc;

    /// <summary>
    /// Gets a default, disabled, instrument configuration.
    /// </summary>
    public static readonly InstrumentConfiguration BasicDisabled = new InstrumentConfiguration( false, "false" );

    /// <summary>
    /// Gets a default enabled instrument configuration without any options.
    /// </summary>
    public static readonly InstrumentConfiguration BasicEnabled = new InstrumentConfiguration( true, "true" );

    /// <summary>
    /// Initializes a new configuration.
    /// </summary>
    /// <param name="enabled">Whether the instrument must be enabled.</param>
    public InstrumentConfiguration( bool enabled )
        : this( enabled, null )
    {
    }

    internal InstrumentConfiguration( bool enabled,
                                      string? jsonDesc )
    {
        _enabled = enabled;
        _jsonDesc = jsonDesc;
    }

    /// <summary>
    /// Gets whether the instrument is enabled.
    /// </summary>
    public bool Enabled => _enabled;

    //public LocalAggregateKind AggregateKind => _aggregateKind;

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

    /// <summary>
    /// Tries to match a configuration and forward the <paramref name="head"/> on success.
    /// </summary>
    /// <param name="head">The head to match.</param>
    /// <param name="instrumentConfiguration">The <see cref="InstrumentConfiguration"/> on success.</param>
    /// <returns>True on success (head has been forwarded), false otherwise.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, [NotNullWhen( true )] out InstrumentConfiguration? instrumentConfiguration )
    {
        var h = head;
        if( head.TryMatchBool( out var enabled ) )
        {
            instrumentConfiguration = enabled ? BasicEnabled : BasicDisabled;
            return true;
        }
        head = h;
        instrumentConfiguration = null;
        return false;
    }
}

