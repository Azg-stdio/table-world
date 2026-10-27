using System.Collections.Generic;
using System.Linq;
using Core.Net;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;

namespace TableWorld.View.Net
{
    /// <summary>
    /// The host's clock on every machine, and how late each speaker's voice arrives (karaoke, ADR 0013, T12.3). A client
    /// pings the host every <see cref="ClockPingSeconds"/>; voice frames carry their send time on the host's clock, so any
    /// machine measures each speaker's voice delay (<see cref="VoiceDelayOf"/>), which a shared song will be delayed by.
    /// </summary>
    public sealed partial class NetworkTableSession
    {
        public const float ClockPingSeconds = 2f;
        /// <summary>Voice delays kept per speaker for the median.</summary>
        private const int DelaySamples = 50;

        private readonly HostClock _clock = new HostClock();
        private readonly Dictionary<string, Queue<double>> _voiceDelays = new Dictionary<string, Queue<double>>();
        private float _nextClockPing;

        /// <summary>This machine's clock minus the host's, in ms (0 on the host).</summary>
        public double ClockOffset => IsHost ? 0 : _clock.Offset;

        /// <summary>Now on the host's clock, in ms.</summary>
        public double HostNow => LocalNow - ClockOffset;

        private static double LocalNow => Time.realtimeSinceStartupAsDouble * 1000.0;

        /// <summary>The median delay of <paramref name="speaker"/>'s recent voice frames here, in ms, before the jitter buffer; null before any.</summary>
        public double? VoiceDelayOf(string speaker)
        {
            if (!_voiceDelays.TryGetValue(speaker, out var delays) || delays.Count == 0)
                return null;
            var sorted = delays.OrderBy(d => d).ToList();
            return sorted[sorted.Count / 2];
        }

        /// <summary>A client keeps its view of the host's clock fresh.</summary>
        private void TickClock()
        {
            if (IsHost || _network == null || !_network.IsConnectedClient || Time.unscaledTime < _nextClockPing)
                return;
            _nextClockPing = Time.unscaledTime + ClockPingSeconds;
            SendTo(NetworkManager.ServerClientId, HostClock.Ping(LocalNow));
        }

        /// <summary>The host answers a clock ping at once.</summary>
        private bool OnHostClock(ulong sender, JObject message)
        {
            if ((string)message["kind"] != "clock")
                return false;
            SendTo(sender, HostClock.Answer(message, LocalNow));
            return true;
        }

        private bool OnClientClock(JObject message) => (string)message["kind"] == "clock" && _clock.Receive(message, LocalNow);

        /// <summary>Stamps an outgoing voice frame with now on the host's clock.</summary>
        private void Stamp(VoicePacket packet) => packet.SentAt = unchecked((uint)(long)HostNow);

        /// <summary>Records how late a speaker's voice frame arrived.</summary>
        private void Measure(VoicePacket packet)
        {
            if (packet.SentAt is not { } sent)
                return;
            if (!_voiceDelays.TryGetValue(packet.Seat, out var delays))
                _voiceDelays[packet.Seat] = delays = new Queue<double>();
            delays.Enqueue(SingAlong.Delay(sent, HostNow));
            while (delays.Count > DelaySamples)
                delays.Dequeue();
        }
    }
}
