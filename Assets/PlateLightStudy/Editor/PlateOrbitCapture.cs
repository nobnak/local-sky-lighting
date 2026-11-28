using System;
using System.IO;
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
            Directory.CreateDirectory(directory);

            EditorSceneManager.OpenScene("Assets/PlateLightStudy/PlateLightStudy.unity");
            var study = Object.FindFirstObjectByType<PlateLightStudy>();
            var camera = Object.FindFirstObjectByType<Camera>();
            study.ApplyFixedReferenceLighting(LightingPhase);
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
                Debug.Log($"Captured {FrameCount} frames with fixed lighting phase {LightingPhase}.");
            } finally {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(target);
            }
        }
        #endregion

        #region camera path
        static void SetCameraPose(Camera camera, float time) {
            var guide = Quaternion.Euler(Pitch.Evaluate(time), Yaw.Evaluate(time), Roll.Evaluate(time));
            var aim = new Vector3(AimX.Evaluate(time), AimY.Evaluate(time), 0);
            var position = aim + guide * new Vector3(0, 0, -Distance.Evaluate(time));
            camera.transform.SetPositionAndRotation(position, guide);
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
        static readonly AnimationCurve Pitch = Curve(-26, -28, -30, -26, -21, -20);
        static readonly AnimationCurve Yaw = Curve(-9, -5, 1, 5, 9, 10);
        static readonly AnimationCurve Roll = Curve(-7, -5, -3, 0, 3, 4);
        static readonly AnimationCurve AimX = Curve(.04f, .025f, 0, -.025f, -.045f, -.045f);
        static readonly AnimationCurve AimY = Curve(-.08f, -.065f, -.03f, 0, .035f, .04f);
        static readonly AnimationCurve Distance = Curve(4.33f, 4.24f, 4.17f, 4.17f, 4.1f, 4.1f);
        #endregion
    }
}
