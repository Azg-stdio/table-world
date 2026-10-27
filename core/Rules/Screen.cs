namespace Core.Rules
{
    /// <summary>
    /// The karaoke room's big screen, shared by everyone (ADR 0013, T12.11): which YouTube video, playing or paused, and
    /// where. <see cref="Position"/> was true at <see cref="At"/> on the host's clock, so every machine works out where
    /// the video is now (<see cref="PositionAt"/>) and puts its own player there, each listener a little later by the
    /// singer's voice delay.
    /// </summary>
    public sealed class ScreenState
    {
        /// <summary>The YouTube video id; null when the screen is off.</summary>
        public string? VideoId { get; set; }
        public bool Playing { get; set; }
        /// <summary>Seconds into the video at <see cref="At"/>.</summary>
        public double Position { get; set; }
        /// <summary>When <see cref="Position"/> was true, milliseconds on the host's clock.</summary>
        public double At { get; set; }

        /// <summary>Seconds into the video at <paramref name="hostTime"/> (ms on the host's clock): still while paused, never before the start.</summary>
        public double PositionAt(double hostTime) =>
            Playing ? System.Math.Max(0, Position + (hostTime - At) / 1000.0) : Position;

        public ScreenState Clone() => (ScreenState)MemberwiseClone();
    }
}
