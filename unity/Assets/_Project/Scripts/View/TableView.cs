using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Rules;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Owns the core <see cref="GameEngine"/> for this table and one <see cref="ObjectView"/> per object on the table.
    /// Everything that changes the table goes through <see cref="Execute"/>; views only follow the resulting events.
    /// </summary>
    public sealed class TableView : MonoBehaviour
    {
        [Tooltip("TextAsset under a Resources folder, loaded on Start. Empty: load nothing.")]
        [SerializeField] private string _tableResource = "Tables/sample";
        [SerializeField] private Transform _objectsRoot;
        [Tooltip("Seed for shuffles and rolls. 0 picks one from the clock.")]
        [SerializeField] private int _seed;

        /// <summary>A free object still moving after this long reports its pose anyway.</summary>
        public const float ReportAwakeAfterSeconds = 2f;
        public const float PositionTolerance = 0.0005f;
        public const float RotationToleranceDegrees = 0.5f;
        public const float RollSeconds = 0.55f;

        private readonly Dictionary<string, ObjectView> _views = new Dictionary<string, ObjectView>();
        private readonly List<GameObject> _zoneViews = new List<GameObject>();
        private readonly Dictionary<string, float> _awakeSince = new Dictionary<string, float>();
        private readonly List<ObjectView> _scratch = new List<ObjectView>();

        /// <summary>Id whose pose is being reported from its own physics: the view already shows it, so it must not be snapped.</summary>
        private string _reportingId;

        public const string QuickSaveName = "quicksave";

        private string _packageRoot;

        /// <summary>Folder the table's asset paths are relative to; null for tables without files (builtin assets only).</summary>
        public string PackageRoot => _packageRoot;

        /// <summary>Second source of asset bytes after the package folder: a network client's asset cache (T3.4).</summary>
        public Func<Asset, byte[]> FetchAsset { get; set; }

        /// <summary>Rules interpreter for this table; free play goes through it too.</summary>
        public GameEngine Game { get; private set; }

        /// <summary>The table engine under <see cref="Game"/>: objects, zones, containment.</summary>
        public TableEngine Engine => Game?.Table;

        /// <summary>Where saves live: &lt;persistentDataPath&gt;/Saves/&lt;name&gt;.json.</summary>
        public static string SavesFolder => Path.Combine(Application.persistentDataPath, "Saves");

        public static string SavePath(string name) => Path.Combine(SavesFolder, name + ".json");
        public AssetLibrary Assets { get; private set; }
        public IReadOnlyDictionary<string, ObjectView> Views => _views;

        /// <summary>Raised after every command, successful or not.</summary>
        public event Action<Command, CommandResult> CommandExecuted;

        public void Configure(string tableResource, Transform objectsRoot, int seed)
        {
            _tableResource = tableResource;
            _objectsRoot = objectsRoot;
            _seed = seed;
        }

        private void Start()
        {
            if (string.IsNullOrEmpty(_tableResource))
                return;
            var text = Resources.Load<TextAsset>(_tableResource);
            if (text == null)
            {
                Debug.LogError($"[TableView] No TextAsset at Resources/{_tableResource}.");
                return;
            }
            Load(text.text, packageRoot: null);
        }

        /// <summary>Replaces the table with the one in <paramref name="json"/>. Returns false (and logs every error) when it is invalid.</summary>
        public bool Load(string json, string packageRoot)
        {
            var parsed = TableJson.Parse(json);
            if (!parsed.Ok)
            {
                Debug.LogError("[TableView] Invalid table:\n" + string.Join("\n", parsed.Errors));
                return false;
            }

            _packageRoot = packageRoot;
            Assets = new AssetLibrary(parsed.State.Assets, packageRoot, FetchAsset);
            Replace(parsed.State);
            _undo.Clear();
            _redo.Clear();
            _loadedAt = Time.time;
            Loaded?.Invoke();
            return true;
        }

        private void Replace(TableState state)
        {
            Clear();
            var seed = _seed != 0 ? (ulong)_seed : (ulong)DateTime.UtcNow.Ticks;
            Game = new GameEngine(state, seed);
            SpawnAll();
        }

        // ---- undo / redo (Ctrl+Z / Ctrl+Y) ----------------------------------------------------------------------

        public const int MaxUndo = 50;
        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();

        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;

        /// <summary>Remembers the table as it is now, before a player action changes it.</summary>
        public void Checkpoint()
        {
            if (Engine == null)
                return;
            var json = TableJson.Serialize(Engine.State, indented: false);
            if (_undo.Count > 0 && _undo[_undo.Count - 1] == json)
                return;
            _undo.Add(json);
            if (_undo.Count > MaxUndo)
                _undo.RemoveAt(0);
            _redo.Clear();
        }

        /// <summary>Undo/redo replace the whole table, so they are local-only (not while networked).</summary>
        public bool Undo() => !Networked && Step(_undo, _redo);

        public bool Redo() => !Networked && Step(_redo, _undo);

        private bool Step(List<string> from, List<string> to)
        {
            if (from.Count == 0)
                return false;
            to.Add(TableJson.Serialize(Engine.State, indented: false));
            var json = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Replace(TableJson.Parse(json).State);
            _loadedAt = Time.time;
            Loaded?.Invoke();
            return true;
        }

        /// <summary>Raised after a table is loaded (the HUD rebuilds its seats).</summary>
        public event Action Loaded;

        /// <summary>An object hit something hard enough to be heard (speed in m/s). Silent for a moment after a table loads.</summary>
        public event Action<ObjectView, float> Impacted;

        /// <summary>Objects settle for a moment after a table loads; that clatter is not played.</summary>
        public const float QuietAfterLoadSeconds = 0.5f;
        private float _loadedAt = float.NegativeInfinity;

        private void OnImpact(ObjectView view, float speed)
        {
            if (Time.time - _loadedAt > QuietAfterLoadSeconds)
                Impacted?.Invoke(view, speed);
        }

        // ---- networking hooks (T3.2) --------------------------------------------------------------------------

        /// <summary>When set (networked play), commands go through it instead of straight to <see cref="Game"/>.</summary>
        public Func<Command, CommandResult> Router { get; set; }

        /// <summary>When set, the seat this machine plays (a network client); otherwise hot seat.</summary>
        public string SeatOverride { get; set; }

        /// <summary>When set, physics poses are only reported for objects it accepts (a client reports what it touched).</summary>
        public Func<ObjectView, bool> ReportsPoseOf { get; set; }

        public bool Networked => Router != null;

        /// <summary>
        /// True on the machine that hosts the table (solo play too); false on a network client. Hidden zones that list
        /// <c>host</c> reveal their contents only here.
        /// </summary>
        public bool HostsTable { get; set; } = true;

        /// <summary>Raised with a sentence for the player, e.g. why the rules refused a gesture.</summary>
        public event Action<string> Notice;

        public void Tell(string text) => Notice?.Invoke(text);

        /// <summary>Raised when the local player handles an object (grab, drop): a network client then owns its physics for a while.</summary>
        public event Action<string> Touched;

        public void NotifyTouched(string objectId) => Touched?.Invoke(objectId);

        /// <summary>Shows another engine's table (a snapshot or a rebuilt prediction from the network).</summary>
        public void Adopt(GameEngine game)
        {
            Clear();
            // Keep loaded textures when the asset list did not change (a rebuilt prediction, a resync).
            var assets = TableJson.Serialize(new TableState { Assets = game.State.Assets }, indented: false);
            if (Assets == null || assets != _assetsJson)
                Assets = new AssetLibrary(game.State.Assets, _packageRoot, FetchAsset);
            _assetsJson = assets;
            Game = game;
            SpawnAll();
            _undo.Clear();
            _redo.Clear();
            _loadedAt = Time.time;
            Loaded?.Invoke();
        }

        private string _assetsJson;

        /// <summary>Raised after an asset's bytes arrive and the objects were repainted (the room re-dresses the table).</summary>
        public event Action<string> AssetRefreshed;

        /// <summary>An asset's bytes arrived: reload its texture and repaint every object.</summary>
        public void RefreshAsset(string assetId)
        {
            if (Assets == null)
                return;
            Assets.Forget(assetId);
            foreach (var view in _views.Values)
                if (view != null)
                    ObjectViewFactory.Refresh(view, Assets);
            AssetRefreshed?.Invoke(assetId);
        }

        /// <summary>Applies the events of a command another peer performed (already executed on <see cref="Game"/>).</summary>
        public void ApplyRemote(Command command, CommandResult result)
        {
            Apply(result.Events);
            CommandExecuted?.Invoke(command, result);
        }

        private void SpawnAll()
        {
            foreach (var obj in Engine.State.Objects.Where(o => o.Container == null))
                Spawn(obj);
            SpawnZones();
            PresentHands();
        }

        // ---- hands (TTS style) ------------------------------------------------------------------------------------

        /// <summary>
        /// How far hand cards are raised from lying flat, toward their owner: 40° faces the default camera (55° pitch)
        /// almost square on, so the owner reads them without them standing like a wall.
        /// </summary>
        public const float HandTiltDegrees = 40f;
        /// <summary>Hand cards are shown this much closer to the owner's table edge than the zone's centre line.</summary>
        public const float HandEdgeOffset = 0.03f;
        /// <summary>Hand cards glide to their places at this rate (about 0.15 s).</summary>
        public const float HandMotionRate = 22f;
        private string _presentedSeat;
        private bool _presentedHosting = true;
        private readonly List<(HiddenZone Zone, GameObject Fog)> _fogs = new List<(HiddenZone, GameObject)>();

        /// <summary>
        /// Cards in a hand zone stand up at the table edge facing their owner, out of the physics simulation; the owner
        /// sees their faces, everyone else their backs. The state keeps them flat in their slots: this is view only.
        /// </summary>
        public void PresentHands()
        {
            if (Engine == null)
                return;
            var viewer = LocalSeat;
            _presentedSeat = viewer;
            _presentedHosting = HostsTable;
            foreach (var fog in _fogs)
                if (fog.Fog != null)
                    fog.Fog.SetActive(!fog.Zone.Reveals(viewer, HostsTable));
            var hands = new Dictionary<HandZone, List<ObjectView>>();
            foreach (var view in _views.Values)
            {
                if (view == null)
                    continue;
                // Hidden zones (G17): what lies inside is not drawn for viewers the zone does not list.
                var zoneHidden = !view.Held && Engine.HiddenZoneHiding(view.Id, viewer, HostsTable) != null;
                if (zoneHidden != view.ZoneHidden)
                    view.SetZoneHidden(zoneHidden);
                if (!(view.Model is Card))
                    continue;
                var hand = view.Held ? null : HandZoneOf(view.Id);
                var hide = hand != null && hand.Owner != viewer;
                if (hide != view.HideFace)
                {
                    view.HideFace = hide;
                    ObjectViewFactory.Refresh(view, Assets);
                }
                if (hand == null)
                {
                    if (view.InHand)
                    {
                        view.LeaveHand();
                        view.Body.isKinematic = view.Pinned || view.Held;
                        view.SnapToState();
                    }
                    continue;
                }
                if (!hands.TryGetValue(hand, out var members))
                    hands[hand] = members = new List<ObjectView>();
                members.Add(view);
            }
            foreach (var pair in hands)
                StandInHand(pair.Key, pair.Value);
        }

        /// <summary>
        /// Spreads a hand's cards evenly along the zone in the player's order (left to right as the owner sees it),
        /// overlapping when they do not fit, each standing and leaning back toward the owner.
        /// </summary>
        private static void StandInHand(HandZone hand, List<ObjectView> cards)
        {
            var yaw = Quaternion.Euler(0f, hand.Transform.Rotation.Y, 0f);
            var center = hand.Transform.Position.ToUnity();
            float LocalX(ObjectView v) => (Quaternion.Inverse(yaw) * (v.Model.Transform.Position.ToUnity() - center)).x;
            cards.Sort((a, b) => LocalX(a).CompareTo(LocalX(b)));
            var size = (((Card)cards[0].Model).Size ?? Card.DefaultSize).ToUnity();
            var width = hand.Size.X * hand.Transform.Scale.X;
            var step = cards.Count < 2 ? 0f : Mathf.Min(size.x * 1.08f, (width - size.x) / (cards.Count - 1));
            var lift = size.y / 2f * Mathf.Sin(HandTiltDegrees * Mathf.Deg2Rad) + 0.004f;
            var pose = yaw * Quaternion.Euler(-HandTiltDegrees, 0f, 0f);
            for (var i = 0; i < cards.Count; i++)
            {
                var view = cards[i];
                view.EnterHand();
                // Later cards sit a little closer to the owner, so overlapping cards never z-fight.
                var local = new Vector3((i - (cards.Count - 1) / 2f) * step, 0f, -HandEdgeOffset - 0.0015f * i);
                var p = center + yaw * local;
                view.HandPosition = new Vector3(p.x, lift, p.z);
                view.HandRotation = pose;
            }
        }

        /// <summary>Hand cards move to their places smoothly (a drop into the hand, a reorder, a card leaving) instead of jumping.</summary>
        private void GlideHandCards()
        {
            var k = 1f - Mathf.Exp(-HandMotionRate * Time.fixedDeltaTime);
            foreach (var view in _views.Values)
            {
                if (view == null || !view.InHand || view.Held || view.RemoteHolder != null)
                    continue;
                var body = view.Body;
                if ((body.position - view.HandPosition).sqrMagnitude < 1e-8f && Quaternion.Angle(body.rotation, view.HandRotation) < 0.05f)
                    continue;
                body.MovePosition(Vector3.Lerp(body.position, view.HandPosition, k));
                body.MoveRotation(Quaternion.Slerp(body.rotation, view.HandRotation, k));
            }
        }

        private HandZone HandZoneOf(string objectId)
        {
            foreach (var zoneId in Engine.ZonesOf(objectId))
                if (Engine.State.Zones.FirstOrDefault(z => z.Id == zoneId) is HandZone hand)
                    return hand;
            return null;
        }

        /// <summary>
        /// Writes the table to <paramref name="path"/> as core JSON after recording every live physics pose, so the file
        /// matches what is on screen. Returns the state hash written.
        /// </summary>
        public string Save(string path)
        {
            foreach (var view in _views.Values.ToList())
                if (view != null && !view.Held && !view.Animating)
                    ReportPose(view);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, TableJson.Serialize(Engine.State));
            return TableJson.Hash(Engine.State);
        }

        /// <summary>Loads a saved table. Asset paths keep resolving against the package the table was first loaded from.</summary>
        public bool LoadFile(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[TableView] No save at {path}.");
                return false;
            }
            return Load(File.ReadAllText(path), _packageRoot);
        }

        public CommandResult Execute(Command command)
        {
            // A body that is still settling has not reported its pose yet; record it first, or the command's
            // ObjectUpdated would snap the object back to its last recorded position.
            if (!(command is MoveCommand))
                foreach (var id in TargetsOf(command))
                    if (ViewOf(id) is { Animating: false } view && !view.Body.isKinematic)
                        ReportPose(view);

            // Every player command can be undone; pose reports from physics are not separate steps.
            if (!(command is MoveCommand && _reportingId != null))
                Checkpoint();

            var result = Router != null ? Router(command) : Game.Execute(command);
            // A refused command is normal play (the HUD tells the player why through CommandExecuted), not a console warning.
            if (result.Ok)
                Apply(result.Events);
            if (result.StateRestored)
            {
                // A rules command failed midway and the engine rolled the table back: rebuild every view.
                Clear();
                SpawnAll();
            }
            CommandExecuted?.Invoke(command, result);
            return result;
        }

        public ObjectView ViewOf(string id) => id != null && _views.TryGetValue(id, out var view) ? view : null;

        /// <summary>Objects on the table a table command reads or changes.</summary>
        private static IEnumerable<string> TargetsOf(Command command)
        {
            switch (command)
            {
                case FlipCommand c: yield return c.ObjectId; break;
                case LockCommand c: yield return c.ObjectId; break;
                case RollCommand c: yield return c.ObjectId; break;
                case ShuffleCommand c: yield return c.ObjectId; break;
                case TakeCommand c: yield return c.ContainerId; break;
                case DealCommand c: yield return c.DeckId; break;
                case InsertCommand c:
                    yield return c.ObjectId;
                    yield return c.IntoId;
                    break;
                case GroupCommand c:
                    foreach (var id in c.ObjectIds)
                        yield return id;
                    break;
                case ScaleCommand c: yield return c.ObjectId; break;
                case DeleteCommand c: yield return c.ObjectId; break;
                case CloneCommand c: yield return c.ObjectId; break;
                case SetStateCommand c: yield return c.ObjectId; break;
                case CutCommand c: yield return c.DeckId; break;
                case SplitCommand c: yield return c.DeckId; break;
                case SpreadCommand c: yield return c.DeckId; break;
                case SetDieValueCommand c: yield return c.ObjectId; break;
                case SetPropertiesCommand c: yield return c.ObjectId; break;
            }
        }

        /// <summary>
        /// Seat that commands from this machine act as. Solo (hot seat): the seat whose turn it is during a game or with
        /// free-play turns on (G22), otherwise the first seat. Online it is pinned per machine.
        /// </summary>
        public string LocalSeat => SeatOverride ?? (Game?.Session is { Started: true, Over: false } session
            ? session.CurrentSeat
            : Engine?.State.FreeTurns is { Enabled: true, Current: { } turn }
                ? turn
                : Engine?.State.Players.Seats.FirstOrDefault()?.Id);

        /// <summary>
        /// Physics moves free objects; the state learns their final pose here. When a body falls asleep (or has been
        /// awake too long) and its pose differs from the state, a <see cref="MoveCommand"/> records it.
        /// </summary>
        private void LateUpdate()
        {
            if (Engine == null)
                return;
            if (_presentedSeat != LocalSeat || _presentedHosting != HostsTable)
                PresentHands();
            _scratch.Clear();
            _scratch.AddRange(_views.Values);
            foreach (var view in _scratch)
            {
                if (view == null || view.Held || view.Animating || view.Body.isKinematic || (ReportsPoseOf != null && !ReportsPoseOf(view)))
                {
                    if (view != null)
                        _awakeSince.Remove(view.Id);
                    continue;
                }
                if (!IsResting(view))
                {
                    if (!_awakeSince.TryGetValue(view.Id, out var since))
                        _awakeSince[view.Id] = Time.time;
                    else if (Time.time - since > ReportAwakeAfterSeconds)
                    {
                        // Never asleep (jittering on a die, say): the same lay-flat failsafe as a body that came to rest.
                        if (!LayFlat(view))
                            ReportPose(view);
                        _awakeSince[view.Id] = Time.time;
                    }
                    continue;
                }
                if (_awakeSince.Remove(view.Id) && !LayFlat(view))
                    ReportPose(view);
            }
        }

        public const float DieSettleSpeed = 0.15f;
        /// <summary>Tumble (spin about a horizontal axis) above which a die is still rolling. Spin about the vertical is always damped.</summary>
        public const float DieSettleTumble = 4f;
        public const float DieSettleRate = 12f;

        /// <summary>
        /// Dice settle assist. PhysX barely resists a die spinning about the vertical axis while flat on a face, so a thrown
        /// die would slide in circles for seconds. Once a die rests on a face and is no longer tumbling, its slide and spin are damped
        /// quickly; tumbling and bouncing are untouched.
        /// </summary>
        private void FixedUpdate()
        {
            if (Engine == null)
                return;
            ReturnLostObjects();
            GlideHandCards();
            var damping = Mathf.Exp(-DieSettleRate * Time.fixedDeltaTime);
            foreach (var view in _views.Values)
            {
                if (view == null || !(view.Model is Die die) || view.Held || view.Animating)
                    continue;
                var body = view.Body;
                if (body.isKinematic || body.IsSleeping())
                    continue;
                var velocity = body.linearVelocity;
                var spin = body.angularVelocity;
                var tumble = spin - Vector3.up * Vector3.Dot(spin, Vector3.up);
                if (velocity.sqrMagnitude > DieSettleSpeed * DieSettleSpeed || tumble.sqrMagnitude > DieSettleTumble * DieSettleTumble)
                    continue;
                // Only a die lying on something settles: one released straight down is still for an instant at the top
                // of its fall, and putting it to sleep there froze it in mid-air.
                if (!view.Touching || !RestsOnFace(Meshes.DiceGeometry.Get(die.Faces), body.rotation))
                    continue;
                // Writing velocities keeps a body awake; once it is still, let PhysX put it to sleep.
                if (velocity.sqrMagnitude < 1e-6f && body.angularVelocity.sqrMagnitude < 1e-4f)
                {
                    body.Sleep();
                    continue;
                }
                body.linearVelocity = new Vector3(velocity.x * damping, velocity.y, velocity.z * damping);
                body.angularVelocity *= damping;
            }
        }

        /// <summary>
        /// Failsafe: an object that fell through or off the table (a solver glitch, a hard throw) is put back on the
        /// surface above where it left, still, instead of being lost below the world.
        /// </summary>
        private void ReturnLostObjects()
        {
            foreach (var view in _views.Values)
            {
                if (view == null || view.Held || view.Animating || view.InHand || view.Body.isKinematic)
                    continue;
                var p = view.Body.position;
                if (!TableGeometry.IsLost(p))
                    continue;
                var back = TableGeometry.ClampToDropArea(new Vector3(p.x, 0f, p.z)) + Vector3.up * (view.Renderer.bounds.extents.y + 0.01f);
                view.Body.linearVelocity = Vector3.zero;
                view.Body.angularVelocity = Vector3.zero;
                view.Body.position = back;
                view.transform.position = back;
                view.Body.WakeUp();
                ReportWhenResting(view);
            }
        }

        /// <summary>Cards and decks that come to rest leaning more than this are laid flat once.</summary>
        public const float MaxRestingTiltDegrees = 3f;
        private readonly Dictionary<string, int> _laidFlat = new Dictionary<string, int>();

        /// <summary>
        /// Failsafe: a card or deck that settled propped on an edge (half on a deck, against a token) is laid flat, face
        /// kept. First where it is, a little higher, so it can drop flat onto what it leans on; if it still ends up
        /// tilted, it is slid clear of whatever holds it up and put flat on the felt. Then it is left alone.
        /// </summary>
        private bool LayFlat(ObjectView view)
        {
            if (!(view.Model is Card || view.Model is Deck))
                return false;
            var up = view.transform.up;
            var tilt = Vector3.Angle(up, up.y >= 0f ? Vector3.up : Vector3.down);
            if (tilt <= MaxRestingTiltDegrees)
                return false;
            _laidFlat.TryGetValue(view.Id, out var tries);
            if (tries >= 2)
                return false;
            _laidFlat[view.Id] = tries + 1;

            var forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;
            var flat = Quaternion.LookRotation(forward.normalized, Vector3.up) * (up.y >= 0f ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f));
            view.transform.rotation = flat;
            Physics.SyncTransforms();
            // First try: flat just above whatever lies under its footprint (never inside it: a card laid flat into a die
            // stays interpenetrating and both jitter forever). If it ends up propped again, slide it clear onto the felt.
            var position = tries == 1 ? ClearOfSupports(view, flat) : AboveSupports(view, flat);
            view.Body.rotation = flat;
            view.Body.position = position;
            view.transform.SetPositionAndRotation(position, flat);
            view.Body.linearVelocity = Vector3.zero;
            view.Body.angularVelocity = Vector3.zero;
            view.Body.WakeUp();
            ReportWhenResting(view);
            return true;
        }

        private readonly RaycastHit[] _supportHits = new RaycastHit[32];

        /// <summary>Where <paramref name="view"/>, laid flat, sits on top of the highest thing under its footprint (or the felt).</summary>
        private Vector3 AboveSupports(ObjectView view, Quaternion flat)
        {
            var half = view.Collider is BoxCollider box ? Vector3.Scale(box.size, view.transform.lossyScale) / 2f : view.Renderer.bounds.extents;
            var p = view.Body.position;
            var count = Physics.BoxCastNonAlloc(new Vector3(p.x, 1f, p.z), new Vector3(half.x, 0.0005f, half.z), Vector3.down, _supportHits, flat, 2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var top = 0f;
            for (var i = 0; i < count; i++)
                if (_supportHits[i].rigidbody != view.Body && _supportHits[i].distance > 0f)
                    top = Mathf.Max(top, _supportHits[i].point.y);
            return new Vector3(p.x, top + half.y + 0.002f, p.z);
        }

        /// <summary>How far, at most, a card is moved to find free felt; rings are this far apart, sixteen spots each.</summary>
        public const float ClearSearchRadius = 0.25f;
        public const float ClearSearchStep = 0.01f;
        private readonly Collider[] _clearHits = new Collider[16];

        private Vector3 ClearOfSupports(ObjectView view, Quaternion flat)
        {
            var half = view.Collider is BoxCollider box ? Vector3.Scale(box.size, view.transform.lossyScale) / 2f : view.Renderer.bounds.extents;
            var start = new Vector3(view.Body.position.x, half.y + 0.001f, view.Body.position.z);
            // The nearest spot where the flat card touches no other object: rings around where it is, closest first. (Pushing
            // away from one neighbour drove a card between two dice into the other one and back.)
            for (var radius = 0f; radius <= ClearSearchRadius; radius += ClearSearchStep)
            {
                var spots = radius == 0f ? 1 : 16;
                for (var i = 0; i < spots; i++)
                {
                    var angle = i * Mathf.PI * 2f / spots;
                    var spot = TableGeometry.ClampToDropArea(start + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                    if (!TouchesOtherObject(view, spot, half, flat))
                        return spot;
                }
            }
            return start;
        }

        private bool TouchesOtherObject(ObjectView view, Vector3 at, Vector3 half, Quaternion rotation)
        {
            // A hair of clearance, so the card does not start out touching its neighbour.
            var count = Physics.OverlapBoxNonAlloc(at, half + new Vector3(0.002f, 0f, 0.002f), _clearHits, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
                if (_clearHits[i].attachedRigidbody != null && _clearHits[i].attachedRigidbody != view.Body)
                    return true;
            return false;
        }

        /// <summary>A body counts as resting when asleep, or when it has barely moved for <see cref="StillSeconds"/>.</summary>
        public const float StillSeconds = 0.25f;
        public const float StillSpeed = 0.004f;
        public const float StillSpin = 0.08f;
        private readonly Dictionary<string, float> _stillSince = new Dictionary<string, float>();

        /// <summary>
        /// Asleep, or nearly still for a while: PhysX can take a long time to put a slowly creeping body to sleep, and
        /// until then the state would not learn where the object is.
        /// </summary>
        private bool IsResting(ObjectView view)
        {
            if (view.Body.IsSleeping())
            {
                _stillSince.Remove(view.Id);
                return true;
            }
            if (view.Body.linearVelocity.magnitude > StillSpeed || view.Body.angularVelocity.magnitude > StillSpin)
            {
                _stillSince.Remove(view.Id);
                return false;
            }
            if (!_stillSince.TryGetValue(view.Id, out var since))
            {
                _stillSince[view.Id] = Time.time;
                return false;
            }
            return Time.time - since >= StillSeconds;
        }

        /// <summary>
        /// Reports <paramref name="view"/>'s pose when it next comes to rest, even if it falls asleep before a frame
        /// sees it awake (a slow frame can drop, land and sleep a card within one frame's physics steps).
        /// </summary>
        public void ReportWhenResting(ObjectView view)
        {
            if (view != null && !_awakeSince.ContainsKey(view.Id))
                _awakeSince[view.Id] = Time.time;
        }

        /// <summary>The player picked it up: a later rest may be laid flat again.</summary>
        public void ForgetRest(string objectId) => _laidFlat.Remove(objectId);

        private static bool RestsOnFace(Meshes.DiceGeometry geometry, Quaternion rotation)
        {
            foreach (var normal in geometry.ValueNormals)
                if (Vector3.Dot(rotation * normal, Vector3.down) > 0.995f)
                    return true;
            return false;
        }

        /// <summary>Sends a Move when the view's pose differs from the state's. Returns true if one was sent.</summary>
        public bool ReportPose(ObjectView view)
        {
            // A hand card's standing pose is only how it is shown; another player's carried object is theirs to report.
            if (view.InHand || view.RemoteHolder != null)
                return false;
            var t = view.Model.Transform;
            var position = view.transform.position;
            var rotation = view.transform.rotation;
            if (Vector3.Distance(position, t.Position.ToUnity()) < PositionTolerance &&
                Quaternion.Angle(rotation, t.Rotation.ToRotation()) < RotationToleranceDegrees)
                return false;
            int? dieValue = null;
            if (view.Model is Die die)
                dieValue = Meshes.DiceGeometry.Get(die.Faces).ValueFor(rotation);
            _reportingId = view.Id;
            try
            {
                return Execute(new MoveCommand
                {
                    ObjectId = view.Id,
                    Position = position.ToCore(),
                    Rotation = rotation.eulerAngles.ToCore(),
                    DieValue = dieValue,
                    Actor = LocalSeat,
                }).Ok;
            }
            finally
            {
                _reportingId = null;
            }
        }

        /// <summary>A short kinematic tumble that lands on the value the engine already chose.</summary>
        private IEnumerator AnimateRoll(ObjectView view, int value)
        {
            view.Animating = true;
            view.Body.isKinematic = true;
            var start = view.transform.position;
            var startRotation = view.transform.rotation;
            var geometry = Meshes.DiceGeometry.Get(((Die)view.Model).Faces);
            var yaw = UnityEngine.Random.Range(0f, 360f);
            var end = geometry.RotationFor(value, yaw);
            var spin = UnityEngine.Random.onUnitSphere;
            for (var t = 0f; t < RollSeconds; t += Time.deltaTime)
            {
                if (view == null)
                    yield break;
                var k = t / RollSeconds;
                var height = 4f * k * (1f - k) * 0.06f;
                view.transform.position = start + Vector3.up * height;
                var tumble = Quaternion.AngleAxis(720f * (1f - k), spin);
                view.transform.rotation = Quaternion.Slerp(startRotation, tumble * end, k);
                yield return null;
            }
            if (view == null)
                yield break;
            view.transform.SetPositionAndRotation(start, end);
            view.Animating = false;
            view.Body.isKinematic = view.Pinned;
            ReportPose(view);
        }

        private void Apply(IReadOnlyList<StateEvent> events)
        {
            foreach (var e in events)
            {
                switch (e)
                {
                    case ObjectAdded added:
                        if (Engine.Find(added.ObjectId) is { Container: null } obj)
                            Spawn(obj);
                        break;
                    case TableSettingsChanged _:
                        foreach (var each in _views.Values)
                            if (each != null)
                                ApplyPhysicsMode(each);
                        break;
                    case DieRolled rolled:
                        if (ViewOf(rolled.ObjectId) is { } die && isActiveAndEnabled)
                            StartCoroutine(AnimateRoll(die, rolled.Value));
                        break;
                    case ObjectRemoved removed:
                        Despawn(removed.ObjectId);
                        break;
                    case ContainerChanged changed when changed.To != null:
                        Despawn(changed.ObjectId);
                        break;
                    case ContainerChanged changed:
                        if (Engine.Find(changed.ObjectId) is { } taken)
                            Spawn(taken);
                        break;
                    case ObjectUpdated updated:
                        if (ViewOf(updated.ObjectId) is { } view && Engine.Find(updated.ObjectId) is { } model)
                        {
                            view.Rebind(model);
                            ObjectViewFactory.Refresh(view, Assets);
                            // A pose reported from this view's own physics is already on screen; snapping would stop
                            // the body (velocity reset) and fight the simulation.
                            // Hand cards are placed by the hand presentation (they glide), not snapped to the state's flat slot.
                            if (updated.ObjectId != _reportingId && HandZoneOf(updated.ObjectId) == null)
                                view.SnapToState();
                        }
                        break;
                }
            }
            PresentHands();
        }

        private void Spawn(TableObject obj)
        {
            if (_views.ContainsKey(obj.Id))
                return;
            var view = ObjectViewFactory.Create(obj, Assets, _objectsRoot != null ? _objectsRoot : transform);
            view.ImpactHandler = OnImpact;
            ApplyPhysicsMode(view);
            _views[obj.Id] = view;
        }

        private void Despawn(string id)
        {
            if (_views.TryGetValue(id, out var view))
            {
                _views.Remove(id);
                if (view != null)
                {
                    view.gameObject.SetActive(false);
                    Destroy(view.gameObject);
                }
            }
        }

        // ---- physics modes (G19) ---------------------------------------------------------------------------------------

        public PhysicsMode PhysicsMode => Engine?.State.Physics ?? PhysicsMode.Full;

        /// <summary>
        /// Full: plain physics. Semi-lock: nothing tips over (rotation about the table's horizontal axes is frozen), but
        /// dice keep tumbling so a throw still rolls. Locked: every body is kinematic; drops are set down in place.
        /// </summary>
        private void ApplyPhysicsMode(ObjectView view)
        {
            var mode = PhysicsMode;
            view.TablePinned = mode == PhysicsMode.Locked;
            view.Body.constraints = mode == PhysicsMode.SemiLock && !(view.Model is Die)
                ? RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ
                : RigidbodyConstraints.None;
            if (view.Held || view.Animating || view.InHand || view.RemoteHolder != null || !view.Renderer.enabled)
                return;
            if (view.Body.isKinematic != view.Pinned)
            {
                view.Body.isKinematic = view.Pinned;
                if (!view.Body.isKinematic)
                    view.Body.WakeUp();
            }
        }

        /// <summary>
        /// Where <paramref name="view"/> comes to rest straight below where it is: on the felt, or on top of whatever lies
        /// under it. Used to set objects down when the table's physics is locked.
        /// </summary>
        public Vector3 RestingSpot(ObjectView view)
        {
            Physics.SyncTransforms();
            var bounds = view.Renderer.bounds;
            var count = Physics.BoxCastNonAlloc(new Vector3(bounds.center.x, 2f, bounds.center.z),
                new Vector3(bounds.extents.x * 0.9f, 0.0005f, bounds.extents.z * 0.9f), Vector3.down, _supportHits, Quaternion.identity, 3f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var top = 0f;
            for (var i = 0; i < count; i++)
            {
                var hit = _supportHits[i];
                if (hit.distance <= 0f || hit.rigidbody == view.Body || hit.collider.GetComponentInParent<ObjectView>() is { Held: true })
                    continue;
                top = Mathf.Max(top, hit.point.y);
            }
            var p = view.transform.position;
            return new Vector3(p.x, p.y - bounds.min.y + top + 0.001f, p.z);
        }

        private void SpawnZones()
        {
            var seats = Engine.State.Players.Seats;
            foreach (var zone in Engine.State.Zones)
            {
                // A hand is drawn in its owner's colour, a hidden zone's fog in the colour of the first seat that sees into it.
                var owner = zone switch
                {
                    HandZone hand => seats.FirstOrDefault(s => s.Id == hand.Owner),
                    HiddenZone hidden => seats.FirstOrDefault(s => hidden.VisibleTo.Contains(s.Id)),
                    _ => null,
                };
                var parent = ViewOf(zone.Parent)?.transform ?? (_objectsRoot != null ? _objectsRoot : transform);
                var view = ZoneViewFactory.Create(zone, ZoneViewFactory.SeatColor(owner?.Color), Assets, parent);
                if (view == null)
                    continue;
                _zoneViews.Add(view);
                if (zone is HiddenZone hiddenZone && view.transform.Find(ZoneViewFactory.FogName) is { } fog)
                    _fogs.Add((hiddenZone, fog.gameObject));
            }
        }

        /// <summary>
        /// Removes every view. They are deactivated first: Destroy is deferred, and colliders still alive during the next
        /// physics step would overlap the freshly loaded objects and shove them apart.
        /// </summary>
        private void Clear()
        {
            foreach (var zone in _zoneViews)
                if (zone != null)
                {
                    zone.SetActive(false);
                    Destroy(zone);
                }
            _zoneViews.Clear();
            _fogs.Clear();
            foreach (var view in _views.Values)
                if (view != null)
                {
                    view.gameObject.SetActive(false);
                    Destroy(view.gameObject);
                }
            _views.Clear();
            _awakeSince.Clear();
        }
    }
}
