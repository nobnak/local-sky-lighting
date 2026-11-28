using System;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if !UNITY_WEBGL || UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

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

        Quaternion rigToPlate = Quaternion.identity;
        int lighting;
        float zoom = 1f;
        Vector2 tilt;
        Quaternion phonePose;
        Quaternion neutralPose;
        bool hasPose;
        float screenAngle;
        float headingAngle;
#if !UNITY_WEBGL || UNITY_EDITOR
        InputAction mouseButton;
        Mouse dragMouse;
        Vector2 previousDragPosition;
        Vector2 plateTilt;
        Quaternion restingPlateRotation;
        bool hdrOutputActive;
        Tonemapping hdrTonemapping;
        VolumeProfile hdrProfile;
        TonemappingMode sdrTonemappingMode;
        bool sdrTonemappingActive;
        bool sdrTonemappingOverride;
        bool addedTonemapping;
#endif

        #region Unity lifecycle
        void OnEnable() {
            properties = new MaterialPropertyBlock();
            lightingDirty = true;
#if !UNITY_WEBGL || UNITY_EDITOR
            if (!Application.isPlaying) return;
            if (plate != null) restingPlateRotation = plate.transform.localRotation;
            mouseButton = new InputAction(type: InputActionType.Button, binding: "<Mouse>/leftButton");
            mouseButton.performed += OnMousePressed;
            mouseButton.canceled += OnMouseReleased;
            mouseButton.Enable();
#endif
        }

        void OnValidate() => lightingDirty = true;

#if !UNITY_WEBGL || UNITY_EDITOR
        void OnDisable() {
            RestoreSdrTonemapping();
            hdrOutputActive = false;
            dragMouse = null;
            mouseButton?.Dispose();
            mouseButton = null;
            if (plateTilt != Vector2.zero && plate != null) plate.transform.localRotation = restingPlateRotation;
            plateTilt = Vector2.zero;
            rigToPlate = Quaternion.identity;
        }

        void OnApplicationFocus(bool focused) {
            if (!focused) dragMouse = null;
        }

        void Update() {
            if (!Application.isPlaying) return;
            var output = HDROutputSettings.main;
            var active = CameraAllowsHdrOutput && output.available && output.active;
            if (active != hdrOutputActive) {
                SetHdrTonemapping(active);
                hdrOutputActive = active;
            }

            UpdateMouseDrag();
            if (Application.isFocused && Mouse.current is { } mouse) {
                var scroll = mouse.scroll.ReadValue().y;
                if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange &&
                    (Application.platform == RuntimePlatform.WindowsPlayer ||
                     Application.platform == RuntimePlatform.WindowsEditor)) scroll /= 120f;
                if (scroll != 0) ZoomBy(Mathf.Pow(ZoomStep, scroll));
            }
        }

        void OnGUI() {
            if (!Application.isPlaying) return;
            var output = HDROutputSettings.main;
            var canSwitch = CanSwitchHdr(output);
            var pending = canSwitch && output.HDRModeChangeRequested;
            var label = hdrOutputActive ? "HDR  >  SDR" : "SDR  >  HDR";
            if (!canSwitch) label = hdrOutputActive ? "HDR output" : "SDR (HDR unavailable)";
            else if (pending) label = "Switching output...";

            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && canSwitch && !pending;
            GUI.Button(HdrButtonRect, label);
            GUI.enabled = wasEnabled;
        }
#endif

        void LateUpdate() {
            if (plateCamera != null) plateCamera.orthographicSize = 3.1f / (Mathf.Min(plateCamera.aspect, 640f / 382f) * zoom);
            if (plate == null || (!lightingDirty && referencePhase != 0)) return;
            UpdateSpotlight();
            lightingDirty = false;
        }
        #endregion

        #region public interface
        [Preserve]
        public void ZoomBy(float factor) {
            if (float.IsFinite(factor) && factor > 0) zoom = Mathf.Clamp(zoom * factor, MinZoom, MaxZoom);
        }

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
        public void ApplyFixedReferenceLighting(int phase, Vector2 manualTilt = default) {
            SetReferencePhase(phase);
            ResetTilt();
            tilt = ClampTilt(manualTilt);
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
            headingAngle = 0;
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
                if (Mathf.Abs(Mathf.DeltaAngle(headingAngle, heading)) > .001f) {
                    headingAngle = heading;
                    lightingDirty = true;
                }
            }

            var poseWithoutHeading = Quaternion.AngleAxis(-headingAngle, Vector3.forward) * phonePose;
            var originalNormal = Quaternion.Inverse(poseWithoutHeading) * (neutralPose * Vector3.forward);
            var nextTilt = ClampTilt(new Vector2(Mathf.Atan2(originalNormal.x, originalNormal.z),
                Mathf.Atan2(originalNormal.y, originalNormal.z)) * Mathf.Rad2Deg);
            if ((nextTilt - tilt).sqrMagnitude <= .000001f) return;
            tilt = nextTilt;
            lightingDirty = true;
        }
        #endregion

