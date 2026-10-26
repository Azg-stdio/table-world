using System.Collections.Generic;
using TableWorld.View.Settings;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The in-game controls list (TTS's Help menu), opened with F1 or ? or the Controls button. The bindings below are
    /// the ones <see cref="TableInteraction"/>, <see cref="TableCamera"/> and the HUD implement; keep them in step with
    /// <c>docs/tts-interactions.md</c>. Styling lives in Hud.uss (help-*).
    /// </summary>
    public sealed class ControlsHelp
    {
        public static readonly (string Group, (string Keys, string What)[] Rows)[] Sections =
        {
            ("Camera", new[]
            {
                ("Right mouse drag on the table", "Orbit"),
                ("Middle mouse drag, or {PanForward} {PanLeft} {PanBack} {PanRight} / arrows", "Pan"),
                ("Mouse wheel", "Zoom"),
                ("{QuickZoom}, or middle click on the table", "Quick zoom to the pointer (again to go back)"),
                ("{ResetCamera}", "Back to your seat's view"),
                ("{TopDown}", "Top-down view (again to go back)"),
                ("{RotateLeft} / {RotateRight} over nothing", "Turn the camera"),
                ("Ctrl + 1–9 / Shift + 1–9", "Save / load a camera view"),
            }),
            ("Moving things", new[]
            {
                ("Left drag", "Pick up and move; release to drop (release while moving to throw)"),
                ("Drag a deck or bag at once", "Take its top card or an item"),
                ("Press a deck or bag, hold still, then drag", "Lift the whole deck or bag (also Shift + drag)"),
                ("Click", "Select (outlined in your colour): keys like {RotateLeft} / {RotateRight}, {Flip}, {Roll}, {Lock} act on the selection until you click elsewhere"),
                ("Left drag on empty table", "Box select; drag one of them to carry them all"),
                ("Mouse wheel while holding", "Raise or lower what you hold"),
                ("Right mouse while holding", "Tap down (lower it to the table)"),
                ("Right click another object while holding", "Add it to what you hold"),
                ("Alt + right click while holding", "Drop the last one added, or the bottom card of a held deck"),
                ("{RotateLeft} / {RotateRight}", "Rotate: tap = next 15° step, hold = turn smoothly"),
                ("Alt + {RotateLeft} / {RotateRight}", "Tilt on the other axis"),
                ("{Flip}, or middle click", "Flip"),
                ("Alt + middle click", "Flip end over end"),
                ("Hold {Grow} / {Shrink}", "Scale"),
                ("{PlaceUnder} while holding, over another object", "Place it underneath"),
                ("Hold {Nudge}", "Nudge nearby objects away from the pointer"),
            }),
            ("Looking", new[]
            {
                ("Hold Alt", "Close-up of what is under the pointer (wheel zooms, {RotateLeft} / {RotateRight} turns)"),
                ("Hold Alt + Shift", "Peek at the underside (others are told when you peek at something face down)"),
                ("Hover", "Name, then description"),
                ("Click a PDF", "Read it in a window (arrows or Page Up/Down turn pages; right click turns the page on the table for everyone)"),
            }),
            ("Cards, decks, bags, dice", new[]
            {
                ("Type a number over a deck or bag", "Draw that many to your hand (1 then 2 quickly = 12)"),
                ("{Roll}", "Shuffle a deck or bag, roll a die; rolls every selected or held die at once. Press it again and again to toss dice higher"),
                ("Shake a held deck or bag", "Shuffle it (a few quick strokes back and forth)"),
                ("{Group}", "Group cards into a deck"),
                ("{Lock}", "Lock or unlock"),
                ("Number keys over an object with states", "Switch state"),
                ("Right click", "Menu: deal, search, cut, split, spread, set a die, name, colour, …"),
            }),
            ("Your hand", new[]
            {
                ("Hover a card at the bottom of the screen", "Raise and enlarge it"),
                ("Drag it out", "Pick it up (in a game: drop it on the table to play it)"),
                ("Release over the hand", "Put a card into your hand at that spot"),
                ("Hide hand / Show hand", "Sink the hand to a thin strip"),
            }),
            ("Playing a game", new[]
            {
                ("Drag a card from your hand to the table", "Played, when the rules have a play for it (a refused play comes back, with the reason); otherwise put down like any card"),
                ("Drag the pile the rules draw from", "Draw"),
                ("Buttons at the top and in game panels", "The game's actions"),
            }),
            ("Table", new[]
            {
                ("Ctrl + C / Ctrl + V", "Copy / paste"),
                ("{Delete}", "Delete"),
                ("Ctrl + Z / Ctrl + Y", "Undo / redo (playing alone)"),
                ("Numpad 1–9, 0", "Scripting keys 1–10: a scripted game may give them actions (TTS scripting buttons)"),
                ("Right click the table → Rewind", "Host: step the whole table back in time, and forward again"),
                ("Right click the table → Grid", "Host: squares or hexes, snapping, cell size, lines"),
                ("Draw / Arrow / Erase (left tool bar)", "Draw on the table in your colour, drag an arrow, rub drawings out; Esc goes back to Grab"),
                ("Gizmo (left tool bar), or right click → Exact position…", "Type an object's exact position, height, turn, tilt and scale"),
                ("{QuickSave} / {QuickLoad}", "Quick save / quick load"),
                ("Tab + click", "Ping"),
                ("Tab + drag", "Measure"),
                ("{Blindfold}", "Blindfold on or off (everyone is told)"),
                ("Blind round (left tool bar)", "Until the reveal, what each player moves is hidden from the others; Reveal shows it all"),
                ("Right click a card → Show to everyone", "The card comes up big on every screen, to read it out; click it away"),
                ("Calculator (left tool bar)", "A pocket calculator for totals and damage, only on your screen"),
                ("Right click a piece → Hide from others", "Only you see it (a secret bid or chip); Show to everyone brings it back"),
                ("F1 or ?", "This list"),
                ("Notes (top bar)", "The table's notebook: shared pages, and private ones for your seat"),
                ("Escape", "Close what is open; with nothing open, the menu (settings, leave, quit)"),
            }),
            ("Chat", new[]
            {
                ("Enter, or click the chat", "Open the chat (Enter sends, Escape or a click on the table closes)"),
                ("/roll 3d6+2 (or /r d20)", "Roll dice in the chat for everyone to see"),
                ("Hold {Emotes}, point, let go", "Emote: your chalker waves, laughs, shrugs … for everyone (Settings → You → Chalker face picks its personality)"),
                ("Hold {Talk}", "Talk (push to talk; Settings → Voice switches to open-when-you-speak, and has a hear-yourself test)"),
                ("Log tab", "Everything done at the table, in order"),
            }),
        };

        private readonly VisualElement _panel;
        /// <summary>Labels and their text with {Action} where a moved key goes (G31), filled in each time the list opens.</summary>
        private readonly List<(Label Label, string Text)> _keyed = new List<(Label, string)>();

        public ControlsHelp(VisualElement root)
        {
            _panel = root.Q("helpPanel");
            var list = root.Q<ScrollView>("helpList");
            root.Q<Button>("helpClose").clicked += Hide;
            root.Q<Button>("helpButton").clicked += Toggle;
            foreach (var (group, rows) in Sections)
            {
                var title = new Label(group);
                title.AddToClassList("help-group");
                list.Add(title);
                foreach (var (keys, what) in rows)
                {
                    var row = new VisualElement();
                    row.AddToClassList("help-row");
                    var k = new Label(keys);
                    k.AddToClassList("help-keys");
                    var w = new Label(what);
                    _keyed.Add((k, keys));
                    _keyed.Add((w, what));
                    w.AddToClassList("help-what");
                    row.Add(k);
                    row.Add(w);
                    list.Add(row);
                }
            }
        }

        public bool Visible => _panel != null && !_panel.ClassListContains("hidden");

        public void Toggle()
        {
            if (Visible)
                Hide();
            else
                Show();
        }

        public void Show()
        {
            foreach (var (label, text) in _keyed)
                label.text = KeyBindings.Fill(text);
            _panel?.RemoveFromClassList("hidden");
        }

        public void Hide() => _panel?.AddToClassList("hidden");
    }
}
