using System;
using System.IO;
using Core.Engine;
using Core.Music;
using Core.Net;
using Core.Rules;
using UnityEngine;
using UnityEngine.Networking;

namespace TableWorld.View
{
    /// <summary>
    /// Plays the table's music (G27, TTS Music Player; M12) on this machine: the current track of
    /// <see cref="TableState.Music"/>, started over whenever its <see cref="MusicState.Starts"/> changes, paused with it.
    /// A track is a web address, one of the table's audio assets, or a track of the music library
    /// (<c>library:pack/track</c>): this player's own copy when they have the pack, else the file the host sent
    /// (<see cref="NetworkTableSession"/> lists the tracks with the table's files). One track fades into the next over
    /// <see cref="MusicState.Crossfade"/> seconds. The host moves to the next track when one ends, for everyone.
    /// A preview (<see cref="Preview"/>) plays a track on this machine alone while a game is being built.
    /// </summary>
    public sealed class MusicPlayerView : MonoBehaviour
    {
        /// <summary>Music volume, set from the player settings (<see cref="Settings.GameSettings.MusicVolume"/>).</summary>
        public static float Volume = 0.5f;

        /// <summary>Formats tried in turn: TTS mods use MP3 mostly, some OGG or WAV, and Steam's addresses name no extension.</summary>
        private static readonly AudioType[] Formats = { AudioType.MPEG, AudioType.OGGVORBIS, AudioType.WAV };

        /// <summary>The player's music library; tests point it at a folder of their own.</summary>
        public static MusicLibrary Library
        {
            get => _library ??= new MusicLibrary();
            set => _library = value;
        }

        private static MusicLibrary _library;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _library = null;

        private TableView _table;
        private AudioSource _source;
        private AudioSource _fading;
        private AudioSource _preview;
        private string _loadedKey;
        private UnityWebRequest _request;
        private int _format;
        private bool _started;
        private float _fadeIn = 1f, _fadeOut;
        private float _crossfade = MusicState.DefaultCrossfade;
        private float _retryAt = -1f;
        private readonly System.Random _random = new System.Random();

        /// <summary>The track this machine has loaded (tests and the HUD), or null.</summary>
        public string LoadedUrl { get; private set; }

        /// <summary>The track that should play here but has not arrived from the host yet, or null.</summary>
        public string Waiting { get; private set; }

        public bool IsPlaying => _source != null && _source.isPlaying;

        /// <summary>The volume this machine plays the table's music at now (tests read it).</summary>
        public float CurrentVolume => _source != null ? _source.volume : 0f;

        /// <summary>Raised when a track starts playing here, with its title (the HUD shows it for a moment).</summary>
        public event Action<string> TrackStarted;

        private void Awake()
        {
            _table = GetComponent<TableView>();
            _source = NewSource("Music");
            _fading = NewSource("Music (fading out)");
            _preview = NewSource("Music preview");
        }

        private AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = false;
            return source;
        }

        private void Update()
        {
            var music = _table != null ? _table.Engine?.State.Music : null;
            var key = music?.Current == null ? null : music.Current.Url + "#" + music.Starts;
            if (key != _loadedKey)
                Load(music, key);
            else if (Waiting != null && _retryAt >= 0f && Time.unscaledTime >= _retryAt)
                Fetch(music.Current, key);

            // The old track fades out as the new one fades in; a preview ducks the table's music.
            var delta = Time.unscaledDeltaTime / Mathf.Max(0.05f, _crossfade);
            _fadeIn = Mathf.Min(1f, _fadeIn + delta);
            _fadeOut = Mathf.Max(0f, _fadeOut - delta);
            var level = Volume * Mathf.Clamp01(music?.Volume ?? 1f) * (_preview.isPlaying ? 0.25f : 1f);
            _source.volume = level * _fadeIn;
            _fading.volume = level * _fadeOut;
            if (_fadeOut <= 0f && _fading.isPlaying)
                _fading.Stop();
            _preview.volume = Volume;

            if (_source.clip == null || music == null)
                return;
            _source.loop = music.Repeat || music.Playlist.Count == 1 && music.Index == 0;
            if (music.Playing && !_source.isPlaying)
            {
                // A track that played to its end: the host picks the next one for everyone.
                if (_started && _source.time <= 0f)
                {
                    _started = false;
                    if (_table.HostsTable)
                        _table.Execute(new SetMusicCommand { Music = music.Next(_random) });
                    return;
                }
                if (_started)
                    _source.UnPause();
                else
                {
                    _source.Play();
                    _started = true;
                    TrackStarted?.Invoke(Title(music.Current));
                }
            }
            else if (!music.Playing && _source.isPlaying)
                _source.Pause();
        }

