using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using TableWorld.View.Net;
using TableWorld.View.Voice;
using UnityEngine;

namespace TableWorld.View.Chalkers
{
    /// <summary>
    /// The other players as <see cref="Chalker"/>s (T7.3), each in its seat's colour sitting on its seat's
    /// <see cref="ChalkerChair"/> at the table's edge (T11.10); seats nobody plays keep an empty chair. The head
    /// turns to where that player looks (presence gaze), the pupils follow their pointer, and the mouth opens with their
    /// voice. With "Hear myself" on, a mirror chalker sits across the
    /// table, looks back at this player and talks with their own voice: the whole voice path, tested alone.
    /// <para>
    /// In the karaoke room (T12.6) everyone sits on sofas along the table's sides instead of chairs, and the
    /// <see cref="Singer"/> holds a microphone under the room's stage light.
    /// </para>
    /// </summary>
    public sealed class ChalkersView : MonoBehaviour
    {
        public const string MirrorKey = "\u0001mirror";
        /// <summary>How far from the table's edge a chalker's origin (and its chair's) sits, in metres.</summary>
        public const float SeatDistance = 0.22f;
        /// <summary>The closest a seat comes to a table corner along its edge, so neighbouring chairs fit.</summary>
        public const float CornerClearance = 0.25f;

        [SerializeField] private TableView _table;
        [SerializeField] private NetworkTableSession _session;
        [SerializeField] private VoiceChat _voice;

        private readonly Dictionary<string, Chalker> _chalkers = new Dictionary<string, Chalker>();
        private readonly Dictionary<string, ChalkerChair> _chairs = new Dictionary<string, ChalkerChair>();
        /// <summary>The last emote count seen per seat: an emote plays when it changes.</summary>
        private readonly Dictionary<string, int> _emoteCounts = new Dictionary<string, int>();
        private NetworkTableSession _subscribed;
        private EnvironmentView _environment;
        /// <summary>The karaoke room's sofas, by the side of the table they stand along.</summary>
        private readonly Dictionary<int, ChalkerSofa> _sofas = new Dictionary<int, ChalkerSofa>();
        private readonly Dictionary<int, (Vector3 Centre, Vector3 Facing, int Places)> _sofaRows = new Dictionary<int, (Vector3, Vector3, int)>();
        private Dictionary<string, (Vector3 Position, Vector3 Facing)> _sofaPlaces;
        private int _sofaFrame = -1;

        /// <summary>
        /// The seat singing (karaoke): its chalker holds the microphone and the stage light is on it. Set, it wins; unset, a
        /// game running in the karaoke room makes whoever's turn it is the singer (until sing mode, T12.5). Null: nobody.
        /// </summary>
        public string Singer
        {
            get => _singer ?? (Karaoke && _table.Game?.Session is { Started: true, Over: false } session ? session.CurrentSeat : null);
            set => _singer = value;
        }

        private string _singer;

        /// <summary>The karaoke room is up: sofas instead of chairs.</summary>
        public bool Karaoke => _table != null && _table.Engine?.State.Environment is { Room: RoomPreset.Karaoke, RoomAsset: null } && !TableGeometry.Hidden;

        /// <summary>The sofas on screen, by table side (0 near, 1 right, 2 far, 3 left).</summary>
        public IReadOnlyDictionary<int, ChalkerSofa> Sofas => _sofas;

        public void Configure(TableView table, NetworkTableSession session, VoiceChat voice)
        {
            _table = table;
            _session = session;
            _voice = voice;
        }

        /// <summary>The chalkers on screen, by seat (<see cref="MirrorKey"/> for the mirror).</summary>
        public IReadOnlyDictionary<string, Chalker> Shown => _chalkers;

        /// <summary>The chairs on screen, by seat (<see cref="MirrorKey"/> for the mirror's): every seat but your own.</summary>
        public IReadOnlyDictionary<string, ChalkerChair> Chairs => _chairs;

