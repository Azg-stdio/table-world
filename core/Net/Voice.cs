using System;
using System.Collections.Generic;
using System.Text;
using Concentus.Enums;
using Concentus.Structs;

namespace Core.Net
{
    /// <summary>Voice is mono Opus at 48 kHz in 20 ms frames: what Opus is built for, and what every speaker and listener agree on.</summary>
    public static class VoiceFormat
    {
        public const int SampleRate = 48000;
        public const int FrameSamples = 960;
        public const float FrameSeconds = FrameSamples / (float)SampleRate;
        /// <summary>Speech bits per second; 24 kbps is clear speech for about 3 KB/s per speaker.</summary>
        public const int Bitrate = 24000;
    }

    /// <summary>One 20 ms Opus frame from one player.</summary>
    public sealed class VoicePacket
    {
        public string Seat { get; set; } = "";
        /// <summary>Counts frames per speaker and wraps at 65 536; the listener orders and spots gaps with it.</summary>
        public ushort Sequence { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Voice packets on the wire. Unlike every other message they are binary, not JSON: 50 a second per speaker would
    /// spend more on base64 and keys than on the sound. Layout: version (1), sequence (2, little endian), seat length (1),
    /// seat (UTF-8), payload length (2), payload.
    /// </summary>
    public static class VoiceCodec
    {
        public const byte Version = 1;
        /// <summary>An Opus frame at our bitrate is about 60 bytes; anything this big is not ours.</summary>
        public const int MaxPayload = 1000;
        public const int MaxSeat = 64;

        public static byte[] Encode(VoicePacket packet)
        {
            var seat = Encoding.UTF8.GetBytes(packet.Seat);
            if (seat.Length > MaxSeat || packet.Payload.Length > MaxPayload)
                throw new ArgumentException("Voice packet too big.");
            var bytes = new byte[6 + seat.Length + packet.Payload.Length];
            bytes[0] = Version;
            bytes[1] = (byte)packet.Sequence;
            bytes[2] = (byte)(packet.Sequence >> 8);
            bytes[3] = (byte)seat.Length;
            seat.CopyTo(bytes, 4);
            var at = 4 + seat.Length;
            bytes[at] = (byte)packet.Payload.Length;
            bytes[at + 1] = (byte)(packet.Payload.Length >> 8);
            packet.Payload.CopyTo(bytes, at + 2);
            return bytes;
        }

        /// <summary>The packet in <paramref name="bytes"/>, or null when it is not a well-formed voice packet.</summary>
        public static VoicePacket? Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 6 || bytes[0] != Version)
                return null;
            var seatLength = bytes[3];
            if (seatLength > MaxSeat || bytes.Length < 6 + seatLength)
                return null;
            var at = 4 + seatLength;
            var payloadLength = bytes[at] | (bytes[at + 1] << 8);
            if (payloadLength > MaxPayload || bytes.Length != at + 2 + payloadLength)
                return null;
            var payload = new byte[payloadLength];
            Array.Copy(bytes, at + 2, payload, 0, payloadLength);
            return new VoicePacket
            {
                Sequence = (ushort)(bytes[1] | (bytes[2] << 8)),
                Seat = Encoding.UTF8.GetString(bytes, 4, seatLength),
                Payload = payload,
            };
        }
    }

    /// <summary>Turns this player's 20 ms microphone frames into numbered Opus packets.</summary>
    public sealed class VoiceEncoder
    {
        private readonly OpusEncoder _opus;
        private readonly byte[] _buffer = new byte[VoiceCodec.MaxPayload];
        private ushort _sequence;

        public VoiceEncoder()
        {
            _opus = OpusEncoder.Create(VoiceFormat.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            _opus.Bitrate = VoiceFormat.Bitrate;
            _opus.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            // Pure C# Opus: complexity 5 keeps the encoder near 1 % of a core with little audible loss.
            _opus.Complexity = 5;
        }

        /// <summary>Encodes one frame of <see cref="VoiceFormat.FrameSamples"/> samples in -1..1.</summary>
        public VoicePacket Encode(float[] frame, string seat)
        {
            if (frame.Length != VoiceFormat.FrameSamples)
                throw new ArgumentException($"A voice frame is {VoiceFormat.FrameSamples} samples.");
            var length = _opus.Encode(frame, 0, VoiceFormat.FrameSamples, _buffer, 0, _buffer.Length);
            var payload = new byte[length];
            Array.Copy(_buffer, payload, length);
            return new VoicePacket { Seat = seat, Sequence = _sequence++, Payload = payload };
        }
    }

    /// <summary>
    /// One speaker as a listener hears them: packets arrive out of order, late or never, and the sound card pulls samples
    /// at its own pace. The stream waits until <see cref="TargetFrames"/> are buffered before it plays (60 ms of jitter
    /// room), conceals lost frames with Opus's own concealment, drops to <see cref="TargetFrames"/> when more than
    /// <see cref="MaxFrames"/> pile up (so delay never grows), and goes quiet when the speaker stops sending. Packets are
    /// pushed from the network thread and samples read from the audio thread, so every member locks.
    /// </summary>
    public sealed class VoiceStream
    {
        public const int TargetFrames = 3;
        public const int MaxFrames = 10;
        /// <summary>Missing frames concealed in a row before the stream decides the speaker stopped.</summary>
        public const int MaxConcealed = 3;

