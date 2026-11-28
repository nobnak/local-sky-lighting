using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace LocalSkyLighting {
    public static class PlateOrbitCapture {
        #region public interface
        public static void Render() {
            var args = Environment.GetCommandLineArgs();
            var outputFlag = Array.IndexOf(args, "-plateOrbitOutput");
            if (outputFlag < 0 || outputFlag + 1 >= args.Length)
                throw new ArgumentException("Specify -plateOrbitOutput followed by an output directory.");
            var directory = Path.GetFullPath(args[outputFlag + 1]);
            RenderVariant(directory, Array.IndexOf(args, "-plateOrbitTuned") >= 0);
        }
        #endregion

        #region render
        static void RenderVariant(string directory, bool tuneReflection) {
            Directory.CreateDirectory(directory);

            EditorSceneManager.OpenScene("Assets/PlateLightStudy/PlateLightStudy.unity");
            var study = Object.FindFirstObjectByType<PlateLightStudy>();
            var camera = Object.FindFirstObjectByType<Camera>();
            if (tuneReflection) TuneReflection(study);
            study.ApplyFixedReferenceLighting(LightingPhase);
            if (tuneReflection) SetMaterialOverrides(study);
            camera.orthographic = false;
            camera.fieldOfView = 26;
            camera.aspect = (float)Width / Height;

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            try {
                using var poses = File.CreateText(Path.Combine(directory, "poses.csv"));
                poses.WriteLine("frame,official_seconds,x,y,z,qx,qy,qz,qw,vertical_fov");
                for (int frame = 0; frame < FrameCount; frame++) {
                    float time = frame / (float)FrameRate;
                    SetCameraPose(camera, time);
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                    File.WriteAllBytes(Path.Combine(directory, $"orbit-{frame:D3}.png"), image.EncodeToPNG());
                    var p = camera.transform.position;
                    var q = camera.transform.rotation;
                    poses.WriteLine(FormattableString.Invariant(
                        $"{frame},{36 + time:F6},{p.x:R},{p.y:R},{p.z:R},{q.x:R},{q.y:R},{q.z:R},{q.w:R},{camera.fieldOfView:R}"));
                }
                Debug.Log($"Captured {FrameCount} frames in {directory} with fixed lighting phase {LightingPhase}.");
            } finally {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(target);
            }
        }

        static void TuneReflection(PlateLightStudy study) {
            using var serialized = new SerializedObject(study);
            var emitters = serialized.FindProperty("emitters");
            for (int i = 0; i < emitters.arraySize; i++) {
                var property = emitters.GetArrayElementAtIndex(i);
                var light = (PlateLightStudy.AreaLight)property.boxedValue;
                var area = light.width * light.height;
                var broad = Mathf.SmoothStep(0, 1, (area - .24f) / .85f);
                light.power *= Mathf.Lerp(1, .22f, broad);
                property.boxedValue = light;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetMaterialOverrides(PlateLightStudy study) {
            var renderer = study.GetComponent<MeshRenderer>();
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetFloat("_RoughnessScale", .70f);
            properties.SetFloat("_MirrorShare", .88f);
            renderer.SetPropertyBlock(properties);
        }
        #endregion

        #region camera path
        static void SetCameraPose(Camera camera, float time) {
            var angle = Azimuth.Evaluate(time);
            var orbit = Quaternion.AngleAxis(angle, Vector3.forward);
            var roll = Quaternion.AngleAxis(-.26f * angle, Vector3.forward);
            camera.transform.SetPositionAndRotation(orbit * InitialPosition, orbit * InitialView * roll);
        }

        static AnimationCurve Curve(params float[] values) {
            var keys = new Keyframe[values.Length];
            for (int i = 0; i < keys.Length; i++) {
                int previous = Mathf.Max(i - 1, 0);
                int next = Mathf.Min(i + 1, keys.Length - 1);
                float slope = (values[next] - values[previous]) / (Times[next] - Times[previous]);
                keys[i] = new Keyframe(Times[i], values[i], slope, slope);
            }
            return new AnimationCurve(keys);
        }
        #endregion

        #region capture settings
        const int Width = 960;
        const int Height = 540;
        const int FrameRate = 24;
        const int FrameCount = 11 * FrameRate;
        const int LightingPhase = 12;
        // Approximate framing guides relative to the 0:36 start of the official shot.
        static readonly float[] Times = { 0, 2, 5, 7, 10, 11 };
        static readonly AnimationCurve Azimuth = Curve(0, 7, 19, 28, 39, 42);
        static readonly Quaternion InitialView = Quaternion.Euler(-26, -9, -7);
        static readonly Vector3 InitialPosition = new Vector3(.025f, -.055f, 0) + InitialView * new Vector3(0, 0, -4.27f);
        #endregion
    }
}
