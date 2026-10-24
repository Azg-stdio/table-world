using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Tts;
using MoonSharp.Interpreter;

namespace Core.Scripting
{
    /// <summary>TTS <c>UI.*</c> (global and <c>obj.UI</c>), <c>Player[color]</c>, and UI events reaching their handlers (ADR 0004).</summary>
    public sealed partial class ScriptHost
    {
        /// <summary>A seat colour's player name, when the game knows it (TTS <c>player.steam_name</c>).</summary>
        public Func<string, string?>? PlayerName { get; set; }

        /// <summary>The <c>UI</c> table for the global UI (<paramref name="objectId"/> null) or an object's.</summary>
        private DynValue UiApi(Script script, string? objectId)
        {
            var t = new Table(script);
            DynValue Fn(Func<CallbackArguments, int, DynValue> body) => DynValue.NewCallback((_, args) =>
                body(args, args.Count > 0 && args[0].Type == DataType.Table && args[0].Table == t ? 1 : 0));
            DynValue At(CallbackArguments a, int self, int i) => a.Count > i + self ? a[i + self] : DynValue.Nil;
            string? Str(CallbackArguments a, int self, int i) => At(a, self, i).Type == DataType.Nil ? null : UiString(At(a, self, i));
            List<UiNode> Nodes() => objectId == null ? Engine.State.ScriptUi : Engine.Find(objectId)?.ScriptUi ?? new List<UiNode>();
            DynValue Set(string? id, Dictionary<string, string?> attributes, bool setValue = false, string? value = null) =>
                id == null ? DynValue.False : DynValue.NewBoolean(Issue(new SetUiAttributesCommand
                {
                    ObjectId = objectId, ElementId = id, Attributes = attributes, SetValue = setValue, Value = value,
                }));
            DynValue Replace(List<UiNode> nodes) => DynValue.NewBoolean(Issue(new SetScriptUiCommand { ObjectId = objectId, Nodes = nodes }));

            t["setAttribute"] = Fn((a, s) =>
            {
                var name = Str(a, s, 1);
                return name == null ? DynValue.False : Set(Str(a, s, 0), new Dictionary<string, string?> { [XmlUi.CanonicalName(name)] = Str(a, s, 2) ?? "" });
            });
            t["setAttributes"] = Fn((a, s) =>
            {
                if (At(a, s, 1).Type != DataType.Table)
                    return DynValue.False;
                var attributes = new Dictionary<string, string?>();
                foreach (var pair in At(a, s, 1).Table.Pairs)
                    if (pair.Key.Type == DataType.String)
                        attributes[XmlUi.CanonicalName(pair.Key.String)] = UiString(pair.Value);
                return Set(Str(a, s, 0), attributes);
            });
            t["getAttribute"] = Fn((a, s) =>
            {
                var node = Str(a, s, 0) is { } id ? UiNode.Find(Nodes(), id) : null;
                var name = Str(a, s, 1);
                if (node == null || name == null)
                    return DynValue.Nil;
                name = XmlUi.CanonicalName(name);
                // A Text's or Button's words may be its content rather than a text attribute.
                if (name == "text" && node.Get("text") == null && node.Value != null)
                    return DynValue.NewString(node.Value);
                return node.Get(name) is { } v ? DynValue.NewString(v) : DynValue.Nil;
            });
            t["getAttributes"] = Fn((a, s) =>
            {
                if (!(Str(a, s, 0) is { } id && UiNode.Find(Nodes(), id) is { } node))
                    return DynValue.Nil;
                var attributes = new Table(script);
                foreach (var pair in node.Attributes)
                    attributes[pair.Key] = pair.Value;
                return DynValue.NewTable(attributes);
            });
            t["setValue"] = Fn((a, s) =>
            {
                // The words replace a text attribute too, so the element shows them.
                var id = Str(a, s, 0);
                var node = id != null ? UiNode.Find(Nodes(), id) : null;
                var attributes = new Dictionary<string, string?>();
                if (node?.Get("text") != null)
                    attributes["text"] = null;
                return Set(id, attributes, setValue: true, value: Str(a, s, 1) ?? "");
            });
            t["getValue"] = Fn((a, s) => Str(a, s, 0) is { } id && UiNode.Find(Nodes(), id) is { } node
                ? DynValue.NewString(node.Value ?? node.Get("text") ?? node.Get("value") ?? "")
                : DynValue.Nil);
            t["show"] = Fn((a, s) => Set(Str(a, s, 0), new Dictionary<string, string?> { ["active"] = "True" }));
            t["hide"] = Fn((a, s) => Set(Str(a, s, 0), new Dictionary<string, string?> { ["active"] = "False" }));
            t["setXml"] = Fn((a, s) =>
            {
                List<UiNode> nodes;
                try
                {
                    nodes = XmlUi.Parse(Str(a, s, 0) ?? "");
                }
                catch (System.Xml.XmlException e)
                {
                    Log?.Invoke($"[script] UI.setXml: the XML does not read ({e.Message})");
                    return DynValue.False;
                }
                return Replace(nodes);
            });
            t["getXml"] = Fn((a, s) => DynValue.NewString(XmlUi.Write(Nodes())));
            t["setXmlTable"] = Fn((a, s) => At(a, s, 0).Type == DataType.Table ? Replace(FromXmlTable(At(a, s, 0).Table)) : DynValue.False);
            t["getXmlTable"] = Fn((a, s) => DynValue.NewTable(ToXmlTable(script, Nodes())));
            // Custom UI images come with the table's assets (the importer maps TTS CustomUIAssets); adding them at run time is not supported yet.
            t["setCustomAssets"] = Fn((a, s) => DynValue.True);
            t["getCustomAssets"] = Fn((a, s) =>
            {
                var list = new Table(script);
                var i = 1;
                foreach (var asset in Engine.State.Assets.Where(x => x.Type == AssetType.Image))
                {
                    var entry = new Table(script);
                    entry["name"] = asset.Id;
                    entry["url"] = asset.Path;
                    list[i++] = entry;
                }
                return DynValue.NewTable(list);
            });
            t["loading"] = false;
            return DynValue.NewTable(t);
        }

