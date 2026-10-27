using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>
    /// What one seat may know of a table (ADR 0012 §1, T11.2). The host keeps the whole state; each peer receives its
    /// seat's view: other seats' secret choices without their parameters, its own role, traits and team, and what it knows
    /// of others' (T11.4),
    /// and no random generator state (a peer holding it could foresee every shuffle and deal). A null seat is the public
    /// view: spectators get it, and the state hash covers it, so peers with different secrets agree on the hash.
    /// </summary>
    public static class SeatView
    {
        /// <summary>The seat's view of <paramref name="state"/> as compact JSON (a snapshot).</summary>
        public static string Serialize(TableState state, string? seat, TableEngine? table = null) =>
            Redacted(state, seat, () => Unknowns(state, TableJson.Serialize(state, indented: false), seat, table));

        /// <summary>Hash of the public view: what every peer can check, whatever its own secrets.</summary>
        public static string Hash(TableState state, TableEngine? table = null) => Redacted(state, null, () =>
        {
            if (!Strict(state))
                return TableJson.Hash(state);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Unknowns(state, TableJson.Serialize(state, indented: false), null, table)));
            return "sha256:" + string.Concat(bytes.Select(b => b.ToString("x2")));
        });

        /// <summary>
        /// Tables whose cards are secret on the wire too (ADR 0012, T11.16): those with rules or roles. A free-play table
        /// (a TTS import, a sandbox) keeps TTS's honest play: everything reaches every machine and the view hides it.
        /// </summary>
        public static bool Strict(TableState state) => state.SecretCards;

        /// <summary>
        /// The cards <paramref name="seat"/> may not know (null: what is hidden from anyone): in another seat's hand, in a
        /// hidden zone that does not show it to them, face down on the table; and the face-down decks, whose order nobody
        /// knows. Empty on a free-play table. <paramref name="table"/> is the live engine over <paramref name="state"/>, when
        /// there is one (its zone index saves rebuilding it).
        /// </summary>
        public static (HashSet<string> Cards, HashSet<string> Decks) Hidden(TableState state, string? seat, TableEngine? table = null)
        {
            var cards = new HashSet<string>();
            var decks = new HashSet<string>();
            if (!Strict(state))
                return (cards, decks);
            table ??= new TableEngine(state, 1);
            var seats = state.Players.Seats.Select(s => s.Id).ToList();
            foreach (var obj in state.Objects)
            {
                switch (obj)
                {
                    case Card card when card.Container == null:
                        var hidden = seat == null ? seats.Any(s => table.IsHiddenFrom(card.Id, s)) : table.IsHiddenFrom(card.Id, seat);
                        var inHand = table.ZonesOf(card.Id).Any(z => state.Zones.FirstOrDefault(x => x.Id == z) is HandZone);
                        if (hidden || !card.IsFaceUp && !inHand)
                            cards.Add(card.Id);
                        break;
                    case Deck deck when deck.Container == null && !deck.IsFaceUp && deck.Cards.Count > 0:
                        decks.Add(deck.Id);
                        break;
                }
            }
            return (cards, decks);
        }

        /// <summary>What may not be known of a card: everything but where it is, its kind and its back.</summary>
        private static readonly string[] Identity =
            { "name", "description", "gmNotes", "tags", "faceAsset", "faceIndex", "meta", "text", "script", "scriptState", "states", "ttsExtra", "buttons", "inputs", "scriptUi", "entryId" };

        /// <summary>One object as <paramref name="seat"/> receives it: unknown cards keep only their place and back.</summary>
        public static JToken Object(TableObject obj, (HashSet<string> Cards, HashSet<string> Decks) hidden)
        {
            var token = TableJson.ToToken(obj);
            if (!(token is JObject json) || !hidden.Cards.Contains(obj.Id) && !hidden.Decks.Contains(obj.Id))
                return token;
            if (hidden.Cards.Contains(obj.Id))
                Blank(json, keepId: true);
            else if (json["cards"] is JArray entries)
                // A deck card's id would name it (uno_red_5): it goes too.
                foreach (var entry in entries.OfType<JObject>())
                    Blank(entry, keepId: false);
            // Back through the model, so the order of the fields is the one every machine writes.
            return TableJson.ToToken((TableObject)TableJson.FromToken(json, typeof(TableObject))!);
        }

        private static void Blank(JObject card, bool keepId)
        {
            foreach (var key in Identity)
                card.Remove(key);
            if (!keepId)
                card.Remove("id");
            card["unknown"] = true;
        }

        /// <summary>The table's JSON with every object <paramref name="seat"/> may not know made unknown.</summary>
        private static string Unknowns(TableState state, string text, string? seat, TableEngine? table)
        {
            var hidden = Hidden(state, seat, table);
            if (hidden.Cards.Count == 0 && hidden.Decks.Count == 0)
                return text;
            var json = JObject.Parse(text);
            if (json["objects"] is JArray objects)
                for (var i = 0; i < objects.Count && i < state.Objects.Count; i++)
                    if (hidden.Cards.Contains(state.Objects[i].Id) || hidden.Decks.Contains(state.Objects[i].Id))
                        objects[i] = Object(state.Objects[i], hidden);
            return json.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>The seat's view of the session (null without one), for a patch.</summary>
        public static JToken? Session(TableState state, string? seat) =>
            state.Session == null ? null : Redacted(state, seat, () => TableJson.ToToken(state.Session));

        /// <summary>
        /// Puts back into <paramref name="incoming"/>, a seat's view sent back as a whole table (an agent's edit while
        /// playing), the secrets it could not hold, from <paramref name="live"/>: unknown cards, roles, what each seat
        /// knows, secret choices and the random generator. Nothing changes when the two are not the same game in play.
        /// </summary>
        public static void Restore(TableState incoming, TableState live)
        {
            // Cards this seat could not know come back as they really are (T11.16).
            for (var i = 0; i < incoming.Objects.Count; i++)
            {
                var obj = incoming.Objects[i];
                var unknown = obj is Card { Unknown: true } || obj is Deck deck && deck.Cards.Any(c => c.Unknown);
                if (unknown && live.Objects.FirstOrDefault(o => o.Id == obj.Id) is { } real)
                    incoming.Objects[i] = (TableObject)TableJson.FromToken(TableJson.ToToken(real), typeof(TableObject))!;
            }
            if (!(incoming.Session is { Started: true } mine) || !(live.Session is { Started: true } full) || mine.Turn != full.Turn)
                return;
            mine.Roles = full.Roles.ToDictionary(p => p.Key, p => p.Value);
            mine.Knows = full.Knows.ToDictionary(p => p.Key, p => p.Value.ToList());
            mine.Traits = full.Traits.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value));
            mine.Teams = new Dictionary<string, string>(full.Teams);
            foreach (var name in live.SecretVariables.Where(full.Variables.ContainsKey))
                mine.Variables[name] = full.Variables[name].DeepClone();
            foreach (var pair in full.Votes.Where(p => !mine.Votes.ContainsKey(p.Key)))
                mine.Votes[pair.Key] = pair.Value;
            mine.LastVote ??= full.LastVote;
            if (mine.Secrets.Select(c => c.Seat + "/" + c.Action).SequenceEqual(full.Secrets.Select(c => c.Seat + "/" + c.Action)))
                mine.Secrets = full.Secrets.Select(c => new SecretChoice { Seat = c.Seat, Action = c.Action, Params = new Dictionary<string, string>(c.Params) }).ToList();
            mine.RandomState = full.RandomState;
        }

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
            var roles = session.Roles;
            var knows = session.Knows;
            var traits = session.Traits;
            var teams = session.Teams;
            var votes = session.Votes;
            var lastVote = session.LastVote;
            var variables = session.Variables;
            if (state.SecretVariables.Count > 0)
                session.Variables = variables.Where(p => !state.SecretVariables.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            session.RandomState = 0;
            session.Roles = roles.Where(p => p.Key == seat).ToDictionary(p => p.Key, p => p.Value);
            session.Knows = knows.Where(p => p.Key == seat).ToDictionary(p => p.Key, p => p.Value);
            session.Traits = traits.Where(p => p.Key == seat).ToDictionary(p => p.Key, p => p.Value);
            session.Teams = teams.Where(p => p.Key == seat).ToDictionary(p => p.Key, p => p.Value);
            // A private vote's ballots are not even seen to exist by anyone but the one who cast it.
            var privateVotes = new HashSet<string>(state.Actions.Where(a => a.Vote is { Private: true }).Select(a => a.Id));
            // Its count too: the rules read it on the host; a peer would learn the wolves' victim before dawn.
            session.Votes = votes.Where(p => !privateVotes.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            if (lastVote != null && privateVotes.Contains(lastVote))
                session.LastVote = null;
            session.Secrets = secrets.Where(s => s.Seat == seat || !privateVotes.Contains(s.Action)).Select(s => s.Seat == seat
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
                session.Roles = roles;
                session.Knows = knows;
                session.Traits = traits;
                session.Teams = teams;
                session.Votes = votes;
                session.LastVote = lastVote;
                session.Variables = variables;
            }
        }
    }
}
