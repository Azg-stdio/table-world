using System;
using System.Collections.Generic;
using Core.Net;
using TableWorld.View.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TableWorld.View.Voice
{
    /// <summary>
    /// Voice chat (T7.3). Captures this player's microphone in 20 ms frames, opens it while <see cref="TalkKey"/> is held
    /// or while they speak (<see cref="Mode"/>), encodes it with Opus and hands each packet to the session
    /// (<see cref="Spoke"/>). Packets from other players come back through <see cref="Receive"/> and play from a
    /// <see cref="VoiceSpeaker"/> per seat, which the chalkers carry on their heads. The microphone runs only while
    /// someone could hear it: online, or with <see cref="HearMyself"/> on.
    /// </summary>
    public sealed class VoiceChat : MonoBehaviour
    {
        public const Key TalkKey = Key.T;
        /// <summary>The stream of this player's own voice when <see cref="HearMyself"/> is on.</summary>
        public const string SelfSeat = "\u0001self";

        // Settings (GameSettings.Apply), the way TableSounds.Volume works.
        public static VoiceMode Mode { get; set; } = VoiceMode.PushToTalk;
        public static string Device { get; set; } = "";
        public static float Volume { get; set; } = 1f;
        public static float ThresholdDb { get; set; } = -45f;
        public static bool HearMyself { get; set; }

        [SerializeField] private TableView _table;
        [SerializeField] private TableInteraction _interaction;

        private readonly Dictionary<string, VoiceSpeaker> _speakers = new Dictionary<string, VoiceSpeaker>();
        private readonly VoiceGate _gate = new VoiceGate();
        private readonly float[] _frame = new float[VoiceFormat.FrameSamples];
        private VoiceEncoder _encoder;
        private int _framed;
        private AudioClip _clip;
        private string _device;
        private int _rate;
        private int _readAt;
        private double _phase;
        private float _previous;

        /// <summary>Each packet this player says; the session sends it.</summary>
        public event Action<VoicePacket> Spoke;

        /// <summary>True when there is someone to talk to (set by the session: online and synced).</summary>
        public Func<bool> Online { get; set; }

        /// <summary>Whose voice this machine sends (set by the session: the seat, or a spectator's own key).</summary>
        public Func<string> SpeakerKey { get; set; }

        /// <summary>The microphone is open and sending this frame.</summary>
        public bool Talking { get; private set; }

        /// <summary>Loudness of the last microphone frame, 0–1 RMS, open or not (for a meter).</summary>
        public float MicLevel { get; private set; }

        /// <summary>Why the microphone is not working, or null.</summary>
        public string MicProblem { get; private set; }

        public bool MicRunning => _clip != null;

        /// <summary>Runs the microphone even offline, without sending anything (the chalker lab).</summary>
        public bool Listen { get; set; }

        /// <summary>Holds the talk key from code (tests, automated sessions).</summary>
        public bool ForceTalk { get; set; }

        /// <summary>Feeds these samples (48 kHz mono) instead of the microphone: tests and automated sessions without one.</summary>
        public Func<int, float[]> FakeMicrophone { get; set; }

        /// <summary>Packets this machine sent, and frames received from others, since the scene started.</summary>
        public int Sent { get; private set; }
        public int Received { get; private set; }

        public void Configure(TableView table, TableInteraction interaction)
        {
            _table = table;
            _interaction = interaction;
        }

        public IEnumerable<string> Speakers => _speakers.Keys;

        /// <summary>How loud <paramref name="seat"/> is right now, 0–1 RMS; 0 when silent or unknown.</summary>
        public float LevelOf(string seat) => _speakers.TryGetValue(seat, out var speaker) ? speaker.Stream.Level : 0f;

        public VoiceSpeaker SpeakerOf(string seat) => _speakers.TryGetValue(seat, out var speaker) ? speaker : null;

        /// <summary>A packet from another player (on the main thread, from the session).</summary>
        public void Receive(VoicePacket packet)
        {
            if (string.IsNullOrEmpty(packet.Seat))
                return;
            if (!_speakers.TryGetValue(packet.Seat, out var speaker) || speaker == null)
            {
                speaker = new GameObject($"Voice {packet.Seat}").AddComponent<VoiceSpeaker>();
                speaker.transform.SetParent(transform, false);
                _speakers[packet.Seat] = speaker;
            }
            speaker.Stream.Push(packet.Sequence, packet.Payload);
            Received++;
        }

        /// <summary>Forgets every other player's voice (the session ended).</summary>
        public void Clear()
        {
            foreach (var pair in _speakers)
                if (pair.Key != SelfSeat && pair.Value != null)
                    Destroy(pair.Value.gameObject);
            var self = SpeakerOf(SelfSeat);
            _speakers.Clear();
            if (self != null)
                _speakers[SelfSeat] = self;
        }

        private void Update()
        {
            var online = Online != null && Online();
            var wanted = Mode != VoiceMode.Off && (online || HearMyself || Listen);
            if (!wanted)
            {
                StopMicrophone();
                Talking = false;
                MicLevel = 0f;
            }
            else if (FakeMicrophone == null && (_clip == null || _device != DeviceName()))
                StartMicrophone();

            if (!HearMyself && _speakers.TryGetValue(SelfSeat, out var self))
            {
                if (self != null)
                    Destroy(self.gameObject);
                _speakers.Remove(SelfSeat);
            }
            if (wanted)
                Capture(online);
        }

        private string DeviceName()
        {
            foreach (var name in UnityEngine.Microphone.devices)
                if (name == Device)
                    return name;
            return null;
        }

        private void StartMicrophone()
        {
            StopMicrophone();
            if (UnityEngine.Microphone.devices.Length == 0)
            {
                if (MicProblem == null)
                    Debug.LogWarning("[Voice] No microphone found.");
                MicProblem = "No microphone found";
                return;
            }
            _device = DeviceName();
            UnityEngine.Microphone.GetDeviceCaps(_device, out var min, out var max);
            _rate = min == 0 && max == 0 ? VoiceFormat.SampleRate : Mathf.Clamp(VoiceFormat.SampleRate, min, max);
            _clip = UnityEngine.Microphone.Start(_device, true, 1, _rate);
            if (_clip == null)
            {
                MicProblem = "The microphone could not start";
                Debug.LogWarning($"[Voice] Microphone '{_device ?? "default"}' could not start.");
                return;
            }
            _rate = _clip.frequency;
            MicProblem = null;
            _readAt = 0;
            _phase = 0;
            _framed = 0;
            Debug.Log($"[Voice] Microphone '{_device ?? "default"}' at {_rate} Hz.");
        }

        private void StopMicrophone()
        {
            if (_clip == null)
                return;
            UnityEngine.Microphone.End(_device);
            Destroy(_clip);
            _clip = null;
            _framed = 0;
        }

        /// <summary>Takes what the microphone recorded since last frame, resampled to 48 kHz, and sends whole frames.</summary>
        private void Capture(bool online)
        {
            if (FakeMicrophone != null)
            {
                var wanted = Mathf.RoundToInt(Time.unscaledDeltaTime * VoiceFormat.SampleRate);
                var samples = FakeMicrophone(wanted);
                Take(samples, samples.Length, online);
                return;
            }
            if (_clip == null)
                return;
            var position = UnityEngine.Microphone.GetPosition(_device);
            var available = (position - _readAt + _clip.samples) % _clip.samples;
            if (available <= 0)
                return;
            var chunk = new float[available];
            // A looping microphone clip reads around its end.
            _clip.GetData(chunk, _readAt);
            _readAt = position;
            if (_rate == VoiceFormat.SampleRate)
            {
                Take(chunk, available, online);
                return;
            }
            // Linear resampling to 48 kHz: plenty for speech.
            var step = (double)_rate / VoiceFormat.SampleRate;
            var resampled = new List<float>((int)(available / step) + 2);
            while (_phase < available)
            {
                var i = (int)_phase;
                var before = i == 0 ? _previous : chunk[i - 1];
                var t = (float)(_phase - i);
                resampled.Add(Mathf.Lerp(before, chunk[i], t));
                _phase += step;
            }
            _phase -= available;
            _previous = chunk[available - 1];
            Take(resampled.ToArray(), resampled.Count, online);
        }

        private void Take(float[] samples, int count, bool online)
        {
            for (var i = 0; i < count; i++)
            {
                _frame[_framed++] = samples[i];
                if (_framed < VoiceFormat.FrameSamples)
                    continue;
                _framed = 0;
                Frame(online);
            }
        }

        private void Frame(bool online)
        {
            MicLevel = VoiceGate.Rms(_frame);
            _gate.ThresholdDb = ThresholdDb;
            var spoken = _gate.Process(_frame);
            var typing = _interaction != null && _interaction.KeyboardBlocked != null && _interaction.KeyboardBlocked();
            var held = ForceTalk || (!typing && Keyboard.current != null && Keyboard.current[TalkKey].isPressed);
            Talking = Mode == VoiceMode.PushToTalk ? held : Mode == VoiceMode.VoiceActivation && (spoken || ForceTalk);
            if (!Talking)
                return;
            _encoder ??= new VoiceEncoder();
            var seat = SpeakerKey != null ? SpeakerKey() : _table != null ? _table.LocalSeat : null;
            var packet = _encoder.Encode(_frame, seat ?? "");
            if (online && seat != null)
            {
                Spoke?.Invoke(packet);
                Sent++;
            }
            if (HearMyself)
                Receive(new VoicePacket { Seat = SelfSeat, Sequence = packet.Sequence, Payload = packet.Payload });
        }

        private void OnDisable()
        {
            StopMicrophone();
            Talking = false;
        }
    }
}
