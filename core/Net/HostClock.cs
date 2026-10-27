using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>
    /// This machine's view of the host's clock (karaoke, ADR 0013): clients ping the host every few seconds and keep the
    /// latest samples; the offset comes from the fastest of them (<see cref="SingAlong.ClockOffset"/>). On the host the
    /// offset is zero. Times are milliseconds.
    /// </summary>
    public sealed class HostClock
    {
        public const int Samples = 20;

        private readonly Queue<(double Sent, double HostTime, double Received)> _pings = new Queue<(double, double, double)>();

        /// <summary>This machine's clock minus the host's.</summary>
        public double Offset { get; private set; }

        public int Count => _pings.Count;

        /// <summary>Now on the host's clock, from now on this one.</summary>
        public double ToHost(double local) => local - Offset;

        /// <summary>The ping a client sends.</summary>
        public static JObject Ping(double local) => new JObject { ["kind"] = "clock", ["sent"] = local };

        /// <summary>The host's answer: the ping with its own time added.</summary>
        public static JObject Answer(JObject ping, double hostNow) => new JObject { ["kind"] = "clock", ["sent"] = ping["sent"], ["host"] = hostNow };

        /// <summary>Takes an answer received at <paramref name="local"/>; returns false for anything else.</summary>
        public bool Receive(JObject answer, double local)
        {
            if ((string?)answer["kind"] != "clock" || answer["sent"] == null || answer["host"] == null)
                return false;
            _pings.Enqueue(((double)answer["sent"]!, (double)answer["host"]!, local));
            while (_pings.Count > Samples)
                _pings.Dequeue();
            Offset = SingAlong.ClockOffset(_pings);
            return true;
        }
    }
}
