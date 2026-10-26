using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>
    /// What one seat may know of a table (ADR 0012 §1, T11.2). The host keeps the whole state; each peer receives its
    /// seat's view: other seats' secret choices without their parameters, and no random generator state (a peer holding
    /// it could foresee every shuffle and deal). A null seat is the public view: spectators get it, and the state hash
    /// covers it, so peers with different secrets agree on the hash.
    /// </summary>
    public static class SeatView
    {
        /// <summary>The seat's view of <paramref name="state"/> as compact JSON (a snapshot).</summary>
        public static string Serialize(TableState state, string? seat) => Redacted(state, seat, () => TableJson.Serialize(state, indented: false));

        /// <summary>Hash of the public view: what every peer can check, whatever its own secrets.</summary>
        public static string Hash(TableState state) => Redacted(state, null, () => TableJson.Hash(state));

        /// <summary>The seat's view of the session (null without one), for a patch.</summary>
        public static JToken? Session(TableState state, string? seat) =>
            state.Session == null ? null : Redacted(state, seat, () => TableJson.ToToken(state.Session));

        /// <summary>
        /// Every secret as it stands, parameters included: when a command changes it, the command recorded or revealed a
        /// choice and peers cannot replay it.
        /// </summary>
        public static string SecretKey(TableState state) =>
            state.Session == null || state.Session.Secrets.Count == 0
                ? ""
                : string.Join("|", state.Session.Secrets.Select(s =>
                    s.Seat + "/" + s.Action + "/" + string.Join(",", s.Params.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value))));

        /// <summary>
        /// Runs <paramref name="read"/> while the state shows only what <paramref name="seat"/> may see, then puts it back.
        /// Swapping in place avoids copying the table (a 2 800-object TTS table takes a third of a second to copy).
        /// </summary>
        private static T Redacted<T>(TableState state, string? seat, Func<T> read)
        {
            var session = state.Session;
            if (session == null)
                return read();
            var random = session.RandomState;
            var secrets = session.Secrets;
            session.RandomState = 0;
            session.Secrets = secrets.Select(s => s.Seat == seat
                ? s
                : new SecretChoice { Seat = s.Seat, Action = s.Action, Params = new Dictionary<string, string>() }).ToList();
            try
            {
                return read();
            }
            finally
            {
                session.RandomState = random;
                session.Secrets = secrets;
            }
        }
    }
}
