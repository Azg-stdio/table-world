namespace Core.Rules
{
    /// <summary>
    /// A button a script put on an object (M5, TTS <c>createButton</c>): a label, where it sits on the object (metres in
    /// the object's frame, before its scale), its size and colours, and the function a click calls (in the global script
    /// when <see cref="Owner"/> is null, else in that object's script). Buttons are table state, set only by the host's
    /// scripts, so every player sees and can click them.
    /// </summary>
    public sealed class ButtonDef
    {
        public const int MaxButtons = 64;
        public const float DefaultSize = 0.006f;

        public string Label { get; set; } = "";
        public string Click { get; set; } = "";
        public string? Owner { get; set; }
        public Vec3 Position { get; set; }
        public Vec3 Rotation { get; set; }
        /// <summary>Metres; the default is TTS's 100 (see <c>ScriptHost.ButtonUnitsPerTtsUnit</c>). 0 wide or high: a label, not clickable.</summary>
        [System.ComponentModel.DefaultValue(DefaultSize)]
        public float Width { get; set; } = DefaultSize;
        [System.ComponentModel.DefaultValue(DefaultSize)]
        public float Height { get; set; } = DefaultSize;
        /// <summary>Text height in metres.</summary>
        [System.ComponentModel.DefaultValue(DefaultSize)]
        public float FontSize { get; set; } = DefaultSize;
        public float[]? Color { get; set; }
        public float[]? FontColor { get; set; }
        public string? Tooltip { get; set; }
    }
}
