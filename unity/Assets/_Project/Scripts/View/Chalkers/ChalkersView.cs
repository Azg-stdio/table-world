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
        public const float SeatDistance = 0.25f;
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
            get => _singer ?? (TableGeometry.Walk ? _stageSingers.FirstOrDefault()
                : Karaoke && _table.Game?.Session is { Started: true, Over: false } session ? session.CurrentSeat : null);
            set => _singer = value;
        }

        private string _singer;

        /// <summary>Who stands on the stage now, in seat order (a room people walk in): the first sings, any others sing along.</summary>
        private readonly List<string> _stageSingers = new List<string>();

        /// <summary>Everyone on the stage now, in seat order.</summary>
        public IReadOnlyList<string> StageSingers => _stageSingers;

        /// <summary>
        /// Seats taken from this window besides the one the camera is at (a hot seat: one machine, several players). Each
        /// gets a chalker of its own, idle, looking at the table, unless a remote player sits there.
        /// </summary>
        public Func<IEnumerable<string>> LocalSeats { get; set; }

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
            if (TableGeometry.Walk)
            {
                Walking(Time.deltaTime);
                return;
            }
            if (_walker != null)
            {
                _walker.End();
                _walker = null;
            }
            var seen = new HashSet<string>();
            var seated = new HashSet<string>();
            var dt = Time.deltaTime;
            // A chair at every seat, played or not; this player's own chalker sits in theirs (first person looks out of it).
            var karaoke = Karaoke;
            if (karaoke)
                BuildSofas();
            else
                ClearSofas();
            if (!TableGeometry.Hidden && !karaoke)
                foreach (var seat in _table.Engine.State.Players.Seats)
                    if (Place(seat.Id) is { } place)
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
                    var carried = frame.Held.Count > 0 ? new Vector3(frame.Held[0].Position.X, frame.Held[0].Position.Y, frame.Held[0].Position.Z) : (Vector3?)null;
                    Prepare(chalker, frame.Seat, frame.HandUp, frame.Lean, carried, place.Position);
                    chalker.ThumbUp = frame.Thumb || Raised(frame.Seat);
                    chalker.Pose(place.Position, place.Facing, gaze ?? pointer ?? Vector3.zero, pointer ?? gaze, _voice != null ? _voice.LevelOf(frame.Seat) : 0f, dt);
                    HoldCards(chalker, frame.Seat);
                    Carry(chalker, frame.Seat);
                }

            if (LocalSeats == null && FindAnyObjectByType<Hud.HudController>() is { } hud)
                LocalSeats = hud.HotSeats;
            foreach (var seat in LocalSeats?.Invoke() ?? Enumerable.Empty<string>())
            {
                if (seat == _table.LocalSeat || seen.Contains(seat) || !(Place(seat) is { } place))
                    continue;
                seen.Add(seat);
                var chalker = ChalkerFor(seat, ColorOf(seat));
                chalker.EyesClosed = _table.Game is { EyesClosedPhase: true } nightGame && !nightGame.IsAwake(seat, _table.LocalSeat);
                chalker.Microphone = Singer != null && Singer == seat;
                Prepare(chalker, seat, false, 0f, null, place.Position);
                chalker.ThumbUp = Raised(seat);
                chalker.Pose(place.Position, place.Facing, Vector3.zero, null, 0f, dt);
                HoldCards(chalker, seat);
            }

            Yourself(seen, dt);

            // Only your own chalker hides its body (you look out of it) or fades (it stands in your way). A seat you looked
            // out of before switching seats at a hot-seat table, or that you left online, shows again.
            foreach (var pair in _chalkers)
                if (pair.Key != _table.LocalSeat && seen.Contains(pair.Key))
                {
                    pair.Value.HideHead(false);
                    pair.Value.Ghost.Update(false, ColorOf(pair.Key), dt);
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
            ShowMarks();
        }

        private TableCamera _tableCamera;
        private TableInteraction _interaction;

        /// <summary>Your own chalker, the one you see from inside in first person (null without a seat, or walking).</summary>
        public Chalker Yours => _table != null && _table.LocalSeat != null && _chalkers.TryGetValue(_table.LocalSeat, out var yours) ? yours : null;

        /// <summary>
        /// Your own chalker in your chair (docs/dev/camera.md): the seated camera looks out of its eyes, its head drawn only
        /// as a shadow then; lifted, you see it from outside and know which one is you. It leans with the camera, raises its
        /// cards when you look at them, reaches for what you carry, and talks with your microphone.
        /// </summary>
        private void Yourself(HashSet<string> seen, float dt)
        {
            if (_tableCamera == null && Camera.main != null)
                _tableCamera = Camera.main.GetComponent<TableCamera>();
            if (_interaction == null)
                _interaction = _table.GetComponent<TableInteraction>();
            var me = _table.LocalSeat;
            if (me == null || seen.Contains(me) || !(Place(me) is { } place))
            {
                if (_tableCamera != null)
                    _tableCamera.SetSeat(null, Vector3.forward);
                return;
            }
            seen.Add(me);
            var yours = ChalkerFor(me, ColorOf(me));
            yours.Personality = PersonalityOf(_session != null ? _session.LocalLook : "");
            yours.Microphone = Singer != null && Singer == me;
            var view = Camera.main != null ? Camera.main.transform : null;
            var pointer = _interaction != null ? _interaction.PointerOnTable : null;
            var ahead = view != null ? view.position + view.forward * 2f : place.Position + place.Facing * 2f;
            // Seated, the head turns where you look; lifted, to your pointer, as other players see it.
            var seated = _tableCamera != null && _tableCamera.IsSeated;
            var gaze = seated ? ahead : pointer ?? ahead;
            var carried = _interaction != null && _interaction.Held != null ? _interaction.Held.transform.position : (Vector3?)null;
            Prepare(yours, me, _interaction != null && _interaction.HandUp, _tableCamera != null ? _tableCamera.Lean : 0f, carried, place.Position);
            yours.ThumbUp = _interaction != null && _interaction.ThumbUp || Raised(me);
            // On your own screen your cards stay in the hand bar and at your edge of the table, as in TTS, where they read
            // easily; the other players see your chalker hold them (their machines pose it from your hand).
            yours.HoldingCards = false;
            yours.Pose(place.Position, place.Facing, gaze, pointer ?? gaze, _voice != null && _voice.Talking ? _voice.MicLevel : 0f, dt);
            HoldCards(yours, me);
            if (_tableCamera != null)
            {
                var eyes = yours.SteadyEyes;
                var inside = Vector3.Distance(_tableCamera.transform.position, eyes) < 0.35f;
                yours.HideHead(inside);
                _tableCamera.SetSeat(eyes, place.Facing);
                // Lifted, it fades where it stands between you and the table, rimmed in your colour.
                var covering = !inside && !_tableCamera.IsSeated && Covers(yours, _tableCamera.GetComponent<Camera>());
                yours.Ghost.Update(covering, ColorOf(me), dt);
            }
        }

        /// <summary>
        /// Whether <paramref name="chalker"/> stands between <paramref name="view"/> and some of the table: a grid of points on
        /// the table's top, each behind the chalker and inside the screen box its body covers.
        /// </summary>
        public static bool Covers(Chalker chalker, Camera view)
        {
            if (view == null)
                return false;
            var root = chalker.Root.transform;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < 8; i++)
            {
                var corner = root.TransformPoint(new Vector3((i & 1) == 0 ? -BodyBox.x : BodyBox.x, (i & 2) == 0 ? 0f : BodyBox.y,
                    (i & 4) == 0 ? -BodyBox.z * 0.5f : BodyBox.z));
                var seen = view.WorldToViewportPoint(corner);
                if (seen.z <= 0f)
                    return false;
                min = Vector2.Min(min, seen);
                max = Vector2.Max(max, seen);
            }
            var near = view.WorldToViewportPoint(root.position).z;
            var size = TableGeometry.Size;
            for (var x = 0; x <= 6; x++)
            for (var z = 0; z <= 4; z++)
            {
                var point = new Vector3((x / 6f - 0.5f) * size.x, 0f, (z / 4f - 0.5f) * size.y);
                var seen = view.WorldToViewportPoint(point);
                if (seen.z > near && seen.x > min.x && seen.x < max.x && seen.y > min.y && seen.y < max.y)
                    return true;
            }
            return false;
        }

        /// <summary>A seated chalker's body for <see cref="Covers"/>: half its width, its height, its reach forward, in metres.</summary>
        private static readonly Vector3 BodyBox = new Vector3(0.25f, 0.72f, 0.35f);

        /// <summary>A thumb the phase raises (Avalon's minions for Merlin), as this machine may see it.</summary>
        private bool Raised(string seat) => _table.Game is { } game && game.ThumbRaised(seat, _table.Spectating ? null : _table.LocalSeat);

        /// <summary>What a seated chalker does with its hands this frame: hold its seat's cards (raised or not), reach for what its player carries.</summary>
        private void Prepare(Chalker chalker, string seat, bool raise, float lean, Vector3? carried, Vector3 at)
        {
            chalker.HoldingCards = _table.HandCardCount(seat) > 0;
            chalker.RaiseHand = raise;
            chalker.LeanIn = lean;
            // Something carried across the table: the arm points at it rather than stretching through the table.
            chalker.ReachFor = carried is { } c && Vector3.Distance(new Vector3(c.x, 0f, c.z), new Vector3(at.x, 0f, at.z)) < MaxReach ? c : (Vector3?)null;
        }

        /// <summary>How far from its chair a chalker reaches for something its player carries, in metres.</summary>
        public const float MaxReach = 1.1f;

        private void HoldCards(Chalker chalker, string seat)
        {
            if (chalker.CardHold is { } hold)
                _table.HoldHand(seat, hold);
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
            // Walking and singing yourself (no mirror): the light finds your own body.
            if (Singer != null && Singer == _table.LocalSeat && _walker != null)
            {
                var forward = Quaternion.Euler(0f, _walker.Yaw, 0f) * Vector3.forward;
                _environment.AimStage(Chalker.StandingOrigin(_walker.Feet), forward);
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

        private TableWorld.View.Karaoke.Walker _walker;

        /// <summary>This player's walker in a room people walk in (null at a table).</summary>
        public TableWorld.View.Karaoke.Walker Walker => _walker;

        /// <summary>
        /// A room people walk in (T12.10): no chairs; this player walks in first person, and every other player's chalker
        /// stands, walks or sits on a sofa where their presence says. With Hear myself on, your own chalker stands in
        /// front of you, a mirror of your body.
        /// </summary>
        private void Walking(float dt)
        {
            var environment = FindAnyObjectByType<EnvironmentView>();
            var lounge = environment != null ? environment.Lounge : null;
            foreach (var chair in _chairs.Values)
                chair.Destroy();
            _chairs.Clear();
            ClearSofas();
            if (_walker == null && Camera.main != null && environment != null)
            {
                var index = _table.Engine.State.Players.Seats.FindIndex(s => s.Id == _table.LocalSeat);
                _walker = TableWorld.View.Karaoke.Walker.Begin(Camera.main, environment, Mathf.Max(0, index));
                _walker.Taken = () => (_session?.Others ?? Enumerable.Empty<(string, Core.Net.PresenceFrame)>())
                    .Where(o => o.Item2.Body?.Place != null).Select(o => o.Item2.Body!.Place!.Value);
            }

            // Whoever stands on the stage sings (ADR 0013 §5): the others from their presence, this player from their walker.
            _stageSingers.Clear();
            if (lounge != null)
            {
                var onStage = new HashSet<string>();
                if (_session != null)
                    foreach (var (_, frame) in _session.Others)
                        if (frame.Body is { Place: null } other && lounge.OnStage(new Vector3(other.Position.X, 0f, other.Position.Z)))
                            onStage.Add(frame.Seat);
                if (_walker != null && _walker.Place == null && _table.LocalSeat != null && lounge.OnStage(_walker.Feet))
                    onStage.Add(_table.LocalSeat);
                _stageSingers.AddRange(_table.Engine.State.Players.Seats.Select(s => s.Id).Where(onStage.Contains));
            }

            var seen = new HashSet<string>();
            if (_session != null)
                foreach (var (_, frame) in _session.Others)
                {
                    if (frame.Body is not { } body)
                        continue;
                    seen.Add(frame.Seat);
                    var chalker = ChalkerFor(frame.Seat, ColorOf(frame.Seat));
                    chalker.Personality = PersonalityOf(_session.LookAtSeat(frame.Seat));
                    chalker.Microphone = Singer == frame.Seat || _stageSingers.Contains(frame.Seat);
                    if (_emoteCounts.TryGetValue(frame.Seat, out var count) && count != frame.EmoteCount && Enum.TryParse<ChalkerEmote>(frame.Emote, out var emote))
                        chalker.Play(emote);
                    _emoteCounts[frame.Seat] = frame.EmoteCount;
                    var gaze = frame.Gaze is { } g ? new Vector3(g.X, g.Y, g.Z) : (Vector3?)null;
                    var (position, facing) = BodyPlace(body, lounge);
                    chalker.Standing = body.Place == null;
                    chalker.Walking = body.Moving;
                    chalker.Pose(position, facing, gaze ?? position + facing * 3f + Vector3.up * 0.5f, gaze, _voice != null ? _voice.LevelOf(frame.Seat) : 0f, dt);
                    Carry(chalker, frame.Seat);
                }

            var camera = Camera.main;
            if (VoiceChat.HearMyself && camera != null && _walker != null)
            {
                seen.Add(MirrorKey);
                var mirror = ChalkerFor(MirrorKey, ColorOf(_table.LocalSeat));
                mirror.Personality = PersonalityOf(_session != null ? _session.LocalLook : "");
                var flat = new Vector3(camera.transform.forward.x, 0f, camera.transform.forward.z).normalized;
                if (flat.sqrMagnitude < 1e-4f)
                    flat = Vector3.forward;
                // Standing in front of you, facing you, moving as you move.
                var feet = new Vector3(camera.transform.position.x, EnvironmentView.FloorY, camera.transform.position.z) + flat * 1.6f;
                mirror.Standing = true;
                mirror.Walking = _walker.Moving;
                mirror.Microphone = Singer != null && Singer == _table.LocalSeat;
                mirror.Pose(Chalker.StandingOrigin(feet), -flat, camera.transform.position, camera.transform.position, _voice != null ? _voice.LevelOf(VoiceChat.SelfSeat) : 0f, dt);
                Carry(mirror, VoiceChat.SelfSeat);
            }

            foreach (var key in _chalkers.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _chalkers[key].Destroy();
                _chalkers.Remove(key);
                _emoteCounts.Remove(key);
            }
            AimStage();
        }

        /// <summary>Where a walking player's chalker goes: on its sofa place, or standing where its feet are, facing its way.</summary>
        public static (Vector3 Position, Vector3 Facing) BodyPlace(Core.Net.AvatarPose body, TableWorld.View.Karaoke.Lounge lounge)
        {
            if (body.Place is { } place && lounge != null && place < lounge.Places.Count)
                return lounge.Places[place];
            var facing = Quaternion.Euler(0f, body.Yaw, 0f) * Vector3.forward;
            return (Chalker.StandingOrigin(new Vector3(body.Position.X, body.Position.Y, body.Position.Z)), facing);
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
                if (key == MirrorKey || key == _table.LocalSeat)
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
            if (!Enum.TryParse<ChalkerEmote>(name, out var emote))
                return;
            if (_chalkers.TryGetValue(MirrorKey, out var mirror))
                mirror.Play(emote);
            Yours?.Play(emote);
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

        /// <summary>
        /// Where each player's name goes: over the head of every chalker at the table (not yours while you look out of
        /// it, nor the mirror), with its seat and colour.
        /// </summary>
        public IEnumerable<(string Seat, Vector3 Above, Color Color)> Nameplates()
        {
            foreach (var pair in _chalkers)
            {
                if (pair.Key == MirrorKey || pair.Value.HeadHidden || _table.Engine == null)
                    continue;
                yield return (pair.Key, pair.Value.SeenEyes + pair.Value.Root.transform.up * NameAbove, ColorOf(pair.Key));
            }
        }

        /// <summary>How far above a chalker's eyes its name sits, in metres (above the placeholder's eyeballs and any mark).</summary>
        public const float NameAbove = 0.25f;

        /// <summary>The player whose chalker is under <paramref name="screen"/> (the nearest), or null.</summary>
        public string SeatAt(Camera view, Vector2 screen)
        {
            string best = null;
            var nearest = float.MaxValue;
            foreach (var pair in _chalkers)
            {
                if (pair.Key == MirrorKey || pair.Value.HeadHidden)
                    continue;
                var root = pair.Value.Root.transform;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                var behind = false;
                for (var i = 0; i < 8; i++)
                {
                    var corner = root.TransformPoint(new Vector3((i & 1) == 0 ? -BodyBox.x : BodyBox.x, (i & 2) == 0 ? 0.2f : BodyBox.y + 0.1f,
                        (i & 4) == 0 ? -BodyBox.z * 0.5f : BodyBox.z * 0.5f));
                    var seen = view.WorldToScreenPoint(corner);
                    behind |= seen.z <= 0f;
                    min = Vector2.Min(min, seen);
                    max = Vector2.Max(max, seen);
                }
                var depth = view.WorldToScreenPoint(root.position).z;
                if (behind || screen.x < min.x || screen.x > max.x || screen.y < min.y || screen.y > max.y || depth >= nearest)
                    continue;
                nearest = depth;
                best = pair.Key;
            }
            return best;
        }

        // ---- marks over players (the rules' marks: an arrow over each chosen player, a crown over the leader) ----

        private readonly Dictionary<(string Mark, string Seat), Transform> _marks = new Dictionary<(string, string), Transform>();
        private readonly List<Material> _markMaterials = new List<Material>();
        private Mesh _cone;

        /// <summary>Shows each rules mark over the chalkers of the players wearing it, bobbing, and hides the rest.</summary>
        private void ShowMarks()
        {
            var game = _table.Game;
            var wanted = new HashSet<(string, string)>();
            if (game != null)
                foreach (var mark in game.State.Marks)
                    foreach (var seat in game.Marked(mark))
                        if (_chalkers.TryGetValue(seat, out var chalker))
                        {
                            var key = (mark.Id, seat);
                            wanted.Add(key);
                            if (!_marks.TryGetValue(key, out var shown) || shown == null)
                                _marks[key] = shown = BuildMark(mark);
                            var bob = mark.Shape == MarkShape.Arrow ? Mathf.Abs(Mathf.Sin(Time.time * 3.2f)) * 0.04f : Mathf.Sin(Time.time * 1.5f) * 0.008f;
                            shown.position = chalker.SeenEyes + chalker.Root.transform.up * (MarkAbove + bob);
                            shown.rotation = Quaternion.Euler(0f, Time.time * (mark.Shape == MarkShape.Crown ? 30f : 90f), 0f);
                            // Seen from inside your own head, your mark would fill the view.
                            shown.gameObject.SetActive(!chalker.HeadHidden);
                        }
            foreach (var key in _marks.Keys.Where(k => !wanted.Contains(k)).ToList())
            {
                if (_marks[key] != null)
                    Destroy(_marks[key].gameObject);
                _marks.Remove(key);
            }
        }

        /// <summary>How far above a chalker's eyes a mark floats, in metres.</summary>
        public const float MarkAbove = 0.08f;

        private Transform BuildMark(MarkDef mark)
        {
            var c = mark.Color is { Length: 3 } rgb ? new Color(rgb[0], rgb[1], rgb[2]) : new Color(0.98f, 0.78f, 0.2f);
            var material = new Material(RequiredShaders.Lit) { name = "Mark " + mark.Id };
            material.SetColor("_BaseColor", c);
            material.SetFloat("_Smoothness", 0.6f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", c * 0.35f);
            _markMaterials.Add(material);
            var root = new GameObject("Mark " + mark.Id).transform;
            root.SetParent(transform, false);
            GameObject Part(PrimitiveType type, Vector3 at, Vector3 size)
            {
                var part = GameObject.CreatePrimitive(type);
                Destroy(part.GetComponent<Collider>());
                part.transform.SetParent(root, false);
                part.transform.localPosition = at;
                part.transform.localScale = size;
                part.GetComponent<Renderer>().sharedMaterial = material;
                return part;
            }
            if (mark.Shape == MarkShape.Arrow)
            {
                // A head pointing down at the player, on a short shaft.
                var head = new GameObject("Head");
                head.transform.SetParent(root, false);
                head.transform.localScale = new Vector3(0.04f, 0.05f, 0.04f);
                head.AddComponent<MeshFilter>().sharedMesh = _cone ??= Cone();
                head.AddComponent<MeshRenderer>().sharedMaterial = material;
                Part(PrimitiveType.Cylinder, new Vector3(0f, 0.075f, 0f), new Vector3(0.016f, 0.03f, 0.016f));
            }
            else
            {
                // A band with five points.
                Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.09f, 0.016f, 0.09f));
                for (var i = 0; i < 5; i++)
                {
                    var angle = i * Mathf.PI * 2f / 5f;
                    var point = Part(PrimitiveType.Cube, new Vector3(Mathf.Cos(angle) * 0.04f, 0.03f, Mathf.Sin(angle) * 0.04f), new Vector3(0.018f, 0.038f, 0.018f));
                    point.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 45f);
                }
            }
            return root;
        }

        /// <summary>A cone of height 1 and radius 1, its point at the origin and its base up.</summary>
        private static Mesh Cone()
        {
            const int around = 20;
            var vertices = new List<Vector3> { Vector3.zero, Vector3.up };
            for (var i = 0; i <= around; i++)
            {
                var angle = i * Mathf.PI * 2f / around;
                vertices.Add(new Vector3(Mathf.Cos(angle), 1f, Mathf.Sin(angle)));
            }
            var triangles = new List<int>();
            for (var i = 0; i < around; i++)
            {
                triangles.AddRange(new[] { 0, 2 + i + 1, 2 + i });
                triangles.AddRange(new[] { 1, 2 + i, 2 + i + 1 });
            }
            var mesh = new Mesh { name = "Mark cone" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
            if (TableGeometry.Round)
            {
                // Round, the same way: from the near side's middle, going right.
                var angle = share * 2f * Mathf.PI;
                var away = new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle));
                return (away * (TableGeometry.Radius + SeatDistance), -away);
            }
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
            if (TableGeometry.Round)
            {
                // A round table has no corners: straight out from the centre.
                var away = new Vector3(direction.x, 0f, direction.z).normalized;
                return (away * (TableGeometry.Radius + SeatDistance), -away);
            }
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
            foreach (var material in _markMaterials)
                Destroy(material);
            if (_cone != null)
                Destroy(_cone);
            foreach (var chair in _chairs.Values)
                chair.Destroy();
            _chairs.Clear();
            ClearSofas();
        }
    }
}
