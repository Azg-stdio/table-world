using System;
using System.Collections.Generic;
using System.Linq;
using Core.Net;
using Xunit;
using Xunit.Abstractions;

namespace Core.Tests.Net
{
    /// <summary>T7.3 voice: the wire format, Opus round trips, and a listener's stream under reordering, loss and stalls.</summary>
    public class VoiceTests
    {
        private const int Frame = VoiceFormat.FrameSamples;
        private readonly ITestOutputHelper _output;

        public VoiceTests(ITestOutputHelper output) => _output = output;

        /// <summary>A voice-like tone: 220 Hz with a harmonic, at about -12 dBFS.</summary>
        private static float[] Tone(int frame)
        {
            var samples = new float[Frame];
            for (var i = 0; i < Frame; i++)
            {
                var t = (frame * Frame + i) / (double)VoiceFormat.SampleRate;
                samples[i] = (float)(0.25 * Math.Sin(2 * Math.PI * 220 * t) + 0.1 * Math.Sin(2 * Math.PI * 440 * t));
            }
            return samples;
        }

        private static List<VoicePacket> Speak(int frames, string seat = "red")
        {
            var encoder = new VoiceEncoder();
            return Enumerable.Range(0, frames).Select(f => encoder.Encode(Tone(f), seat)).ToList();
        }

        private static float[] ReadFrames(VoiceStream stream, int frames)
        {
            var output = new float[frames * Frame];
            for (var f = 0; f < frames; f++)
                stream.Read(output, f * Frame, Frame);
            return output;
        }

        /// <summary>Packets arriving one per 20 ms while the sound card pulls 20 ms: what a steady line looks like.</summary>
        private static float[] Live(VoiceStream stream, IEnumerable<VoicePacket> packets, int extraFrames)
        {
            var output = new List<float>();
            foreach (var p in packets)
            {
                stream.Push(p.Sequence, p.Payload);
                output.AddRange(ReadFrames(stream, 1));
            }
            output.AddRange(ReadFrames(stream, extraFrames));
            return output.ToArray();
        }

        [Fact]
        public void Codec_RoundTrips()
        {
            var packet = new VoicePacket { Seat = "blue", Sequence = 65000, Payload = new byte[] { 1, 2, 3, 250 } };
            var back = VoiceCodec.Decode(VoiceCodec.Encode(packet))!;
            Assert.Equal("blue", back.Seat);
            Assert.Equal((ushort)65000, back.Sequence);
            Assert.Equal(packet.Payload, back.Payload);
        }

