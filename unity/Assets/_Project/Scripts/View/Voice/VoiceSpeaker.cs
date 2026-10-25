using System;
using Core.Net;
using UnityEngine;

namespace TableWorld.View.Voice
{
    /// <summary>
    /// Plays one player's voice. The audio thread pulls each mixer buffer (about 20 ms) from that player's
    /// <see cref="VoiceStream"/> in <see cref="OnAudioFilterRead"/>, resampled to the output rate, and multiplies it into a
    /// looping clip of ones: the source keeps its 3D position and volume while the samples are ours. (A streamed
    /// <c>AudioClip</c> reader was tried first: Unity pulls it about a second ahead in big batches, far too late for a
    /// conversation, and the jitter buffer overflowed.) Mostly positional, so a voice comes from its chalker's mouth,
    /// but never so far that it fades at the table.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class VoiceSpeaker : MonoBehaviour
    {
        public VoiceStream Stream { get; } = new VoiceStream();

        private AudioSource _source;
        private AudioClip _ones;
        private int _outputRate;
        private float[] _samples = new float[4096];
        private double _phase;
        private float _last;

        private void Awake()
        {
            _outputRate = AudioSettings.outputSampleRate;
            _source = GetComponent<AudioSource>();
            _ones = AudioClip.Create("VoiceCarrier", _outputRate, 1, _outputRate, false);
            var ones = new float[_outputRate];
            for (var i = 0; i < ones.Length; i++)
                ones[i] = 1f;
            _ones.SetData(ones, 0);
            _source.clip = _ones;
            _source.loop = true;
            _source.spatialBlend = 0.7f;
            _source.dopplerLevel = 0f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 1.5f;
            _source.maxDistance = 8f;
            _source.volume = VoiceChat.Volume;
            _source.Play();
        }

        private void Update() => _source.volume = VoiceChat.Volume;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            var frames = data.Length / channels;
            if (_outputRate == VoiceFormat.SampleRate)
            {
                Ensure(frames);
                Stream.Read(_samples, 0, frames);
                for (var f = 0; f < frames; f++)
                    for (var c = 0; c < channels; c++)
                        data[f * channels + c] *= _samples[f];
                return;
            }
            // Linear resampling from 48 kHz: _samples[0] is the last sample of the previous buffer, new ones follow.
            var step = VoiceFormat.SampleRate / (double)_outputRate;
            var lastPosition = _phase + (frames - 1) * step;
            var needed = (int)Math.Floor(lastPosition) + 1;
            Ensure(needed + 1);
            _samples[0] = _last;
            Stream.Read(_samples, 1, needed);
            for (var f = 0; f < frames; f++)
            {
                var position = _phase + f * step;
                var i = (int)position;
                var v = _samples[i] + (_samples[i + 1] - _samples[i]) * (float)(position - i);
                for (var c = 0; c < channels; c++)
                    data[f * channels + c] *= v;
            }
            _last = _samples[needed];
            _phase = Math.Max(0, _phase + frames * step - needed);
        }

        private void Ensure(int length)
        {
            if (_samples.Length < length)
                _samples = new float[length];
        }

        private void OnDestroy()
        {
            if (_ones != null)
                Destroy(_ones);
        }
    }
}
