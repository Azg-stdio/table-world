using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>
    /// Who sits where at a hosted table (G10), kept by the host. Every participant (the host is
    /// <see cref="SessionHost.HostId"/>) sits at one seat or watches as a spectator (null). A seat holds one participant.
    /// </summary>
    public sealed class Seating
    {
        private readonly Dictionary<string, string?> _seatOf = new Dictionary<string, string?>();

        /// <summary>Participants and their seats (null: spectator).</summary>
        public IReadOnlyDictionary<string, string?> SeatOf => _seatOf;

        public string? Seat(string participant) => _seatOf.TryGetValue(participant, out var seat) ? seat : null;

        public bool IsTaken(string seat) => _seatOf.Values.Contains(seat);

        /// <summary>A newcomer takes the first free seat of <paramref name="seats"/>, or watches when all are taken.</summary>
        public string? Join(string participant, IEnumerable<string> seats)
        {
            Leave(participant);
            var seat = seats.FirstOrDefault(s => !IsTaken(s));
            _seatOf[participant] = seat;
            return seat;
        }

        /// <summary>
        /// Moves <paramref name="participant"/> to <paramref name="seat"/> (null: spectate). Refused with a reason when the
        /// seat does not exist or someone else sits there.
        /// </summary>
        public bool Sit(string participant, string? seat, IEnumerable<string> seats, out string? error)
        {
            error = null;
            if (seat != null && !seats.Contains(seat))
                error = $"There is no seat '{seat}'.";
            else if (seat != null && _seatOf.Any(p => p.Value == seat && p.Key != participant))
                error = $"Someone already sits at {seat}.";
            if (error != null)
                return false;
            _seatOf[participant] = seat;
            return true;
        }

        public void Leave(string participant) => _seatOf.Remove(participant);

        /// <summary>Seats that no longer exist (another table was loaded) are left; their players watch until they pick one.</summary>
        public void Keep(IEnumerable<string> seats)
        {
            var valid = new HashSet<string>(seats);
            foreach (var participant in _seatOf.Keys.ToList())
                if (_seatOf[participant] is { } seat && !valid.Contains(seat))
                    _seatOf[participant] = null;
        }

        /// <summary>The broadcast every player gets after a change: <c>{ kind: "seating", seats: { seatId: participant } }</c>.</summary>
        public JObject Message()
        {
            var seats = new JObject();
            foreach (var pair in _seatOf.Where(p => p.Value != null).OrderBy(p => p.Value))
                seats[pair.Value!] = pair.Key;
            return new JObject { ["kind"] = "seating", ["seats"] = seats };
        }

        /// <summary>Reads a <see cref="Message"/> on a client: seat id → participant id.</summary>
        public static Dictionary<string, string> Read(JObject message) =>
            ((JObject?)message["seats"])?.Properties().ToDictionary(p => p.Name, p => (string)p.Value!) ?? new Dictionary<string, string>();
    }
}
