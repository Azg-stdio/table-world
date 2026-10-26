using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>An object a player is carrying, with its live pose.</summary>
    public sealed class HeldPose
    {
        public string Id { get; set; } = "";
        public Vec3 Position { get; set; }
        public Vec3 Rotation { get; set; }
    }

    /// <summary>
    /// What one player is doing right now: where their pointer is and what they carry (G1–G3). Presence is not table
    /// state: frames are sent often over an unreliable channel, the newest wins, and a silent player fades out. The table
    /// only changes through commands (a drop still ends in a move command).
    /// </summary>
    public sealed class PresenceFrame
    {
        public string Seat { get; set; } = "";
        /// <summary>Increases with every frame a player sends; older frames that arrive late are ignored.</summary>
        public long Sequence { get; set; }
        /// <summary>Where the pointer touches the table, or null when it is off the table.</summary>
        public Vec3? Pointer { get; set; }
        /// <summary>The point at the centre of the player's view (T7.3): their chalker turns its head to it. Null when unknown.</summary>
        public Vec3? Gaze { get; set; }
        public List<HeldPose> Held { get; set; } = new List<HeldPose>();
        /// <summary>The player's latest emote (T7.4), by name, or null; it plays when <see cref="EmoteCount"/> changes.</summary>
        public string? Emote { get; set; }
        /// <summary>Counts the player's emotes, so the same emote twice plays twice and a re-sent frame plays nothing.</summary>
        public int EmoteCount { get; set; }
        /// <summary>The object under the player's pointer, or null (the host's scripts hear it: TTS onObjectHover).</summary>
        public string? Hover { get; set; }
    }

    /// <summary>Presence frames on the wire: <c>{ kind: "presence", seat, seq, pointer?, gaze?, emote?: { name, n }, hover?, held: [{ id, p, r }] }</c>.</summary>
    public static class PresenceCodec
    {
        /// <summary>A frame carrying more held objects than this is refused (a TTS player can carry a big group, not the table).</summary>
        public const int MaxHeld = 128;
        /// <summary>Coordinates beyond this many metres are nonsense and refused.</summary>
        public const float MaxCoordinate = 1000f;

        public static JObject Encode(PresenceFrame frame) => new JObject
        {
            ["kind"] = "presence",
            ["seat"] = frame.Seat,
            ["seq"] = frame.Sequence,
            ["pointer"] = frame.Pointer is { } p ? new JArray(p.X, p.Y, p.Z) : null,
            ["gaze"] = frame.Gaze is { } g ? new JArray(g.X, g.Y, g.Z) : null,
            ["emote"] = frame.Emote != null ? new JObject { ["name"] = frame.Emote, ["n"] = frame.EmoteCount } : null,
            ["hover"] = frame.Hover,
            ["held"] = new JArray(frame.Held.Select(h => new JObject
            {
                ["id"] = h.Id,
                ["p"] = new JArray(h.Position.X, h.Position.Y, h.Position.Z),
                ["r"] = new JArray(h.Rotation.X, h.Rotation.Y, h.Rotation.Z),
            })),
        };

        /// <summary>The frame in <paramref name="message"/>, or null when it is not a well-formed presence message.</summary>
        public static PresenceFrame? Decode(JObject message)
        {
            if ((string?)message["kind"] != "presence" || !(message["seat"] is JValue { Type: JTokenType.String } seat))
                return null;
            var frame = new PresenceFrame { Seat = (string)seat!, Sequence = (long?)message["seq"] ?? 0 };
            if (message["pointer"] is JArray pointer)
            {
                if (!(ReadVec(pointer) is { } point))
                    return null;
                frame.Pointer = point;
            }
            if (message["gaze"] is JArray gaze)
            {
                if (!(ReadVec(gaze) is { } point))
                    return null;
                frame.Gaze = point;
            }
            if (message["emote"] is JObject emote)
            {
                var name = emote["name"]?.Type == JTokenType.String ? PlayerInfo.CleanLook((string?)emote["name"]) : "";
                if (name.Length == 0 || emote["n"]?.Type != JTokenType.Integer)
                    return null;
                frame.Emote = name;
                frame.EmoteCount = (int)emote["n"]!;
            }
            if (message["hover"] is JValue { Value: string hovered })
            {
                if (hovered.Length == 0 || hovered.Length > 64)
                    return null;
                frame.Hover = hovered;
            }
            if (message["held"] is JArray held)
            {
                if (held.Count > MaxHeld)
                    return null;
                foreach (var entry in held.OfType<JObject>())
                {
                    var id = (string?)entry["id"];
                    if (string.IsNullOrEmpty(id) || !(entry["p"] is JArray p) || !(entry["r"] is JArray r) ||
                        !(ReadVec(p) is { } position) || !(ReadVec(r) is { } rotation))
                        return null;
                    frame.Held.Add(new HeldPose { Id = id!, Position = position, Rotation = rotation });
                }
            }
            return frame;
        }

        private static Vec3? ReadVec(JArray values)
        {
            if (values.Count != 3)
                return null;
            var v = new float[3];
            for (var i = 0; i < 3; i++)
            {
                if (values[i].Type != JTokenType.Float && values[i].Type != JTokenType.Integer)
                    return null;
                v[i] = (float)values[i];
                if (float.IsNaN(v[i]) || float.IsInfinity(v[i]) || Math.Abs(v[i]) > MaxCoordinate)
                    return null;
            }
            return new Vec3(v[0], v[1], v[2]);
        }
    }

    /// <summary>
    /// The latest presence of every other player: newer frames replace older ones, late frames are dropped, and a
    /// player silent for <see cref="TimeoutSeconds"/> (gone, or their connection stalled) no longer holds anything.
    /// </summary>
    public sealed class PresenceBoard
    {
        public const double TimeoutSeconds = 1.5;

        private readonly Dictionary<string, (PresenceFrame Frame, double At)> _peers = new Dictionary<string, (PresenceFrame, double)>();

        /// <summary>Takes a frame from <paramref name="peer"/>; false when it is older than the one already known.</summary>
        public bool Update(string peer, PresenceFrame frame, double now)
        {
            if (_peers.TryGetValue(peer, out var known) && frame.Sequence <= known.Frame.Sequence)
                return false;
            _peers[peer] = (frame, now);
            return true;
        }

        public void Remove(string peer) => _peers.Remove(peer);

        /// <summary>Every player heard from within the timeout.</summary>
        public IEnumerable<(string Peer, PresenceFrame Frame)> Active(double now) =>
            _peers.Where(p => now - p.Value.At <= TimeoutSeconds).Select(p => (p.Key, p.Value.Frame));

        /// <summary>The seat of the player carrying <paramref name="objectId"/>, or null.</summary>
        public string? HolderOf(string objectId, double now) =>
            Active(now).Where(a => a.Frame.Held.Any(h => h.Id == objectId)).Select(a => a.Frame.Seat).FirstOrDefault();
    }
}
