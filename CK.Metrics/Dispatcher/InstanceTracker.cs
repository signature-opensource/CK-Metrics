using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CK.Metrics;

sealed class InstanceTracker<T> where T : class, ITrackedMetricsInfo
{
    readonly (T?,object?)[] _first;
    Dictionary<int, (T?, object?)>? _remainders;

    public InstanceTracker( int firstCount )
    {
        _first = new (T?, object?)[firstCount];
    }

    public (T?, object?) Find( int trackedId )
    {
        Throw.CheckArgument( trackedId >= 0 );
        return trackedId < _first.Length
                ? _first[trackedId]
                : _remainders != null
                    ? _remainders.GetValueOrDefault( trackedId )
                    : default;
    }

    public bool TryRemove( int trackedId, [NotNullWhen(true)]out T? removed, out object? state )
    {
        if( trackedId < _first.Length )
        {
            ref var entry = ref _first[trackedId];
            removed = entry.Item1;
            if( removed == null )
            {
                Throw.DebugAssert( entry.Item2 == null );
                state = null;
                return false;
            }
            state = entry.Item2;
            entry.Item1 = null;
            entry.Item2 = null;
            return true;
        }
        if( _remainders != null && _remainders.TryGetValue( trackedId, out var e ) )
        {
            removed = e.Item1;
            Throw.DebugAssert( removed != null );
            state = e.Item2;
            _remainders.Remove( trackedId );
            return true;
        }
        removed = null;
        state = null;
        return false;
    }

    public bool TryAdd( IActivityMonitor monitor,
                        T newOne,
                        [NotNullWhen(false)]out T? exists,
                        Func<IActivityMonitor,T,object?> stateProvider )
    {
        Throw.CheckNotNullArgument( newOne );
        int id = newOne.TrackeId;
        if( id < _first.Length )
        {
            ref var e = ref _first[id];
            if( (exists = e.Item1) != null )
            {
                return false;
            }
            e.Item1 = newOne;
            e.Item2 = stateProvider( monitor, newOne );
        }
        else
        {
            if( _remainders == null )
            {
                _remainders = new Dictionary<int, (T?, object?)>();
            }
            else if( _remainders.TryGetValue( id, out var e ) )
            {
                Throw.DebugAssert( e.Item1 != null );
                exists = e.Item1;
                return false;
            }
            exists = null;
            _remainders.Add( id, (newOne, stateProvider( monitor, newOne )) );
        }
        return true;
    }

    public List<(T?, object?)>? Cleanup( IActivityMonitor monitor, Func<IActivityMonitor,T,object?,bool> filter )
    {
        List<(T?, object?)>? removed = null;
        if( _remainders != null )
        {
            foreach( var e in _remainders.Values )
            {
                Throw.DebugAssert( e.Item1 != null );
                if( filter( monitor, e.Item1, e.Item2 ) )
                {
                    removed ??= new List<(T?, object?)>();
                    removed.Add( e );
                }
            }
        }
        if( removed != null )
        {
            Throw.DebugAssert( _remainders != null );
            foreach( var r in removed )
            {
                Throw.DebugAssert( r.Item1  != null );
                _remainders.Remove( r.Item1.TrackeId );
            }
        }
        for( int i = 0; i < _first.Length; i++ )
        {
            ref var e = ref _first[i];
            if( e.Item1 != null )
            {
                if( filter( monitor, e.Item1, e.Item2 ) )
                {
                    removed ??= new List<(T?, object?)>();
                    removed.Add( e );
                    e.Item1 = null;
                    e.Item2 = null;
                }
            }
        }
        return removed;
    }
}

