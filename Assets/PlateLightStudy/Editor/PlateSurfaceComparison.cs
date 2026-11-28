using System;
using System.IO;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace LocalSkyLighting {
    public static class PlateSurfaceComparison {
        #region public interface
        public static void Render() {
            var args = Environment.GetCommandLineArgs();
            var output = Path.GetFullPath(Argument(args, "-strikeComparisonOutput"));
            var maps = Path.GetFullPath(Argument(args, "-strikeComparisonMaps"));
            var exposureScale = ExposureScale(args);
            var frameCount = Array.IndexOf(args, "-strikeComparisonPreview") >= 0 ? 5 : FullFrameCount;
            EditorSceneManager.OpenScene("Assets/PlateLightStudy/PlateLightStudy.unity");
            var study = Object.FindAnyObjectByType<PlateLightStudy>();
            using var setup = new SerializedObject(study);
            var plate = (MeshRenderer)setup.FindProperty("plate").objectReferenceValue;
            var camera = (Camera)setup.FindProperty("plateCamera").objectReferenceValue;
            var original = plate.sharedMaterials;
            var captureMaterials = Array.ConvertAll(original, material => new Material(material));
            foreach (var material in captureMaterials)
                material.SetFloat("_Exposure", material.GetFloat("_Exposure") * exposureScale);
            var current = captureMaterials[0];
            var metal = new Material(current);
            metal.SetFloat("_ContinuousMetal", 1);
            var struck = new Material(metal);
            var surface = LoadTexture(maps, "overlap-surface.rgba16f", TextureFormat.RGBAHalf);
            var grain = LoadTexture(maps, "overlap-grain.rgba8", TextureFormat.RGBA32);
            struck.SetTexture("_Surface", surface);
            struck.SetTexture("_ScratchDirection", grain);
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var frame = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try {
                camera.orthographic = true;
                camera.aspect = (float)Width / Height;
                camera.orthographicSize = 1.82f;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                var variants = new[] { current, metal, struck };
                Capture(output, study, plate, camera, request, frame, captureMaterials, variants, frameCount, orbit: false);
                Capture(output, study, plate, camera, request, frame, captureMaterials, variants, frameCount, orbit: true);
            } finally {
                plate.sharedMaterials = original;
                captureMaterials[0] = current;
                foreach (var material in captureMaterials) Object.DestroyImmediate(material);
                RenderTexture.active = previous;
                Object.DestroyImmediate(frame);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(metal);
                Object.DestroyImmediate(struck);
                Object.DestroyImmediate(surface);
                Object.DestroyImmediate(grain);
            }
        }
        #endregion

        #region capture
        static void Capture(string output, PlateLightStudy study, MeshRenderer plate, Camera camera,
            UniversalRenderPipeline.SingleCameraRequest request, Texture2D frame, Material[] captureMaterials,
            Material[] variants,
            int frameCount, bool orbit) {
            var directory = Path.Combine(output, orbit ? "orbit" : "light");
            Directory.CreateDirectory(directory);
            using var poses = File.CreateText(Path.Combine(directory, "poses.csv"));
            poses.WriteLine("frame,azimuth,light_x,light_y,ortho_size");
            for (var i = 0; i < frameCount; i++) {
                var time = i / (float)(frameCount - 1);
                var sweep = Mathf.Cos(time * Mathf.PI);
                var azimuth = orbit ? -26 * sweep : 0;
                var tilt = orbit ? Vector2.zero : new Vector2(21 * sweep, 10 * Mathf.Sin(time * 2 * Mathf.PI));
                var cameraRotation = Quaternion.AngleAxis(azimuth, Vector3.forward) * BaseView;
                camera.transform.SetPositionAndRotation(cameraRotation * new Vector3(0, 0, -4.27f), cameraRotation);
                study.ApplyFixedReferenceLighting(-1, tilt);
                for (var variant = 0; variant < variants.Length; variant++) {
                    captureMaterials[0] = variants[variant];
                    plate.sharedMaterials = captureMaterials;
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = request.destination;
                    frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                    File.WriteAllBytes(Path.Combine(directory, $"{i:D3}-{variant}.png"), frame.EncodeToPNG());
                }
                poses.WriteLine(FormattableString.Invariant($"{i},{azimuth:R},{tilt.x:R},{tilt.y:R},{camera.orthographicSize:R}"));
            }
            Debug.Log($"Captured {frameCount} matched triplets in {directory}.");
        }

        static Texture2D LoadTexture(string directory, string fileName, TextureFormat format) {
            var texture = new Texture2D(MapSize, MapSize, format, true, true) {
                name = fileName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            // The raw file holds level zero. SetPixelData leaves the other levels for Apply.
            texture.SetPixelData(File.ReadAllBytes(Path.Combine(directory, fileName)), 0);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }

        static float ExposureScale(string[] args) {
            const string flag = "-strikeComparisonExposureEV";
            if (Array.IndexOf(args, flag) < 0) return 1;
            var value = Argument(args, flag);
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ev) ||
                !float.IsFinite(ev) || ev < -16 || ev > 16)
                throw new ArgumentException($"Specify {flag} in EV, between -16 and 16.");
            Debug.Log(FormattableString.Invariant($"Capture exposure: {ev:+0.##;-0.##;0} EV"));
            return Mathf.Pow(2, ev);
        }

        static string Argument(string[] args, string flag) {
            var index = Array.IndexOf(args, flag);
            if (index < 0 || index + 1 == args.Length) throw new ArgumentException($"Missing {flag} <directory>.");
            return args[index + 1];
        }
        #endregion

        #region capture settings
        const int Width = 640;
        const int Height = 416;
        const int MapSize = 1536;
        const int FullFrameCount = 144;
        static readonly Quaternion BaseView = Quaternion.Euler(-24, 0, 0);
        #endregion
    }
}