        [Fact]
        public void Codec_RefusesMalformed()
        {
            var bytes = VoiceCodec.Encode(new VoicePacket { Seat = "blue", Sequence = 1, Payload = new byte[] { 1, 2, 3 } });
            Assert.Null(VoiceCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
            Assert.Null(VoiceCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
            var wrongVersion = (byte[])bytes.Clone();
            wrongVersion[0] = 9;
            Assert.Null(VoiceCodec.Decode(wrongVersion));
            Assert.Null(VoiceCodec.Decode(new byte[] { 1, 0, 0, 200, 0, 0 }));
            Assert.Null(VoiceCodec.Decode(Array.Empty<byte>()));
        }

        [Fact]
        public void Encoder_FramesAreSmall()
        {
            var packets = Speak(50);
            var average = packets.Average(p => p.Payload.Length);
            _output.WriteLine($"average Opus frame: {average:F1} bytes");
            // 24 kbps is 60 bytes per 20 ms frame.
            Assert.InRange(average, 20, 90);
            Assert.Equal(Enumerable.Range(0, 50).Select(i => (ushort)i), packets.Select(p => p.Sequence));
        }

        [Fact]
        public void Stream_PlaysTheVoiceBack()
        {
            var stream = new VoiceStream();
            var output = Live(stream, Speak(50), VoiceStream.TargetFrames - 1);
            var inputRms = VoiceGate.Rms(Enumerable.Range(0, 50).SelectMany(Tone).ToArray());
            var outputRms = VoiceGate.Rms(output);
            _output.WriteLine($"input RMS {inputRms:F3}, output RMS {outputRms:F3}");
            Assert.InRange(outputRms, inputRms * 0.7f, inputRms * 1.3f);
            Assert.Equal(0, stream.Concealed);
            Assert.True(stream.Level > 0.05f);
        }

        [Fact]
        public void Stream_WaitsForJitterRoom()
        {
            var stream = new VoiceStream();
            var packets = Speak(VoiceStream.TargetFrames);
            for (var i = 0; i < VoiceStream.TargetFrames - 1; i++)
                stream.Push(packets[i].Sequence, packets[i].Payload);
            Assert.False(stream.Playing);
            Assert.All(ReadFrames(stream, 1), s => Assert.Equal(0f, s));
            stream.Push(packets.Last().Sequence, packets.Last().Payload);
            Assert.True(stream.Playing);
        }

        [Fact]
        public void Stream_ReordersWithoutConcealing()
        {
            var packets = Speak(30);
            var stream = new VoiceStream();
            // Swap neighbours: every second packet arrives before the one ahead of it.
            for (var i = 0; i + 1 < packets.Count; i += 2)
            {
                stream.Push(packets[i + 1].Sequence, packets[i + 1].Payload);
                stream.Push(packets[i].Sequence, packets[i].Payload);
            }
            ReadFrames(stream, 30);
            Assert.Equal(0, stream.Concealed);
            Assert.Equal(0, stream.Late);
        }

        [Fact]
        public void Stream_ConcealsALostFrame_ThenStopsWhenTheSpeakerDoes()
        {
            var packets = Speak(20);
            packets.RemoveAt(10);
            var stream = new VoiceStream();
            Live(stream, packets, 0);
            Assert.Equal(1, stream.Concealed);
            ReadFrames(stream, 6);
            Assert.False(stream.Playing, "nothing left: the stream goes quiet");
            Assert.Equal(1, stream.Concealed);
            Assert.Equal(0f, stream.Level);
        }

        [Fact]
        public void Stream_CatchesUpAfterAStall()
        {
            var stream = new VoiceStream();
            foreach (var p in Speak(40))
                stream.Push(p.Sequence, p.Payload);
            Assert.True(stream.Skipped > 0);
            Assert.True(stream.Buffered <= VoiceStream.MaxFrames);
        }

        [Fact]
        public void Stream_SequenceWraps()
        {
            var payloads = Speak(20).Select(p => p.Payload).ToList();
            var stream = new VoiceStream();
            for (var i = 0; i < payloads.Count; i++)
            {
                stream.Push((ushort)(65530 + i), payloads[i]);
                if (i >= VoiceStream.TargetFrames)
                    ReadFrames(stream, 1);
            }
            ReadFrames(stream, 5);
            Assert.Equal(0, stream.Late);
            Assert.Equal(0, stream.Concealed);
            Assert.Equal(0, stream.Skipped);
        }

        [Fact]
        public void Stream_NewBurstAfterSilence()
        {
            var packets = Speak(40);
            var stream = new VoiceStream();
            Live(stream, packets.Take(10), 10);
            Assert.False(stream.Playing);
            // The speaker talks again later: their numbers continue from where they stopped.
            Assert.True(VoiceGate.Rms(Live(stream, packets.Skip(30), 0)) > 0.05f);
        }

        /// <summary>
        /// A talking player over a bad line: 20–120 ms of random delay per packet and 3 % loss, the sound card pulling
        /// 20 ms at a time. Almost every frame plays; what is lost is concealed, not heard as gaps.
        /// </summary>
        [Fact]
        public void Stream_SurvivesJitterAndLoss()
        {
            var random = new Random(7);
            const int frames = 500;
            var packets = Speak(frames);
            var arrivals = packets.Where(_ => random.NextDouble() >= 0.03)
                .Select(p => (At: p.Sequence * 20.0 + 20 + random.NextDouble() * 100, Packet: p))
                .OrderBy(a => a.At).ToList();
            var stream = new VoiceStream();
            var next = 0;
            var silentFrames = 0;
            var buffer = new float[Frame];
            for (var ms = 0.0; ms < frames * 20 + 400; ms += 20)
            {
                while (next < arrivals.Count && arrivals[next].At <= ms)
                {
                    stream.Push(arrivals[next].Packet.Sequence, arrivals[next].Packet.Payload);
                    next++;
                }
                var playing = stream.Playing;
                stream.Read(buffer, 0, Frame);
                if (playing && VoiceGate.Rms(buffer) < 0.01f)
                    silentFrames++;
            }
            _output.WriteLine($"lost {frames - arrivals.Count}, concealed {stream.Concealed}, late {stream.Late}, skipped {stream.Skipped}, silent while playing {silentFrames}");
            Assert.True(stream.Late + stream.Skipped < frames * 0.05, "few frames thrown away");
            Assert.True(silentFrames < frames * 0.05, "few gaps");
        }

        [Fact]
        public void Gate_OpensOnSpeech_AndHangsOn()
        {
            var gate = new VoiceGate();
            Assert.False(gate.Process(new float[Frame]));
            Assert.True(gate.Process(Tone(0)));
            Assert.True(gate.Process(new float[Frame]), "the end of a word is kept");
            var frames = (int)Math.Ceiling(VoiceGate.HangSeconds / VoiceFormat.FrameSeconds);
            for (var i = 0; i < frames; i++)
                gate.Process(new float[Frame]);
            Assert.False(gate.Process(new float[Frame]));
        }

        [Fact]
        public void Mouth_FollowsLevel()
        {
            var mouth = new MouthFollower();
            Assert.Equal(0f, mouth.Update(0f, 0.1f));
            for (var i = 0; i < 20; i++)
                mouth.Update(0.3f, 0.02f);
            Assert.True(mouth.Openness > 0.9f);
            mouth.Update(0f, 0.02f);
            Assert.InRange(mouth.Openness, 0.3f, 0.9f);
            for (var i = 0; i < 30; i++)
                mouth.Update(0f, 0.02f);
            Assert.True(mouth.Openness < 0.05f);
        }
    }
}
