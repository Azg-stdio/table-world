using System;
using System.Collections.Generic;
using Core.Engine;
using Core.Rules;
using TableWorld.View.Net;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Table sounds (G5), from Kenney's CC0 packs in Art/ThirdParty: picking up, landing (from real physics impacts,
    /// louder for harder hits), shuffling, rolling, dealing, flipping and pinging. Commands sound on every screen (they
    /// arrive as commands), other players' pickups through live presence, and each machine hears its own physics.
    /// Clips are assigned by the scene builder so builds keep them.
    /// </summary>
    public sealed class TableSounds : MonoBehaviour
    {
        /// <summary>Volume of table sounds, set from the player settings (<see cref="Settings.GameSettings.TableVolume"/>).</summary>
        public static float Volume = 0.8f;
        public const int Voices = 12;

        [Serializable]
        public sealed class Clips
        {
            public AudioClip[] CardPlace = Array.Empty<AudioClip>();
            public AudioClip[] CardSlide = Array.Empty<AudioClip>();
            public AudioClip[] CardShuffle = Array.Empty<AudioClip>();
            public AudioClip[] CardFan = Array.Empty<AudioClip>();
            public AudioClip[] ChipLay = Array.Empty<AudioClip>();
            public AudioClip[] ChipsHandle = Array.Empty<AudioClip>();
            public AudioClip[] DiceGrab = Array.Empty<AudioClip>();
            public AudioClip[] DiceThrow = Array.Empty<AudioClip>();
            public AudioClip[] DieImpact = Array.Empty<AudioClip>();
            public AudioClip[] WoodImpact = Array.Empty<AudioClip>();
            public AudioClip[] SoftImpact = Array.Empty<AudioClip>();
            public AudioClip[] Ping = Array.Empty<AudioClip>();
        }

        [SerializeField] private TableView _table;
        [SerializeField] private TableInteraction _interaction;
        [SerializeField] private PresenceView _presence;
        [SerializeField] private Clips _clips = new Clips();

        private readonly List<AudioSource> _voices = new List<AudioSource>();
        private int _next;
        private int _rollFrame = -1;
        private readonly System.Random _random = new System.Random();

        /// <summary>Sounds played so far, by bank name (tests read it).</summary>
        public Dictionary<string, int> Played { get; } = new Dictionary<string, int>();

        public void Configure(TableView table, TableInteraction interaction, PresenceView presence, Clips clips)
        {
            _table = table;
            _interaction = interaction;
            _presence = presence;
            _clips = clips;
        }

        private void Awake()
        {
            for (var i = 0; i < Voices; i++)
            {
                var source = new GameObject($"Voice {i}").AddComponent<AudioSource>();
                source.transform.SetParent(transform, false);
                source.playOnAwake = false;
                // Mostly positional: a die thrown on the far side sounds from there, but nothing at the table is faint.
                source.spatialBlend = 0.6f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 0.6f;
                source.maxDistance = 8f;
                _voices.Add(source);
            }
        }

        private void OnEnable()
        {
            if (_table != null)
            {
                _table.Impacted += OnImpact;
                _table.CommandExecuted += OnCommand;
            }
            if (_interaction != null)
            {
                _interaction.PickedUp += OnPickedUp;
                _interaction.Pinged += OnPing;
            }
            if (_presence != null)
                _presence.RemotePickedUp += OnPickedUp;
        }

        private void OnDisable()
        {
            if (_table != null)
            {
                _table.Impacted -= OnImpact;
                _table.CommandExecuted -= OnCommand;
            }
            if (_interaction != null)
            {
                _interaction.PickedUp -= OnPickedUp;
                _interaction.Pinged -= OnPing;
            }
            if (_presence != null)
                _presence.RemotePickedUp -= OnPickedUp;
        }

        private void OnPickedUp(ObjectView view)
        {
            switch (view.Model)
            {
                case Die _: Play(nameof(Clips.DiceGrab), _clips.DiceGrab, view.transform.position, 0.6f); break;
                case Token _: Play(nameof(Clips.ChipsHandle), _clips.ChipsHandle, view.transform.position, 0.5f); break;
                case Card _:
                case Deck _: Play(nameof(Clips.CardSlide), _clips.CardSlide, view.transform.position, 0.35f); break;
                default: Play(nameof(Clips.SoftImpact), _clips.SoftImpact, view.transform.position, 0.25f); break;
            }
        }

        /// <summary>Landings and knocks: the bank by kind of object, louder for harder hits (full volume at 1.5 m/s).</summary>
        private void OnImpact(ObjectView view, float speed)
        {
            var loudness = Mathf.Clamp01(speed / 1.5f);
            switch (view.Model)
            {
                case Card _:
                case Deck _: Play(nameof(Clips.CardPlace), _clips.CardPlace, view.transform.position, 0.25f + 0.6f * loudness); break;
                case Die _: Play(nameof(Clips.DieImpact), _clips.DieImpact, view.transform.position, 0.2f + 0.7f * loudness); break;
                case Token _: Play(nameof(Clips.ChipLay), _clips.ChipLay, view.transform.position, 0.2f + 0.6f * loudness); break;
                case Bag _: Play(nameof(Clips.SoftImpact), _clips.SoftImpact, view.transform.position, 0.2f + 0.6f * loudness); break;
                default: Play(nameof(Clips.WoodImpact), _clips.WoodImpact, view.transform.position, 0.15f + 0.6f * loudness); break;
            }
        }

        private void OnCommand(Command command, CommandResult result)
        {
            if (!result.Ok)
                return;
            // A rule's cue (M12): played here when it is for this player (or for everyone).
            foreach (var e in result.Events)
                if (e is SoundCue cue && cue.For(_table.Spectating ? null : _table.LocalSeat))
                    Cue(cue.Sound, cue.Volume);
            switch (command)
            {
                case ShuffleCommand c:
                    Play(nameof(Clips.CardShuffle), _table.ViewOf(c.ObjectId)?.Model is Bag ? _clips.ChipsHandle : _clips.CardShuffle, At(c.ObjectId), 0.7f);
                    break;
                case RollCommand c when Time.frameCount != _rollFrame:
                    _rollFrame = Time.frameCount;
                    Play(nameof(Clips.DiceThrow), _clips.DiceThrow, At(c.ObjectId), 0.7f);
                    break;
                case DealCommand c:
                    Play(nameof(Clips.CardSlide), _clips.CardSlide, At(c.DeckId), 0.6f);
                    break;
                case FlipCommand c:
                    Play(nameof(Clips.CardFan), _clips.CardFan, At(c.ObjectId), 0.4f);
                    break;
                case GroupCommand c when c.ObjectIds.Count > 0:
                    Play(nameof(Clips.CardPlace), _clips.CardPlace, At(c.ObjectIds[0]), 0.5f);
                    break;
                case SetCounterCommand c:
                    Play(nameof(Clips.ChipLay), _clips.ChipLay, At(c.ObjectId), 0.25f);
                    break;
                case SetClockCommand c when _table.ViewOf(c.ObjectId)?.Model is Clock { Mode: ClockMode.Timer, Running: false, Seconds: <= 0f } && c.Seconds == 0f:
                    // Time's up.
                    Play(nameof(Clips.Ping), _clips.Ping, At(c.ObjectId), 1f);
                    break;
                case PerformActionCommand _:
                    Play(nameof(Clips.CardSlide), _clips.CardSlide, Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.4f);
                    break;
            }
        }

        private AudioSource _cueSource;
        private readonly Dictionary<string, AudioClip> _cues = new Dictionary<string, AudioClip>();

        /// <summary>
        /// Plays a cue that marks a moment, not a place: a built-in one (made in code, <see cref="Core.Music.CueSynth"/>),
        /// a track of the music library, or one of the table's audio assets. Counted in <see cref="Played"/> as
        /// <c>cue:&lt;name&gt;</c>.
        /// </summary>
        public void Cue(string sound, float volume)
        {
            Played["cue:" + sound] = Played.TryGetValue("cue:" + sound, out var n) ? n + 1 : 1;
            if (_cueSource == null)
            {
                _cueSource = new GameObject("Cues").AddComponent<AudioSource>();
                _cueSource.transform.SetParent(transform, false);
                _cueSource.playOnAwake = false;
                _cueSource.spatialBlend = 0f;
            }
            if (_cues.TryGetValue(sound, out var clip) && clip != null)
            {
                _cueSource.PlayOneShot(clip, Mathf.Clamp01(volume) * Volume);
                return;
            }
            if (Core.Music.CueSynth.Make(sound) is { } samples)
            {
                clip = AudioClip.Create("cue " + sound, samples.Length, 1, Core.Music.CueSynth.Rate, false);
                clip.SetData(samples, 0);
                _cues[sound] = clip;
                _cueSource.PlayOneShot(clip, Mathf.Clamp01(volume) * Volume);
                return;
            }
            StartCoroutine(LoadCue(sound, volume));
        }

        /// <summary>A library track or an audio asset as a cue: loaded once, then played like the built-in ones.</summary>
        private System.Collections.IEnumerator LoadCue(string sound, float volume)
        {
            string path = null;
            if (sound.StartsWith(SoundCues.LibraryPrefix))
                path = MusicPlayerView.Library.PathOf(sound);
            else if (_table.Engine?.State.Assets.Find(a => a.Id == sound && a.Type == AssetType.Audio) is { } asset)
            {
                if (_table.Assets?.ReadPackageFile(asset) == null && _table.FetchAsset?.Invoke(asset) is { } bytes)
                {
                    path = System.IO.Path.Combine(Application.temporaryCachePath, "cue-" + asset.Id.GetHashCode().ToString("x") + System.IO.Path.GetExtension(asset.Path));
                    System.IO.File.WriteAllBytes(path, bytes);
                }
                else if (_table.PackageRoot != null)
                    path = System.IO.Path.Combine(_table.PackageRoot, asset.Path);
            }
            if (path == null || !System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[Sound] No sound '{sound}' on this machine.");
                yield break;
            }
            var type = System.IO.Path.GetExtension(path).ToLowerInvariant() switch { ".ogg" => AudioType.OGGVORBIS, ".wav" => AudioType.WAV, _ => AudioType.MPEG };
            using var request = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(new System.Uri(System.IO.Path.GetFullPath(path)).AbsoluteUri, type);
            yield return request.SendWebRequest();
            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                yield break;
            var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(request);
            if (clip == null)
                yield break;
            _cues[sound] = clip;
            _cueSource.PlayOneShot(clip, Mathf.Clamp01(volume) * Volume);
        }

        private void OnPing(Vector3 point) => Play(nameof(Clips.Ping), _clips.Ping, point, 0.5f);

        private Vector3 At(string objectId) => _table.ViewOf(objectId) is { } view ? view.transform.position : Vector3.zero;

        /// <summary>A random clip of the bank, slightly varied in pitch so repeats do not sound mechanical.</summary>
        public void Play(string bankName, AudioClip[] bank, Vector3 at, float volume)
        {
            Played[bankName] = Played.TryGetValue(bankName, out var n) ? n + 1 : 1;
            if (bank == null || bank.Length == 0 || _voices.Count == 0)
                return;
            var source = _voices[_next];
            _next = (_next + 1) % _voices.Count;
            source.transform.position = at;
            source.clip = bank[_random.Next(bank.Length)];
            source.volume = Mathf.Clamp01(volume) * Volume;
            source.pitch = 0.94f + (float)_random.NextDouble() * 0.12f;
            source.Play();
        }
    }
}
