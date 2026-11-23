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
            var sun = Object.FindAnyObjectByType<Light>();
            bool initialSun = sun.enabled;
            var target = new RenderTexture(640, 720, 24);
            var image = new Texture2D(640, 720, TextureFormat.RGB24, false);
            string output = Environment.GetEnvironmentVariable("LOCAL_SKY_CAPTURE_DIR") ?? Path.GetFullPath("Temp");
            Directory.CreateDirectory(output);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            foreach (var (tilted, opposite, hdr, sunlight, name) in new[] {
                (false, false, true, true, "level-hdr"),
                (true, false, false, true, "tilted-uniform"),
                (true, false, true, true, "tilted-hdr"),
                (true, true, false, true, "opposite-uniform"),
                (true, true, true, true, "opposite-hdr"),
                (true, false, true, false, "sky-only-toward"),
                (true, true, true, false, "sky-only-opposite")
            }) {
                sun.enabled = sunlight;
                demo.SetReflectionMode(hdr);
                demo.SetPreview(tilted, opposite);
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 640, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, $"sky-{name}.png"), image.EncodeToPNG());
            }
            sun.enabled = initialSun;
            demo.SetReflectionMode(true);
            demo.SetPreview(false);
            RenderTexture.active = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
        }

        #region Constants
        private const string AppUiTextMatePath = "Packages/com.unity.dt.app-ui/Runtime/Core/Platform/WebGL/Plugins/libAppUITextMateLib.a";
        private const string ScenePath = "Assets/Scenes/HdrTreadPlateDemo.unity";
        #endregion
    }
}
