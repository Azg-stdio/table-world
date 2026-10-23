namespace Core.Tts
{
    /// <summary>The one TTS ↔ metres conversion (docs/tts-mapping.md, "Units and axes"). TTS calls its unit an inch.</summary>
    public static class TtsUnits
    {
        public const float TtsUnitsToMeters = 0.03f;

        /// <summary>TTS table surface height in TTS units on the default tables.</summary>
        public const float TableSurfaceY = 0.96f;
    }
}
