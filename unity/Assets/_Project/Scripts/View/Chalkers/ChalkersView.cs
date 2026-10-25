using System.Collections.Generic;
using System.Linq;
using TableWorld.View.Net;
using TableWorld.View.Voice;
using UnityEngine;

namespace TableWorld.View.Chalkers
{
    /// <summary>
    /// The other players as <see cref="Chalker"/>s (T7.3), each in its seat's colour sitting behind its hand. The head
    /// turns to where that player looks (presence gaze), the pupils follow their pointer, and the mouth opens with their
    /// voice. With "Hear myself" on, a mirror chalker sits across the
    /// table, looks back at this player and talks with their own voice: the whole voice path, tested alone.
    /// </summary>
    public sealed class ChalkersView : MonoBehaviour
    {
        public const string MirrorKey = "\u0001mirror";
        /// <summary>How far behind its hand zone (or the table edge, for the mirror) a chalker sits, in metres.</summary>
        public const float SeatBack = 0.32f;

        [SerializeField] private TableView _table;
        [SerializeField] private NetworkTableSession _session;
        [SerializeField] private VoiceChat _voice;

        private readonly Dictionary<string, Chalker> _chalkers = new Dictionary<string, Chalker>();

        public void Configure(TableView table, NetworkTableSession session, VoiceChat voice)
        {
            _table = table;
            _session = session;
            _voice = voice;
        }

        /// <summary>The chalkers on screen, by seat (<see cref="MirrorKey"/> for the mirror).</summary>
        public IReadOnlyDictionary<string, Chalker> Shown => _chalkers;

        private void LateUpdate()
        {
            if (_table == null || _table.Engine == null)
                return;
            var seen = new HashSet<string>();
            var dt = Time.deltaTime;
            if (_session != null)
                foreach (var (_, frame) in _session.Others)
                {
                    if (!(SeatPoint(frame.Seat) is { } seatPoint))
                        continue;
                    seen.Add(frame.Seat);
                    var chalker = ChalkerFor(frame.Seat, ColorOf(frame.Seat));
                    var position = Behind(seatPoint, SeatBack);
                    var gaze = frame.Gaze is { } g ? new Vector3(g.X, g.Y, g.Z) : (Vector3?)null;
                    var pointer = frame.Pointer is { } p ? new Vector3(p.X, p.Y, p.Z) : (Vector3?)null;
                    chalker.Pose(position, null, gaze ?? pointer ?? Vector3.zero, pointer ?? gaze, _voice != null ? _voice.LevelOf(frame.Seat) : 0f, dt);
                    Carry(chalker, frame.Seat);
                }

            var camera = Camera.main;
            if (VoiceChat.HearMyself && camera != null)
            {
                seen.Add(MirrorKey);
                var chalker = ChalkerFor(MirrorKey, ColorOf(_table.LocalSeat));
                var from = camera.transform.position;
                var across = new Vector3(-from.x, 0f, -from.z);
                if (across.sqrMagnitude < 1e-4f)
                    across = Vector3.forward;
                var interaction = _table.GetComponent<TableInteraction>();
                var pointer = interaction != null ? interaction.PointerOnTable : null;
                // It looks back at you, and its eyes follow your pointer.
                chalker.Pose(EdgePoint(across.normalized) + across.normalized * SeatBack, null, from, pointer ?? from, _voice != null ? _voice.LevelOf(VoiceChat.SelfSeat) : 0f, dt);
                Carry(chalker, VoiceChat.SelfSeat);
            }

            foreach (var key in _chalkers.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _chalkers[key].Destroy();
                _chalkers.Remove(key);
            }
        }

        /// <summary>The voice plays from the chalker's mouth.</summary>
        private void Carry(Chalker chalker, string seat)
        {
            var speaker = _voice != null ? _voice.SpeakerOf(seat) : null;
            if (speaker != null)
                speaker.transform.position = chalker.Mouth;
        }

        private Chalker ChalkerFor(string key, Color color)
        {
            if (!_chalkers.TryGetValue(key, out var chalker))
            {
                chalker = new Chalker(key, transform, color);
                _chalkers[key] = chalker;
            }
            return chalker;
        }

        private Color ColorOf(string seat) =>
            ZoneViewFactory.SeatColor(_table.Engine.State.Players.Seats.FirstOrDefault(s => s.Id == seat)?.Color ?? "white");

        private Vector3? SeatPoint(string seat)
        {
            var state = _table.Engine.State;
            var hand = state.Players.Seats.FirstOrDefault(s => s.Id == seat)?.HandZone;
            var zone = hand == null ? null : state.Zones.FirstOrDefault(z => z.Id == hand);
            return zone?.Transform.Position.ToUnity();
        }

        /// <summary>A point moved <paramref name="distance"/> further from the table's centre, on the table top.</summary>
        private static Vector3 Behind(Vector3 point, float distance)
        {
            var flat = new Vector3(point.x, 0f, point.z);
            var outward = flat.sqrMagnitude < 1e-4f ? Vector3.back : flat.normalized;
            return flat + outward * distance;
        }

        /// <summary>Where a ray from the centre along <paramref name="direction"/> leaves the table.</summary>
        private static Vector3 EdgePoint(Vector3 direction)
        {
            var half = TableGeometry.Size / 2f;
            var tx = Mathf.Abs(direction.x) < 1e-4f ? float.MaxValue : half.x / Mathf.Abs(direction.x);
            var tz = Mathf.Abs(direction.z) < 1e-4f ? float.MaxValue : half.y / Mathf.Abs(direction.z);
            return direction * Mathf.Min(tx, tz);
        }

        private void OnDestroy()
        {
            foreach (var chalker in _chalkers.Values)
                chalker.Destroy();
            _chalkers.Clear();
        }
    }
}
