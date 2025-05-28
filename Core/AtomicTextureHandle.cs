#if PLATFORM_STANDALONE_OSX
#define TEXTURE_ATOMIC_NOT_SUPPORTED
#endif

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace CMAA2.Core
{
    public struct AtomicTextureHandle
    {
        public int Width;
        public int Height;
#if TEXTURE_ATOMIC_NOT_SUPPORTED
        public BufferHandle Handle;
#else
        public TextureHandle Handle;
#endif

        public static AtomicTextureHandle CreateTransientUint(IComputeRenderGraphBuilder builder, int width, int height)
        {
            var handle = new AtomicTextureHandle()
            {
                Width = width,
                Height = height,
            };

#if TEXTURE_ATOMIC_NOT_SUPPORTED
            var desc = new BufferDesc(width * height, sizeof(uint), GraphicsBuffer.Target.Structured);
            handle.Handle = builder.CreateTransientBuffer(desc);
#else
            var desc = new TextureDesc(width, height)
            {
                format = GraphicsFormat.R8_UInt,
                enableRandomWrite = true,
            };
            handle.Handle = builder.CreateTransientTexture(desc);
#endif
            return handle;
        }
    }
}