#if !UNITY_WEBGL || UNITY_EDITOR
        #region mouse input
        void OnMousePressed(InputAction.CallbackContext context) {
            if (!Application.isFocused || context.control.device is not Mouse mouse) return;
            var position = mouse.position.ReadValue();
            if (!HdrButtonRect.Contains(new Vector2(position.x, Screen.height - position.y))) {
                dragMouse = mouse;
                previousDragPosition = position;
                return;
            }

            var output = HDROutputSettings.main;
            if (CanSwitchHdr(output) && !output.HDRModeChangeRequested)
                output.RequestHDRModeChange(!output.active);
        }

        void OnMouseReleased(InputAction.CallbackContext context) {
            UpdateMouseDrag();
            dragMouse = null;
        }

        void UpdateMouseDrag() {
            if (dragMouse == null || !Application.isFocused) return;
            var position = dragMouse.position.ReadValue();
            if (position == previousDragPosition) return;
            var degreesPerPixel = 2 * TiltLimit / Mathf.Max(1, Mathf.Min(Screen.width, Screen.height));
            SetPlateTilt(plateTilt + (position - previousDragPosition) * degreesPerPixel);
            previousDragPosition = position;
        }

        void SetPlateTilt(Vector2 angles) {
            if (plate == null || plateCamera == null) return;
            var nextTilt = ClampTilt(angles);
            if (nextTilt == plateTilt) return;
            plateTilt = nextTilt;
            var plateTransform = plate.transform;
            var cameraTransform = plateCamera.transform;
            var parent = plateTransform.parent;
            var up = parent != null ? parent.InverseTransformDirection(cameraTransform.up) : cameraTransform.up;
            var right = parent != null ? parent.InverseTransformDirection(cameraTransform.right) : cameraTransform.right;
            var orientation = Quaternion.AngleAxis(-plateTilt.x, up) * Quaternion.AngleAxis(plateTilt.y, right)
                * restingPlateRotation;
            plateTransform.localRotation = orientation;
            // The shader evaluates the fixed rig in the rotating plate's local coordinates.
            rigToPlate = Quaternion.Inverse(orientation) * restingPlateRotation;
            lightingDirty = true;
        }
        #endregion

        #region display output
        bool CameraAllowsHdrOutput => plateCamera != null && plateCamera.allowHDR && plateCamera.targetDisplay == 0 &&
            plateCamera.targetTexture == null && plateCamera.GetUniversalAdditionalCameraData().allowHDROutput &&
            UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.supportsHDR;

        bool CanSwitchHdr(HDROutputSettings output) => CameraAllowsHdrOutput && output.available &&
            (SystemInfo.hdrDisplaySupportFlags & HDRDisplaySupportFlags.RuntimeSwitchable) != 0;

        static Rect HdrButtonRect => new Rect(12, Screen.height - 46, 172, 34);

        void SetHdrTonemapping(bool enabled) {
            if (!enabled) {
                RestoreSdrTonemapping();
                return;
            }
            if (postProcessing == null || hdrTonemapping != null) return;
            hdrProfile = postProcessing.profile;
            addedTonemapping = !hdrProfile.TryGet(out hdrTonemapping);
            if (addedTonemapping) hdrTonemapping = hdrProfile.Add<Tonemapping>();
            sdrTonemappingActive = hdrTonemapping.active;
            sdrTonemappingMode = hdrTonemapping.mode.value;
            sdrTonemappingOverride = hdrTonemapping.mode.overrideState;
            hdrTonemapping.active = true;
            hdrTonemapping.mode.Override(TonemappingMode.None);
        }

        void RestoreSdrTonemapping() {
            if (hdrTonemapping == null) return;
            if (addedTonemapping) hdrProfile.Remove<Tonemapping>();
            else {
                hdrTonemapping.active = sdrTonemappingActive;
                hdrTonemapping.mode.value = sdrTonemappingMode;
                hdrTonemapping.mode.overrideState = sdrTonemappingOverride;
            }
            hdrTonemapping = null;
            hdrProfile = null;
        }
        #endregion
#endif

        #region drawing
        static Vector2 ClampTilt(Vector2 value) => new Vector2(
            Mathf.Clamp(value.x, -TiltLimit, TiltLimit), Mathf.Clamp(value.y, -TiltLimit, TiltLimit));

        void UpdateSpotlight() {
            var rotation = Quaternion.AngleAxis(-tilt.x, Vector3.up) * Quaternion.AngleAxis(tilt.y, Vector3.right)
                * Quaternion.AngleAxis(-headingAngle, Vector3.forward);
            var lightToPlate = rigToPlate * rotation;
            var count = emitters == null || emitters.Length == 0 ? 1 : Mathf.Min(emitters.Length, MaxEmitters);
            for (int i = 0; i < count; i++) {
                var emitter = emitters == null || emitters.Length == 0 ? spotlight : emitters[i];
                var area = Mathf.Max(emitter.width * emitter.height, .0001f);
                positions[i] = lightToPlate * emitter.position;
                rights[i] = WithW(lightToPlate * emitter.right, emitter.width * .5f);
                ups[i] = WithW(lightToPlate * emitter.up, emitter.height * .5f);
                forwards[i] = lightToPlate * emitter.forward;
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
            properties.SetVector(ProjectionRight, rigToPlate * Vector3.right);
            properties.SetVector(ProjectionUp, rigToPlate * Vector3.up);
            properties.SetFloat(ProjectionFalloffScale, referencePhase < 0 ? 2f : 1f);
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
        static readonly int ProjectionRight = Shader.PropertyToID("_ProjectionRight");
        static readonly int ProjectionUp = Shader.PropertyToID("_ProjectionUp");
        static readonly int ProjectionFalloffScale = Shader.PropertyToID("_ProjectionFalloffScale");
        static readonly int LightRadiance = Shader.PropertyToID("_LightRadiance");
        const float TiltLimit = 24f;
        const float MinZoom = .75f;
        const float MaxZoom = 3f;
        const float ZoomStep = 1.15f;
        const int MaxEmitters = 9;
        static readonly float[] ProjectionRadii = { 1.78f, 1.18f, .47f };
        #endregion
    }
}
