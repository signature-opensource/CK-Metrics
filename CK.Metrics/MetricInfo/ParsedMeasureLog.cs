using System;

namespace CK.Metrics;

public readonly struct ParsedMeasureLog
{
    readonly string _text;
    readonly int _instrumentId;
    readonly int _mStart;
    readonly int _mLength;
    readonly int _tStart;

    internal ParsedMeasureLog( string text, int instrumentId, int mStart, int mLength, int tStart )
    {
        _text = text;
        _instrumentId = instrumentId;
        _mStart = mStart;
        _mLength = mLength;
        _tStart = tStart;
    }

    public string Text => _text;

    public int InstrumentId => _instrumentId;

    public ReadOnlySpan<char> Measure => _text.AsSpan( _mStart, _mLength );

    public ReadOnlySpan<char> Tags => _text.AsSpan( _tStart, TagsLength );

    public int MeasureIndex => _mStart;

    public int MeasureLength => _mLength;

    public int TagsIndex => _tStart;

    public int TagsLength => _tStart == 0 ? 0 : _text.Length - _tStart - 1;
}

