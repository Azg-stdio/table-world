using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using Core.Text;
using Newtonsoft.Json.Linq;

namespace Core.Engine
{
    /// <summary>
    /// Eyes-closed phases and voice channels (ADR 0012 §3–4, T11.7–T11.8). A phase may say who is awake; the others sleep:
    /// they take no actions, their screen goes dark, and they neither see the awake players' pointers nor hear them. A
    /// phase's <c>voice</c> says who hears whom. On the host every answer is exact; on a peer it uses only what that seat
    /// knows (its own role, what it was shown), so a sleeping seat learns nothing from who seems awake.
    /// </summary>
    public sealed partial class GameEngine
    {
        /// <summary>The phase being played now, or null.</summary>
        public PhaseDef? Phase => CurrentPhase();

        /// <summary>True while the phase in play closes some eyes (it names who is awake).</summary>
        public bool EyesClosedPhase => Session is { Started: true, Over: false } && CurrentPhase()?.Awake != null;

        /// <summary>
        /// Whether <paramref name="seat"/> is awake now, as <paramref name="viewer"/> can tell (null: the host, who knows
        /// all). Outside an eyes-closed phase everyone is awake.
        /// </summary>
        public bool IsAwake(string seat, string? viewer = null)
        {
            if (IsOut(seat) && Session is { Started: true, Over: false })
                return false;
            if (!EyesClosedPhase)
                return true;
            var awake = CurrentPhase()!.Awake!;
            switch (awake)
            {
                case "all":
                    return true;
                case "nobody":
                    return false;
            }
            if (awake.StartsWith("team:"))
                return KnownTeam(seat, viewer) == awake.Substring(5);
            if (awake.StartsWith("role:"))
                return KnownRole(seat, viewer) == awake.Substring(5);
            if (!References.IsReference(awake))
                return seat == awake;
            return WithContext(new Context { Actor = Session!.CurrentSeat }, () => TryResolve(awake)) switch
            {
                Seat one => one.Id == seat,
                JArray list => list.Any(t => t.Type == JTokenType.String && (string)t! == seat),
                JValue { Type: JTokenType.String } id => (string)id! == seat,
                _ => false,
            };
        }

        /// <summary>What a sleeping seat reads on its dark screen.</summary>
        public string SleepText => CurrentPhase() is { SleepText: { Length: > 0 } text } phase ? Template(Translations.Text(State, $"phases.{phase.Id}.sleepText", text)!) : Names.Message(State, "eyesClosed");

        /// <summary>
        /// Whether <paramref name="listener"/> hears <paramref name="speaker"/> now (seats; null for a spectator or a host
        /// without a seat, who hear everyone). Decided on the host, which relays voice.
        /// </summary>
        public bool Hears(string? speaker, string? listener)
        {
            if (listener == null || speaker == listener || !(Session is { Started: true, Over: false }))
                return true;
            // The eliminated are silent at the table.
            if (speaker != null && IsOut(speaker))
                return false;
            var phase = CurrentPhase();
            var voice = phase?.Voice ?? (phase?.Awake != null ? PhaseVoice.Awake : PhaseVoice.All);
            switch (voice)
            {
                case PhaseVoice.None:
                    return false;
                case PhaseVoice.Awake:
                    return speaker != null && IsAwake(speaker) && IsAwake(listener);
                case PhaseVoice.Team:
                    return speaker != null && TeamOf(speaker) is { } team && TeamOf(listener) == team;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Whether <paramref name="viewer"/> sees <paramref name="seat"/>'s pointer, gaze and held pieces now: an awake player
        /// is not seen by the sleepers. Spectators and a host without a seat see everyone.
        /// </summary>
        public bool Sees(string seat, string? viewer) => viewer == null || seat == viewer || !EyesClosedPhase || !IsAwake(seat) || IsAwake(viewer);

        /// <summary>
        /// A gesture (ADR 0012 §6): the rules' <c>onGesture</c> effects run with the gesture and its target. Always the
        /// host's to run, since it passes between two players; outside a game with that event it changes nothing.
        /// </summary>
        private CommandResult Gesture(GestureCommand command)
        {
            if (!(Session is { Started: true, Over: false }) || !State.Events.ContainsKey(GameEvents.OnGesture))
                return CommandResult.Success(new List<StateEvent>());
            var seat = command.Actor;
            if (seat == null || Playing.All(s => s.Id != seat))
                return CommandResult.Fail("actor", ErrorCodes.NotPlaying, "Only a player in the game makes gestures that count.").Text("refusal.notPlaying");
            if (command.Target != null && Playing.All(s => s.Id != command.Target))
                return CommandResult.Fail("target", ErrorCodes.UnknownSeat, $"Seat '{command.Target}' is not playing.");
            var parameters = new Dictionary<string, string> { ["gesture"] = command.Gesture };
            if (command.Target != null)
                parameters["target"] = command.Target;
            var result = Transaction(() =>
            {
                WithContext(new Context { Actor = seat, Params = parameters }, () => RunEffects(State.Events[GameEvents.OnGesture]));
                AdvanceLoop();
                CheckWins(WinCheck.AnyCommand);
            });
            result.ReadsSecrets = true;
            return result;
        }

        /// <summary>True once <paramref name="seat"/> is out of the game (<c>eliminate</c>).</summary>
        public bool IsOut(string seat) => Session != null && Session.Eliminated.Contains(seat);

        /// <summary>Puts a seat out of the game; with <paramref name="reveal"/> its role is shown to all.</summary>
        private void Eliminate(string seat, bool reveal)
        {
            var session = Session!;
            if (session.Eliminated.Contains(seat))
                return;
            session.Eliminated.Add(seat);
            Log(Names.Message(State, "eliminated", ("seat", Names.Seat(State, seat))));
            if (reveal && RoleOf(seat) != null)
                RevealRole(seat, null, teamOnly: false);
        }

        /// <summary><c>$alive.count</c>, <c>$alive.team.&lt;id&gt;</c>, <c>$alive.role.&lt;id&gt;</c>: seats still in the game.</summary>
        private object? AliveReference(string[] parts)
        {
            var alive = Playing.Where(s => !IsOut(s.Id)).Select(s => s.Id).ToList();
            if (parts.Length < 2 || parts[1] == "count")
                return new JValue(alive.Count);
            if (parts.Length < 3)
                return null;
            _secretsRead = true;
            return parts[1] switch
            {
                "team" => new JValue(alive.Count(s => TeamOf(s) == parts[2])),
                "role" => new JValue(alive.Count(s => RoleOf(s) == parts[2])),
                _ => null,
            };
        }

        private string? KnownTeam(string seat, string? viewer)
        {
            if (viewer == null || viewer == seat)
            {
                _secretsRead = true;
                return TeamOf(seat);
            }
            var sight = Sight(seat, viewer);
            return sight?.Team ?? (sight?.Role is { } role ? State.Roles?.List.FirstOrDefault(r => r.Id == role)?.Team : null);
        }

        private string? KnownRole(string seat, string? viewer)
        {
            if (viewer == null || viewer == seat)
            {
                _secretsRead = true;
                return RoleOf(seat);
            }
            return Sight(seat, viewer)?.Role;
        }

        private RoleSight? Sight(string seat, string viewer) =>
            Session != null && Session.Knows.TryGetValue(viewer, out var sights) ? sights.FirstOrDefault(s => s.Seat == seat) : null;
    }
}
