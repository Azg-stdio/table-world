using System.Collections.Generic;

namespace Core.Engine
{
    /// <summary>
    /// Rewind time (G26, TTS host "Rewind"): the table as it was, saved every <see cref="IntervalSeconds"/> while it
    /// changes, the last <see cref="Capacity"/> points. Stepping back and forth moves a cursor; a change after stepping
    /// back starts a new future (the points after the cursor are dropped), as in TTS.
    /// </summary>
    public sealed class Timeline
    {
        public const double IntervalSeconds = 10.0;
        public const int Capacity = 60;
        /// <summary>
        /// The points together hold at most this many characters (about 64 MB): a big mod table is megabytes of JSON, so
        /// it keeps fewer points rather than filling memory. The newest point is always kept.
        /// </summary>
        public const long MaxCharacters = 32_000_000;

        private long _characters;

        private readonly List<(string Json, double At)> _points = new List<(string, double)>();
        private int _cursor = -1;

        public int Count => _points.Count;

        /// <summary>Index of the point the table shows after stepping (the last one when not stepping).</summary>
        public int Cursor => _cursor;

        public bool CanGoBack => _cursor > 0;
        public bool CanGoForward => _cursor >= 0 && _cursor < _points.Count - 1;

        /// <summary>
        /// Offers the table as it is now (<paramref name="json"/>, canonical); kept when it differs from the newest point and
        /// at least <see cref="IntervalSeconds"/> passed since it, or when <paramref name="force"/> (before a rewind).
        /// Returns true when it was kept.
        /// </summary>
        public bool Record(string json, double now, bool force = false)
        {
            if (_cursor >= 0 && _points[_cursor].Json == json)
                return false;
            if (!force && _cursor >= 0 && now - _points[_cursor].At < IntervalSeconds)
                return false;
            // A change after rewinding: the old future is gone.
            if (_cursor < _points.Count - 1)
            {
                for (var i = _cursor + 1; i < _points.Count; i++)
                    _characters -= _points[i].Json.Length;
                _points.RemoveRange(_cursor + 1, _points.Count - _cursor - 1);
            }
            _points.Add((json, now));
            _characters += json.Length;
            while (_points.Count > 1 && (_points.Count > Capacity || _characters > MaxCharacters))
            {
                _characters -= _points[0].Json.Length;
                _points.RemoveAt(0);
            }
            _cursor = _points.Count - 1;
            return true;
        }

        /// <summary>The previous point (its table and when it was saved), or null at the start.</summary>
        public (string Json, double At)? Back()
        {
            if (!CanGoBack)
                return null;
            _cursor--;
            return _points[_cursor];
        }

        /// <summary>The next point, or null at the newest.</summary>
        public (string Json, double At)? Forward()
        {
            if (!CanGoForward)
                return null;
            _cursor++;
            return _points[_cursor];
        }

        /// <summary>How long ago (as of <paramref name="now"/>) the point <paramref name="steps"/> back from the cursor was saved; null when there is none.</summary>
        public double? AgeOf(int steps, double now)
        {
            var index = _cursor - steps;
            return index >= 0 && index < _points.Count ? now - _points[index].At : (double?)null;
        }

        public void Clear()
        {
            _characters = 0;
            _points.Clear();
            _cursor = -1;
        }
    }
}
