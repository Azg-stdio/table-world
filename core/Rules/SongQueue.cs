namespace Core.Rules
{
    /// <summary>A song waiting its turn on the karaoke screen (ADR 0013 §5, T12.12): the YouTube video, its title as YouTube gives it, who added it.</summary>
    public sealed class QueuedSong
    {
        public const int MaxTitle = 120;

        public string VideoId { get; set; } = "";
        /// <summary>The video's title (YouTube's), or null when it could not be found out.</summary>
        public string? Title { get; set; }
        /// <summary>The seat that added it (it may remove it), or null for the host.</summary>
        public string? Seat { get; set; }

        public QueuedSong Clone() => (QueuedSong)MemberwiseClone();
    }
}
