using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Owns the core <see cref="TableEngine"/> for this table and one <see cref="ObjectView"/> per object on the table.
    /// Everything that changes the table goes through <see cref="Execute"/>; views only follow the resulting events.
    /// </summary>
    public sealed class TableView : MonoBehaviour
    {
        [Tooltip("TextAsset under a Resources folder, loaded on Start. Empty: load nothing.")]
        [SerializeField] private string _tableResource = "Tables/sample";
        [SerializeField] private Transform _objectsRoot;
        [Tooltip("Seed for shuffles and rolls. 0 picks one from the clock.")]
        [SerializeField] private int _seed;

        private readonly Dictionary<string, ObjectView> _views = new Dictionary<string, ObjectView>();

        public TableEngine Engine { get; private set; }
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

            Clear();
            var seed = _seed != 0 ? (ulong)_seed : (ulong)DateTime.UtcNow.Ticks;
            Engine = new TableEngine(parsed.State, seed);
            Assets = new AssetLibrary(parsed.State.Assets, packageRoot);
            foreach (var obj in Engine.State.Objects.Where(o => o.Container == null))
                Spawn(obj);
            return true;
        }

        public CommandResult Execute(Command command)
        {
            var result = Engine.Execute(command);
            if (result.Ok)
                Apply(result.Events);
            else
                Debug.LogWarning($"[TableView] {command.GetType().Name} rejected: {result.Error}");
            CommandExecuted?.Invoke(command, result);
            return result;
        }

        public ObjectView ViewOf(string id) => id != null && _views.TryGetValue(id, out var view) ? view : null;

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
                    Destroy(view.gameObject);
            }
        }

        private void Clear()
        {
            foreach (var view in _views.Values)
                if (view != null)
                    Destroy(view.gameObject);
            _views.Clear();
        }
    }
}
