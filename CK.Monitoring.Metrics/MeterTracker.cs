using CK.Core;
using CK.Metrics;
using System.Diagnostics.CodeAnalysis;

namespace CK.Monitoring.Metrics;


public sealed class MeterTracker<T> where T : class, ITrackedMetricsInfo
{
    readonly T?[] _first;
    Dictionary<int, T>? _remainders;

    public MeterTracker( int firstCount )
    {
        _first = new T[firstCount];
    }

    public T? Find( int trackedId )
    {
        Throw.CheckArgument( trackedId >= 0 );
        return trackedId < _first.Length
                ? _first[trackedId]
                : _remainders != null
                    ? _remainders.GetValueOrDefault( trackedId )
                    : null;
    }

    public bool TryRemove( int trackedId )
    {
        if( trackedId < _first.Length )
        {
            if( _first[trackedId] == null ) return false;
            _first[trackedId] = null;
            return true;
        }
        return _remainders != null
                ? _remainders.Remove( trackedId )
                : false;
    }

    public bool TryAdd( T newOne, [NotNullWhen(false)]out T? exists )
    {
        Throw.CheckNotNullArgument( newOne );
        int meterId = newOne.TrackeId;
        if( meterId < _first.Length )
        {
            if( (exists = _first[meterId]) != null )
            {
                return false;
            }
            _first[meterId] = newOne;
        }
        else
        {
            if( _remainders == null )
            {
                _remainders = new Dictionary<int, T>();
            }
            else if( _remainders.TryGetValue( meterId, out exists ) )
            {
                return false;
            }
            exists = null;
            _remainders.Add( meterId, newOne );
        }
        return true;
    }

    public void Add( T newOne )
    {
        Throw.CheckArgument( "The MeterInfo is already registered.", TryAdd( newOne ) );
    }

    public void Remove( int trackedId )
    {
        Throw.CheckArgument( "The MeterInfo is not registered.", TryRemove( trackedId ) );
    }
}

