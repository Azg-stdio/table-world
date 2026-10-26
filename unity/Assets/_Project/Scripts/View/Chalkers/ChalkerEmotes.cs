using UnityEngine;

namespace TableWorld.View.Chalkers
{
    /// <summary>The resting face a player picks for their chalker.</summary>
    public enum ChalkerPersonality { Cheerful, Calm, Grumpy, Shy, Cool, Curious, Sleepy }

    /// <summary>Short reactions: a face and a body movement for a couple of seconds (T7.4).</summary>
    public enum ChalkerEmote { None, Wave, Laugh, Nod, No, Shrug, Cheer, Think, Surprised, Sad, Angry, Love }

    /// <summary>
    /// A body pose as offsets from rest, in degrees and metres. Arms: <c>Raise</c> lifts the arm out to the side, <c>Forward</c>
    /// swings it to the front, <c>Elbow</c> bends the forearm forward.
    /// </summary>
    public struct BodyPose
    {
        public Vector3 Head;
        public float Lean;
        public float Sway;
        public float Bob;
        public float LeftRaise, LeftForward, LeftElbow;
        public float RightRaise, RightForward, RightElbow;

        public static BodyPose Lerp(BodyPose a, BodyPose b, float t) => new BodyPose
        {
            Head = Vector3.Lerp(a.Head, b.Head, t),
            Lean = Mathf.Lerp(a.Lean, b.Lean, t),
            Sway = Mathf.Lerp(a.Sway, b.Sway, t),
            Bob = Mathf.Lerp(a.Bob, b.Bob, t),
            LeftRaise = Mathf.Lerp(a.LeftRaise, b.LeftRaise, t),
            LeftForward = Mathf.Lerp(a.LeftForward, b.LeftForward, t),
            LeftElbow = Mathf.Lerp(a.LeftElbow, b.LeftElbow, t),
            RightRaise = Mathf.Lerp(a.RightRaise, b.RightRaise, t),
            RightForward = Mathf.Lerp(a.RightForward, b.RightForward, t),
            RightElbow = Mathf.Lerp(a.RightElbow, b.RightElbow, t),
        };
    }

    /// <summary>The faces of the personalities and the timelines of the emotes.</summary>
    public static class ChalkerEmotes
    {
        /// <summary>Seated with the forearms on the table, hands just past its edge (shoulders 0.2 m above the top, T11.10).</summary>
        public static readonly BodyPose Rest = new BodyPose { LeftRaise = 6f, RightRaise = 6f, LeftForward = 32f, RightForward = 32f, LeftElbow = 52f, RightElbow = 52f };

        public static FaceShape Face(ChalkerPersonality personality)
        {
            var f = FaceShape.Neutral;
            switch (personality)
            {
                case ChalkerPersonality.Cheerful:
                    f.Smile = 0.75f;
                    f.Brows = 1f;
                    f.BrowRaise = 0.4f;
                    break;
                case ChalkerPersonality.Calm:
                    f.EyeOpen = 0.8f;
                    f.Smile = 0.25f;
                    break;
                case ChalkerPersonality.Grumpy:
                    f.EyeOpen = 0.75f;
                    f.Brows = 1f;
                    f.BrowTilt = 0.9f;
                    f.BrowRaise = -0.4f;
                    f.Smile = -0.55f;
                    f.MouthWidth = 0.85f;
                    break;
                case ChalkerPersonality.Shy:
                    f.Pupil = 0.8f;
                    f.Brows = 1f;
                    f.BrowTilt = -0.4f;
                    f.Smile = 0.3f;
                    f.MouthWidth = 0.55f;
                    f.Extra = FaceExtra.Blush;
                    break;
                case ChalkerPersonality.Cool:
                    f.EyeOpen = 0.45f;
                    f.Smile = 0.2f;
                    f.Smirk = 0.8f;
                    f.MouthWidth = 0.85f;
                    f.Extra = FaceExtra.Sparkle;
                    break;
                case ChalkerPersonality.Curious:
                    f.EyeOpen = 1.15f;
                    f.Pupil = 1.25f;
                    f.Brows = 1f;
                    f.BrowRaise = 0.3f;
                    f.BrowAsym = 0.9f;
                    f.MouthWidth = 0.45f;
                    f.MouthOpen = 0.15f;
                    break;
                case ChalkerPersonality.Sleepy:
                    f.EyeOpen = 0.3f;
                    f.Pupil = 0.8f;
                    f.Smile = 0.1f;
                    f.MouthWidth = 0.7f;
                    f.Extra = FaceExtra.Zzz;
                    break;
            }
            return f;
        }

