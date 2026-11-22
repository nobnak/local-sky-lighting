using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace LocalSkyLighting {
    public static class SkyLightingDemoEditor {
        [MenuItem("Local Sky Lighting/Build Web Demo to docs")]
        public static void BuildWeb() {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new InvalidOperationException("Install Web Build Support for this Unity Editor.");
            var compression = PlayerSettings.WebGL.compressionFormat;
            var fallback = PlayerSettings.WebGL.decompressionFallback;
            var template = PlayerSettings.WebGL.template;
            var stripping = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL);
            // The demo uses Unity UI Toolkit and does not need App UI's TextMate library.
            var textMate = AssetImporter.GetAtPath(AppUiTextMatePath) as PluginImporter;
            textMate?.SetIncludeInBuildDelegate(_ => false);
            try {
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                PlayerSettings.WebGL.decompressionFallback = true;
                PlayerSettings.WebGL.template = "PROJECT:LocalSkyLighting";
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
                var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "docs", BuildTarget.WebGL, BuildOptions.None);
                if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(report.summary.result.ToString());
                File.WriteAllText("docs/.nojekyll", "");
            } finally {
                textMate?.SetIncludeInBuildDelegate(null);
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, stripping);
                PlayerSettings.WebGL.template = template;
                PlayerSettings.WebGL.decompressionFallback = fallback;
                PlayerSettings.WebGL.compressionFormat = compression;
            }
        }

        public static void Capture() {
            EditorSceneManager.OpenScene(ScenePath);
            var demo = Object.FindAnyObjectByType<SkyLightingDemo>();
            var camera = Object.FindAnyObjectByType<Camera>();
            var target = new RenderTexture(640, 720, 24);
            var image = new Texture2D(640, 720, TextureFormat.RGB24, false);
            string output = Environment.GetEnvironmentVariable("LOCAL_SKY_CAPTURE_DIR") ?? Path.GetFullPath("Temp");
            Directory.CreateDirectory(output);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Vector3 world = SolarDirection.FromLocation(35.6762, 139.6503, new DateTimeOffset(2026, 9, 22, 6, 0, 0, TimeSpan.Zero));
            Vector3 north = PhoneOrientation.ToScene(PhoneOrientation.ToWorld(0, 16, 0), world);
            Vector3 south = PhoneOrientation.ToScene(PhoneOrientation.ToWorld(-180, 16, 0), world);
            if (north.x >= 0 || south.x <= 0 || north.z >= 0 || south.z >= 0)
                throw new InvalidOperationException("Expected westward sun on opposite sides of the plate, above it in both cases.");
            Debug.Log($"Sun E/N/U {world}; north preview {north}; south preview {south}");
            foreach (var (heading, name) in new[] { (0f, "north"), (180f, "south") }) {
                demo.SetHeading(heading);
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 640, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, $"sky-{name}.png"), image.EncodeToPNG());
            }
            RenderTexture.active = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
        }

        #region Constants
        private const string AppUiTextMatePath = "Packages/com.unity.dt.app-ui/Runtime/Core/Platform/WebGL/Plugins/libAppUITextMateLib.a";
        private const string ScenePath = "Assets/Scenes/SkyLightingDemo.unity";
        #endregion
    }
}
