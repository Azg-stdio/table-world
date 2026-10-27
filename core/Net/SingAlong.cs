using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Net
{
    /// <summary>
    /// Karaoke timing (proposed ADR 0013): each listener plays the shared song late by exactly how long the singer's voice
    /// takes to reach them, so voice and music arrive together on every screen. Clocks are compared with the host by
    /// ping (the half round trip, NTP-style, keeping the fastest samples); a voice frame carries the singer's send time,
    /// so a listener measures the voice's delay itself, jitter buffer included.
    /// </summary>
    public static class SingAlong
    {
        /// <summary>A voice packet's delay from its send time to now, both on the host's clock (ms), across the 32-bit wrap.</summary>
        public static double Delay(uint sentAtHost, double nowHost) => unchecked((uint)(long)nowHost - sentAtHost);

        /// <summary>
        /// This machine's clock minus the host's, from ping samples (<c>sent</c>, <c>hostTime</c>, <c>received</c> on this
        /// clock, ms): the fastest round trips are the least queued, so their midpoint is the best guess.
        /// </summary>
        public static double ClockOffset(IEnumerable<(double Sent, double HostTime, double Received)> pings, int keep = 5)
        {
            var best = pings.OrderBy(p => p.Received - p.Sent).Take(keep).ToList();
            if (best.Count == 0)
                return 0;
            return best.Average(p => (p.Sent + p.Received) / 2 - p.HostTime);
        }

        /// <summary>
        /// How late a listener plays the song, in ms: the voice frames' arrival (on the listener's clock, after its
        /// jitter buffer) minus their send time (on the singer's clock), both turned into host time with each machine's
        /// clock offset. The median of recent frames keeps a late packet from moving the song.
        /// </summary>
        public static double SongDelay(IEnumerable<(double SentBySinger, double PlayedHere)> frames, double singerOffset, double listenerOffset)
        {
            var delays = frames.Select(f => (f.PlayedHere - listenerOffset) - (f.SentBySinger - singerOffset)).OrderBy(d => d).ToList();
            if (delays.Count == 0)
                return 0;
            return delays[delays.Count / 2];
        }
    }
}
