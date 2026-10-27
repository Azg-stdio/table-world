using System;
using System.Collections.Generic;
using System.Linq;
using Core.Net;
using Xunit.Abstractions;

namespace Core.Tests.Net
{
    /// <summary>
    /// Karaoke ⚗ (proposed ADR 0013): before building anything, how close can voice and music line up for every listener?
    /// A simulated internet: each machine reaches the host with its own latency (10–80 ms one way), path asymmetry up to
    /// 30 %, jitter (exponential, mean 8 ms), and its own clock error. One singer: each listener delays the song by the
    /// measured voice delay. Two singers: one song timing per listener cannot fit both voices.
    /// </summary>
    public class SingAlongSimulationTests
    {
        private readonly ITestOutputHelper _output;

        public SingAlongSimulationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private sealed class Peer
        {
            public double ToHost, FromHost, Clock;
        }

        private static Peer NewPeer(Random r)
        {
            var oneWay = 10 + r.NextDouble() * 70;
            var skew = 1 + (r.NextDouble() * 2 - 1) * 0.3;
            return new Peer { ToHost = oneWay * skew, FromHost = oneWay / skew, Clock = (r.NextDouble() * 2 - 1) * 5000 };
        }

        private static double Jitter(Random r) => -Math.Log(1 - r.NextDouble()) * 8;

        /// <summary>The peer's clock offset as it would measure it: 20 pings to the host over a jittery path.</summary>
        private static double Measured(Peer p, Random r)
        {
            var pings = new List<(double, double, double)>();
            for (var i = 0; i < 20; i++)
            {
                var hostNow = 1000.0 * i;
                var sent = hostNow + p.Clock;
                var atHost = hostNow + p.ToHost + Jitter(r);
                var back = atHost + p.FromHost + Jitter(r);
                pings.Add((sent, atHost, back + p.Clock));
            }
            return SingAlong.ClockOffset(pings);
        }

        /// <summary>How far the singer's voice lands from the music at a listener, in ms, after the listener sets its delay.</summary>
        private static double Error(Peer singer, Peer listener, double singerOffset, double listenerOffset, Random r, double buffer)
        {
            var frames = new List<(double, double)>();
            var trueDelays = new List<double>();
            for (var i = 0; i < 50; i++)
            {
                var hostSent = 20.0 * i;
                var delay = singer.ToHost + Jitter(r) + listener.FromHost + Jitter(r);
                // The jitter buffer plays frames on a steady schedule: late frames are absorbed, not passed on.
                var played = hostSent + Math.Max(delay, singer.ToHost + listener.FromHost + buffer);
                trueDelays.Add(played - hostSent);
                frames.Add((hostSent + singer.Clock, played + listener.Clock));
            }
            var songDelay = SingAlong.SongDelay(frames, singerOffset, listenerOffset);
            // The music plays songDelay after the singer started; the voice arrives after its true (median) delay.
            var truth = trueDelays.OrderBy(d => d).ElementAt(trueDelays.Count / 2);
            return Math.Abs(songDelay - truth);
        }

        private static (double P50, double P95, double Max) Spread(List<double> values)
        {
            values.Sort();
            return (values[values.Count / 2], values[values.Count * 95 / 100], values[^1]);
        }

        [Fact]
        public void OneSinger_EveryListenerLinesUp_TwoSingersDoNot()
        {
            var r = new Random(7);
            var solo = new List<double>();
            var duet = new List<double>();
            var singersHearEachOther = new List<double>();
            for (var room = 0; room < 500; room++)
            {
                var peers = Enumerable.Range(0, 6).Select(_ => NewPeer(r)).ToList();
                var offsets = peers.Select(p => Measured(p, r)).ToList();
                var singer = peers[0];
                for (var l = 1; l < peers.Count; l++)
                    solo.Add(Error(singer, peers[l], offsets[0], offsets[l], r, buffer: 60));

                // A duet: the listener can delay the song for one singer only; the other voice is off by the difference.
                var second = peers[1];
                for (var l = 2; l < peers.Count; l++)
                {
                    var a = singer.ToHost + peers[l].FromHost;
                    var b = second.ToHost + peers[l].FromHost;
                    duet.Add(Math.Abs(a - b));
                }
                // And each singer hears the other a whole voice path late (both ways through the host, plus the buffer).
                singersHearEachOther.Add(singer.ToHost + second.FromHost + 60);
            }
            var s = Spread(solo);
            var d = Spread(duet);
            var h = Spread(singersHearEachOther);
            _output.WriteLine($"One singer, {solo.Count} listeners: voice vs music off by p50 {s.P50:F1} ms, p95 {s.P95:F1} ms, max {s.Max:F1} ms");
            _output.WriteLine($"Two singers, the second voice vs music: p50 {d.P50:F0} ms, p95 {d.P95:F0} ms, max {d.Max:F0} ms");
            _output.WriteLine($"Two singers hearing each other: late by p50 {h.P50:F0} ms, p95 {h.P95:F0} ms");

            // Lip-sync tolerance is about 45 ms of audio late and 15 ms early (ITU-R BT.1359): one singer fits, a duet does not.
            Assert.True(s.P95 < 30, $"one singer p95 {s.P95:F1} ms");
            Assert.True(d.P50 > 15, $"a duet lines up by chance only (p50 {d.P50:F0} ms)");
        }
    }
}