        private void LateUpdate()
        {
            if (_table == null || _table.Engine == null)
                return;
            if (_session != null && _subscribed != _session)
            {
                _session.LocalEmoted += OnLocalEmote;
                _subscribed = _session;
            }
            var seen = new HashSet<string>();
            var seated = new HashSet<string>();
            var dt = Time.deltaTime;
            // A chair at every seat but the one this player sits in (the camera is there), played or not.
            var karaoke = Karaoke;
            if (karaoke)
                BuildSofas();
            else
                ClearSofas();
            if (!TableGeometry.Hidden && !karaoke)
                foreach (var seat in _table.Engine.State.Players.Seats)
                    if (seat.Id != _table.LocalSeat && Place(seat.Id) is { } place)
                    {
                        seated.Add(seat.Id);
                        ChairFor(seat.Id).Place(place.Position, place.Facing);
                    }
            if (_session != null)
                foreach (var (_, frame) in _session.Others)
                {
                    if (!(Place(frame.Seat) is { } place))
                        continue;
                    seen.Add(frame.Seat);
                    var chalker = ChalkerFor(frame.Seat, ColorOf(frame.Seat));
                    chalker.Personality = PersonalityOf(_session.LookAtSeat(frame.Seat));
                    // Eyes closed: asleep as far as this player can tell (a sleeper's screen is dark anyway).
                    chalker.EyesClosed = _table.Game is { EyesClosedPhase: true } game && !game.IsAwake(frame.Seat, _table.LocalSeat);
                    chalker.Microphone = Singer != null && Singer == frame.Seat;
                    // A new count is a new emote; the first frame seen only records it (no replay of an old one).
                    if (_emoteCounts.TryGetValue(frame.Seat, out var count) && count != frame.EmoteCount && Enum.TryParse<ChalkerEmote>(frame.Emote, out var emote))
                        chalker.Play(emote);
                    _emoteCounts[frame.Seat] = frame.EmoteCount;
                    var gaze = frame.Gaze is { } g ? new Vector3(g.X, g.Y, g.Z) : (Vector3?)null;
                    var pointer = frame.Pointer is { } p ? new Vector3(p.X, p.Y, p.Z) : (Vector3?)null;
                    chalker.Pose(place.Position, place.Facing, gaze ?? pointer ?? Vector3.zero, pointer ?? gaze, _voice != null ? _voice.LevelOf(frame.Seat) : 0f, dt);
                    Carry(chalker, frame.Seat);
                }

            var camera = Camera.main;
            if (VoiceChat.HearMyself && camera != null)
            {
                seen.Add(MirrorKey);
                var chalker = ChalkerFor(MirrorKey, ColorOf(_table.LocalSeat));
                chalker.Personality = PersonalityOf(_session != null ? _session.LocalLook : "");
                chalker.EyesClosed = _table.Game is { EyesClosedPhase: true } night && _table.LocalSeat != null && !night.IsAwake(_table.LocalSeat, _table.LocalSeat);
                chalker.Microphone = Singer != null && Singer == _table.LocalSeat;
                var from = camera.transform.position;
                var across = new Vector3(-from.x, 0f, -from.z);
                if (across.sqrMagnitude < 1e-4f)
                    across = Vector3.forward;
                var interaction = _table.GetComponent<TableInteraction>();
                var pointer = interaction != null ? interaction.PointerOnTable : null;
                // It looks back at you, and its eyes follow your pointer.
                var mirrorPlace = AtEdge(across.normalized, null);
                // A seat's chair already there: the mirror sits in it rather than in a second chair on top.
                var taken = _chairs.Where(p => p.Key != MirrorKey && seated.Contains(p.Key))
                    .Select(p => p.Value.Root.transform)
                    .FirstOrDefault(t => Vector3.Distance(t.position, mirrorPlace.Position) < ChalkerChair.SeatWidth);
                if (karaoke)
                {
                    // On a sofa: the nearest place someone else would sit.
                    var places = SofaPlaces().Where(p => p.Key != _table.LocalSeat).Select(p => p.Value).ToList();
                    if (places.Count > 0)
                        mirrorPlace = places.OrderBy(p => Vector3.Distance(p.Position, mirrorPlace.Position)).First();
                }
                else if (taken != null)
                    mirrorPlace = (taken.position, taken.forward);
                else if (!TableGeometry.Hidden)
                {
                    seated.Add(MirrorKey);
                    ChairFor(MirrorKey).Place(mirrorPlace.Position, mirrorPlace.Facing);
                }
                chalker.Pose(mirrorPlace.Position, mirrorPlace.Facing, from, pointer ?? from, _voice != null ? _voice.LevelOf(VoiceChat.SelfSeat) : 0f, dt);
                Carry(chalker, VoiceChat.SelfSeat);
            }

            foreach (var key in _chalkers.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _chalkers[key].Destroy();
                _chalkers.Remove(key);
                _emoteCounts.Remove(key);
            }
            foreach (var key in _chairs.Keys.Where(k => !seated.Contains(k)).ToList())
            {
                _chairs[key].Destroy();
                _chairs.Remove(key);
            }
            AimStage();
        }