        private static string Title(MusicTrack track) =>
            !string.IsNullOrWhiteSpace(track.Title) ? track.Title : Path.GetFileNameWithoutExtension(track.Url.Split('?')[0].Split('/')[^1]);

        private void Load(MusicState music, string key)
        {
            _loadedKey = key;
            _started = false;
            _crossfade = Mathf.Clamp(music?.Crossfade ?? MusicState.DefaultCrossfade, 0f, 20f);
            // What plays now fades out on the second source while the next one loads.
            if (_source.clip != null && _source.isPlaying && _crossfade > 0f)
            {
                if (_fading.clip != null)
                    Destroy(_fading.clip);
                (_source, _fading) = (_fading, _source);
                _fadeOut = _fadeIn;
                _source.clip = null;
            }
            else
            {
                _source.Stop();
                if (_source.clip != null)
                    Destroy(_source.clip);
                _source.clip = null;
            }
            _fadeIn = _crossfade > 0f ? 0f : 1f;
            _request?.Abort();
            _request = null;
            LoadedUrl = null;
            Waiting = null;
            _retryAt = -1f;
            if (music?.Current == null)
                return;
            _format = 0;
            Fetch(music.Current, key);
        }

        /// <summary>
        /// Where this machine reads a track from: its own library copy, the table's package, the file the host sent (written
        /// to a cache file), or the web. Null while the host's file has not arrived.
        /// </summary>
        private string Source(MusicTrack track, out AudioType? type)
        {
            type = null;
            if (track.IsLibrary)
            {
                if (Library.PathOf(track.Url, track.Hash) is { } own)
                    return Local(own, out type);
                return Received(AssetHost.MusicId(track), track.Hash, out type);
            }
            if (_table.Engine?.State.Assets.Find(a => a.Id == track.Url && a.Type == AssetType.Audio) is { } asset)
            {
                if (_table.Assets?.PackageFile(asset) is { } file)
                    return Local(file, out type);
                return Received(asset.Id, asset.Hash, out type);
            }
            return Asset.FetchAddress(track.Url);
        }

        private static string Local(string path, out AudioType? type)
        {
            type = TypeOf(path);
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        /// <summary>The bytes the host sent for <paramref name="assetId"/>, kept in a file so they stream like any other.</summary>
        private string Received(string assetId, string hash, out AudioType? type)
        {
            type = null;
            var bytes = _table.FetchAsset?.Invoke(new Asset { Id = assetId, Type = AssetType.Audio, Hash = hash });
            if (bytes == null || bytes.Length == 0)
                return null;
            var name = (hash != null && AssetHash.IsValid(hash) ? hash.Substring(7, 24) : Mathf.Abs(assetId.GetHashCode()).ToString()) + Extension(bytes);
            var folder = Path.Combine(Application.temporaryCachePath, "Music");
            var path = Path.Combine(folder, name);
            try
            {
                Directory.CreateDirectory(folder);
                if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
                    File.WriteAllBytes(path, bytes);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[Music] Could not keep the track {assetId}: {e.Message}");
                return null;
            }
            return Local(path, out type);
        }

        private static string Extension(byte[] bytes) =>
            bytes.Length > 4 && bytes[0] == 'O' && bytes[1] == 'g' && bytes[2] == 'g' && bytes[3] == 'S' ? ".ogg"
            : bytes.Length > 4 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' ? ".wav" : ".mp3";

        private static AudioType? TypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ogg" => AudioType.OGGVORBIS,
            ".wav" => AudioType.WAV,
            ".mp3" => AudioType.MPEG,
            _ => null,
        };

