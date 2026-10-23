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
            Loaded?.Invoke();
            return true;
        }

        /// <summary>Raised after a table is loaded (the HUD rebuilds its seats).</summary>
        public event Action Loaded;

        // ---- networking hooks (T3.2) --------------------------------------------------------------------------

        /// <summary>When set (networked play), commands go through it instead of straight to <see cref="Game"/>.</summary>
        public Func<Command, CommandResult> Router { get; set; }

        /// <summary>When set, the seat this machine plays (a network client); otherwise hot seat.</summary>
        public string SeatOverride { get; set; }

        /// <summary>When set, physics poses are only reported for objects it accepts (a client reports what it touched).</summary>
        public Func<ObjectView, bool> ReportsPoseOf { get; set; }

        public bool Networked => Router != null;

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
            Loaded?.Invoke();
        }

        private string _assetsJson;

        /// <summary>An asset's bytes arrived: reload its texture and repaint every object.</summary>
        public void RefreshAsset(string assetId)
        {
            if (Assets == null)
                return;
            Assets.Forget(assetId);
            foreach (var view in _views.Values)
                if (view != null)
                    ObjectViewFactory.Refresh(view, Assets);
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
            if (result.Ok)
                Apply(result.Events);
            else
                Debug.LogWarning($"[TableView] {command.GetType().Name} rejected: {result.Error}");
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
        /// Seat that commands from this machine act as. Solo (hot seat): the seat whose turn it is during a game,
        /// otherwise the first seat. Multiplayer (M3) will pin it per client.
        /// </summary>
        public string LocalSeat => SeatOverride ?? (Game?.Session is { Started: true, Over: false } session
            ? session.CurrentSeat
            : Engine?.State.Players.Seats.FirstOrDefault()?.Id);

        /// <summary>
        /// Physics moves free objects; the state learns their final pose here. When a body falls asleep (or has been
        /// awake too long) and its pose differs from the state, a <see cref="MoveCommand"/> records it.
        /// </summary>
        private void LateUpdate()
        {
            if (Engine == null)
                return;
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
                if (!view.Body.IsSleeping())
                {
                    if (!_awakeSince.TryGetValue(view.Id, out var since))
                        _awakeSince[view.Id] = Time.time;
                    else if (Time.time - since > ReportAwakeAfterSeconds)
                    {
                        ReportPose(view);
                        _awakeSince[view.Id] = Time.time;
                    }
                    continue;
                }
                if (_awakeSince.Remove(view.Id))
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
                if (!RestsOnFace(Meshes.DiceGeometry.Get(die.Faces), body.rotation))
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
            view.Body.isKinematic = view.Model.Locked;
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
                            if (updated.ObjectId != _reportingId)
                                view.SnapToState();
                        }
                        break;
                }
            }
        }

        private void Spawn(TableObject obj)
        {
            if (_views.ContainsKey(obj.Id))
                return;
            _views[obj.Id] = ObjectViewFactory.Create(obj, Assets, _objectsRoot != null ? _objectsRoot : transform);
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

        private void SpawnZones()
        {
            var seats = Engine.State.Players.Seats;
            foreach (var zone in Engine.State.Zones)
            {
                var owner = zone is HandZone hand ? seats.FirstOrDefault(s => s.Id == hand.Owner) : null;
                var parent = ViewOf(zone.Parent)?.transform ?? (_objectsRoot != null ? _objectsRoot : transform);
                var view = ZoneViewFactory.Create(zone, ZoneViewFactory.SeatColor(owner?.Color), Assets, parent);
                if (view != null)
                    _zoneViews.Add(view);
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