        /// <summary>The room's stage light on the singer's place, or off.</summary>
        private void AimStage()
        {
            if (_environment == null)
                _environment = FindAnyObjectByType<EnvironmentView>();
            if (_environment == null)
                return;
            // The singer's chalker where it sits (yours, with Hear myself, sits across from you), else their place.
            var key = Singer != null && Singer == _table.LocalSeat ? MirrorKey : Singer;
            if (key != null && _chalkers.TryGetValue(key, out var shown))
            {
                _environment.AimStage(shown.Root.transform.position, shown.Root.transform.forward);
                return;
            }
            var place = Singer != null ? Place(Singer) : null;
            _environment.AimStage(place?.Position, place?.Facing);
        }

        /// <summary>
        /// Where everyone sits in the karaoke room: each seat goes to the side of the table its chair would be on, and the
        /// seats on a side sit next to each other on one sofa, in the order they would sit, kept within the side when
        /// they fit. The far side faces away from the room's screen: its seats move to the left and right sofas while
        /// those have room (<see cref="SideRoom"/>), so nobody sings with their back to the screen or blocks it. Worked
        /// out once a frame.
        /// </summary>
        private Dictionary<string, (Vector3 Position, Vector3 Facing)> SofaPlaces()
        {
            if (_sofaPlaces != null && _sofaFrame == Time.frameCount)
                return _sofaPlaces;
            _sofaFrame = Time.frameCount;
            _sofaPlaces = new Dictionary<string, (Vector3, Vector3)>();
            _sofaRows.Clear();
            var half = TableGeometry.Size / 2f;
            var bySide = new Dictionary<int, List<(string Seat, Vector3 At)>>();
            foreach (var seat in _table.Engine.State.Players.Seats)
            {
                if (!(ChairPlace(seat.Id) is { } place))
                    continue;
                var p = place.Position;
                var side = Mathf.Abs(p.x) / half.x > Mathf.Abs(p.z) / half.y ? (p.x > 0f ? 1 : 3) : (p.z > 0f ? 2 : 0);
                if (!bySide.TryGetValue(side, out var list))
                    bySide[side] = list = new List<(string, Vector3)>();
                list.Add((seat.Id, p));
            }
            if (bySide.TryGetValue(2, out var far))
            {
                foreach (var seat in far.OrderBy(s => Mathf.Abs(s.At.x)).ToList())
                {
                    var to = seat.At.x >= 0f ? 1 : 3;
                    var other = to == 1 ? 3 : 1;
                    // The sides first, then the near side, which faces the screen too.
                    var target = Count(to) < SideRoom(half.y) ? to : Count(other) < SideRoom(half.y) ? other : Count(0) < SideRoom(half.x) ? 0 : -1;
                    if (target < 0)
                        continue;
                    far.Remove(seat);
                    if (!bySide.TryGetValue(target, out var list))
                        bySide[target] = list = new List<(string, Vector3)>();
                    // At the far end of a side, where it meets the far side; on the near side, across from where it was.
                    list.Add((seat.Seat, new Vector3(seat.At.x, 0f, target == 0 ? -half.y : half.y)));
                }
                if (far.Count == 0)
                    bySide.Remove(2);
            }
            int Count(int side) => bySide.TryGetValue(side, out var list) ? list.Count : 0;
            foreach (var (side, list) in bySide)
            {
                var outward = side switch { 0 => Vector3.back, 1 => Vector3.right, 2 => Vector3.forward, _ => Vector3.left };
                var facing = -outward;
                var right = Vector3.Cross(Vector3.up, facing);
                var sideHalf = side % 2 == 0 ? half.x : half.y;
                var across = side % 2 == 0 ? half.y : half.x;
                var sorted = list.OrderBy(s => Vector3.Dot(s.At, right)).ToList();
                var width = sorted.Count * ChalkerSofa.PlaceWidth;
                var room = Mathf.Max(0f, sideHalf - width / 2f);
                var centre = Mathf.Clamp(sorted.Average(s => Vector3.Dot(s.At, right)), -room, room);
                var line = outward * (across + SeatDistance);
                for (var i = 0; i < sorted.Count; i++)
                    _sofaPlaces[sorted[i].Seat] = (line + right * (centre - width / 2f + ChalkerSofa.PlaceWidth * (i + 0.5f)), facing);
                _sofaRows[side] = (line + right * centre, facing, sorted.Count);
            }
            return _sofaPlaces;
        }

