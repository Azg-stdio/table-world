using UnityEngine;

namespace TableWorld.View.Chalkers
{
    /// <summary>
    /// A hand of cards held by a seated <see cref="Chalker"/> (docs/dev/camera.md): the cards fan out from a grip between
    /// its hands, their faces toward its eyes. Lowered, the fan rests just over the table's edge, leaning back; raised, it
    /// is up in front of the face to be read. The table places the hand's cards with <see cref="Card"/>.
    /// </summary>
    public readonly struct HandHold
    {
        /// <summary>Lowered: the grip over the table's edge at chest height, on the holding (left) hand's side so the arm does not cross the chest (the chair is <see cref="ChalkersView.SeatDistance"/> out), in the chalker's space.</summary>
        public static readonly Vector3 LoweredGrip = new Vector3(-0.11f, 0.17f, 0.34f);
        /// <summary>
        /// Raised: the grip under the chin, a forearm out, where the fan reads whole from the eyes. A face further forward
        /// (a snout) pushes it out to
        /// <see cref="FaceClearance"/> in front.
        /// </summary>
        public static readonly Vector3 RaisedGrip = new Vector3(-0.07f, 0.38f, 0.33f);
        public const float FaceClearance = 0.12f;
        /// <summary>How far the cards lean back from upright, lowered and raised, in degrees.</summary>
        public const float LoweredLean = 40f;
        public const float RaisedLean = 14f;
        /// <summary>The widest a fan opens, and the turn between two cards when few are held, in degrees.</summary>
        public const float MaxSpread = 64f;
        public const float CardTurn = 9f;

        /// <summary>The bottom centre of the fan, where the cards meet: the holding (left) hand is there.</summary>
        public readonly Vector3 Grip;
        /// <summary>Up the cards (toward their tops, before the fan turns them).</summary>
        public readonly Vector3 Up;
        /// <summary>The way the cards' faces look: toward the holder's eyes.</summary>
        public readonly Vector3 Face;
        /// <summary>The holder's right across the cards.</summary>
        public readonly Vector3 Right;

        public HandHold(Vector3 grip, Vector3 up, Vector3 face)
        {
            Grip = grip;
            Up = up.normalized;
            Face = Vector3.ProjectOnPlane(face, Up).normalized;
            Right = Vector3.Cross(Face, Up);
        }

        /// <summary>
        /// The hold of a chalker whose root is <paramref name="root"/> and torso <paramref name="torso"/> (the cards lean
        /// with it), its eyes at <paramref name="eyes"/>, raised by <paramref name="raise"/> (0–1), its face's front
        /// <paramref name="face"/> metres ahead of its root.
        /// </summary>
        public static HandHold For(Transform root, Transform torso, Vector3 eyes, float raise, float face = 0f)
        {
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(raise));
            var lean = Mathf.Lerp(LoweredLean, RaisedLean, t) * Mathf.Deg2Rad;
            var raised = new Vector3(RaisedGrip.x, RaisedGrip.y, Mathf.Max(RaisedGrip.z, face + FaceClearance));
            var grip = root.TransformPoint(Vector3.Lerp(LoweredGrip, raised, t));
            var up = root.TransformDirection(new Vector3(0f, Mathf.Cos(lean), Mathf.Sin(lean)));
            // The torso's lean and sway carry the hands and the cards with them.
            var turn = torso.rotation * Quaternion.Inverse(root.rotation);
            grip = torso.position + turn * (grip - torso.position);
            up = turn * up;
            return new HandHold(grip, up, eyes - grip);
        }

        /// <summary>
        /// Where card <paramref name="index"/> of <paramref name="count"/> (left to right as the holder sees them) sits, its
        /// face (local +Y) toward the holder and its top (local −Z, as a TTS image reads) up the fan. Later cards lie a
        /// little nearer the eyes, over the ones to their left.
        /// </summary>
        public (Vector3 Position, Quaternion Rotation) Card(int index, int count, Vector2 size)
        {
            var step = count < 2 ? 0f : Mathf.Min(CardTurn, MaxSpread / (count - 1));
            var angle = (index - (count - 1) / 2f) * step;
            // Turning about the face axis (toward the holder) by a positive angle tips a card's top to the holder's right.
            var fan = Quaternion.AngleAxis(angle, Face);
            var up = fan * Up;
            var position = Grip + up * (size.y * 0.42f) + Face * (0.0012f * index);
            return (position, Quaternion.LookRotation(-up, Face));
        }

    }
}