        private void Fetch(MusicTrack track, string key)
        {
            var address = Source(track, out var known);
            if (address == null)
            {
                // Not here yet: the host's file is on its way; look again in a moment.
                Waiting = track.Url;
                _retryAt = Time.unscaledTime + 1.5f;
                return;
            }
            Waiting = null;
            _retryAt = -1f;
            var request = UnityWebRequestMultimedia.GetAudioClip(address, known ?? Formats[_format]);
            // A local file streams from disk instead of being decoded whole into memory first; a download is kept compressed.
            var stream = address.StartsWith("file:");
            if (stream)
                ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = true;
            else
                ((DownloadHandlerAudioClip)request.downloadHandler).compressed = true;
            _request = request;
            request.SendWebRequest().completed += _ =>
            {
                if (_request != request || key != _loadedKey)
                {
                    request.Dispose();
                    return;
                }
                AudioClip clip = null;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        clip = DownloadHandlerAudioClip.GetContent(request);
                    }
                    catch (Exception)
                    {
                        clip = null;
                    }
                }
                var error = request.error;
                request.Dispose();
                _request = null;
                if (clip == null || clip.length <= 0f)
                {
                    if (clip != null)
                        Destroy(clip);
                    // Another format, unless the address itself failed or the file said what it is.
                    if (error == null && known == null && ++_format < Formats.Length)
                    {
                        Fetch(track, key);
                        return;
                    }
                    Debug.LogWarning($"[Music] Could not play {track.Url}: {error ?? "not MP3, OGG or WAV audio"}");
                    return;
                }
                clip.name = track.Url;
                _source.clip = clip;
                LoadedUrl = track.Url;
            };
        }

        /// <summary>
        /// Plays <paramref name="url"/> on this machine only (a library track, an audio asset, a web address), the table's
        /// music ducked under it; null stops it. The agent previews tracks this way while building a game.
        /// </summary>
        public void Preview(string url, string path = null)
        {
            _preview.Stop();
            if (_preview.clip != null)
                Destroy(_preview.clip);
            _preview.clip = null;
            PreviewUrl = url;
            if (url == null)
                return;
            var track = new MusicTrack { Url = url };
            var address = path != null ? Local(path, out var type) : Source(track, out type);
            if (address == null)
            {
                Debug.LogWarning($"[Music] Nothing to preview at {url}.");
                return;
            }
            var request = UnityWebRequestMultimedia.GetAudioClip(address, type ?? AudioType.MPEG);
            ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = address.StartsWith("file:");
            request.SendWebRequest().completed += _ =>
            {
                AudioClip clip = null;
                if (request.result == UnityWebRequest.Result.Success)
                    try
                    {
                        clip = DownloadHandlerAudioClip.GetContent(request);
                    }
                    catch (Exception)
                    {
                        clip = null;
                    }
                request.Dispose();
                if (clip == null || PreviewUrl != url)
                {
                    if (clip != null)
                        Destroy(clip);
                    return;
                }
                _preview.clip = clip;
                _preview.Play();
            };
        }

        /// <summary>The track previewing on this machine, or null.</summary>
        public string PreviewUrl { get; private set; }

        public bool Previewing => _preview != null && _preview.isPlaying;

        private void OnDestroy()
        {
            _request?.Abort();
            foreach (var source in new[] { _source, _fading, _preview })
                if (source != null && source.clip != null)
                    Destroy(source.clip);
        }
    }
}
