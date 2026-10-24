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
        private readonly CardImages _images = new CardImages();
        private ObjectView _searchContainer;
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
            _menu.RegisterCallback<GeometryChangedEvent>(_ => KeepMenuOnScreen());
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
            // A spectator only gets the camera and table entries.
            if (_table.Spectating)
                target = null;
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

        /// <summary>A menu opened near the bottom or right edge moves up or left so all of it shows.</summary>
        private void KeepMenuOnScreen()
        {
            var screen = _root.layout;
            var menu = _menu.layout;
            if (float.IsNaN(menu.height) || float.IsNaN(screen.height))
                return;
            const float margin = 4f;
            if (menu.yMax > screen.height - margin)
                _menu.style.top = Mathf.Max(margin, screen.height - menu.height - margin);
            if (menu.xMax > screen.width - margin)
                _menu.style.left = Mathf.Max(margin, screen.width - menu.width - margin);
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
            ZoneEntries(_menuWorldPoint);
            Item("Undo (Ctrl+Z)", () => _table.Undo());
            Item("Redo (Ctrl+Y)", () => _table.Redo());
            if (_table.MayChangeTable && !_table.Engine.State.HasRules)
            {
                // Free-play turns (G22). A rules game runs its own turns.
                var turns = _table.Engine.State.FreeTurns;
                var on = turns is { Enabled: true };
                var skip = turns?.SkipEmptyHands ?? false;
                Row("Turns", new[]
                {
                    ((on ? "" : "● ") + "Off", (Action)(() => Run(new SetTurnOrderCommand { Enabled = false }))),
                    ((on && !turns.Reverse ? "● " : "") + "In order", (Action)(() => Run(new SetTurnOrderCommand { SkipEmptyHands = skip }))),
                    ((on && turns.Reverse ? "● " : "") + "Reversed", (Action)(() => Run(new SetTurnOrderCommand { Reverse = true, SkipEmptyHands = skip }))),
                });
                if (on)
                    Item(skip ? "● Skip empty hands" : "Skip empty hands", () => Run(new SetTurnOrderCommand { Reverse = turns.Reverse, SkipEmptyHands = !skip }));
            }
            if (_table.MayChangeTable)
                GridEntries();
            if (_table.MayChangeTable)
                Row("Physics", new[] { (PhysicsMode.Full, "Full"), (PhysicsMode.SemiLock, "Semi-lock"), (PhysicsMode.Locked, "Locked") }
                    .Select(m => ((_table.PhysicsMode == m.Item1 ? "● " : "") + m.Item2, (Action)(() => Run(new SetPhysicsCommand { Mode = m.Item1 })))));
            if (_table.CanRewind)
            {
                // Rewind time (G26): back to the previous saved moment, or forward again after rewinding.
                var back = _table.RewindTarget(Time.unscaledTimeAsDouble);
                var entries = new List<(string, Action)>();
                if (back.HasValue)
                    entries.Add(($"◀ {Ago(back.Value)}", () => _table.Rewind()));
                if (_table.Timeline.CanGoForward)
                    entries.Add(("Forward ▶", () => _table.Forward()));
                if (entries.Count > 0)
                    Row("Rewind", entries);
            }
            Item("Save table (F5)", () => _table.Save(TableView.SavePath(TableView.QuickSaveName)));
            Item("Load table (F9)", () => _table.LoadFile(TableView.SavePath(TableView.QuickSaveName)));
        }

        /// <summary>Grid rows (G28): shape (or none), snapping, cell size, lines. Each choice keeps the other settings.</summary>
        private void GridEntries()
        {
            var grid = _table.Engine.State.Grid;
            GridDef With(System.Action<GridDef> change)
            {
                var copy = grid == null ? new GridDef() : Copy(grid);
                change(copy);
                return copy;
            }
            string Mark(bool on) => on ? "● " : "";
            Row("Grid", new[]
            {
                (Mark(grid == null) + "None", (Action)(() => Run(new SetGridCommand { Grid = null }))),
                (Mark(grid?.Shape == GridShape.Square) + "Squares", (Action)(() => Run(new SetGridCommand { Grid = With(g => g.Shape = GridShape.Square) }))),
                (Mark(grid?.Shape == GridShape.Hex) + "Hexes", (Action)(() => Run(new SetGridCommand { Grid = With(g => { g.Shape = GridShape.Hex; g.SizeZ = g.SizeX * 1.1547f; }) }))),
            });
            if (grid == null)
                return;
            Row("Snap", new[] { GridSnapping.Off, GridSnapping.Centers, GridSnapping.Corners, GridSnapping.Both }
                .Select(s => (Mark(grid.Snapping == s) + s, (Action)(() => Run(new SetGridCommand { Grid = With(g => g.Snapping = s) })))));
            Row("Cell", new[] { 0.025f, 0.04f, 0.05f, 0.075f }.Select(size => (Mark(Mathf.Abs(grid.SizeX - size) < 0.0005f) + $"{size * 100f:0.#} cm",
                (Action)(() => Run(new SetGridCommand { Grid = With(g => { g.SizeX = size; g.SizeZ = g.Shape == GridShape.Hex ? size * 1.1547f : size; }) })))));
            Item(grid.Lines ? "Grid lines: hide" : "Grid lines: show", () => Run(new SetGridCommand { Grid = With(g => g.Lines = !g.Lines) }));
        }

        private static GridDef Copy(GridDef g) => new GridDef
        {
            Shape = g.Shape, Lines = g.Lines, Snapping = g.Snapping, SizeX = g.SizeX, SizeZ = g.SizeZ,
            OffsetX = g.OffsetX, OffsetZ = g.OffsetZ, Color = (float[])g.Color.Clone(), Opacity = g.Opacity,
        };

        /// <summary>"0:40 ago", "12:05 ago".</summary>
        public static string Ago(double seconds)
        {
            var s = Mathf.Max(0, Mathf.RoundToInt((float)seconds));
            return $"{s / 60}:{s % 60:00} ago";
        }

        /// <summary>Takes <paramref name="count"/> pieces off a stack and sets them down beside it (G14).</summary>
        private void TakeFromStack(ObjectView view, int count)
        {
            var beside = view.transform.position + view.transform.right * 0.06f;
            Run(new TakeCommand { ContainerId = view.Id, Count = count, Position = beside.ToCore() });
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
                case var stack when Stacks.CountOf(stack) > 1:
                {
                    var count = Stacks.CountOf(stack);
                    Section($"Stack · {count}");
                    Row("Take", new[] { 1, 2, 5, 10 }.Where(n => n < count).Select(n => ($"{n}", (Action)(() => TakeFromStack(view, n)))));
                    Item("Split in half", () => TakeFromStack(view, count / 2));
                    break;
                }
                case Die die:
                    Section($"d{die.Faces}{(die.Value is { } shown ? $" · shows {die.Face(shown)}" : "")}");
                    var selectedDice = _interaction.Selection.Contains(view) ? _interaction.Selection.Where(v => v.Model is Die).ToList() : null;
                    if (selectedDice != null && selectedDice.Count > 1)
                        Item($"Roll {selectedDice.Count} selected dice (R)", () => _interaction.RollDice(selectedDice));
                    else
                        Item("Roll (R)", () => _interaction.RollOrShuffle(view));
                    // Labelled faces (G21) are listed one per line; numbers fit on one row.
                    var setValue = Enumerable.Range(1, die.Faces).Select(v => (die.Face(v), (Action)(() => Run(new SetDieValueCommand { ObjectId = die.Id, Value = v }))));
                    if (die.Numeric)
                        Row("Set value", setValue);
                    else
                    {
                        Section("Set face");
                        foreach (var (label, set) in setValue)
                            Item(label, set);
                    }
                    break;
                case Counter counter:
                    Section($"Counter · {counter.Value}");
                    Row("Add", new[] { -10, -1, 1, 10 }.Select(n => (n > 0 ? $"+{n}" : $"−{-n}", (Action)(() => _interaction.AddToCounter(view, n)))));
                    Item("Reset to 0", () => Run(new SetCounterCommand { ObjectId = counter.Id, Value = 0 }));
                    break;
                case Clock clock:
                    Section(clock.Mode == ClockMode.Clock ? "Clock" : $"{clock.Mode} · {view.GetComponent<Readout>()?.Text}");
                    if (clock.Mode != ClockMode.Clock)
                    {
                        Item(clock.Running ? "Pause" : "Start", () => _interaction.StartOrPause(view));
                        Item("Reset", () => _interaction.ResetClock(view));
                    }
                    Row("Timer", new[] { (60f, "1 min"), (300f, "5 min"), (600f, "10 min") }.Select(t => (t.Item2, (Action)(() => _interaction.SetTimer(view, t.Item1)))));
                    Row("Mode", new[] { ClockMode.Clock, ClockMode.Stopwatch, ClockMode.Timer }.Select(m => ((clock.Mode == m ? "● " : "") + m,
                        (Action)(() => Run(new SetClockCommand { ObjectId = clock.Id, Mode = m, Seconds = m == ClockMode.Timer ? clock.Duration : 0f, Running = false })))));
                    break;
                case Bag bag:
                    Section($"{bag.Type} · {_table.Engine.ContentsOf(bag.Id).Count()} inside");
                    Item("Take 1 to hand", () => _interaction.DrawOrSwitch(view, 1));
                    Item("Search", () => OpenSearch(view));
                    if (!(bag is InfiniteBag))
                        Item("Shuffle (R)", () => _interaction.RollOrShuffle(view));
                    break;
            }

            ZoneEntries(view.transform.position);

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

        /// <summary>Layout and randomize zones under <paramref name="point"/> (G18): Lay out / Randomize.</summary>
        private void ZoneEntries(Vector3 point)
        {
            if (_table.Spectating)
                return;
            foreach (var zone in _table.Engine.State.Zones)
            {
                if (!(zone is LayoutZone || zone is RandomizeZone) ||
                    !Geometry.Contains(zone, new Vec3(point.x, zone.Transform.Position.Y, point.z)))
                    continue;
                var name = string.IsNullOrEmpty(zone.Name) ? (zone is LayoutZone ? "Layout zone" : "Randomize zone") : zone.Name;
                Section(name);
                var id = zone.Id;
                if (zone is LayoutZone)
                    Item("Lay out", () => Run(new ArrangeZoneCommand { ZoneId = id }));
                else
                    Item("Randomize", () => Run(new RandomizeZoneCommand { ZoneId = id }));
            }
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
            _searchContainer = container;
            _searchList.Clear();
            var grid = new VisualElement();
            grid.AddToClassList("search-grid");
            _searchList.Add(grid);
            if (result.DeckContents != null)
            {
                var deck = (Deck)container.Model;
                _searchTitle.text = $"Search: {deck.Name ?? "Deck"} ({result.DeckContents.Count} cards, top first)";
                for (var i = 0; i < result.DeckContents.Count; i++)
                {
                    var card = result.DeckContents[i];
                    var face = _images.Face(_table.Assets, card.FaceAsset ?? deck.FaceAsset, card.FaceIndex);
                    grid.Add(SearchCell(container, card.Name ?? card.Id ?? $"Card {i + 1}", card.Id, i, face));
                }
            }
            else
            {
                _searchTitle.text = $"Search: {container.Model.Name ?? "Bag"} ({result.BagContents.Count} items)";
                foreach (var id in result.BagContents)
                    grid.Add(SearchCell(container, _table.Engine.Find(id)?.Name ?? _table.Engine.Find(id)?.Type ?? id, id, -1, null));
            }
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

        /// <summary>
        /// One card (face image and name) or bag item (name) in the Search window. Pressing it takes it out and puts it in
        /// your hand-held grip, so it follows the pointer onto the table (TTS: drag a card out of the search window).
        /// </summary>
        private VisualElement SearchCell(ObjectView container, string label, string entryId, int index, Sprite face)
        {
            var cell = new VisualElement { tooltip = label };
            cell.AddToClassList(face != null ? "search-card" : "search-item");
            if (face != null)
            {
                var image = new VisualElement();
                image.AddToClassList("search-card-image");
                // Card faces are runtime images, so they can only be set from code.
                image.style.backgroundImage = new StyleBackground(face);
                cell.Add(image);
            }
            var name = new Label(label);
            name.AddToClassList("search-card-name");
            cell.Add(name);
            cell.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0)
                    DragOutOfSearch(container, entryId, index);
            });
            return cell;
        }

        /// <summary>Takes an entry out beside the container and picks it up, so it follows the pointer. Returns the taken object.</summary>
        public ObjectView DragOutOfSearch(ObjectView container, string entryId, int index)
        {
            if (_interaction.Held != null)
                return null;
            var beside = container.transform.position + new Vector3(0f, 0.04f, -0.12f);
            var command = new TakeCommand { ContainerId = container.Id, Actor = _table.LocalSeat, Position = beside.ToCore() };
            if (entryId != null)
                command.EntryId = entryId;
            else
                command.Index = Math.Max(0, index);
            var result = _table.Execute(command);
            if (!result.Ok || !(_table.ViewOf(result.CreatedId) is { } taken))
                return null;
            _interaction.Grab(taken, false);
            if (SearchOpen && container != null && _table.ViewOf(container.Id) != null)
                OpenSearch(container);
            return taken;
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
            _lookCaption.text = view.HideFace ? "A card in another player's hand" : (peek ? "Peek: " : "") + (view.Model.Name ?? view.Model.Id);
        }

        /// <summary>
        /// The hover tooltip's first line(s): the name, and for a deck or bag how many cards or items it holds (TTS shows the
        /// count). Null for an unnamed card, and for a card in someone else's hand (its name would give it away).
        /// </summary>
        public static string TooltipText(ObjectView view, Core.Engine.TableEngine engine)
        {
            if (view.HideFace)
                return null;
            var model = view.Model;
            string count = null;
            switch (model)
            {
                case Core.Rules.Deck deck:
                    count = deck.Cards.Count == 1 ? "1 card" : $"{deck.Cards.Count} cards";
                    break;
                case Core.Rules.InfiniteBag _:
                    count = "endless";
                    break;
                case var stack when Core.Rules.Stacks.CountOf(stack) > 1:
                    count = $"stack of {Core.Rules.Stacks.CountOf(stack)}";
                    break;
                case Core.Rules.Bag bag:
                    var items = engine.ContentsOf(bag.Id).Count();
                    count = items == 1 ? "1 item" : $"{items} items";
                    break;
            }
            var name = model.Name ?? (model is Core.Rules.Card ? null : model.Type);
            return count == null ? name : $"{name}\n{count}";
        }

        private void UpdateTooltip()
        {
            var view = _interaction.TooltipView;
            var seconds = _interaction.TooltipSeconds;
            var show = view != null && seconds >= TooltipNameSeconds && _interaction.Held == null && _interaction.Looking == null && !MenuOpen
                       && !(view.Model.IsFaceUp == false && view.Model.TtsExtra?.Value<bool?>("HideWhenFaceDown") == true);
            var text = view == null ? null : TooltipText(view, _table.Engine);
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