        public static float Duration(ChalkerEmote emote) => emote switch
        {
            ChalkerEmote.None => 0f,
            ChalkerEmote.Nod => 1.4f,
            ChalkerEmote.No => 1.4f,
            ChalkerEmote.Shrug => 1.8f,
            ChalkerEmote.Think => 3f,
            ChalkerEmote.Sad => 3f,
            ChalkerEmote.Love => 2.6f,
            _ => 2.2f,
        };

        /// <summary>How much of the emote shows at <paramref name="t"/> (0–1 through it): it eases in and out.</summary>
        public static float Weight(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Min(t / 0.15f, (1f - t) / 0.2f));

        /// <summary>
        /// The face and pose of <paramref name="emote"/> at <paramref name="seconds"/> into it, starting from
        /// <paramref name="resting"/>. <paramref name="look"/> overrides where the pupils point, when the emote decides.
        /// </summary>
        public static (FaceShape Face, BodyPose Pose, Vector2? Look) Evaluate(ChalkerEmote emote, float seconds, FaceShape resting)
        {
            var f = resting;
            var p = Rest;
            Vector2? look = null;
            var s = seconds;
            float Wave(float hz) => Mathf.Sin(s * hz * 2f * Mathf.PI);
            switch (emote)
            {
                case ChalkerEmote.Wave:
                    f.Smile = 0.9f;
                    f.Brows = 1f;
                    f.BrowRaise = 0.6f;
                    f.BrowTilt = 0f;
                    p.RightRaise = 140f + Wave(2.5f) * 18f;
                    p.RightElbow = 35f;
                    p.Head = new Vector3(0f, 0f, -6f);
                    p.Sway = Wave(1.25f) * 3f;
                    break;
                case ChalkerEmote.Laugh:
                    f.EyeSmile = 1f;
                    f.Smile = 1f;
                    f.MouthOpen = 0.55f + 0.25f * Mathf.Abs(Wave(4f));
                    f.MouthWidth = 1.1f;
                    p.Head = new Vector3(-12f + Wave(4f) * 4f, 0f, Wave(2f) * 5f);
                    p.Lean = -6f;
                    p.Bob = Mathf.Abs(Wave(4f)) * 0.012f;
                    p.LeftForward = p.RightForward = 35f;
                    p.LeftElbow = p.RightElbow = 80f;
                    p.LeftRaise = p.RightRaise = 15f;
                    break;
                case ChalkerEmote.Nod:
                    f.Smile = Mathf.Max(f.Smile, 0.4f);
                    p.Head = new Vector3(Mathf.Max(0f, Wave(1.8f)) * 22f, 0f, 0f);
                    break;
                case ChalkerEmote.No:
                    f.Smile = -0.3f;
                    f.Brows = 1f;
                    f.BrowTilt = 0.3f;
                    f.BrowRaise = 0f;
                    p.Head = new Vector3(0f, Wave(2.2f) * 28f, 0f);
                    break;
                case ChalkerEmote.Shrug:
                    f.Brows = 1f;
                    f.BrowRaise = 1f;
                    f.BrowTilt = -0.3f;
                    f.Smile = 0f;
                    f.Smirk = 0.7f;
                    f.MouthWidth = 0.8f;
                    p.LeftRaise = p.RightRaise = 45f;
                    p.LeftForward = p.RightForward = 25f;
                    p.LeftElbow = p.RightElbow = 95f;
                    p.Bob = 0.025f;
                    p.Head = new Vector3(0f, 0f, 12f);
                    break;
                case ChalkerEmote.Cheer:
                    f.EyeSmile = 1f;
                    f.Smile = 1f;
                    f.MouthOpen = 0.7f;
                    f.Extra = FaceExtra.Sparkle;
                    p.LeftRaise = p.RightRaise = 160f + Wave(3f) * 10f;
                    p.LeftElbow = p.RightElbow = 10f;
                    p.Bob = Mathf.Abs(Wave(1.6f)) * 0.06f;
                    p.Head = new Vector3(-10f, 0f, 0f);
                    break;
                case ChalkerEmote.Think:
                    f.Brows = 1f;
                    f.BrowAsym = 0.8f;
                    f.BrowTilt = 0.2f;
                    f.Smile = -0.1f;
                    f.Smirk = -0.6f;
                    f.MouthWidth = 0.6f;
                    f.Extra = FaceExtra.Dots;
                    look = new Vector2(0.6f, 0.8f);
                    p.RightForward = 80f;
                    p.RightRaise = 5f;
                    p.RightElbow = 135f;
                    p.Head = new Vector3(-12f, 10f, 10f + Wave(0.5f) * 3f);
                    break;
                case ChalkerEmote.Surprised:
                    f.EyeOpen = 1.45f;
                    f.EyeSmile = 0f;
                    f.Pupil = 0.6f;
                    f.Brows = 1f;
                    f.BrowRaise = 1.4f;
                    f.BrowTilt = -0.2f;
                    f.Smile = 0f;
                    f.MouthOpen = 0.8f;
                    f.MouthWidth = 0.55f;
                    f.Extra = FaceExtra.None;
                    p.Lean = -10f;
                    p.LeftRaise = p.RightRaise = 50f;
                    p.LeftElbow = p.RightElbow = 60f;
                    p.Bob = 0.03f * Mathf.Clamp01(1f - s * 2f);
                    break;
                case ChalkerEmote.Sad:
                    f.EyeOpen = 0.8f;
                    f.EyeSmile = 0f;
                    f.Brows = 1f;
                    f.BrowTilt = -1f;
                    f.BrowRaise = 0.2f;
                    f.Smile = -0.85f;
                    f.MouthWidth = 0.8f;
                    f.MouthOpen = 0f;
                    f.Extra = FaceExtra.Tear;
                    look = new Vector2(0f, -0.6f);
                    p.Head = new Vector3(22f, 0f, 6f);
                    p.Lean = 10f;
                    p.Bob = -0.02f;
                    p.LeftRaise = p.RightRaise = 2f;
                    break;
                case ChalkerEmote.Angry:
                    f.EyeOpen = 0.75f;
                    f.EyeSmile = 0f;
                    f.Pupil = 0.8f;
                    f.Brows = 1f;
                    f.BrowTilt = 1.2f;
                    f.BrowRaise = -0.6f;
                    f.Smile = -0.8f;
                    f.MouthOpen = 0.25f;
                    f.Extra = FaceExtra.Anger;
                    p.LeftRaise = p.RightRaise = 25f;
                    p.LeftForward = p.RightForward = 20f + Mathf.Abs(Wave(3f)) * 25f;
                    p.LeftElbow = p.RightElbow = 100f;
                    p.Sway = Wave(6f) * 2f;
                    p.Lean = 6f;
                    break;
                case ChalkerEmote.Love:
                    f.Smile = 0.85f;
                    f.EyeSmile = 0f;
                    f.Extra = FaceExtra.Hearts;
                    p.Head = new Vector3(0f, 0f, Wave(0.8f) * 10f);
                    p.Sway = Wave(0.8f) * 5f;
                    p.LeftForward = p.RightForward = 50f;
                    p.LeftRaise = p.RightRaise = 30f;
                    p.LeftElbow = p.RightElbow = 70f;
                    break;
            }
            return (f, p, look);
        }
    }
}
