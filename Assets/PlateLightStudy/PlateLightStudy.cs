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
        [SerializeField] AreaLight[] emitters;

        readonly Vector4[] positions = new Vector4[MaxEmitters];
        readonly Vector4[] rights = new Vector4[MaxEmitters];
        readonly Vector4[] ups = new Vector4[MaxEmitters];
        readonly Vector4[] forwards = new Vector4[MaxEmitters];
        readonly Vector4[] radiances = new Vector4[MaxEmitters];
        readonly Vector4[] cones = new Vector4[MaxEmitters];
        int referencePhase = -1;
        float cycleStart;

        MaterialPropertyBlock properties;
        bool lightingDirty;

        int lighting;
        Vector2 tilt;
        Quaternion phonePose;
        Quaternion neutralPose;
        bool hasPose;
        float screenAngle;
        float orbitAngle;
        bool cameraDirty;
        bool hasCameraHome;
        Vector3 cameraHomePosition;
        Quaternion cameraHomeRotation;

        #region Unity lifecycle
        void OnEnable() {
            properties = new MaterialPropertyBlock();
            lightingDirty = true;
            if (!Application.isPlaying || plateCamera == null) return;
            cameraHomePosition = plateCamera.transform.position;
            cameraHomeRotation = plateCamera.transform.rotation;
            hasCameraHome = true;
            cameraDirty = true;
        }

        void OnDisable() {
            if (hasCameraHome && plateCamera != null)
                plateCamera.transform.SetPositionAndRotation(cameraHomePosition, cameraHomeRotation);
            hasCameraHome = false;
        }

        void OnValidate() => lightingDirty = true;

        void LateUpdate() {
            if (plateCamera != null) plateCamera.orthographicSize = 3.1f / Mathf.Min(plateCamera.aspect, 640f / 382f);
            if (hasCameraHome && cameraDirty && plateCamera != null) {
                var orbit = Quaternion.AngleAxis(orbitAngle, Vector3.forward);
                var center = plate != null ? plate.transform.position : transform.position;
                plateCamera.transform.SetPositionAndRotation(center + orbit * (cameraHomePosition - center),
                    orbit * cameraHomeRotation);
                cameraDirty = false;
            }
            if (plate == null || (!lightingDirty && referencePhase != 0)) return;
            UpdateSpotlight();
            lightingDirty = false;
        }
        #endregion

        #region public interface
        [Preserve]
        public void SetLighting(int value) {
            lighting = Mathf.Clamp(value, 0, ProjectionRadii.Length - 1);
            lightingDirty = true;
        }

        [Preserve]
        public void SetReferencePhase(int value) {
            referencePhase = value;
            cycleStart = Time.unscaledTime;
            lightingDirty = true;
        }

#if UNITY_EDITOR
        public void ApplyFixedReferenceLighting(int phase) {
            SetReferencePhase(phase);
            ResetTilt();
            UpdateSpotlight();
            lightingDirty = false;
        }
#endif

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
            ResetTilt();
        }

        [Preserve]
        public void ResetTilt() {
            if (hasPose) neutralPose = phonePose;
            tilt = Vector2.zero;
            orbitAngle = 0;
            cameraDirty = true;
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

            // Split the relative pose into world-vertical twist and residual tilt.
            var relative = phonePose * Quaternion.Inverse(neutralPose);
            if (!input.headingUnavailable && relative.z * relative.z + relative.w * relative.w > .0001f) {
                var heading = Mathf.DeltaAngle(0, 2 * Mathf.Atan2(relative.z, relative.w) * Mathf.Rad2Deg);
                if (Mathf.Abs(Mathf.DeltaAngle(orbitAngle, heading)) > .001f) {
                    orbitAngle = heading;
                    cameraDirty = true;
                }
            }

            var poseWithoutHeading = Quaternion.AngleAxis(-orbitAngle, Vector3.forward) * phonePose;
            var originalNormal = Quaternion.Inverse(poseWithoutHeading) * (neutralPose * Vector3.forward);
            var nextTilt = ClampTilt(new Vector2(Mathf.Atan2(originalNormal.x, originalNormal.z),
                Mathf.Atan2(originalNormal.y, originalNormal.z)) * Mathf.Rad2Deg);
            if ((nextTilt - tilt).sqrMagnitude <= .000001f) return;
            tilt = nextTilt;
            lightingDirty = true;
        }
        #endregion

        #region drawing
        static Vector2 ClampTilt(Vector2 value) => new Vector2(
            Mathf.Clamp(value.x, -TiltLimit, TiltLimit), Mathf.Clamp(value.y, -TiltLimit, TiltLimit));

        void UpdateSpotlight() {
            var rotation = Quaternion.AngleAxis(-tilt.x, Vector3.up) * Quaternion.AngleAxis(tilt.y, Vector3.right);
            var count = emitters == null || emitters.Length == 0 ? 1 : Mathf.Min(emitters.Length, MaxEmitters);
            for (int i = 0; i < count; i++) {
                var emitter = emitters == null || emitters.Length == 0 ? spotlight : emitters[i];
                var area = Mathf.Max(emitter.width * emitter.height, .0001f);
                positions[i] = rotation * emitter.position;
                rights[i] = WithW(rotation * emitter.right, emitter.width * .5f);
                ups[i] = WithW(rotation * emitter.up, emitter.height * .5f);
                forwards[i] = rotation * emitter.forward;
                radiances[i] = WithW(emitter.color * (emitter.power / (Mathf.PI * area)), area);
                cones[i] = new Vector4(.559193f, .829038f, 0, 0);
            }
            properties.SetInt(LightCount, count);
            properties.SetVectorArray(LightPosition, positions);
            properties.SetVectorArray(LightRight, rights);
            properties.SetVectorArray(LightUp, ups);
            properties.SetVectorArray(LightForward, forwards);
            properties.SetVectorArray(LightRadiance, radiances);
            properties.SetVectorArray(BeamCones, cones);
            var phase = referencePhase == 0 ? 4 + Mathf.PingPong(Time.unscaledTime - cycleStart, 10) : referencePhase;
            var radius = referencePhase < 0 ? ProjectionRadii[lighting] :
                Mathf.Lerp(.47f, ProjectionRadii[0], Mathf.SmoothStep(0, 1, (phase - 4) / 5));
            var gain = referencePhase < 0 ? 1 : Mathf.Lerp(1, .25f, Mathf.SmoothStep(0, 1, (phase - 9) / 5));
            var shift = rotation * new Vector3(0, 0, 1.3f);
            properties.SetVector(Projection, new Vector4(shift.x, shift.y, radius, gain));
            plate.SetPropertyBlock(properties);
        }

        static Vector4 WithW(Vector3 value, float w) => new Vector4(value.x, value.y, value.z, w);
        #endregion

        #region serialized input and constants
        [Serializable]
        struct ManualTilt { public float x, y; }

        [Serializable]
        struct DevicePose {
            public float alpha, beta, gamma, screen;
            public bool headingUnavailable;
        }

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
        static readonly int BeamCones = Shader.PropertyToID("_BeamCones");
        static readonly int LightCount = Shader.PropertyToID("_LightCount");
        static readonly int Projection = Shader.PropertyToID("_Projection");
        static readonly int LightRadiance = Shader.PropertyToID("_LightRadiance");
        const float TiltLimit = 24f;
        const int MaxEmitters = 9;
        static readonly float[] ProjectionRadii = { 1.78f, 1.18f, .47f };
        #endregion
    }
}