        private readonly object _lock = new object();
        private readonly OpusDecoder _opus = OpusDecoder.Create(VoiceFormat.SampleRate, 1);
        private readonly SortedDictionary<int, byte[]> _frames = new SortedDictionary<int, byte[]>();
        private readonly float[] _pcm = new float[VoiceFormat.FrameSamples];
        private int _pcmAt = VoiceFormat.FrameSamples;
        private bool _playing;
        private bool _started;
        private int _next;
        private int _lastIndex;
        private int _concealed;
        private float _level;

        /// <summary>Loudness of what was last played, 0–1 RMS (drives the mouth).</summary>
        public float Level { get { lock (_lock) return _level; } }

        public bool Playing { get { lock (_lock) return _playing; } }

        public int Buffered { get { lock (_lock) return _frames.Count; } }

        /// <summary>Frames concealed, dropped as late, and dropped to catch up, since the stream began.</summary>
        public int Concealed { get; private set; }
        public int Late { get; private set; }
        public int Skipped { get; private set; }

        public void Push(ushort sequence, byte[] payload)
        {
            lock (_lock)
            {
                // Sequence numbers wrap at 65 536: unwrap each against the last one seen, so ordering keeps working.
                var index = _started ? _lastIndex + (short)(sequence - (ushort)_lastIndex) : sequence;
                _started = true;
                _lastIndex = index;
                if ((_playing && index < _next) || _frames.ContainsKey(index))
                {
                    Late++;
                    return;
                }
                _frames[index] = payload;
                if (!_playing && _frames.Count >= TargetFrames)
                {
                    _playing = true;
                    _concealed = 0;
                    _next = First();
                }
                if (_frames.Count > MaxFrames)
                {
                    // Too much delay built up (a stall, then a burst): keep only the newest frames.
                    while (_frames.Count > TargetFrames)
                    {
                        _frames.Remove(First());
                        Skipped++;
                    }
                    if (_playing)
                        _next = First();
                }
            }
        }

        private int First()
        {
            using var e = _frames.Keys.GetEnumerator();
            e.MoveNext();
            return e.Current;
        }

        /// <summary>Fills <paramref name="count"/> samples of <paramref name="buffer"/>: speech when playing, silence otherwise.</summary>
        public void Read(float[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                double sum = 0;
                var end = offset + count;
                for (var i = offset; i < end; i++)
                {
                    if (_pcmAt >= VoiceFormat.FrameSamples && !NextFrame())
                    {
                        buffer[i] = 0f;
                        continue;
                    }
                    var sample = _pcm[_pcmAt++];
                    buffer[i] = sample;
                    sum += sample * sample;
                }
                _level = count > 0 ? (float)Math.Sqrt(sum / count) : 0f;
            }
        }

        private bool NextFrame()
        {
            if (!_playing)
                return false;
            if (_frames.TryGetValue(_next, out var payload))
            {
                _frames.Remove(_next);
                _opus.Decode(payload, 0, payload.Length, _pcm, 0, VoiceFormat.FrameSamples, false);
                _concealed = 0;
            }
            else if (_concealed < MaxConcealed && _frames.Count > 0)
            {
                _opus.Decode(null, 0, 0, _pcm, 0, VoiceFormat.FrameSamples, false);
                _concealed++;
                Concealed++;
            }
            else
            {
                // Nothing coming: the speaker stopped (or the line is down). Wait for a new burst.
                _playing = false;
                _concealed = 0;
                if (_frames.Count == 0)
                    return false;
                _next = First();
                return false;
            }
            _next++;
            _pcmAt = 0;
            return true;
        }
    }

    /// <summary>
    /// Voice activation: open while the microphone is louder than <see cref="ThresholdDb"/>, and for
    /// <see cref="HangSeconds"/> after it drops, so the ends of words are not cut.
    /// </summary>
    public sealed class VoiceGate
    {
        public const float HangSeconds = 0.3f;

        /// <summary>dBFS above which a frame counts as speech.</summary>
        public float ThresholdDb { get; set; } = -45f;

        private float _openFor;

        public bool Process(float[] frame)
        {
            if (Decibels(Rms(frame)) >= ThresholdDb)
                _openFor = HangSeconds;
            else
                _openFor -= VoiceFormat.FrameSeconds;
            return _openFor > 0f;
        }

        public static float Rms(float[] samples)
        {
            double sum = 0;
            foreach (var s in samples)
                sum += s * s;
            return samples.Length == 0 ? 0f : (float)Math.Sqrt(sum / samples.Length);
        }

        public static float Decibels(float rms) => rms <= 1e-6f ? -120f : 20f * (float)Math.Log10(rms);
    }

    /// <summary>
    /// How open a chalker's mouth is, 0–1, from a voice level: quiet speech barely opens it, loud speech opens it wide;
    /// it opens fast and closes a little slower, like a mouth does.
    /// </summary>
    public sealed class MouthFollower
    {
        public const float QuietDb = -50f;
        public const float LoudDb = -15f;
        public const float OpenRate = 30f;
        public const float CloseRate = 12f;

        public float Openness { get; private set; }

        public float Update(float rms, float deltaSeconds)
        {
            var target = Math.Max(0f, Math.Min(1f, (VoiceGate.Decibels(rms) - QuietDb) / (LoudDb - QuietDb)));
            var rate = target > Openness ? OpenRate : CloseRate;
            Openness += (target - Openness) * (1f - (float)Math.Exp(-rate * deltaSeconds));
            return Openness;
        }
    }
}
