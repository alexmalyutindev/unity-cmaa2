//#if PLATFORM_STANDALONE_OSX
#define TEXTURE_ATOMIC_NOT_SUPPORTED
//#endif

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace CMAA2.Core
{
    public struct AtomicTextureHandle
    {
        public Vector4 Size => new Vector4(Width, Height);

        public int Width;
        public int Height;
#if TEXTURE_ATOMIC_NOT_SUPPORTED
        public BufferHandle Handle;
#else
        public TextureHandle Handle;
#endif

        public static AtomicTextureHandle CreateTransientUint(IBaseRenderGraphBuilder builder, int width, int height)
        {
            var handle = new AtomicTextureHandle()
            {
                Width = width,
                Height = height,
            };

#if TEXTURE_ATOMIC_NOT_SUPPORTED
            var desc = new BufferDesc(width * height, sizeof(uint), GraphicsBuffer.Target.Structured);
            handle.Handle = builder.CreateTransientBuffer(desc);
            builder.UseBuffer(handle.Handle, AccessFlags.ReadWrite);
#else
            var desc = new TextureDesc(width, height)
            {
                format = GraphicsFormat.R8_UInt,
                enableRandomWrite = true,
            };
            handle.Handle = builder.CreateTransientTexture(desc);
            builder.UseTexture(handle.Handle, AccessFlags.ReadWrite);
#endif
            return handle;
        }

        public void Bind(IComputeCommandBuffer cmd, ComputeShader compute, int kernelIndex, string name)
        {
#if TEXTURE_ATOMIC_NOT_SUPPORTED
            cmd.SetComputeBufferParam(compute, kernelIndex, name, Handle);
#else
            cmd.SetComputeTextureParam(compute, kernelIndex, name, Handle);
#endif
        }
    }
}
