using Core.Engine;
using Core.Rules;
using UnityEngine;
using UnityEngine.Networking;

namespace TableWorld.View
{
    /// <summary>
    /// Plays the table's music (G27, TTS Music Player) on this machine: the current track of <see cref="TableState.Music"/>,
    /// streamed from its web address, started over whenever its <see cref="MusicState.Starts"/> changes, paused with it.
    /// The host moves to the next track when one ends (repeat, shuffle or the playlist's order), for everyone.
    /// </summary>
    public sealed class MusicPlayerView : MonoBehaviour
    {
        /// <summary>Music volume, set from the player settings (<see cref="Settings.GameSettings.MusicVolume"/>).</summary>
        public static float Volume = 0.5f;

        /// <summary>Formats tried in turn: TTS mods use MP3 mostly, some OGG or WAV, and Steam's addresses name no extension.</summary>
        private static readonly AudioType[] Formats = { AudioType.MPEG, AudioType.OGGVORBIS, AudioType.WAV };

        private TableView _table;
        private AudioSource _source;
        private string _loadedKey;
        private UnityWebRequest _request;
        private int _format;
        private bool _started;
        private readonly System.Random _random = new System.Random();

        /// <summary>The track this machine has loaded (tests and the HUD), or null.</summary>
        public string LoadedUrl { get; private set; }

        public bool IsPlaying => _source != null && _source.isPlaying;

        private void Awake()
        {
            _table = GetComponent<TableView>();
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.loop = false;
        }

        private void Update()
        {
            var music = _table != null ? _table.Engine?.State.Music : null;
            _source.volume = Volume;
            var key = music?.Current == null ? null : music.Current.Url + "#" + music.Starts;
            if (key != _loadedKey)
                Load(music, key);
            if (_source.clip == null || music == null)
                return;
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
                }
            }
            else if (!music.Playing && _source.isPlaying)
                _source.Pause();
        }

        private void Load(MusicState music, string key)
        {
            _loadedKey = key;
            _started = false;
            _source.Stop();
            if (_source.clip != null)
            {
                Destroy(_source.clip);
                _source.clip = null;
            }
            _request?.Abort();
            _request = null;
            LoadedUrl = null;
            if (music?.Current == null)
                return;
            _format = 0;
            Fetch(music.Current.Url, key);
        }

        private void Fetch(string url, string key)
        {
            var request = UnityWebRequestMultimedia.GetAudioClip(Asset.FetchAddress(url), Formats[_format]);
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
                    catch (System.Exception)
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
                    // Another format, unless the address itself failed.
                    if (error == null && ++_format < Formats.Length)
                    {
                        Fetch(url, key);
                        return;
                    }
                    Debug.LogWarning($"[Music] Could not play {url}: {error ?? "not MP3, OGG or WAV audio"}");
                    return;
                }
                clip.name = url;
                _source.clip = clip;
                LoadedUrl = url;
            };
        }

        private void OnDestroy()
        {
            _request?.Abort();
            if (_source != null && _source.clip != null)
                Destroy(_source.clip);
        }
    }
}
