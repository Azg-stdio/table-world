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

    /// <summary>
    /// A text field a script put on an object (M5, TTS <c>createInput</c>): its text, a placeholder label, and the
    /// function typing calls (<c>input_function(obj, color, value, selected)</c>). Players type into it; the text is
    /// table state, so everyone sees it.
    /// </summary>
    public sealed class InputDef
    {
        public const int MaxInputs = 64;
        public const int MaxLength = 10000;

        /// <summary>Placeholder shown while empty.</summary>
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string InputFunction { get; set; } = "";
        public string? Owner { get; set; }
        public Vec3 Position { get; set; }
        public Vec3 Rotation { get; set; }
        [System.ComponentModel.DefaultValue(ButtonDef.DefaultSize)]
        public float Width { get; set; } = ButtonDef.DefaultSize;
        [System.ComponentModel.DefaultValue(ButtonDef.DefaultSize)]
        public float Height { get; set; } = ButtonDef.DefaultSize;
        [System.ComponentModel.DefaultValue(ButtonDef.DefaultSize)]
        public float FontSize { get; set; } = ButtonDef.DefaultSize;
        public float[]? Color { get; set; }
        public float[]? FontColor { get; set; }
        public string? Tooltip { get; set; }
        /// <summary>TTS alignment: 1 automatic, 2 left, 3 center, 4 right, 5 justified.</summary>
        [System.ComponentModel.DefaultValue(1)]
        public int Alignment { get; set; } = 1;
        /// <summary>TTS validation: 1 none, 2 integer, 3 float, 4 alphanumeric, 5 username, 6 name.</summary>
        [System.ComponentModel.DefaultValue(1)]
        public int Validation { get; set; } = 1;
    }
}
