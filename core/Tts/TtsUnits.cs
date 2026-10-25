namespace Core.Tts
{
    /// <summary>The one TTS ↔ metres conversion (docs/tts-mapping.md, "Units and axes"). TTS calls its unit an inch.</summary>
    public static class TtsUnits
    {
        public const float TtsUnitsToMeters = 0.03f;

        /// <summary>TTS table surface height in TTS units on the default tables.</summary>
        public const float TableSurfaceY = 0.96f;

        /// <summary>
        /// A TTS card at scale 1, in metres: 1.92 × 2.69 units. TTS saves positions after physics, so unlocked decks laid side
        /// by side cannot overlap there: Pokémon Sinnoh's decks at scale 1.5 lie 2.894 units apart, so a card is at most
        /// 1.93 wide (ours, 63.5 mm = 2.12 units, overlapped its neighbours by 8 mm). The length keeps the poker card's
        /// proportions (end to end spacings only bound it by 3.2). Our own games keep the real 63.5 × 88.9 mm card.
        /// </summary>
        /// <summary>
        /// The longer side of a TTS Custom_Token at scale 1, whatever its image's pixel size. Estimate (2026-10-25) from the
        /// eight board-sized tokens of the local mods: their snap points reach 2.0–2.4 units from the centre, the outer
        /// spaces lying just inside the board's edge. Verify in a running TTS with getBounds().
        /// </summary>
        public const float CustomTokenSize = 4.6f * TtsUnitsToMeters;

        public static readonly Core.Rules.Vec2 CardSize = new Core.Rules.Vec2(1.92f * TtsUnitsToMeters, 2.69f * TtsUnitsToMeters);
    }
}
