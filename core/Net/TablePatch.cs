using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>
    /// What a host-run command changed (ADR 0012, T11.2): the objects its events name, with their contents and their
    /// place in the object list; the seat's view of the session; the blind round. Peers apply it instead of replaying the
    /// command. A change that reaches further (lighting, zones, scripts) has no patch: the whole seat view is sent.
    /// </summary>
    public static class TablePatch
    {
        /// <summary>The patch for one seat, or null when only the whole table will do.</summary>
        public static JObject? Build(TableState state, CommandResult result, string? seat, TableEngine? table = null)
        {
            if (result.ScriptsRan || result.StateRestored)
                return null;
            var ids = new HashSet<string>();
            foreach (var e in result.Events)
                switch (e)
                {
                    case GameMessage _:
                    case PrivateMessage _:
                    case AutoMoved _:
                    case SoundCue _:
                        break;
                    case LightingChanged _:
                    case TableSettingsChanged _:
                    case MusicChanged _:
                    case NotebookChanged _:
                    case DrawingsChanged _:
                    case ScriptUiChanged _:
                    case AssetAdded _:
                    case ZoneChanged _:
                        return null;
                    default:
                        if (e.ObjectId.Length == 0)
                            return null;
                        ids.Add(e.ObjectId);
                        break;
                }
            // A container's contents come along: a bag's shuffle reorders them. Each object as this seat may know it.
            var hidden = SeatView.Hidden(state, seat, table);
            // Whoever searches a face-down deck sees what is in it (TTS too); the others still see backs.
            foreach (var searched in result.Events.OfType<Searched>().Where(e => seat != null && e.Actor == seat))
                hidden.Decks.Remove(searched.ObjectId);
            var objects = new JArray();
            for (var i = 0; i < state.Objects.Count; i++)
            {
                var obj = state.Objects[i];
                if (ids.Contains(obj.Id) || obj.Container != null && ids.Contains(obj.Container))
                    objects.Add(new JObject { ["index"] = i, ["object"] = SeatView.Object(obj, hidden) });
            }
            var present = new HashSet<string>(state.Objects.Select(o => o.Id));
            return new JObject
            {
                ["objects"] = objects,
                ["removed"] = new JArray(ids.Where(id => !present.Contains(id)).OrderBy(id => id)),
                ["session"] = SeatView.Session(state, seat),
                ["blind"] = state.Blind == null ? null : TableJson.ToToken(state.Blind),
            };
        }

        /// <summary>Applies <paramref name="patch"/> to <paramref name="game"/>; returns the ids of the objects it changed.</summary>
        public static HashSet<string> Apply(GameEngine game, JObject patch)
        {
            var state = game.State;
            var changed = new HashSet<string>();
            foreach (var id in (patch["removed"] as JArray ?? new JArray()).Select(t => (string)t!))
            {
                state.Objects.RemoveAll(o => o.Id == id);
                changed.Add(id);
            }
            var entries = (patch["objects"] as JArray ?? new JArray()).OfType<JObject>().OrderBy(e => (int)e["index"]!);
            foreach (var entry in entries)
            {
                var obj = (TableObject)TableJson.FromToken(entry["object"]!, typeof(TableObject))!;
                state.Objects.RemoveAll(o => o.Id == obj.Id);
                state.Objects.Insert(System.Math.Min((int)entry["index"]!, state.Objects.Count), obj);
                changed.Add(obj.Id);
            }
            state.Session = patch["session"] is JObject session ? (Session?)TableJson.FromToken(session, typeof(Session)) : null;
            state.Blind = patch["blind"] is JObject blind ? (BlindRound?)TableJson.FromToken(blind, typeof(BlindRound)) : null;
            game.Table.Reindex();
            return changed;
        }
    }
}
