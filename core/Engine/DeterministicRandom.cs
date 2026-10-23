using System;

namespace Core.Engine
{
    /// <summary>
    /// PCG32 (O'Neill, pcg-random.org). Same seed → same sequence on every runtime (.NET, Mono, IL2CPP),
    /// which System.Random does not promise. Shuffles and dice rolls come only from here.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong DefaultIncrement = 1442695040888963407UL;

        private readonly ulong _increment;
        private ulong _state;

        public DeterministicRandom(ulong seed) : this(seed, DefaultIncrement)
        {
        }

        private DeterministicRandom(ulong seed, ulong increment)
        {
            _increment = increment;
            _state = 0;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        /// <summary>pcg32_srandom_r(seed, stream) from the reference implementation; used to check against its published output.</summary>
        internal static DeterministicRandom ForStream(ulong seed, ulong stream) => new DeterministicRandom(seed, (stream << 1) | 1UL);

        /// <summary>Internal state, to save and restore a session mid-game.</summary>
        public ulong State
        {
            get => _state;
            set => _state = value;
        }

        public uint NextUInt()
        {
            var old = _state;
            _state = unchecked(old * Multiplier + _increment);
            var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            var rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
        }

        /// <summary>Uniform integer in [0, maxExclusive), without modulo bias.</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be positive.");
            var bound = (uint)maxExclusive;
            var threshold = (uint)((1UL << 32) - bound) % bound;
            while (true)
            {
                var r = NextUInt();
                if (r >= threshold)
                    return (int)(r % bound);
            }
        }
    }
}
