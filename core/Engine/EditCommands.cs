using System.Collections.Generic;
using Core.Rules;

namespace Core.Engine
{
    /// <summary>Multiplies an object's scale (TTS "+"/"−" keys). Each axis stays within <see cref="TableEngine.MinScale"/>..<see cref="TableEngine.MaxScale"/>.</summary>
    public sealed class ScaleCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public float Factor { get; set; } = 1.1f;
        /// <summary>Scales a locked object too (scripts may, as in TTS; players may not).</summary>
        public bool EvenLocked { get; set; }
    }

    /// <summary>Removes an object from the table; a bag goes with everything inside it.</summary>
    public sealed class DeleteCommand : Command
    {
        public string ObjectId { get; set; } = "";
        /// <summary>Removes only what is inside the bag and keeps the bag (TTS <c>reset()</c>), locked or not.</summary>
        public bool Contents { get; set; }
    }

    /// <summary>Copies an object (with a bag's contents, a deck's cards and its states) under new ids (copy/paste).</summary>
    /// <summary>
    /// Puts a new object on the table (M5, TTS <c>spawnObject</c>). <see cref="ObjectJson"/> is one object as the table
    /// file writes it; it gets a fresh id and is checked like a loaded table's. <see cref="Assets"/> are added when the
    /// table lacks them (built-in pieces). Host only: scripts spawn, players clone.
    /// </summary>
    /// <summary>
    /// Changes the files an object shows (M5, TTS <c>setCustomObject</c>): <see cref="Assets"/> join the table when it
    /// lacks them, then each given id replaces that part of the object (null keeps it). Host only.
    /// </summary>
    public sealed class SetCustomCommand : Command, IHostCommand
    {
        public string ObjectId { get; set; } = "";
        public List<Asset> Assets { get; set; } = new List<Asset>();
        /// <summary>Tokens, tiles, boards, figures, custom dice and bags: their image (or a bag's or model's mesh).</summary>
        public string? Image { get; set; }
        public string? Face { get; set; }
        public string? Back { get; set; }
        public string? Mesh { get; set; }
        public string? Diffuse { get; set; }
    }

    public sealed class SpawnCommand : Command, IHostCommand
    {
        public string ObjectJson { get; set; } = "";
        public List<Asset> Assets { get; set; } = new List<Asset>();
    }

    public sealed class CloneCommand : Command
    {
        public string ObjectId { get; set; } = "";
        /// <summary>Where the copy lands; null puts it beside the original.</summary>
        public Vec3? Position { get; set; }
    }

    /// <summary>Shows another state of a multi-state object (TTS states; number keys or menu).</summary>
    public sealed class SetStateCommand : Command
    {
        public string ObjectId { get; set; } = "";
        /// <summary>Key in the object's <c>states</c>, e.g. "2".</summary>
        public string State { get; set; } = "";
    }

    /// <summary>Lifts the top <see cref="Count"/> cards (default half) off a deck into a new deck beside it.</summary>
    public sealed class CutCommand : Command
    {
        public string DeckId { get; set; } = "";
        public int? Count { get; set; }
        public Vec3? Position { get; set; }
    }

    /// <summary>Splits a deck into <see cref="Piles"/> decks of (almost) equal size laid out in a row; the original keeps the top part.</summary>
    public sealed class SplitCommand : Command
    {
        public string DeckId { get; set; } = "";
        public int Piles { get; set; } = 2;
    }

    /// <summary>Lays every card of a deck out in an overlapping row, top card first; the deck stays, empty.</summary>
    public sealed class SpreadCommand : Command
    {
        public string DeckId { get; set; } = "";
        /// <summary>Distance between card centres along the deck's width, metres.</summary>
        public float Spacing { get; set; } = 0.025f;
    }

    /// <summary>Turns a die to a chosen face (context menu), without rolling.</summary>
    public sealed class SetDieValueCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public int Value { get; set; }
    }

    /// <summary>
    /// Plays an asset-bundle effect on an object (TTS <c>playLoopingEffect</c> / <c>playTriggerEffect</c>, ADR 0008): a
    /// looping effect stays (it is saved), a trigger effect plays once. The views know the effects; the engine only
    /// records the choice, so every machine shows the same one.
    /// </summary>
    public sealed class PlayEffectCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public int Index { get; set; }
        public bool Looping { get; set; } = true;
    }

    /// <summary>
    /// Holds <see cref="ObjectId"/> to <see cref="To"/> (TTS <c>jointTo</c> / <c>addAttachment</c>), or frees it when
    /// <see cref="To"/> is null. Works on locked objects too.
    /// </summary>
    public sealed class SetJointCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public string? To { get; set; }
        public JointType Type { get; set; }
        public bool Collision { get; set; }
        public bool Attached { get; set; }
    }

    /// <summary>Turns a PDF to <see cref="Page"/> (from 0) for everyone (M7 T8.8). Works on locked PDFs too.</summary>
    public sealed class SetPageCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public int Page { get; set; }
    }

    /// <summary>Sets a counter to <see cref="Value"/>, or adds <see cref="Add"/> to it (G27). Works on locked counters too.</summary>
    /// <summary>A key pressed on a table calculator (<see cref="Calculator.Keys"/>).</summary>
    public sealed class PressCalculatorKeyCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public string Key { get; set; } = "";
    }

    /// <summary>A player puts a calculator on the table (any player may: it is a known, harmless object).</summary>
    public sealed class AddCalculatorCommand : Command
    {
        public Vec3 Position { get; set; }
        /// <summary>Degrees about the vertical, so it reads upright for whoever placed it.</summary>
        public float Yaw { get; set; }
    }

    public sealed class SetCounterCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public int? Value { get; set; }
        public int? Add { get; set; }
    }

    /// <summary>
    /// Changes a clock (G27): its mode, its reading in seconds, and whether it runs. Null fields stay as they are. When
    /// pausing, the sender passes the reading it shows, so everyone stops on the same number.
    /// </summary>
    public sealed class SetClockCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public ClockMode? Mode { get; set; }
        public float? Seconds { get; set; }
        public float? Duration { get; set; }
        public bool? Running { get; set; }
    }

    /// <summary>Renames, describes or tints an object (context menu). Null fields stay as they are.</summary>
    public sealed class SetPropertiesCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public string? Name { get; set; }
        public string? Description { get; set; }
        public float[]? Tint { get; set; }
        public bool ClearTint { get; set; }
        /// <summary>The per-object toggles (G16); null leaves each as it is.</summary>
        public bool? Snap { get; set; }
        public bool? Grid { get; set; }
        public bool? Hands { get; set; }
        public bool? HideWhenFaceDown { get; set; }
        public bool? Tooltip { get; set; }
        public bool? MeshHidden { get; set; }
        /// <summary>Replaces the object's Lua (TTS <c>setLuaScript</c>); the host reloads that script. Empty removes it.</summary>
        public string? Script { get; set; }
        /// <summary>Replaces the tags.</summary>
        public List<string>? Tags { get; set; }
        /// <summary>Replaces the seat colours that cannot see the object.</summary>
        public List<string>? InvisibleTo { get; set; }
    }
}
