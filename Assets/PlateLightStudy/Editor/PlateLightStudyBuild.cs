using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LocalSkyLighting {
    public static class PlateLightStudyBuild {
        #region public interface
        public static void BuildWeb() {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var view = new GameObject("PlateLightStudy").AddComponent<PlateLightStudy>();
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;

            var serialized = new SerializedObject(view);
            var textures = serialized.FindProperty("atlases");
            textures.arraySize = AtlasNames.Length;
            for (var i = 0; i < AtlasNames.Length; i++) {
                var path = $"{AssetRoot}/Atlases/{AtlasNames[i]}.jpg";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException($"Missing atlas at {path}");
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
                textures.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            serialized.ApplyModifiedProperties();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save {ScenePath}");

            var splash = PlayerSettings.SplashScreen.show;
            var compression = PlayerSettings.WebGL.compressionFormat;
            var fallback = PlayerSettings.WebGL.decompressionFallback;
            var template = PlayerSettings.WebGL.template;
            var stripping = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL);
            try {
                PlayerSettings.SplashScreen.show = false;
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                PlayerSettings.WebGL.decompressionFallback = true;
                PlayerSettings.WebGL.template = "PROJECT:PlateLightStudy";
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
                var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "docs/plate-light-study",
                    BuildTarget.WebGL, BuildOptions.None);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"Plate Web build: {report.summary.result}");
            } finally {
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, stripping);
                PlayerSettings.WebGL.template = template;
                PlayerSettings.WebGL.decompressionFallback = fallback;
                PlayerSettings.WebGL.compressionFormat = compression;
                PlayerSettings.SplashScreen.show = splash;
            }
        }
        #endregion

        #region constants
        const string AssetRoot = "Assets/PlateLightStudy";
        const string ScenePath = AssetRoot + "/PlateLightStudy.unity";
        static readonly string[] AtlasNames = { "current", "half", "quarter" };
        #endregion
    }
}
