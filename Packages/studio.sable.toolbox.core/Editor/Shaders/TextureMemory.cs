using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Sable.Toolbox.Shaders
{
    /// <summary>
    /// GPU memory a texture occupies once loaded: the sum of its mip levels in its imported format
    /// (what VRChat's texture memory stat counts), not its download size.
    /// </summary>
    public static class TextureMemory
    {
        public static long Bytes(Texture t)
        {
            if (t == null) return 0;
            GraphicsFormat format = t.graphicsFormat;
            if (format == GraphicsFormat.None) return 0;
            int depth = t is Texture3D t3 ? t3.depth : t is Texture2DArray ta ? ta.depth : t is Cubemap ? 6 : 1;
            long total = 0;
            int w = t.width, h = t.height;
            for (int mip = 0; mip < Mathf.Max(1, t.mipmapCount); mip++)
            {
                total += GraphicsFormatUtility.ComputeMipmapSize(Mathf.Max(1, w >> mip), Mathf.Max(1, h >> mip), format);
            }
            return total * depth;
        }

        public static double Megabytes(Texture t) => Bytes(t) / (1024.0 * 1024.0);
    }
}