        /// <summary>How many sit on a side <paramref name="half"/> long each way from its middle without the sofa passing its ends.</summary>
        public static int SideRoom(float half) => Mathf.Max(1, Mathf.FloorToInt(2f * half / ChalkerSofa.PlaceWidth));

        /// <summary>A sofa along every side someone sits on, sized for them (the player's own place included).</summary>
        private void BuildSofas()
        {
            SofaPlaces();
            foreach (var side in _sofas.Keys.Where(k => !_sofaRows.ContainsKey(k) || _sofas[k].Places != _sofaRows[k].Places).ToList())
            {
                _sofas[side].Destroy();
                _sofas.Remove(side);
            }
            foreach (var (side, row) in _sofaRows)
            {
                if (!_sofas.TryGetValue(side, out var sofa))
                    _sofas[side] = sofa = new ChalkerSofa(side.ToString(), transform, row.Places);
                sofa.Place(row.Centre, row.Facing);
            }
        }

        private void ClearSofas()
        {
            foreach (var sofa in _sofas.Values)
                sofa.Destroy();
            _sofas.Clear();
        }

        /// <summary>How close to the centre of the view a face must be to count as looked at, in degrees.</summary>
        public const float LookAngle = 7f;

        /// <summary>
        /// The seat whose chalker's face the centre of <paramref name="camera"/>'s view rests on (ADR 0012 §6: a wink goes
        /// to whoever you look at), or null. The nearest within <see cref="LookAngle"/> wins.
        /// </summary>
        public string LookedAt(Camera camera)
        {
            if (camera == null)
                return null;
            string best = null;
            var bestAngle = LookAngle;
            foreach (var (key, chalker) in _chalkers)
            {
                if (key == MirrorKey)
                    continue;
                var to = chalker.Mouth + Vector3.up * 0.05f - camera.transform.position;
                var angle = Vector3.Angle(camera.transform.forward, to);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = key;
                }
            }
            return best;
        }

        /// <summary>This player's own emote shows on the mirror chalker (Hear myself), the only chalker of yours you see.</summary>
        private void OnLocalEmote(string name)
        {
            if (_chalkers.TryGetValue(MirrorKey, out var mirror) && Enum.TryParse<ChalkerEmote>(name, out var emote))
                mirror.Play(emote);
        }

        public static ChalkerPersonality PersonalityOf(string look) =>
            Enum.TryParse<ChalkerPersonality>(look, out var personality) && Enum.IsDefined(typeof(ChalkerPersonality), personality) ? personality : ChalkerPersonality.Cheerful;

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

        private ChalkerChair ChairFor(string key)
        {
            if (!_chairs.TryGetValue(key, out var chair))
            {
                chair = new ChalkerChair(key, transform);
                _chairs[key] = chair;
            }
            return chair;
        }

        private Color ColorOf(string seat) =>
            ZoneViewFactory.SeatColor(_table.Engine.State.Players.Seats.FirstOrDefault(s => s.Id == seat)?.Color ?? "white");

        /// <summary>
        /// Where a seat's chalker and chair go: on the table edge its hand zone is nearest to, level with the hand, and
        /// <see cref="SeatDistance"/> out from it, facing in. A seat without a hand zone takes its share of the way round
        /// the table, in seat order, starting from the near side.
        /// </summary>
        public (Vector3 Position, Vector3 Facing)? Place(string seat)
        {
            if (Karaoke)
                return SofaPlaces().TryGetValue(seat, out var onSofa) ? onSofa : ((Vector3, Vector3)?)null;
            return ChairPlace(seat);
        }

