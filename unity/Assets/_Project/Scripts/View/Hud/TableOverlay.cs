using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The HUD layer over the table (docs/tts-interactions.md §3–6): right-click context menus, the Search window, the
    /// Alt look / peek close-up, the hover tooltip, the box-select rectangle and the measuring label. Menu entries call
    /// the same <see cref="TableInteraction"/> gestures as the keys, and are exposed through <see cref="MenuEntries"/> for tests.
    /// Positions that follow the pointer are the only inline styles; everything else is in Hud.uss.
    /// </summary>
    public sealed class TableOverlay
    {
        public const float TooltipNameSeconds = 0.6f;
        public const float TooltipDescriptionSeconds = 1.6f;
        private const string Hidden = "hidden";

        private readonly VisualElement _root;
        private readonly TableView _table;
        private readonly TableInteraction _interaction;
        private readonly TableCamera _camera;
        private readonly LookCamera _look;
        private readonly VisualElement _menu;
        private readonly VisualElement _box;
        private readonly Label _tooltip;
        private readonly Label _measure;
        private readonly VisualElement _lookPanel;
        private readonly VisualElement _lookImage;
        private readonly Label _lookCaption;
        private readonly VisualElement _search;
        private readonly Label _searchTitle;
        private readonly ScrollView _searchList;
        private readonly List<(string Label, Action Run)> _entries = new List<(string, Action)>();
        private Vector3 _menuWorldPoint;
        private ObjectView _menuTarget;

        public TableOverlay(VisualElement root, TableView table, TableInteraction interaction, TableCamera camera, LookCamera look)
        {
            _root = root;
            _table = table;
            _interaction = interaction;
            _camera = camera;
            _look = look;
            _menu = root.Q("contextMenu");
            _box = root.Q("boxRect");
            _tooltip = root.Q<Label>("tooltip");
            _measure = root.Q<Label>("measure");
            _lookPanel = root.Q("lookPanel");
            _lookImage = root.Q("lookImage");
            _lookCaption = root.Q<Label>("lookCaption");
            _search = root.Q("searchPanel");
            _searchTitle = root.Q<Label>("searchTitle");
            _searchList = root.Q<ScrollView>("searchList");
            root.Q<Button>("searchClose").clicked += CloseSearch;

            _interaction.ContextMenuRequested += OpenMenu;
            _interaction.LookChanged += OnLook;
            _root.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!_menu.ClassListContains(Hidden) && !_menu.worldBound.Contains(e.position))
                    CloseMenu();
            }, TrickleDown.TrickleDown);
        }

        public bool MenuOpen => !_menu.ClassListContains(Hidden);
        public bool SearchOpen => !_search.ClassListContains(Hidden);

        /// <summary>Labels of the open menu's entries, in order (tests).</summary>
        public IReadOnlyList<string> MenuEntries => _entries.Select(e => e.Label).ToList();

        /// <summary>Runs the open menu's entry with this label, as a click would (tests).</summary>
        public bool Choose(string label)
        {
            var entry = _entries.FirstOrDefault(e => e.Label == label);
            if (entry.Run == null)
                return false;
            entry.Run();
            return true;
        }

        public void Update()
        {
            UpdateBox();
            UpdateTooltip();
            UpdateMeasure();
            if (_look.Target != null)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f)
                    _look.Zoom(Mathf.Sign(mouse.scroll.ReadValue().y));
                if (keyboard != null && keyboard.qKey.wasPressedThisFrame) _look.Turn(-TableInteraction.RotateStep);
                if (keyboard != null && keyboard.eKey.wasPressedThisFrame) _look.Turn(TableInteraction.RotateStep);
            }
        }

        // ---- context menu -----------------------------------------------------------------------------------------

        /// <summary>Opens the menu for <paramref name="target"/> (null: the global table menu) at a screen point.</summary>
        public void OpenMenu(ObjectView target, Vector2 screen)
        {
            _menuTarget = target;
            _menuWorldPoint = WorldPointUnder(screen);
            _menu.Clear();
            _entries.Clear();
            if (target == null)
                BuildGlobalMenu();
            else
                BuildObjectMenu(target);
            var panelPoint = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, Screen.height - screen.y));
            _menu.style.left = panelPoint.x;
            _menu.style.top = panelPoint.y;
            _menu.RemoveFromClassList(Hidden);
        }

        public void CloseMenu()
        {
            _menu.AddToClassList(Hidden);
            _entries.Clear();
        }

        private void BuildGlobalMenu()
        {
            Title("Table");
            Item(_camera.IsTopDown ? "Camera: back to 3rd person (P)" : "Camera: top-down (P)", () => _camera.ToggleTopDown());
            Item("Reset camera (Space)", () => _camera.ResetView());
            Item("Save camera 1 (Ctrl+1)", () => _camera.Save(1));
            Item("Load camera 1 (Shift+1)", () => _camera.Load(1));
            Item("Paste (Ctrl+V)", () => _interaction.Paste(_menuWorldPoint));
            Item("Undo (Ctrl+Z)", () => _table.Undo());
            Item("Redo (Ctrl+Y)", () => _table.Redo());
            Item("Save table (F5)", () => _table.Save(TableView.SavePath(TableView.QuickSaveName)));
            Item("Load table (F9)", () => _table.LoadFile(TableView.SavePath(TableView.QuickSaveName)));
        }

        private void BuildObjectMenu(ObjectView view)
        {
            var model = view.Model;
            Title(string.IsNullOrEmpty(model.Name) ? $"{model.Type} {model.Id}" : model.Name);

            switch (model)
            {
                case Deck deck:
                    Section($"Deck · {deck.Cards.Count} cards");
                    Item("Draw 1 to hand", () => _interaction.DrawOrSwitch(view, 1));
                    Item("Draw 5 to hand", () => _interaction.DrawOrSwitch(view, 5));
                    Item("Deal 1 to everyone", () => Run(new DealCommand { DeckId = deck.Id, Count = 1 }));
                    Item("Shuffle (R)", () => _interaction.RollOrShuffle(view));
                    Item("Search", () => OpenSearch(view));
                    Item("Cut", () => Run(new CutCommand { DeckId = deck.Id }));
                    Row("Split into", new[] { 2, 3, 4 }.Where(n => n <= deck.Cards.Count).Select(n => ($"{n}", (Action)(() => Run(new SplitCommand { DeckId = deck.Id, Piles = n })))));
                    Item("Spread", () => Run(new SpreadCommand { DeckId = deck.Id }));
                    break;
                case Die die:
                    Section($"d{die.Faces}{(die.Value.HasValue ? $" · shows {die.Value}" : "")}");
                    Item("Roll (R)", () => _interaction.RollOrShuffle(view));
                    Row("Set value", Enumerable.Range(1, die.Faces).Select(v => ($"{v}", (Action)(() => Run(new SetDieValueCommand { ObjectId = die.Id, Value = v })))));
                    break;
                case Bag bag:
                    Section($"{bag.Type} · {_table.Engine.ContentsOf(bag.Id).Count()} inside");
                    Item("Take 1 to hand", () => _interaction.DrawOrSwitch(view, 1));
                    Item("Search", () => OpenSearch(view));
                    if (!(bag is InfiniteBag))
                        Item("Shuffle (R)", () => _interaction.RollOrShuffle(view));
                    break;
            }

            if (model.States.Count > 0)
                Row("States", model.States.Keys.OrderBy(k => k).Select(k => ($"{k} {model.States[k].Name}".Trim(), (Action)(() => Run(new SetStateCommand { ObjectId = model.Id, State = k })))));

            Section("Object");
            Item("Flip (F)", () => _interaction.Flip(view));
            Item("Rotate 90° (E ×6)", () => _interaction.Rotate(view, 90f));
            Item(model.Locked ? "Unlock" : "Lock", () => _interaction.ToggleLock(view));
            Item("Copy (Ctrl+C)", () => _interaction.Copy(new[] { view }));
            Item("Duplicate", () => Run(new CloneCommand { ObjectId = model.Id }));
            Row("Scale", new[] { ("−", (Action)(() => _interaction.Scale(new[] { view }, 1f / TableInteraction.ScaleStep))), ("+", (Action)(() => _interaction.Scale(new[] { view }, TableInteraction.ScaleStep))) });
            Swatches(view);
            Rename(view);
            Item("Delete (Del)", () => _interaction.Delete(new[] { view }));
        }

        private void Run(Command command)
        {
            command.Actor = _table.LocalSeat;
            _table.Execute(command);
        }

        private void Title(string text)
        {
            var label = new Label(text);
            label.AddToClassList("menu-title");
            _menu.Add(label);
        }

        private void Section(string text)
        {
            var label = new Label(text);
            label.AddToClassList("menu-section");
            _menu.Add(label);
        }

        private void Item(string text, Action action, VisualElement into = null)
        {
            void Run()
            {
                CloseMenu();
                action();
            }
            var button = new Button(Run) { text = text };
            button.AddToClassList("menu-item");
            (into ?? _menu).Add(button);
            _entries.Add((text, Run));
        }

        /// <summary>A labelled row of small buttons; each is listed in <see cref="MenuEntries"/> as "label: value".</summary>
        private void Row(string label, IEnumerable<(string Text, Action Action)> items)
        {
            Section(label);
            var row = new VisualElement();
            row.AddToClassList("menu-row");
            _menu.Add(row);
            foreach (var (text, action) in items)
            {
                var name = $"{label}: {text}";
                void Run()
                {
                    CloseMenu();
                    action();
                }
                var button = new Button(Run) { text = text };
                button.AddToClassList("menu-item");
                row.Add(button);
                _entries.Add((name, Run));
            }
        }

        private void Swatches(ObjectView view)
        {
            Section("Tint");
            var row = new VisualElement();
            row.AddToClassList("menu-row");
            _menu.Add(row);
            var colors = new (string Name, float[] Rgb)[]
            {
                ("red", new[] { 0.86f, 0.2f, 0.18f }), ("blue", new[] { 0.2f, 0.42f, 0.9f }), ("green", new[] { 0.25f, 0.75f, 0.3f }),
                ("yellow", new[] { 0.95f, 0.85f, 0.2f }), ("white", null), ("black", new[] { 0.16f, 0.16f, 0.17f }),
            };
            foreach (var (name, rgb) in colors)
            {
                var label = $"Tint: {name}";
                void Run()
                {
                    CloseMenu();
                    _table.Execute(rgb == null
                        ? new SetPropertiesCommand { ObjectId = view.Id, ClearTint = true, Actor = _table.LocalSeat }
                        : new SetPropertiesCommand { ObjectId = view.Id, Tint = rgb, Actor = _table.LocalSeat });
                }
                var swatch = new Button(Run) { tooltip = name };
                swatch.AddToClassList("menu-swatch");
                swatch.AddToClassList("menu-swatch--" + name);
                row.Add(swatch);
                _entries.Add((label, Run));
            }
        }

        private void Rename(ObjectView view)
        {
            Section("Name and description");
            var name = new TextField { value = view.Model.Name ?? "" };
            name.AddToClassList("menu-field");
            var description = new TextField { value = view.Model.Description ?? "", multiline = true };
            description.AddToClassList("menu-field");
            _menu.Add(name);
            _menu.Add(description);
            Item("Apply name and description", () => _table.Execute(new SetPropertiesCommand
            {
                ObjectId = view.Id, Name = name.value, Description = description.value, Actor = _table.LocalSeat,
            }));
        }

        // ---- search -----------------------------------------------------------------------------------------------

        /// <summary>Lists a deck's cards (top first) or a bag's contents, each with a Take button.</summary>
        public void OpenSearch(ObjectView container)
        {
            var result = _table.Execute(new SearchCommand { ContainerId = container.Id, Actor = _table.LocalSeat });
            if (!result.Ok)
                return;
            _searchList.Clear();
            _searchTitle.text = $"Search: {container.Model.Name ?? container.Id}";
            if (result.DeckContents != null)
                foreach (var card in result.DeckContents)
                    SearchRow(container, card.Name ?? card.Id ?? $"card {card.FaceIndex}", card.Id, index: result.DeckContents.ToList().IndexOf(card));
            else
                foreach (var id in result.BagContents)
                    SearchRow(container, _table.Engine.Find(id)?.Name ?? id, id, index: -1);
            _search.RemoveFromClassList(Hidden);
        }

        public void CloseSearch() => _search.AddToClassList(Hidden);

        /// <summary>Takes an item shown in the Search window (by entry id, or by index for cards without one).</summary>
        public bool TakeFromSearch(ObjectView container, string entryId, int index)
        {
            var command = new TakeCommand { ContainerId = container.Id, Actor = _table.LocalSeat };
            if (entryId != null)
                command.EntryId = entryId;
            else
                command.Index = Math.Max(0, index);
            var ok = _table.Execute(command).Ok;
            if (ok && SearchOpen)
                OpenSearch(container);
            return ok;
        }

        private void SearchRow(ObjectView container, string label, string entryId, int index)
        {
            var row = new VisualElement();
            row.AddToClassList("search-row");
            var text = new Label(label);
            text.AddToClassList("game-ui-label");
            var take = new Button(() => TakeFromSearch(container, entryId, index)) { text = "Take" };
            take.AddToClassList("hud-button");
            row.Add(text);
            row.Add(take);
            _searchList.Add(row);
        }

        // ---- looking, tooltip, box, measure -----------------------------------------------------------------------

        private void OnLook(ObjectView view, bool peek)
        {
            _look.Show(view, peek);
            _lookPanel.EnableInClassList(Hidden, view == null);
            if (view == null)
                return;
            // A render texture is created at runtime, so it can only be set from code.
            _lookImage.style.backgroundImage = Background.FromRenderTexture(_look.Texture);
            _lookCaption.text = (peek ? "Peek: " : "") + (view.Model.Name ?? view.Model.Id);
        }

        private void UpdateTooltip()
        {
            var view = _interaction.TooltipView;
            var seconds = _interaction.TooltipSeconds;
            var show = view != null && seconds >= TooltipNameSeconds && _interaction.Held == null && _interaction.Looking == null && !MenuOpen
                       && !(view.Model.IsFaceUp == false && view.Model.TtsExtra?.Value<bool?>("HideWhenFaceDown") == true);
            var text = view == null ? null : view.Model.Name ?? (view.Model is Card c ? null : view.Model.Type);
            if (show && seconds >= TooltipDescriptionSeconds && !string.IsNullOrEmpty(view.Model.Description))
                text = $"{text}\n{view.Model.Description}";
            show &= !string.IsNullOrEmpty(text);
            _tooltip.EnableInClassList(Hidden, !show);
            if (show)
            {
                _tooltip.text = text;
                FollowPointer(_tooltip, new Vector2(16, 18));
            }
        }

        private void UpdateMeasure()
        {
            var distance = _interaction.MeasuredDistance;
            _measure.EnableInClassList(Hidden, distance == null);
            if (distance is { } d)
            {
                _measure.text = $"{d * 100f:F1} cm  ·  {d / Core.Tts.TtsUnits.TtsUnitsToMeters:F1} in (TTS)";
                FollowPointer(_measure, new Vector2(16, -28));
            }
        }

        private void UpdateBox()
        {
            var rect = _interaction.BoxRect;
            _box.EnableInClassList(Hidden, rect == null);
            if (!(rect is { } r))
                return;
            var a = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(r.xMin, Screen.height - r.yMax));
            var b = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(r.xMax, Screen.height - r.yMin));
            _box.style.left = a.x;
            _box.style.top = a.y;
            _box.style.width = b.x - a.x;
            _box.style.height = b.y - a.y;
        }

        private void FollowPointer(VisualElement element, Vector2 offset)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
                return;
            var p = mouse.position.ReadValue();
            var panel = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(p.x, Screen.height - p.y));
            element.style.left = panel.x + offset.x;
            element.style.top = panel.y + offset.y;
        }

        private Vector3 WorldPointUnder(Vector2 screen)
        {
            var camera = Camera.main;
            if (camera == null)
                return Vector3.zero;
            var ray = camera.ScreenPointToRay(screen);
            return new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var t) ? ray.GetPoint(t) : Vector3.zero;
        }
    }
}
