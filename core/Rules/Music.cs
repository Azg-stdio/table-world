using System.Collections.Generic;
using System.Linq;

namespace Core.Rules
{
    /// <summary>A track of the table's music: a web address and its title (TTS Music Player audio clip).</summary>
    public sealed class MusicTrack
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";

        public MusicTrack Clone() => new MusicTrack { Title = Title, Url = Url };
    }

    /// <summary>
    /// The table's music (G27, TTS Music Player): a playlist, the track loaded, whether it plays, and how the next one is
    /// chosen. Every machine plays it; the host moves to the next track when one ends. Only the host changes it.
    /// </summary>
    public sealed class MusicState
    {
        public List<MusicTrack> Playlist { get; set; } = new List<MusicTrack>();
        /// <summary>The track loaded; null when none.</summary>
        public MusicTrack? Current { get; set; }
        /// <summary>Where <see cref="Current"/> is in the playlist, or -1 when it is not in it.</summary>
        public int Index { get; set; } = -1;
        public bool Playing { get; set; }
        /// <summary>Play the current track again when it ends (TTS <c>repeat_track</c>).</summary>
        public bool Repeat { get; set; }
        /// <summary>Pick the next track at random (TTS <c>shuffle</c>).</summary>
        public bool Shuffle { get; set; }
        /// <summary>Counts track starts: each change (a new track, the same one again) makes every machine start it over.</summary>
        public int Starts { get; set; }

        public MusicState Clone() => new MusicState
        {
            Playlist = Playlist.Select(t => t.Clone()).ToList(), Current = Current?.Clone(), Index = Index, Playing = Playing,
            Repeat = Repeat, Shuffle = Shuffle, Starts = Starts,
        };

        /// <summary>
        /// The state with the track after the current one loaded and playing: the same one when repeating, a random other
        /// one when shuffling, else the next in the playlist (wrapping round). Unchanged when the playlist is empty and
        /// nothing repeats.
        /// </summary>
        public MusicState Next(System.Random random, int step = 1)
        {
            var next = Clone();
            if (Repeat && Current != null && step > 0)
            {
                next.Starts++;
                next.Playing = true;
                return next;
            }
            if (Playlist.Count == 0)
                return next;
            int index;
            if (Shuffle && Playlist.Count > 1)
            {
                do
                    index = random.Next(Playlist.Count);
                while (index == Index);
            }
            else
                index = ((Index < 0 ? (step > 0 ? -1 : 0) : Index) + step % Playlist.Count + Playlist.Count) % Playlist.Count;
            next.Index = index;
            next.Current = Playlist[index].Clone();
            next.Playing = true;
            next.Starts++;
            return next;
        }
    }
}