        /// <summary>Where a seat's chair goes (<see cref="Place"/> outside the karaoke room).</summary>
        private (Vector3 Position, Vector3 Facing)? ChairPlace(string seat)
        {
            var state = _table.Engine.State;
            var index = state.Players.Seats.FindIndex(s => s.Id == seat);
            if (index < 0)
                return null;
            var hand = state.Players.Seats[index].HandZone;
            var zone = hand == null ? null : state.Zones.FirstOrDefault(z => z.Id == hand);
            if (zone != null)
            {
                var point = zone.Transform.Position.ToUnity();
                var flat = new Vector3(point.x, 0f, point.z);
                if (flat.sqrMagnitude > 1e-4f)
                    return AtEdge(flat.normalized, flat);
            }
            return AroundTable((float)index / state.Players.Seats.Count);
        }

        /// <summary>
        /// A place <paramref name="share"/> (0 to 1) of the way round the table, starting at the middle of the near side
        /// and going right: along a path <see cref="SeatDistance"/> out from the edge with rounded corners, so evenly spread
        /// seats never crowd a corner (ten fit round the default table) and corner seats face it on the slant.
        /// </summary>
        public static (Vector3 Position, Vector3 Facing) AroundTable(float share)
        {
            var half = TableGeometry.Size / 2f;
            var corner = Mathf.PI * SeatDistance / 2f;
            // Near side, its right corner, right side, far corner, far side, far-left corner, left side, near-left corner.
            var lengths = new[] { 2f * half.x, corner, 2f * half.y, corner, 2f * half.x, corner, 2f * half.y, corner };
            var total = lengths.Sum();
            var s = Mathf.Repeat(half.x + share * total, total);
            var piece = 0;
            while (piece < lengths.Length - 1 && s > lengths[piece])
                s -= lengths[piece++];
            Vector2 outward, point;
            if (piece % 2 == 0)
            {
                var side = piece / 2;
                outward = side switch { 0 => Vector2.down, 1 => Vector2.right, 2 => Vector2.up, _ => Vector2.left };
                var start = side switch
                {
                    0 => new Vector2(-half.x, -half.y),
                    1 => new Vector2(half.x, -half.y),
                    2 => new Vector2(half.x, half.y),
                    _ => new Vector2(-half.x, half.y),
                };
                var along = new Vector2(-outward.y, outward.x);
                point = start + along * s + outward * SeatDistance;
            }
            else
            {
                var side = piece / 2;
                var centre = side switch
                {
                    0 => new Vector2(half.x, -half.y),
                    1 => new Vector2(half.x, half.y),
                    2 => new Vector2(-half.x, half.y),
                    _ => new Vector2(-half.x, -half.y),
                };
                var angle = (-90f + 90f * side) * Mathf.Deg2Rad + s / SeatDistance;
                outward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                point = centre + outward * SeatDistance;
            }
            return (new Vector3(point.x, 0f, point.y), new Vector3(-outward.x, 0f, -outward.y));
        }

        /// <summary>
        /// A place on the edge that a ray from the centre along <paramref name="direction"/> leaves the table by; along
        /// that edge it lines up with <paramref name="near"/> when given, kept clear of the corners.
        /// </summary>
        public static (Vector3 Position, Vector3 Facing) AtEdge(Vector3 direction, Vector3? near)
        {
            var half = TableGeometry.Size / 2f;
            var tx = Mathf.Abs(direction.x) < 1e-4f ? float.MaxValue : half.x / Mathf.Abs(direction.x);
            var tz = Mathf.Abs(direction.z) < 1e-4f ? float.MaxValue : half.y / Mathf.Abs(direction.z);
            var along = near ?? direction * Mathf.Min(tx, tz);
            if (tx < tz)
            {
                var outward = new Vector3(Mathf.Sign(direction.x), 0f, 0f);
                var room = Mathf.Max(0f, half.y - CornerClearance);
                return (new Vector3(outward.x * (half.x + SeatDistance), 0f, Mathf.Clamp(along.z, -room, room)), -outward);
            }
            else
            {
                var outward = new Vector3(0f, 0f, Mathf.Sign(direction.z));
                var room = Mathf.Max(0f, half.x - CornerClearance);
                return (new Vector3(Mathf.Clamp(along.x, -room, room), 0f, outward.z * (half.y + SeatDistance)), -outward);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed != null)
                _subscribed.LocalEmoted -= OnLocalEmote;
            foreach (var chalker in _chalkers.Values)
                chalker.Destroy();
            _chalkers.Clear();
            foreach (var chair in _chairs.Values)
                chair.Destroy();
            _chairs.Clear();
            ClearSofas();
        }
    }
}
