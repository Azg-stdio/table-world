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