        /// <summary>A Lua value as XmlUI text: booleans True / False, whole numbers without decimals.</summary>
        private static string UiString(DynValue v) => v.Type switch
        {
            DataType.Boolean => v.Boolean ? "True" : "False",
            DataType.Number => Math.Abs(v.Number - Math.Round(v.Number)) < 1e-9 && Math.Abs(v.Number) < 1e15
                ? ((long)Math.Round(v.Number)).ToString(CultureInfo.InvariantCulture)
                : v.Number.ToString("R", CultureInfo.InvariantCulture),
            DataType.Nil => "",
            _ => v.CastToString() ?? "",
        };

        /// <summary>TTS's table form of the UI: <c>{ tag, attributes, value, children }</c>.</summary>
        private static Table ToXmlTable(Script script, List<UiNode> nodes)
        {
            var list = new Table(script);
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var e = new Table(script);
                e["tag"] = node.Tag;
                var attributes = new Table(script);
                foreach (var pair in node.Attributes)
                    attributes[pair.Key] = pair.Value;
                e["attributes"] = attributes;
                if (node.Value != null)
                    e["value"] = node.Value;
                e["children"] = ToXmlTable(script, node.Children);
                list[i + 1] = e;
            }
            return list;
        }

        private static List<UiNode> FromXmlTable(Table list)
        {
            var nodes = new List<UiNode>();
            for (var i = 1; list.Get(i).Type == DataType.Table; i++)
            {
                var e = list.Get(i).Table;
                var node = new UiNode { Tag = e.Get("tag").CastToString() ?? "Panel" };
                if (e.Get("attributes").Type == DataType.Table)
                    foreach (var pair in e.Get("attributes").Table.Pairs)
                        if (pair.Key.Type == DataType.String)
                        {
                            var name = XmlUi.CanonicalName(pair.Key.String);
                            node.Attributes[name] = UiNode.Normalize(name, UiString(pair.Value));
                        }
                if (e.Get("value").Type != DataType.Nil)
                    node.Value = UiString(e.Get("value"));
                if (e.Get("children").Type == DataType.Table)
                    node.Children = FromXmlTable(e.Get("children").Table);
                nodes.Add(node);
            }
            return nodes;
        }

        // ---- players --------------------------------------------------------------------------------------------------

        /// <summary>TTS's <c>Player</c>: <c>Player[color]</c> / <c>Player.Red</c>, <c>getPlayers()</c>, <c>getAvailableColors()</c>.</summary>
        private DynValue PlayerApi(Script script)
        {
            var t = new Table(script);
            t["getPlayers"] = DynValue.NewCallback((_, a) =>
            {
                var list = new Table(script);
                var i = 1;
                foreach (var seat in Engine.State.Players.Seats)
                    list[i++] = PlayerTable(script, seat.Color);
                return DynValue.NewTable(list);
            });
            t["getAvailableColors"] = DynValue.NewCallback((_, a) =>
            {
                var list = new Table(script);
                var i = 1;
                foreach (var seat in Engine.State.Players.Seats)
                    list[i++] = Capital(seat.Color);
                return DynValue.NewTable(list);
            });
            var meta = new Table(script);
            meta["__index"] = DynValue.NewCallback((_, a) =>
                a.Count > 1 && a[1].Type == DataType.String ? PlayerTable(script, a[1].String) : DynValue.Nil);
            t.MetaTable = meta;
            return DynValue.NewTable(t);
        }

