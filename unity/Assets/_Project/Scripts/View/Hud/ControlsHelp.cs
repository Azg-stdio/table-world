using System.Collections.Generic;
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
                ("Middle mouse drag, or W A S D / arrows", "Pan"),
                ("Mouse wheel", "Zoom"),
                ("Z, or middle click on the table", "Quick zoom to the pointer (again to go back)"),
                ("Space", "Back to your seat's view"),
                ("P", "Top-down view (again to go back)"),
                ("Q / E over nothing", "Turn the camera"),
                ("Ctrl + 1–9 / Shift + 1–9", "Save / load a camera view"),
            }),
            ("Moving things", new[]
            {
                ("Left drag", "Pick up and move; release to drop (release while moving to throw)"),
                ("Drag a deck or bag at once", "Take its top card or an item"),
                ("Press a deck or bag, hold still, then drag", "Lift the whole deck or bag (also Shift + drag)"),
                ("Click", "Select only"),
                ("Left drag on empty table", "Box select; drag one of them to carry them all"),
                ("Mouse wheel while holding", "Raise or lower what you hold"),
                ("Right mouse while holding", "Tap down (lower it to the table)"),
                ("Right click another object while holding", "Add it to what you hold"),
                ("Alt + right click while holding", "Drop the last one added, or the bottom card of a held deck"),
                ("Q / E", "Rotate: tap = next 15° step, hold = turn smoothly"),
                ("Alt + Q / E", "Tilt on the other axis"),
                ("F, or middle click", "Flip"),
                ("Alt + middle click", "Flip end over end"),
                ("Hold + / −", "Scale"),
                ("U while holding, over another object", "Place it underneath"),
                ("Hold N", "Nudge nearby objects away from the pointer"),
            }),
            ("Looking", new[]
            {
                ("Hold Alt", "Close-up of what is under the pointer (wheel zooms, Q / E turns)"),
                ("Hold Alt + Shift", "Peek at the underside (others are told when you peek at something face down)"),
                ("Hover", "Name, then description"),
            }),
            ("Cards, decks, bags, dice", new[]
            {
                ("Type a number over a deck or bag", "Draw that many to your hand (1 then 2 quickly = 12)"),
                ("R", "Shuffle a deck or bag, roll a die; rolls every selected or held die at once"),
                ("Shake a held deck or bag", "Shuffle it (a few quick strokes back and forth)"),
                ("G", "Group cards into a deck"),
                ("L", "Lock or unlock"),
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
                ("Drag a card from your hand to the table", "Play it, if the rules allow (otherwise it comes back, with the reason)"),
                ("Drag the pile the rules draw from", "Draw"),
                ("Buttons at the top and in game panels", "The game's actions"),
            }),
            ("Table", new[]
            {
                ("Ctrl + C / Ctrl + V", "Copy / paste"),
                ("Delete", "Delete"),
                ("Ctrl + Z / Ctrl + Y", "Undo / redo (playing alone)"),
                ("Right click the table → Rewind", "Host: step the whole table back in time, and forward again"),
                ("Right click the table → Grid", "Host: squares or hexes, snapping, cell size, lines"),
                ("Draw / Arrow / Erase (left tool bar)", "Draw on the table in your colour, drag an arrow, rub drawings out; Esc goes back to Grab"),
                ("Gizmo (left tool bar), or right click → Exact position…", "Type an object's exact position, height, turn, tilt and scale"),
                ("F5 / F9", "Quick save / quick load"),
                ("Tab + click", "Ping"),
                ("Tab + drag", "Measure"),
                ("B", "Blindfold on or off (everyone is told)"),
                ("F1 or ?", "This list"),
                ("Escape", "Close what is open; with nothing open, the menu (settings, leave, quit)"),
            }),
            ("Chat", new[]
            {
                ("Enter, or click the chat", "Open the chat (Enter sends, Escape or a click on the table closes)"),
                ("/roll 3d6+2 (or /r d20)", "Roll dice in the chat for everyone to see"),
                ("Log tab", "Everything done at the table, in order"),
            }),
        };

        private readonly VisualElement _panel;

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
                _panel?.RemoveFromClassList("hidden");
        }

        public void Show() => _panel?.RemoveFromClassList("hidden");

        public void Hide() => _panel?.AddToClassList("hidden");
    }
}
