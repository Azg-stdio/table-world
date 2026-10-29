using System;
using System.Collections.Generic;

namespace Core.Music
{
    /// <summary>
    /// The built-in sound cues (M12, <see cref="Rules.SoundCues.Builtin"/>), made in code so every copy of the game has
    /// them with no sound file to license: a gong, a bell, a chime, a drum, a sting, a fanfare, a page turning, a whoosh
    /// and a knock. Mono samples at <see cref="Rate"/>, peaking just under full scale. The same name always gives the same
    /// sound.
    /// </summary>
    public static class CueSynth
    {
        public const int Rate = 44100;

        public static float[]? Make(string name)
        {
            // A seed from the name itself (string.GetHashCode changes from one run to the next).
            var seed = 17;
            foreach (var c in name)
                seed = seed * 31 + c;
            var random = new Random(seed & 0x7fffffff);
            float[] samples;
            switch (name)
            {
                case "gong": samples = Gong(random); break;
                case "bell": samples = Bell(660f, 2.4f, 1f); break;
                case "chime": samples = Chime(); break;
                case "drum": samples = Drum(random); break;
                case "sting": samples = Sting(random); break;
                case "fanfare": samples = Fanfare(); break;
                case "page": samples = Page(random); break;
                case "whoosh": samples = Whoosh(random); break;
                case "knock": samples = Knock(random); break;
                default: return null;
            }
            Normalize(samples, 0.9f);
            return samples;
        }

        private static float[] Buffer(float seconds) => new float[(int)(seconds * Rate)];

        /// <summary>Partials of a struck metal body: inharmonic ratios, each fading at its own speed, with a soft attack.</summary>
        private static void Partials(float[] into, int start, float pitch, (float Ratio, float Amp, float Decay)[] partials, float attack = 0.004f, float gain = 1f)
        {
            for (var i = start; i < into.Length; i++)
            {
                var t = (i - start) / (float)Rate;
                var env = Math.Min(1f, t / attack);
                var v = 0f;
                foreach (var (ratio, amp, decay) in partials)
                    v += amp * (float)Math.Exp(-t / decay) * (float)Math.Sin(2 * Math.PI * pitch * ratio * t);
                into[i] += gain * env * v;
            }
        }

        private static float[] Gong(Random random)
        {
            var s = Buffer(4.5f);
            // A low, wide gong: the beating of near partials gives its shimmer.
            Partials(s, 0, 98f, new[] { (1f, 1f, 2.6f), (1.012f, 0.6f, 2.4f), (1.48f, 0.5f, 1.8f), (2.09f, 0.45f, 1.4f), (2.56f, 0.3f, 1.1f), (3.21f, 0.2f, 0.8f), (4.1f, 0.12f, 0.5f) }, 0.012f);
            // The mallet's thud.
            var low = 0f;
            for (var i = 0; i < Rate / 8 && i < s.Length; i++)
            {
                low += 0.08f * ((float)random.NextDouble() * 2f - 1f - low);
                s[i] += 1.6f * low * (1f - i / (Rate / 8f));
            }
            return s;
        }

        private static float[] Bell(float pitch, float seconds, float gain)
        {
            var s = Buffer(seconds);
            Partials(s, 0, pitch, new[] { (0.5f, 0.35f, seconds * 0.6f), (1f, 1f, seconds * 0.4f), (1.19f, 0.4f, seconds * 0.3f), (1.5f, 0.35f, seconds * 0.25f), (2f, 0.3f, seconds * 0.2f), (2.74f, 0.18f, seconds * 0.12f) }, 0.002f, gain);
            return s;
        }

        private static float[] Chime()
        {
            // Three soft bells rising: E6, G#6, B6.
            var s = Buffer(2.2f);
            var notes = new[] { 1318.5f, 1661.2f, 1975.5f };
            for (var n = 0; n < notes.Length; n++)
                Partials(s, (int)(n * 0.13f * Rate), notes[n], new[] { (1f, 1f, 0.7f), (2.01f, 0.25f, 0.35f), (3f, 0.08f, 0.2f) }, 0.003f, 0.6f);
            return s;
        }

