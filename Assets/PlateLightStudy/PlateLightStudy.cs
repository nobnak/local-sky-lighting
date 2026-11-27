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
        [SerializeField] AreaLight[] lights;

        readonly Vector4[] positions = new Vector4[MaxLights];
        readonly Vector4[] rights = new Vector4[MaxLights];
        readonly Vector4[] ups = new Vector4[MaxLights];
        readonly Vector4[] forwards = new Vector4[MaxLights];
        readonly Vector4[] radiances = new Vector4[MaxLights];
        readonly Vector4[] beamCones = new Vector4[MaxLights];
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
            if (!lightingDirty || plate == null || lights == null) return;
            UpdateLights();
            lightingDirty = false;
        }
        #endregion

        #region public interface
        [Preserve]
        public void SetLighting(int value) {
            lighting = Mathf.Clamp(value, 0, 2);
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

        void UpdateLights() {
            var rotation = Quaternion.AngleAxis(-tilt.x, Vector3.up) * Quaternion.AngleAxis(tilt.y, Vector3.right);
            var count = Mathf.Min(lights.Length, MaxLights);
            for (var i = 0; i < count; i++) {
                var light = lights[i];
                var halfAngle = light.name == GoldLight ? 32f / (1 << lighting) : 56f;
                var outer = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
                var inner = Mathf.Cos(halfAngle * .6f * Mathf.Deg2Rad);
                beamCones[i] = new Vector4(outer, inner, 0, 0);
                var area = Mathf.Max(light.width * light.height, .0001f);
                var radiance = light.power / (Mathf.PI * area);
                positions[i] = rotation * light.position;
                rights[i] = WithW(rotation * light.right, light.width * .5f);
                ups[i] = WithW(rotation * light.up, light.height * .5f);
                forwards[i] = rotation * light.forward;
                radiances[i] = WithW(light.color * radiance, area);
            }
            properties.SetInt(LightCount, count);
            properties.SetVectorArray(LightPosition, positions);
            properties.SetVectorArray(LightRight, rights);
            properties.SetVectorArray(LightUp, ups);
            properties.SetVectorArray(LightForward, forwards);
            properties.SetVectorArray(LightRadiance, radiances);
            properties.SetVectorArray(BeamCones, beamCones);
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

        const int MaxLights = 9;
        const string GoldLight = "Gold streak at lower center";
        static readonly int LightCount = Shader.PropertyToID("_LightCount");
        static readonly int LightPosition = Shader.PropertyToID("_LightPosition");
        static readonly int LightRight = Shader.PropertyToID("_LightRight");
        static readonly int LightUp = Shader.PropertyToID("_LightUp");
        static readonly int LightForward = Shader.PropertyToID("_LightForward");
        static readonly int BeamCones = Shader.PropertyToID("_BeamCones");
        static readonly int LightRadiance = Shader.PropertyToID("_LightRadiance");
        const float TiltLimit = 24f;
        #endregion
    }
}
