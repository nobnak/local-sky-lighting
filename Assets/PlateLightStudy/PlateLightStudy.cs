using System;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LocalSkyLighting {
    [ExecuteAlways]
    public sealed class PlateLightStudy : MonoBehaviour {
        [SerializeField] MeshRenderer plate;
        [SerializeField] Camera plateCamera;
        [SerializeField] Volume postProcessing;
        [SerializeField] AreaLight spotlight;

        MaterialPropertyBlock properties;
        bool lightingDirty;

        int lighting;
        Vector2 tilt;
        Quaternion phonePose;
        Quaternion neutralPose;
        bool hasPose;
        float screenAngle;

        #region Unity lifecycle
        void OnEnable() {
            properties = new MaterialPropertyBlock();
            lightingDirty = true;
        }

        void OnValidate() => lightingDirty = true;

        void LateUpdate() {
            if (plateCamera != null) plateCamera.orthographicSize = 3.1f / Mathf.Min(plateCamera.aspect, 640f / 382f);
            if (!lightingDirty || plate == null) return;
            UpdateSpotlight();
            lightingDirty = false;
        }
        #endregion

        #region public interface
        [Preserve]
        public void SetLighting(int value) {
            lighting = Mathf.Clamp(value, 0, SpotAngles.Length - 1);
            lightingDirty = true;
        }

        [Preserve]
        public void SetBloom(int value) {
            if (postProcessing != null && postProcessing.profile.TryGet<Bloom>(out var bloom))
                bloom.active = value != 0;
        }

        [Preserve]
        public void SetManualTilt(string json) {
            var input = JsonUtility.FromJson<ManualTilt>(json);
            tilt = ClampTilt(new Vector2(input.x, input.y));
            lightingDirty = true;
        }

        [Preserve]
        public void BeginMotion() {
            hasPose = false;
            tilt = Vector2.zero;
            lightingDirty = true;
        }

        [Preserve]
        public void ResetTilt() {
            if (hasPose) neutralPose = phonePose;
            tilt = Vector2.zero;
            lightingDirty = true;
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
            lightingDirty = true;
        }
        #endregion

        #region drawing
        static Vector2 ClampTilt(Vector2 value) => new Vector2(
            Mathf.Clamp(value.x, -TiltLimit, TiltLimit), Mathf.Clamp(value.y, -TiltLimit, TiltLimit));

        void UpdateSpotlight() {
            var rotation = Quaternion.AngleAxis(-tilt.x, Vector3.up) * Quaternion.AngleAxis(tilt.y, Vector3.right);
            var halfAngle = SpotAngles[lighting] * .5f;
            var outer = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
            var inner = Mathf.Cos(halfAngle * .8f * Mathf.Deg2Rad);
            var area = Mathf.Max(spotlight.width * spotlight.height, .0001f);
            var radiance = spotlight.power / (Mathf.PI * area);
            properties.SetVector(LightPosition, rotation * spotlight.position);
            properties.SetVector(LightRight, WithW(rotation * spotlight.right, spotlight.width * .5f));
            properties.SetVector(LightUp, WithW(rotation * spotlight.up, spotlight.height * .5f));
            properties.SetVector(LightForward, rotation * spotlight.forward);
            properties.SetVector(LightRadiance, WithW(spotlight.color * radiance, area));
            properties.SetVector(BeamCone, new Vector4(outer, inner, 0, 0));
            plate.SetPropertyBlock(properties);
        }

        static Vector4 WithW(Vector3 value, float w) => new Vector4(value.x, value.y, value.z, w);
        #endregion

        #region serialized input and constants
        [Serializable]
        struct ManualTilt { public float x, y; }

        [Serializable]
        struct DevicePose { public float alpha, beta, gamma, screen; }

        [Serializable]
        public struct AreaLight {
            public string name;
            public Vector3 position, right, up, forward, color;
            public float power, width, height;
        }

        static readonly int LightPosition = Shader.PropertyToID("_LightPosition");
        static readonly int LightRight = Shader.PropertyToID("_LightRight");
        static readonly int LightUp = Shader.PropertyToID("_LightUp");
        static readonly int LightForward = Shader.PropertyToID("_LightForward");
        static readonly int BeamCone = Shader.PropertyToID("_BeamCone");
        static readonly int LightRadiance = Shader.PropertyToID("_LightRadiance");
        const float TiltLimit = 24f;
        static readonly float[] SpotAngles = { 164f, 136f, 112f };
        #endregion
    }
}