        private static float[] Drum(Random random)
        {
            // A deep frame drum: a sine falling in pitch, a skin's noise on top.
            var s = Buffer(1.3f);
            var phase = 0.0;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)Rate;
                var f = 48f + 90f * (float)Math.Exp(-t / 0.07f);
                phase += 2 * Math.PI * f / Rate;
                s[i] = (float)Math.Sin(phase) * (float)Math.Exp(-t / 0.42f) * Math.Min(1f, t / 0.002f);
                s[i] += 0.25f * ((float)random.NextDouble() * 2f - 1f) * (float)Math.Exp(-t / 0.03f);
            }
            return s;
        }

        private static float[] Sting(Random random)
        {
            // A dark, unresolved chord (A, C, E flat: diminished) swelling and fading, over a drum hit.
            var s = Buffer(2.6f);
            var notes = new[] { 110f, 220f, 261.6f, 311.1f, 415.3f };
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)Rate;
                var env = Math.Min(1f, t / 0.05f) * (float)Math.Exp(-t / 1.1f);
                var v = 0f;
                foreach (var f in notes)
                    for (var h = 1; h <= 5; h++)
                        v += (float)Math.Sin(2 * Math.PI * f * h * t * (1f + 0.002f * (float)Math.Sin(2 * Math.PI * 5 * t))) / (h * h);
                s[i] = env * v * 0.3f;
            }
            var drum = Drum(random);
            for (var i = 0; i < drum.Length && i < s.Length; i++)
                s[i] += 0.8f * drum[i];
            return s;
        }

        private static float[] Fanfare()
        {
            // A bright rising arpeggio, the last note held: C5, E5, G5, C6.
            var s = Buffer(1.9f);
            var notes = new[] { (523.3f, 0f, 0.16f), (659.3f, 0.15f, 0.16f), (784f, 0.3f, 0.16f), (1046.5f, 0.45f, 1.1f) };
            foreach (var (f, at, length) in notes)
            {
                var start = (int)(at * Rate);
                var end = Math.Min(s.Length, start + (int)((length + 0.25f) * Rate));
                for (var i = start; i < end; i++)
                {
                    var t = (i - start) / (float)Rate;
                    var env = Math.Min(1f, t / 0.015f) * (t < length ? 1f : (float)Math.Exp(-(t - length) / 0.08f)) * (float)Math.Exp(-t / 1.6f);
                    var vibrato = 1f + 0.004f * (float)Math.Sin(2 * Math.PI * 5.5 * t) * Math.Min(1f, t / 0.3f);
                    var v = 0f;
                    for (var h = 1; h <= 6; h++)
                        v += (float)Math.Sin(2 * Math.PI * f * vibrato * h * t) * (h == 1 ? 1f : 0.6f / h);
                    s[i] += env * v * 0.45f;
                }
            }
            return s;
        }

        /// <summary>Noise through a one-pole low-pass whose cut-off follows <paramref name="cutoff"/>, shaped by <paramref name="envelope"/>.</summary>
        private static float[] Noise(Random random, float seconds, Func<float, float> cutoff, Func<float, float> envelope)
        {
            var s = Buffer(seconds);
            var y = 0f;
            for (var i = 0; i < s.Length; i++)
            {
                var t = i / (float)Rate;
                var a = 1f - (float)Math.Exp(-2 * Math.PI * cutoff(t) / Rate);
                y += a * ((float)random.NextDouble() * 2f - 1f - y);
                s[i] = y * envelope(t);
            }
            return s;
        }

        private static float[] Page(Random random) =>
            // A page turned: a quick rustle, then the paper settling.
            Noise(random, 0.45f, t => 2500f + 3000f * (float)Math.Sin(Math.PI * Math.Min(1f, t / 0.3f)),
                t => (float)(Math.Exp(-Math.Pow((t - 0.08) / 0.05, 2)) + 0.6 * Math.Exp(-Math.Pow((t - 0.25) / 0.06, 2))));

        private static float[] Whoosh(Random random) =>
            Noise(random, 0.8f, t => 300f + 2200f * (float)Math.Sin(Math.PI * t / 0.8f), t => (float)Math.Pow(Math.Sin(Math.PI * t / 0.8f), 2));

        private static float[] Knock(Random random)
        {
            // Two knocks on a wooden door.
            var s = Buffer(0.5f);
            foreach (var at in new[] { 0f, 0.16f })
            {
                var start = (int)(at * Rate);
                var y = 0f;
                for (var i = start; i < s.Length; i++)
                {
                    var t = (i - start) / (float)Rate;
                    y += 0.15f * ((float)random.NextDouble() * 2f - 1f - y);
                    s[i] += ((float)Math.Sin(2 * Math.PI * 180 * t) * 0.8f + y * 2f) * (float)Math.Exp(-t / 0.025f);
                }
            }
            return s;
        }

        private static void Normalize(float[] samples, float peak)
        {
            var max = 0f;
            foreach (var v in samples)
                max = Math.Max(max, Math.Abs(v));
            if (max <= 0f)
                return;
            var gain = peak / max;
            for (var i = 0; i < samples.Length; i++)
                samples[i] *= gain;
            // A few milliseconds of fade at the end, so nothing clicks off.
            var fade = Math.Min(samples.Length, Rate / 100);
            for (var i = 0; i < fade; i++)
                samples[samples.Length - 1 - i] *= i / (float)fade;
        }

        /// <summary>The cue as a 16-bit mono WAV file (to listen to it outside the game).</summary>
        public static byte[] Wav(float[] samples)
        {
            var data = new List<byte>(44 + samples.Length * 2);
            void Int(int v) => data.AddRange(BitConverter.GetBytes(v));
            void Short(short v) => data.AddRange(BitConverter.GetBytes(v));
            data.AddRange(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            Int(36 + samples.Length * 2);
            data.AddRange(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            Int(16);
            Short(1);
            Short(1);
            Int(Rate);
            Int(Rate * 2);
            Short(2);
            Short(16);
            data.AddRange(System.Text.Encoding.ASCII.GetBytes("data"));
            Int(samples.Length * 2);
            foreach (var v in samples)
                Short((short)Math.Round(Math.Max(-1f, Math.Min(1f, v)) * short.MaxValue));
            return data.ToArray();
        }
    }
}
