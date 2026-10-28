using UnityEngine;

namespace TableWorld.View.Chalkers
{
    /// <summary>Two-bone IK for a chalker's limbs, shapes and rigged model alike.</summary>
    public static class TwoBoneIk
    {
        /// <summary>
        /// Turns <paramref name="upper"/> and <paramref name="lower"/> so <paramref name="end"/> reaches
        /// <paramref name="target"/> (as near as the limb's length allows), the middle joint bending toward
        /// <paramref name="pole"/>. <paramref name="down"/> and <paramref name="forward"/> stand in for a direction the
        /// target or pole do not give.
        /// </summary>
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, Vector3 down, Vector3 forward)
        {
            var a = upper.position;
            var lengthA = Vector3.Distance(a, lower.position);
            var lengthB = Vector3.Distance(lower.position, end.position);
            var toTarget = target - a;
            var distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(lengthA - lengthB) + 1e-3f, (lengthA + lengthB) * 0.999f);
            var direction = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : down;
            var bend = Vector3.ProjectOnPlane(pole - a, direction);
            if (bend.sqrMagnitude < 1e-8f)
                bend = Vector3.ProjectOnPlane(forward, direction);
            bend.Normalize();
            var cos = Mathf.Clamp((lengthA * lengthA + distance * distance - lengthB * lengthB) / (2f * lengthA * distance), -1f, 1f);
            var middle = a + direction * (lengthA * cos) + bend * (lengthA * Mathf.Sqrt(1f - cos * cos));
            upper.rotation = Quaternion.FromToRotation(lower.position - a, middle - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, a + direction * distance - lower.position) * lower.rotation;
        }
    }
}
