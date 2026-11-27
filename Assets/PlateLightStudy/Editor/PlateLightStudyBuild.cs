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
            CreateScene();

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

        #region scene generation
        [MenuItem("Local Sky Lighting/Build Struck Plate Scene")]
        public static void CreateScene() {
            var data = JsonUtility.FromJson<Geometry>(AssetDatabase.LoadAssetAtPath<TextAsset>(Realtime + "/PlateGeometry.json").text);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Realtime + "/Plate.asset");
            if (mesh == null) {
                mesh = new Mesh { name = "Struck plate" };
                AssetDatabase.CreateAsset(mesh, Realtime + "/Plate.asset");
            }
            mesh.Clear();
            mesh.vertices = data.vertices;
            mesh.normals = data.normals;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(data.bowl, 0);
            mesh.SetTriangles(data.rim, 1);

            var texturePath = Realtime + "/Strikes.exr";
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            var platform = importer.GetPlatformTextureSettings("WebGL");
            if (importer.sRGBTexture || importer.npotScale != TextureImporterNPOTScale.None ||
                importer.wrapMode != TextureWrapMode.Clamp || importer.textureCompression != TextureImporterCompression.Uncompressed ||
                !platform.overridden || platform.format != TextureImporterFormat.RGBAHalf) {
                importer.sRGBTexture = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                platform.overridden = true;
                platform.format = TextureImporterFormat.RGBAHalf;
                importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
            }
            var surface = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Realtime + "/PlateSurface.shader");
            var bowl = GetMaterial("Amber bowl", shader, surface, false);
            var rim = GetMaterial("Amber rim", shader, surface, true);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var view = new GameObject("PlateLightStudy").AddComponent<PlateLightStudy>();
            var filter = view.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var plate = view.gameObject.AddComponent<MeshRenderer>();
            plate.sharedMaterials = new[] { bowl, rim };

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -7);
            camera.orthographic = true;
            camera.orthographicSize = 1.85f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var serialized = new SerializedObject(view);
            serialized.FindProperty("plate").objectReferenceValue = plate;
            serialized.FindProperty("plateCamera").objectReferenceValue = camera;
            var lights = serialized.FindProperty("lights");
            lights.arraySize = data.lights.Length;
            for (var i = 0; i < data.lights.Length; i++) lights.GetArrayElementAtIndex(i).boxedValue = data.lights[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save {ScenePath}");
        }

        static Material GetMaterial(string name, Shader shader, Texture2D surface, bool isRim) {
            var path = Realtime + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(shader);
                material.SetFloat("_Rim", isRim ? 1 : 0);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_Surface", surface);
            EditorUtility.SetDirty(material);
            return material;
        }
        #endregion

        #region constants
        const string AssetRoot = "Assets/PlateLightStudy";
        const string ScenePath = AssetRoot + "/PlateLightStudy.unity";
        const string Realtime = AssetRoot + "/Realtime";

        [Serializable]
        sealed class Geometry {
            public Vector3[] vertices, normals;
            public int[] bowl, rim;
            public PlateLightStudy.AreaLight[] lights;
        }
        #endregion
    }
}
