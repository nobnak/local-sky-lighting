using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace LocalSkyLighting {
    public sealed class PlateLightStudy : MonoBehaviour {
        [SerializeField] Texture2D[] atlases;

        int lighting;
        Vector2 tilt;
        Quaternion phonePose;
        Quaternion neutralPose;
        bool hasPose;
        float screenAngle;

        #region Unity lifecycle
        void OnGUI() {
            if (Event.current.type != EventType.Repaint || atlases == null || lighting >= atlases.Length) return;
            var atlas = atlases[lighting];
            if (atlas == null) return;

            var aspect = (float)atlas.width / atlas.height;
            var width = Mathf.Min(Screen.width, Screen.height * aspect);
            var height = width / aspect;
            var viewport = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
            var grid = (tilt / TiltLimit + Vector2.one) * Center;
            var x = Mathf.Min(Mathf.FloorToInt(grid.x), Side - 2);
            var y = Mathf.Min(Mathf.FloorToInt(grid.y), Side - 2);
            var dx = grid.x - x;
            var dy = grid.y - y;

            var tint = GUI.color;
            var weight = 0f;
            DrawFrame(viewport, atlas, x, y, (1f - dx) * (1f - dy), ref weight);
            DrawFrame(viewport, atlas, x + 1, y, dx * (1f - dy), ref weight);
            DrawFrame(viewport, atlas, x, y + 1, (1f - dx) * dy, ref weight);
            DrawFrame(viewport, atlas, x + 1, y + 1, dx * dy, ref weight);
            GUI.color = tint;
        }
        #endregion

        #region public interface
        [Preserve]
        public void SetLighting(int value) => lighting = Mathf.Clamp(value, 0, atlases.Length - 1);

        [Preserve]
        public void SetManualTilt(string json) {
            var input = JsonUtility.FromJson<ManualTilt>(json);
            tilt = ClampTilt(new Vector2(input.x, input.y));
        }

        [Preserve]
        public void BeginMotion() {
            hasPose = false;
            tilt = Vector2.zero;
        }

        [Preserve]
        public void ResetTilt() {
            if (hasPose) neutralPose = phonePose;
            tilt = Vector2.zero;
        }

        [Preserve]
        public void SetPhonePose(string json) {
            var input = JsonUtility.FromJson<DevicePose>(json);
            phonePose = PhoneOrientation.ToWorld(input.alpha, input.beta, input.gamma)
                * Quaternion.AngleAxis(-input.screen, Vector3.forward);
            if (!hasPose || screenAngle != input.screen) {
                hasPose = true;
                screenAngle = input.screen;
                ResetTilt();
                return;
            }

            var originalNormal = Quaternion.Inverse(phonePose) * (neutralPose * Vector3.forward);
            tilt = ClampTilt(new Vector2(Mathf.Atan2(originalNormal.x, originalNormal.z),
                Mathf.Atan2(originalNormal.y, originalNormal.z)) * Mathf.Rad2Deg);
        }
        #endregion

        #region drawing
        static Vector2 ClampTilt(Vector2 value) => new Vector2(
            Mathf.Clamp(value.x, -TiltLimit, TiltLimit), Mathf.Clamp(value.y, -TiltLimit, TiltLimit));

        static void DrawFrame(Rect viewport, Texture2D atlas, int x, int y, float share, ref float weight) {
            if (share <= 0f) return;
            weight += share;
            GUI.color = new Color(1f, 1f, 1f, share / weight);
            var insetX = .5f / atlas.width;
            var insetY = .5f / atlas.height;
            var uv = new Rect((float)x / Side + insetX, 1f - (float)(y + 1) / Side + insetY,
                1f / Side - 2f * insetX, 1f / Side - 2f * insetY);
            GUI.DrawTextureWithTexCoords(viewport, atlas, uv, true);
        }
        #endregion

        #region serialized input and constants
        [Serializable]
        struct ManualTilt { public float x, y; }

        [Serializable]
        struct DevicePose { public float alpha, beta, gamma, screen; }

        const int Side = 5;
        const float Center = (Side - 1) * .5f;
        const float TiltLimit = 24f;
        #endregion
    }
}
