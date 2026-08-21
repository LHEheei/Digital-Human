using UnityEngine;

namespace SrtpAvatar
{
    /// <summary>
    /// Converts the orientation metadata reported by WebCamTexture into the
    /// transform values used by the diagnostic preview. The media uploaded to
    /// the emotion service is oriented separately by CameraFrameTransform.
    /// </summary>
    public static class CameraPreviewTransform
    {
        public static float GetZRotation(int videoRotationAngle)
        {
            return -NormalizeRightAngle(videoRotationAngle);
        }

        public static Rect GetUvRect(bool verticallyMirrored)
        {
            return verticallyMirrored
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
        }

        public static Vector2 GetDisplayBounds(Vector2 textureRectSize, int videoRotationAngle)
        {
            var rotation = NormalizeRightAngle(videoRotationAngle);
            return rotation == 90 || rotation == 270
                ? new Vector2(textureRectSize.y, textureRectSize.x)
                : textureRectSize;
        }

        private static int NormalizeRightAngle(int rotationDegrees)
        {
            var normalized = ((rotationDegrees % 360) + 360) % 360;
            return normalized switch
            {
                90 => 90,
                180 => 180,
                270 => 270,
                _ => 0
            };
        }
    }
}
