namespace TableWorld.View
{
    /// <summary>
    /// The effects a TTS asset bundle carries (looping: Idle, Walk …; trigger: Attack, Faint …), read from TTS's own
    /// component in the bundle through a stand-in class in <c>Assembly-CSharp</c> (ADR 0008). The runtime cannot reference
    /// that assembly, so it reaches the stand-in through this interface.
    /// </summary>
    public interface ITtsBundleEffects
    {
        int LoopingCount { get; }
        int TriggerCount { get; }
        string LoopingName(int index);
        string TriggerName(int index);
        /// <summary>Shows looping effect <paramref name="index"/> until another replaces it.</summary>
        void PlayLooping(int index);
        /// <summary>Plays trigger effect <paramref name="index"/> once and returns how long it lasts, in seconds.</summary>
        float PlayTrigger(int index);
    }
}