        /// <summary>A TTS player as scripts see it: <c>color</c>, <c>steam_name</c>, <c>seated</c>, <c>getHandObjects()</c>, <c>broadcast()</c>.</summary>
        private DynValue PlayerTable(Script script, string color)
        {
            var seat = Engine.State.Players.Seats.FirstOrDefault(s => string.Equals(s.Color, color, StringComparison.OrdinalIgnoreCase));
            var p = new Table(script);
            p["color"] = Capital(color);
            p["steam_name"] = PlayerName?.Invoke(color.ToLowerInvariant()) ?? Capital(color);
            p["steam_id"] = "";
            p["seated"] = seat != null;
            p["team"] = "None";
            p["host"] = false;
            p["admin"] = false;
            p["promoted"] = false;
            DynValue Say(CallbackArguments a)
            {
                // Arguments may start with the player itself (player:broadcast(...)).
                var i = a.Count > 0 && a[0].Type == DataType.Table && a[0].Table == p ? 1 : 0;
                if (a.Count > i && a[i].CastToString() is { } text && text.Length > 0)
                    Broadcast?.Invoke(text);
                return DynValue.Nil;
            }
            p["broadcast"] = DynValue.NewCallback((_, a) => Say(a));
            p["print"] = DynValue.NewCallback((_, a) => Say(a));
            p["getHandObjects"] = DynValue.NewCallback((_, a) => seat?.HandZone is { } hand
                ? List(script, Engine.ObjectsIn(hand).Select(o => o.Id))
                : DynValue.NewTable(new Table(script)));
            p["getHandCount"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(seat?.HandZone != null ? 1 : 0));
            p["getSelectedObjects"] = DynValue.NewCallback((_, a) => DynValue.NewTable(new Table(script)));
            // Camera and pointer control belong to each player's machine; accepted here without effect.
            foreach (var noOp in new[] { "lookAt", "attachCameraToObject", "setCameraMode", "pingTable", "clearSelectedObjects" })
                p[noOp] = DynValue.NewCallback((_, a) => DynValue.Nil);
            return DynValue.NewTable(p);
        }

        // ---- UI events ------------------------------------------------------------------------------------------------

        /// <summary>
        /// A player used a UI element: its handler (<c>"fn"</c>, <c>"Global/fn"</c>, <c>"guid/fn"</c>, optionally
        /// <c>"fn(arg)"</c>) runs as TTS calls it, <c>fn(player, value, id)</c>; an argument in brackets is the value.
        /// Without an owner, a global UI's handler is in the global script and an object UI's in that object's.
        /// </summary>
        private void UiUsed(UiEventCommand ui, string? color)
        {
            var nodes = ui.ObjectId == null ? Engine.State.ScriptUi : Engine.Find(ui.ObjectId)?.ScriptUi;
            if (nodes == null || !(TableEngine.UiAt(nodes, ui.Path) is { } node) || !(node.Get(ui.Event) is { } handler))
                return;
            var value = ui.Value ?? (ui.Event == "onClick" ? "-1" : "");
            var open = handler.IndexOf('(');
            if (open > 0 && handler.EndsWith(")", StringComparison.Ordinal))
            {
                value = handler.Substring(open + 1, handler.Length - open - 2);
                handler = handler.Substring(0, open);
            }
            Script? target;
            var slash = handler.LastIndexOf('/');
            if (slash > 0)
            {
                var owner = handler.Substring(0, slash);
                handler = handler.Substring(slash + 1);
                target = owner == "Global" ? _global : _objectScripts.TryGetValue(owner, out var named) ? named : null;
            }
            else
                target = ui.ObjectId == null ? _global : _objectScripts.TryGetValue(ui.ObjectId, out var own) ? own : null;
            if (target == null)
                return;
            var player = color != null ? PlayerTable(target, color) : DynValue.Nil;
            CallIfDefined(target, handler.Trim(), player, DynValue.NewString(value), node.Id != null ? DynValue.NewString(node.Id) : DynValue.Nil);
        }
    }
}
