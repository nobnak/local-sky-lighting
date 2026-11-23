using System;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace LocalSkyLighting {
    public sealed class SkyLightingDemo : MonoBehaviour {
        [SerializeField] private Light sun;
        [SerializeField] private Camera view;
        [SerializeField] private UIDocument document;
        [SerializeField] private string materialName;
        [SerializeField] private Transform materialRig;
        [SerializeField] private Cubemap hdrReflection;
        [SerializeField] private Cubemap uniformReflection;
        [SerializeField] private float hdrReflectionIntensity = 1;

        private Vector3 sunWorld;
        private Label source;
        private Button reflectionButton;
        private Texture previousReflection;
        private float previousReflectionIntensity;
        private bool useHdrReflection;
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool useMotion;
        private readonly float[] sample = new float[6];
        private float northOffset;
        private int alignment;
        private bool aligned;
        private bool lastAbsolute;
        private float compassOffset;
        private bool compassReady;
#endif

        private bool HasReflectionComparison => hdrReflection != null && uniformReflection != null;

        private void OnEnable() {
            previousReflection = RenderSettings.customReflectionTexture;
            previousReflectionIntensity = RenderSettings.reflectionIntensity;
            SetReflectionMode(true);
            BuildControls();
            SetPreview(false);
#if UNITY_WEBGL && !UNITY_EDITOR
            useMotion = true;
            alignment = 0;
            aligned = false;
            lastAbsolute = false;
            compassReady = false;
            StartLocalSkyMotion();
#endif
        }

        private void OnDisable() {
            if (HasReflectionComparison && (RenderSettings.customReflectionTexture == hdrReflection
                || RenderSettings.customReflectionTexture == uniformReflection)) {
                RenderSettings.customReflectionTexture = previousReflection;
                RenderSettings.reflectionIntensity = previousReflectionIntensity;
            }
            reflectionButton = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            StopLocalSkyMotion();
#endif
        }

        private void Update() {
            view.orthographicSize = Mathf.Max(2.65f, 1.86f / view.aspect);
#if UNITY_WEBGL && !UNITY_EDITOR
            if (ReadLocalSkyMotion(sample) == 0) return;
            var pose = PhoneOrientation.ToWorld(sample[0], sample[1], sample[2]);
            bool absolute = sample[3] != 0f;
            if (absolute != lastAbsolute) {
                aligned = false;
                compassReady = false;
                lastAbsolute = absolute;
            }
            if (sample[5] != alignment) {
                alignment = (int)sample[5];
                northOffset = PhoneOrientation.ScreenTopHeading(pose);
                aligned = true;
            }
            if (!useMotion) return;

            if (sample[4] >= 0f && Mathf.Abs(sample[1]) < 60 && Mathf.Abs(sample[2]) < 60) {
                compassOffset = Mathf.DeltaAngle(sample[4], PhoneOrientation.ScreenTopHeading(pose));
                compassReady = true;
            }
            if (!aligned && !absolute && !compassReady) {
                SetSource("ALIGN NORTH TO USE MOTION");
                return;
            }

            float offset = aligned ? northOffset : absolute ? 0f : compassOffset;
            ApplyPose(Quaternion.AngleAxis(offset, Vector3.forward) * pose);
            SetSource(aligned ? "LIVE  /  NORTH ALIGNED" : "LIVE  /  COMPASS");
#endif
        }

        private void BuildControls() {
            var root = document.rootVisualElement;
            root.Clear();
            root.style.paddingTop = 16;
            root.style.alignItems = Align.Center;
            root.style.color = (Color)new Color32(247, 238, 219, 255);
            root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root.pickingMode = PickingMode.Ignore;

            var title = new Label("Local Sky Lighting");
            title.style.fontSize = 23;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(title);
            if (!string.IsNullOrEmpty(materialName)) root.Add(new Label(materialName));
            root.Add(new Label("TOKYO  /  22 SEP 2026  /  15:00 JST"));
            source = new Label();
            root.Add(source);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 8;
            root.Add(row);
            AddButton(row, "Level", () => SetPreview(false));
            AddButton(row, "Tilt toward sun", () => SetPreview(true));
#if UNITY_WEBGL && !UNITY_EDITOR
            AddButton(row, "Live motion", () => useMotion = true);
#endif
            if (!HasReflectionComparison) return;

            var reflectionRow = new VisualElement();
            reflectionRow.style.flexDirection = FlexDirection.Row;
            reflectionRow.style.marginTop = 8;
            root.Add(reflectionRow);
            AddButton(reflectionRow, "Turn 180°", () => SetPreview(true, opposite: true));
            reflectionButton = AddButton(reflectionRow, ReflectionLabel(), () => SetReflectionMode(!useHdrReflection));
            reflectionButton.style.minWidth = 134;
        }

        private static Button AddButton(VisualElement row, string caption, Action clicked) {
            var button = new Button(clicked) { text = caption };
            button.style.backgroundColor = (Color)new Color32(230, 217, 187, 255);
            button.style.color = (Color)new Color32(39, 33, 24, 255);
            button.style.marginLeft = 4;
            button.style.marginRight = 4;
            button.style.minHeight = 34;
            button.style.minWidth = 82;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(button);
            return button;
        }

        private void SetSource(string caption) {
            if (source != null && source.text != caption) source.text = caption;
        }

        #region Public interface
        public void SetPreview(bool tilted, bool opposite = false) {
#if UNITY_WEBGL && !UNITY_EDITOR
            useMotion = false;
#endif
            sunWorld = SolarDirection.FromLocation(35.6762, 139.6503, ExampleTime);
            SetSource(tilted ? (opposite ? "TURNED 180°  /  SIMULATED" : "TILTED TOWARD SUN  /  SIMULATED") : "LEVEL  /  SIMULATED");
            Vector3 axis = Vector3.Cross(Vector3.forward, sunWorld);
            Quaternion pose = tilted ? Quaternion.AngleAxis(50, axis) : Quaternion.identity;
            if (tilted && opposite) pose = Quaternion.AngleAxis(180, Vector3.forward) * pose;
            if (materialRig != null)
                sun.transform.rotation = Quaternion.LookRotation(-PhoneOrientation.ToUnityWorld(sunWorld));
            ApplyPose(pose);
        }

        public void SetReflectionMode(bool hdr) {
            if (!HasReflectionComparison) return;
            useHdrReflection = hdr;
            RenderSettings.customReflectionTexture = hdr ? hdrReflection : uniformReflection;
            RenderSettings.reflectionIntensity = hdr ? hdrReflectionIntensity : 1;
            if (reflectionButton != null) reflectionButton.text = ReflectionLabel();
        }
        #endregion

        private string ReflectionLabel() => useHdrReflection ? "HDR sky" : "Uniform reflection";

        private void ApplyPose(Quaternion pose) {
            if (materialRig != null) {
                materialRig.rotation = Quaternion.LookRotation(
                    PhoneOrientation.ToUnityWorld(pose * Vector3.up),
                    PhoneOrientation.ToUnityWorld(pose * Vector3.forward));
                return;
            }

            Vector3 direction = PhoneOrientation.ToView(pose, sunWorld);
            sun.transform.rotation = Quaternion.LookRotation(-view.transform.TransformDirection(direction), view.transform.up);
        }

        #region Constants and native interface
        private static readonly DateTimeOffset ExampleTime = new DateTimeOffset(2026, 9, 22, 6, 0, 0, TimeSpan.Zero);
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void StartLocalSkyMotion();
        [DllImport("__Internal")] private static extern void StopLocalSkyMotion();
        [DllImport("__Internal")] private static extern int ReadLocalSkyMotion([Out] float[] buffer);
#endif
        #endregion
    }
}
