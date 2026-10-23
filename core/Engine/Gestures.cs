using System.Collections.Generic;
using System.Linq;
using Core.Rules;

namespace Core.Engine
{
    /// <summary>
    /// Maps table gestures to rules actions while a game runs, so people play with their hands instead of a button list:
    /// dragging a card out of your hand plays it (the legal action that takes it as a parameter), dragging from a pile
    /// draws (the legal parameterless action whose effects take from that pile). Piles and hands the rules use are
    /// "owned" by the rules: a gesture on them that matches nothing legal is refused instead of moving freely.
    /// </summary>
    public static class Gestures
    {
        /// <summary>Legal commands for <paramref name="seat"/> that use <paramref name="objectId"/> as a parameter value.</summary>
        public static List<PerformActionCommand> Plays(GameEngine game, string seat, string objectId)
        {
            if (!Running(game))
                return new List<PerformActionCommand>();
            return game.LegalActions(seat)
                .SelectMany(a => a.Choices.Where(c => c.Values.Contains(objectId)).Select(c => Command(seat, a.ActionId, c)))
                .ToList();
        }

        /// <summary>Legal parameterless commands for <paramref name="seat"/> whose effects take from <paramref name="containerId"/> (or the zone it lies in).</summary>
        public static List<PerformActionCommand> Draws(GameEngine game, string seat, string containerId)
        {
            if (!Running(game))
                return new List<PerformActionCommand>();
            var sources = SourcesOf(game, containerId);
            var legal = game.LegalActions(seat).ToDictionary(a => a.ActionId);
            return game.State.Actions
                .Where(a => a.Params.Count == 0 && legal.ContainsKey(a.Id) && TakesFrom(game, seat, a.Effects, sources, drawsOnly: true))
                .Select(a => Command(seat, a.Id, new Dictionary<string, string>()))
                .ToList();
        }

        /// <summary>True when some action (legal now or not) takes from <paramref name="containerId"/>: free takes from it would bypass the rules.</summary>
        public static bool RulesTakeFrom(GameEngine game, string seat, string containerId)
        {
            if (!Running(game))
                return false;
            var sources = SourcesOf(game, containerId);
            return game.State.Actions.Any(a => TakesFrom(game, seat, a.Effects, sources, drawsOnly: false));
        }

        /// <summary>True when the game plays objects through action parameters (cards from a hand): hand cards then move only through the rules.</summary>
        public static bool PlaysObjects(GameEngine game) =>
            Running(game) && game.State.Actions.Any(a => a.Params.Any(p => p.Type == ParamType.Object));

        private static bool Running(GameEngine game) => game.Session is { Started: true, Over: false };

        private static PerformActionCommand Command(string seat, string actionId, Dictionary<string, string> choice) =>
            new PerformActionCommand { Actor = seat, ActionId = actionId, Params = new Dictionary<string, string>(choice) };

        /// <summary>The container itself and every zone it lies in.</summary>
        private static HashSet<string> SourcesOf(GameEngine game, string containerId)
        {
            var sources = new HashSet<string>(game.Table.ZonesOf(containerId)) { containerId };
            return sources;
        }

        /// <summary>
        /// Whether the effects take from one of <paramref name="sources"/>. With <paramref name="drawsOnly"/>, only moves of
        /// the top or bottom card count, so a pile that an action merely gathers back (UNO's discard, reshuffled with
        /// <c>allButTop</c>) does not draw.
        /// </summary>
        private static bool TakesFrom(GameEngine game, string seat, IEnumerable<Effect> effects, HashSet<string> sources, bool drawsOnly)
        {
            foreach (var effect in effects)
            {
                switch (effect)
                {
                    case MoveEffect { From: { } from } move when (!drawsOnly || move.Take == "top" || move.Take == "bottom") &&
                                                                 game.TargetId(from, seat) is { } id && sources.Contains(id):
                    case DealEffect deal when game.TargetId(deal.From, seat) is { } dealId && sources.Contains(dealId):
                        return true;
                    case IfEffect branch when TakesFrom(game, seat, branch.Then, sources, drawsOnly) || TakesFrom(game, seat, branch.Else, sources, drawsOnly):
                    case ForEachEffect loop when TakesFrom(game, seat, loop.Do, sources, drawsOnly):
                        return true;
                }
            }
            return false;
        }
    }
}
