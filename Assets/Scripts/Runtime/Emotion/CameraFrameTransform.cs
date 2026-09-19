using UnityEngine;

namespace SrtpAvatar
{
    public static class CameraFrameTransform
    {
        public static Color32[] OrientAndScale(Color32[] source, int sourceWidth, int sourceHeight, int rotationDegrees, bool verticallyMirrored, int maximumDimension, out int targetWidth, out int targetHeight)
        {
            Color32[] result = null;
            OrientAndScale(source, sourceWidth, sourceHeight, rotationDegrees, verticallyMirrored, maximumDimension,
                ref result, out targetWidth, out targetHeight);
            return result;
        }

        public static void OrientAndScale(Color32[] source, int sourceWidth, int sourceHeight, int rotationDegrees,
            bool verticallyMirrored, int maximumDimension, ref Color32[] result, out int targetWidth, out int targetHeight)
        {
            targetWidth = 0;
            targetHeight = 0;
            if (source == null || sourceWidth <= 0 || sourceHeight <= 0 || source.Length < sourceWidth * sourceHeight || maximumDimension <= 0)
            {
                result = System.Array.Empty<Color32>();
                return;
            }

            var rotation = NormalizeRightAngle(rotationDegrees);
            var rotatedWidth = rotation == 90 || rotation == 270 ? sourceHeight : sourceWidth;
            var rotatedHeight = rotation == 90 || rotation == 270 ? sourceWidth : sourceHeight;
            var scale = Mathf.Min(1f, maximumDimension / (float)Mathf.Max(rotatedWidth, rotatedHeight));
            targetWidth = Mathf.Max(1, Mathf.RoundToInt(rotatedWidth * scale));
            targetHeight = Mathf.Max(1, Mathf.RoundToInt(rotatedHeight * scale));

            if (result == null || result.Length != targetWidth * targetHeight)
                result = new Color32[targetWidth * targetHeight];
            for (var y = 0; y < targetHeight; y++)
            {
                var rotatedY = Mathf.Min(rotatedHeight - 1, Mathf.FloorToInt((y + 0.5f) * rotatedHeight / targetHeight));
                for (var x = 0; x < targetWidth; x++)
                {
                    var rotatedX = Mathf.Min(rotatedWidth - 1, Mathf.FloorToInt((x + 0.5f) * rotatedWidth / targetWidth));
                    MapToSource(rotatedX, rotatedY, sourceWidth, sourceHeight, rotation, out var sourceX, out var sourceY);
                    if (verticallyMirrored)
                    {
                        sourceY = sourceHeight - 1 - sourceY;
                    }

                    result[y * targetWidth + x] = source[sourceY * sourceWidth + sourceX];
                }
            }

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

        private static void MapToSource(int x, int y, int sourceWidth, int sourceHeight, int rotation, out int sourceX, out int sourceY)
        {
            switch (rotation)
            {
                case 90:
                    sourceX = y;
                    sourceY = sourceHeight - 1 - x;
                    break;
                case 180:
                    sourceX = sourceWidth - 1 - x;
                    sourceY = sourceHeight - 1 - y;
                    break;
                case 270:
                    sourceX = sourceWidth - 1 - y;
                    sourceY = x;
                    break;
                default:
                    sourceX = x;
                    sourceY = y;
                    break;
            }
        }
    }
}
