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
        /// <summary>The list in the interface's language (read again each time it opens).</summary>
        public static (string Group, (string Keys, string What)[] Rows)[] Sections => new (string, (string, string)[])[]
        {
            (Ui.T("Camera"), new[]
            {
                (Ui.T("Seated: right mouse drag"), Ui.T("Look around")),
                (Ui.T("Seated: mouse wheel"), Ui.T("Lean in over the table; out from sitting back lifts you out of your head")),
                (Ui.T("Lifted: right mouse drag on the table"), Ui.T("Orbit")),
                (Ui.T("Middle mouse drag, or {PanForward} {PanLeft} {PanBack} {PanRight} / arrows"), Ui.T("Pan (lifts you out of your seat)")),
                (Ui.T("Lifted: mouse wheel"), Ui.T("Zoom")),
                (Ui.T("{QuickZoom}, or middle click on the table"), Ui.T("Quick zoom to the pointer (again to go back)")),
                ("{ResetCamera}", Ui.T("Back to your seat")),
                ("{TopDown}", Ui.T("Top-down view (again to go back)")),
                ("{SeatView}", Ui.T("Sit in first person, or lift out into the free camera")),
                (Ui.T("Pointer on your cards, or {HandUp}"), Ui.T("Hold your cards up to read them ({HandUp} keeps them up)")),
                ("{Wink}", Ui.T("Wink at the face you look at (games can react to it)")),
                ("{ThumbUp}", Ui.T("Raise your thumb, even with your eyes closed (it drops when the step ends)")),
                (Ui.T("{RotateLeft} / {RotateRight} over nothing"), Ui.T("Turn the camera")),
                (Ui.T("Ctrl + 1–9 / Shift + 1–9"), Ui.T("Save / load a camera view")),
            }),
            (Ui.T("Walking rooms (karaoke)"), new[]
            {
                (Ui.T("{PanForward} {PanLeft} {PanBack} {PanRight} / arrows"), Ui.T("Walk; hold Shift to hurry")),
                (Ui.T("Hold the right mouse button"), Ui.T("Look around")),
                (Ui.T("{Sit} near a sofa"), Ui.T("Sit on the nearest free place; again, or walking, stands you up")),
                (Ui.T("Big screen bar"), Ui.T("Paste a YouTube link and press Play to show it on the screen")),
            }),
            (Ui.T("Moving things"), new[]
            {
                (Ui.T("Left drag"), Ui.T("Pick up and move; release to drop (release while moving to throw)")),
                (Ui.T("Drag a deck or bag at once"), Ui.T("Take its top card or an item")),
                (Ui.T("Press a deck or bag, hold still, then drag"), Ui.T("Lift the whole deck or bag (also Shift + drag)")),
                (Ui.T("Click"), Ui.T("Select (outlined in your colour): keys like {RotateLeft} / {RotateRight}, {Flip}, {Roll}, {Lock} act on the selection until you click elsewhere")),
                (Ui.T("Left drag on empty table"), Ui.T("Box select; drag one of them to carry them all")),
                (Ui.T("Mouse wheel while holding"), Ui.T("Raise or lower what you hold")),
                (Ui.T("Right mouse while holding"), Ui.T("Tap down (lower it to the table)")),
                (Ui.T("Right click another object while holding"), Ui.T("Add it to what you hold")),
                (Ui.T("Alt + right click while holding"), Ui.T("Drop the last one added, or the bottom card of a held deck")),
                (Ui.T("{RotateLeft} / {RotateRight}"), Ui.T("Rotate: tap = next 15° step, hold = turn smoothly")),
                (Ui.T("Alt + {RotateLeft} / {RotateRight}"), Ui.T("Tilt on the other axis")),
                (Ui.T("{Flip}, or middle click"), Ui.T("Flip")),
                (Ui.T("Alt + middle click"), Ui.T("Flip end over end")),
                (Ui.T("Hold {Grow} / {Shrink}"), Ui.T("Scale")),
                (Ui.T("{PlaceUnder} while holding, over another object"), Ui.T("Place it underneath")),
                (Ui.T("Hold {Nudge}"), Ui.T("Nudge nearby objects away from the pointer")),
            }),
            (Ui.T("Looking"), new[]
            {
                (Ui.T("Hold Alt"), Ui.T("Close-up of what is under the pointer (wheel zooms, {RotateLeft} / {RotateRight} turns)")),
                (Ui.T("Hold Alt + Shift"), Ui.T("Peek at the underside (others are told when you peek at something face down)")),
                (Ui.T("Hover"), Ui.T("Name, then description")),
                (Ui.T("Click a PDF"), Ui.T("Read it in a window (arrows or Page Up/Down turn pages; right click turns the page on the table for everyone)")),
            }),
            (Ui.T("Cards, decks, bags, dice"), new[]
            {
                (Ui.T("Type a number over a deck or bag"), Ui.T("Draw that many to your hand (1 then 2 quickly = 12)")),
                ("{Roll}", Ui.T("Shuffle a deck or bag, roll a die; rolls every selected or held die at once. Press it again and again to toss dice higher")),
                (Ui.T("Shake a held deck or bag"), Ui.T("Shuffle it (a few quick strokes back and forth)")),
                ("{Group}", Ui.T("Group cards into a deck")),
                ("{Lock}", Ui.T("Lock or unlock")),
                (Ui.T("Number keys over an object with states"), Ui.T("Switch state")),
                (Ui.T("Right click"), Ui.T("Menu: deal, search, cut, split, spread, set a die, name, colour, …")),
            }),
            (Ui.T("Your hand"), new[]
            {
                (Ui.T("Hover a card at the bottom of the screen"), Ui.T("Raise and enlarge it")),
                (Ui.T("Drag it out"), Ui.T("Pick it up (in a game: drop it on the table to play it)")),
                (Ui.T("Release over the hand"), Ui.T("Put a card into your hand at that spot")),
                (Ui.T("Hide hand / Show hand"), Ui.T("Sink the hand to a thin strip")),
            }),
            (Ui.T("Playing a game"), new[]
            {
                (Ui.T("Drag a card from your hand to the table"), Ui.T("Played, when the rules have a play for it (a refused play comes back, with the reason); otherwise put down like any card")),
                (Ui.T("Drag the pile the rules draw from"), Ui.T("Draw")),
                (Ui.T("Buttons at the top and in game panels"), Ui.T("The game's actions")),
            }),
            (Ui.T("Table"), new[]
            {
                (Ui.T("Ctrl + C / Ctrl + V"), Ui.T("Copy / paste")),
                ("{Delete}", Ui.T("Delete")),
                (Ui.T("Ctrl + Z / Ctrl + Y"), Ui.T("Undo / redo (playing alone)")),
                (Ui.T("Numpad 1–9, 0"), Ui.T("Scripting keys 1–10: a scripted game may give them actions (TTS scripting buttons)")),
                (Ui.T("Right click the table → Rewind"), Ui.T("Host: step the whole table back in time, and forward again")),
                (Ui.T("Right click the table → Grid"), Ui.T("Host: squares or hexes, snapping, cell size, lines")),
                (Ui.T("Draw / Arrow / Erase (left tool bar)"), Ui.T("Draw on the table in your colour, drag an arrow, rub drawings out; Esc goes back to Grab")),
                (Ui.T("Gizmo (left tool bar), or right click → Exact position…"), Ui.T("Type an object's exact position, height, turn, tilt and scale")),
                (Ui.T("{QuickSave} / {QuickLoad}"), Ui.T("Quick save / quick load")),
                (Ui.T("Tab + click"), Ui.T("Ping")),
                (Ui.T("Tab + drag"), Ui.T("Measure")),
                ("{Blindfold}", Ui.T("Blindfold on or off (everyone is told)")),
                (Ui.T("Blind round (left tool bar)"), Ui.T("Until the reveal, what each player moves is hidden from the others; Reveal shows it all")),
                (Ui.T("Right click a card → Show to everyone"), Ui.T("The card comes up big on every screen, to read it out; click it away")),
                (Ui.T("Calculator (left tool bar)"), Ui.T("Puts a calculator on the table: click its keys, or select it and type (digits, + - * /, Enter, Backspace, C)")),
                (Ui.T("Right click a piece → Hide from others"), Ui.T("Only you see it (a secret bid or chip); Show to everyone brings it back")),
                (Ui.T("F1 or ?"), Ui.T("This list")),
                (Ui.T("Notes (top bar)"), Ui.T("The table's notebook: shared pages, and private ones for your seat")),
                (Ui.T("Escape"), Ui.T("Close what is open; with nothing open, the menu (settings, leave, quit)")),
            }),
            (Ui.T("Chat"), new[]
            {
                (Ui.T("Enter, or click the chat"), Ui.T("Open the chat (Enter sends, Escape or a click on the table closes)")),
                (Ui.T("/roll 3d6+2 (or /r d20)"), Ui.T("Roll dice in the chat for everyone to see")),
                (Ui.T("Hold {Emotes}, point, let go"), Ui.T("Emote: your chalker waves, laughs, shrugs … for everyone (Settings → You → Chalker face picks its personality)")),
                (Ui.T("Hold {Talk}"), Ui.T("Talk (push to talk; Settings → Voice switches to open-when-you-speak, and has a hear-yourself test)")),
                (Ui.T("Log tab"), Ui.T("Everything done at the table, in order")),
            }),
        };

        private readonly VisualElement _panel;
        /// <summary>The list's labels in order (a group title, then each row's keys and what they do), filled each time it opens.</summary>
        private readonly List<Label> _labels = new List<Label>();

        public ControlsHelp(VisualElement root)
        {
            _panel = root.Q("helpPanel");
            var list = root.Q<ScrollView>("helpList");
            root.Q<Button>("helpClose").clicked += Hide;
            root.Q<Button>("helpButton").clicked += Toggle;
            foreach (var (_, rows) in Sections)
            {
                var title = new Label();
                title.AddToClassList("help-group");
                list.Add(title);
                _labels.Add(title);
                foreach (var _ in rows)
                {
                    var row = new VisualElement();
                    row.AddToClassList("help-row");
                    var k = new Label();
                    k.AddToClassList("help-keys");
                    var w = new Label();
                    _labels.Add(k);
                    _labels.Add(w);
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
            // In the interface's language, with {Action} where a moved key goes (G31).
            var at = 0;
            foreach (var (group, rows) in Sections)
            {
                _labels[at++].text = group;
                foreach (var (keys, what) in rows)
                {
                    _labels[at++].text = KeyBindings.Fill(keys);
                    _labels[at++].text = KeyBindings.Fill(what);
                }
            }
            _panel?.RemoveFromClassList("hidden");
        }

        public void Hide() => _panel?.AddToClassList("hidden");
    }
}
