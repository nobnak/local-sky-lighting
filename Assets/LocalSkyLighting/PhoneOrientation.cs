using UnityEngine;

namespace LocalSkyLighting {
    public static class PhoneOrientation {
        // Browser intrinsic Z-X'-Y'' rotations. Both frames are right handed:
        // world = east, north, up; phone = screen-right, screen-top, out of screen.
        public static Quaternion ToWorld(float alpha, float beta, float gamma) =>
            Quaternion.AngleAxis(alpha, Vector3.forward)
            * Quaternion.AngleAxis(beta, Vector3.right)
            * Quaternion.AngleAxis(gamma, Vector3.up);

        public static float ScreenTopHeading(Quaternion pose) {
            Vector3 top = pose * Vector3.up;
            return Mathf.Atan2(top.x, top.y) * Mathf.Rad2Deg;
        }

        public static Vector3 ToUnityWorld(Vector3 eastNorthUp) =>
            new Vector3(eastNorthUp.x, eastNorthUp.z, eastNorthUp.y);

        public static Vector3 ToView(Quaternion pose, Vector3 worldDirection) {
            Vector3 phone = Quaternion.Inverse(pose) * worldDirection;
            // Unity camera space uses +Z into the scene, opposite the screen normal.
            return new Vector3(phone.x, phone.y, -phone.z);
        }
    }
}
