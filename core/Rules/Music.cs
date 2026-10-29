using System.Collections.Generic;
using System.Linq;

namespace Core.Rules
{
    /// <summary>
    /// A track of the table's music (TTS Music Player audio clip): a web address, an audio asset of the game (its id), or
    /// a track of the player's music library (<c>library:&lt;pack&gt;/&lt;track&gt;</c>, M12) with the file's
    /// <see cref="Hash"/>, so a machine with the same file plays its own copy.
    /// </summary>
    public sealed class MusicTrack
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>The file's content hash (<c>sha256:...</c>) for a library track; null otherwise.</summary>
        public string? Hash { get; set; }

        public MusicTrack Clone() => new MusicTrack { Title = Title, Url = Url, Hash = Hash };

        public const string LibraryPrefix = "library:";

        [Newtonsoft.Json.JsonIgnore]
        public bool IsLibrary => Url.StartsWith(LibraryPrefix, System.StringComparison.Ordinal);

        /// <summary>What a track may be: a web address, a library track, or one of the table's audio assets.</summary>
        public static bool Valid(TableState state, string? url) =>
            Asset.IsWebAddress(url) || url != null && url.StartsWith(LibraryPrefix, System.StringComparison.Ordinal) && url.IndexOf('/') > LibraryPrefix.Length
            || state.Assets.Any(a => a.Id == url && a.Type == AssetType.Audio);
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
        // Saved unless -1: a 0 left out read back as -1, and a table copied over the wire differed from the host's.
        [System.ComponentModel.DefaultValue(-1)]
        public int Index { get; set; } = -1;
        public bool Playing { get; set; }
        /// <summary>Play the current track again when it ends (TTS <c>repeat_track</c>).</summary>
        public bool Repeat { get; set; }
        /// <summary>Pick the next track at random (TTS <c>shuffle</c>).</summary>
        public bool Shuffle { get; set; }
        /// <summary>Counts track starts: each change (a new track, the same one again) makes every machine start it over.</summary>
        public int Starts { get; set; }

        /// <summary>
        /// A game's named playlists (M12): <c>"tavern"</c>, <c>"ritual"</c>... The <c>music</c> effect loads one into
        /// <see cref="Playlist"/>; <see cref="Start"/> is the one the game starts with.
        /// </summary>
        public Dictionary<string, List<MusicTrack>> Playlists { get; set; } = new Dictionary<string, List<MusicTrack>>();
        /// <summary>The named playlist loaded into <see cref="Playlist"/>, or null.</summary>
        public string? List { get; set; }
        /// <summary>The playlist the game starts with (when it starts, or when the table loads with no music playing).</summary>
        public string? Start { get; set; }
        /// <summary>The game's own level, 0–1, under each player's music volume; null is 1.</summary>
        public float? Volume { get; set; }
        /// <summary>Seconds the next change of track takes, the old one fading out as the new one fades in; null is 2.</summary>
        public float? Crossfade { get; set; }

        public const float DefaultCrossfade = 2f;

        public MusicState Clone() => new MusicState
        {
            Playlist = Playlist.Select(t => t.Clone()).ToList(), Current = Current?.Clone(), Index = Index, Playing = Playing,
            Repeat = Repeat, Shuffle = Shuffle, Starts = Starts,
            Playlists = Playlists.ToDictionary(p => p.Key, p => p.Value.Select(t => t.Clone()).ToList()), List = List, Start = Start,
            Volume = Volume, Crossfade = Crossfade,
        };

        /// <summary>Every track the table names: the playlist, the named playlists and the current one, each address once.</summary>
        public IEnumerable<MusicTrack> AllTracks()
        {
            var seen = new HashSet<string>();
            var start = Start != null && Playlists.TryGetValue(Start, out var first) ? first : new List<MusicTrack>();
            foreach (var track in (Current == null ? Enumerable.Empty<MusicTrack>() : new[] { Current }).Concat(Playlist).Concat(start).Concat(Playlists.Values.SelectMany(p => p)))
                if (seen.Add(track.Url))
                    yield return track;
        }

        /// <summary>
        /// The state with the named playlist <paramref name="name"/> loaded and playing from its first track (a random one
        /// when shuffling). The same playlist already playing is left as it is, so a phase entered again does not restart it.
        /// </summary>
        public MusicState WithPlaylist(string name, System.Func<int, int> pick)
        {
            var next = Clone();
            if (List == name && Playing && Current != null)
                return next;
            if (!Playlists.TryGetValue(name, out var tracks) || tracks.Count == 0)
                return next;
            next.Playlist = tracks.Select(t => t.Clone()).ToList();
            next.List = name;
            next.Index = Shuffle && tracks.Count > 1 ? pick(tracks.Count) : 0;
            next.Current = next.Playlist[next.Index].Clone();
            next.Playing = true;
            next.Starts++;
            return next;
        }

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
