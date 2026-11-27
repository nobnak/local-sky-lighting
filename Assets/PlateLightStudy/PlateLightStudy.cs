using UnityEngine;
using UnityEngine.Scripting;

namespace LocalSkyLighting {
    public sealed class PlateLightStudy : MonoBehaviour {
        [SerializeField] Texture2D[] atlases;

        int lighting;
        int frame = MiddleFrame;

        #region Unity lifecycle
        void OnGUI() {
            if (Event.current.type != EventType.Repaint || atlases == null || lighting >= atlases.Length) return;
            var atlas = atlases[lighting];
            if (atlas == null) return;

            var aspect = (float)atlas.width * Rows / (atlas.height * Columns);
            var width = Mathf.Min(Screen.width, Screen.height * aspect);
            var height = width / aspect;
            var viewport = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
            var insetX = .5f / atlas.width;
            var insetY = .5f / atlas.height;
            var uv = new Rect((float)(frame % Columns) / Columns + insetX,
                1f - (float)(frame / Columns + 1) / Rows + insetY,
                1f / Columns - 2f * insetX, 1f / Rows - 2f * insetY);
            GUI.DrawTextureWithTexCoords(viewport, atlas, uv, false);
        }
        #endregion

        #region public interface
        [Preserve]
        public void SetLighting(int value) => lighting = Mathf.Clamp(value, 0, atlases.Length - 1);

        [Preserve]
        public void SetFrame(int value) => frame = Mathf.Clamp(value, 0, FrameCount - 1);
        #endregion

        #region constants
        const int Rows = 3;
        const int Columns = 4;
        const int FrameCount = 11;
        const int MiddleFrame = FrameCount / 2;
        #endregion
    }
}
