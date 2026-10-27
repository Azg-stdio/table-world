using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TableWorld.View.Settings
{
    /// <summary>The single-key actions a player may move to another key (G31). Combinations (Ctrl + Z …), digits,
    /// Escape, Enter and F1 stay where TTS has them.</summary>
    public enum GameAction
    {
        Flip, RotateLeft, RotateRight, Roll, Lock, Group, PlaceUnder, Nudge, Grow, Shrink, Delete,
        PanForward, PanBack, PanLeft, PanRight, ResetCamera, QuickZoom, TopDown, Blindfold, QuickSave, QuickLoad,
        Talk, Emotes, SeatView, Wink,
    }

    /// <summary>
    /// Which key does what (G31): TTS's keys by default, any of them moved in the settings. Game code asks
    /// <see cref="Pressed"/> / <see cref="Held"/> for an action instead of reading a key. The arrows (pan) and the
    /// numpad + and − (scale) keep working beside whatever the action is bound to.
    /// </summary>
    public static class KeyBindings
    {
        public static readonly IReadOnlyDictionary<GameAction, Key> Defaults = new Dictionary<GameAction, Key>
        {
            [GameAction.Flip] = Key.F, [GameAction.RotateLeft] = Key.Q, [GameAction.RotateRight] = Key.E,
            [GameAction.Roll] = Key.R, [GameAction.Lock] = Key.L, [GameAction.Group] = Key.G, [GameAction.PlaceUnder] = Key.U,
            [GameAction.Nudge] = Key.N, [GameAction.Grow] = Key.Equals, [GameAction.Shrink] = Key.Minus, [GameAction.Delete] = Key.Delete,
            [GameAction.PanForward] = Key.W, [GameAction.PanBack] = Key.S, [GameAction.PanLeft] = Key.A, [GameAction.PanRight] = Key.D,
            [GameAction.ResetCamera] = Key.Space, [GameAction.QuickZoom] = Key.Z, [GameAction.TopDown] = Key.P,
            [GameAction.Blindfold] = Key.B, [GameAction.QuickSave] = Key.F5, [GameAction.QuickLoad] = Key.F9,
            [GameAction.Talk] = Key.T, [GameAction.Emotes] = Key.X, [GameAction.SeatView] = Key.O, [GameAction.Wink] = Key.K,
        };

        /// <summary>Keys that keep their own meaning and cannot take an action.</summary>
        public static readonly IReadOnlyCollection<Key> Reserved = new HashSet<Key>
        {
            Key.Escape, Key.Enter, Key.NumpadEnter, Key.F1, Key.Tab, Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt,
            Key.LeftShift, Key.RightShift, Key.LeftMeta, Key.RightMeta, Key.Slash,
            Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
            Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9,
        };

        private static readonly Dictionary<GameAction, Key> Current = new Dictionary<GameAction, Key>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current.Clear();

        public static Key KeyOf(GameAction action) => Current.TryGetValue(action, out var key) ? key : Defaults[action];

        /// <summary>Uses the settings' moved keys (action name → key name); anything unknown or reserved keeps its default.</summary>
        public static void Use(IReadOnlyDictionary<string, string> moved)
        {
            Current.Clear();
            foreach (var pair in Resolve(moved))
                Current[pair.Key] = pair.Value;
        }

        /// <summary>Every action's key with <paramref name="moved"/> applied (unknown, reserved or invalid entries ignored).</summary>
        public static Dictionary<GameAction, Key> Resolve(IReadOnlyDictionary<string, string> moved)
        {
            var keys = Defaults.ToDictionary(p => p.Key, p => p.Value);
            if (moved != null)
                foreach (var pair in moved)
                    if (Enum.TryParse<GameAction>(pair.Key, out var action) && Enum.IsDefined(typeof(GameAction), action)
                        && Enum.TryParse<Key>(pair.Value, out var key) && key != Key.None && Enum.IsDefined(typeof(Key), key) && !Reserved.Contains(key))
                        keys[action] = key;
            return keys;
        }

        /// <summary>
        /// The settings' moved keys after binding <paramref name="action"/> to <paramref name="key"/>: an action that had
        /// that key takes the old one (a swap), and keys back at their default are not stored.
        /// </summary>
        public static Dictionary<string, string> Rebind(IReadOnlyDictionary<string, string> moved, GameAction action, Key key)
        {
            var keys = Resolve(moved);
            if (Reserved.Contains(key) || key == Key.None)
                return Store(keys);
            var old = keys[action];
            foreach (var other in keys.Where(p => p.Value == key && p.Key != action).Select(p => p.Key).ToList())
                keys[other] = old;
            keys[action] = key;
            return Store(keys);
        }

        /// <summary>The settings' moved keys without anything unusable, and without two actions on one key.</summary>
        public static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string> moved)
        {
            var keys = Resolve(moved);
            foreach (var clash in keys.GroupBy(p => p.Value).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1)).ToList())
                keys[clash.Key] = Defaults[clash.Key];
            // A default given back may clash again; then the defaults win for everything.
            return keys.GroupBy(p => p.Value).Any(g => g.Count() > 1) ? new Dictionary<string, string>() : Store(keys);
        }

        private static Dictionary<string, string> Store(Dictionary<GameAction, Key> keys) =>
            keys.Where(p => Defaults[p.Key] != p.Value).ToDictionary(p => p.Key.ToString(), p => p.Value.ToString());

        public static bool Pressed(Keyboard keyboard, GameAction action) => keyboard != null && Control(keyboard, action) is { wasPressedThisFrame: true };

        public static bool Held(Keyboard keyboard, GameAction action) => keyboard != null && Control(keyboard, action) is { isPressed: true };

        private static KeyControl Control(Keyboard keyboard, GameAction action) => keyboard[KeyOf(action)];

        /// <summary>How the key reads in text ("F", "Space", "F5"), as the keyboard's layout shows it.</summary>
        public static string Label(GameAction action) => Label(KeyOf(action));

        public static string Label(Key key)
        {
            var shown = Keyboard.current != null ? Keyboard.current[key]?.displayName : null;
            if (string.IsNullOrWhiteSpace(shown))
                shown = key.ToString();
            return key switch
            {
                Key.Equals => "+",
                Key.Minus => "−",
                Key.Space => "Space",
                Key.Delete => "Delete",
                _ => shown.Length == 1 ? shown.ToUpperInvariant() : shown,
            };
        }

        /// <summary>The action's name in the settings.</summary>
        public static string Name(GameAction action) => action switch
        {
            GameAction.Flip => "Flip",
            GameAction.RotateLeft => "Rotate left (and turn the camera)",
            GameAction.RotateRight => "Rotate right (and turn the camera)",
            GameAction.Roll => "Roll / shuffle",
            GameAction.Lock => "Lock",
            GameAction.Group => "Group into a deck",
            GameAction.PlaceUnder => "Place underneath",
            GameAction.Nudge => "Nudge (hold)",
            GameAction.Grow => "Scale up (hold)",
            GameAction.Shrink => "Scale down (hold)",
            GameAction.Delete => "Delete",
            GameAction.PanForward => "Pan forward",
            GameAction.PanBack => "Pan back",
            GameAction.PanLeft => "Pan left",
            GameAction.PanRight => "Pan right",
            GameAction.ResetCamera => "Back to your seat's view",
            GameAction.QuickZoom => "Quick zoom",
            GameAction.TopDown => "Top-down view",
            GameAction.Blindfold => "Blindfold",
            GameAction.QuickSave => "Quick save",
            GameAction.QuickLoad => "Quick load",
            GameAction.Talk => "Push to talk (hold)",
            GameAction.Emotes => "Emote wheel (hold)",
            GameAction.SeatView => "View from your seat",
            GameAction.Wink => "Wink at the face you look at",
            _ => action.ToString(),
        };

        /// <summary>Replaces <c>{Action}</c> in help text with the action's key ("{Flip}, or middle click" → "F, or middle click").</summary>
        public static string Fill(string text)
        {
            if (text.IndexOf('{') < 0)
                return text;
            foreach (GameAction action in Enum.GetValues(typeof(GameAction)))
                text = text.Replace("{" + action + "}", Label(action));
            return text;
        }
    }
}
