using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CMAA2.Core
{
    public class CMAA2RenderPass : ScriptableRenderPass
    {
        private const int m_TextureSampleCount = 1;

        private readonly CMAA2Compute _compute;

        public CMAA2RenderPass(ComputeShader cmaa2Compute)
        {
            _compute = new CMAA2Compute(cmaa2Compute);
        }

        private class PassData
        {
            public CMAA2Compute Compute;
            public TextureHandle FrameColor;

            public TextureHandle WorkingEdges; // RWTexture2D<uint> : u1
            public BufferHandle WorkingShapeCandidates; // RWStructuredBuffer<uint> : u2
            public TextureHandle WorkingDeferredBlendItemListHeads; // RWTexture2D<uint> : u5
            public BufferHandle WorkingControlBuffer; // RWByteAddressBuffer : u6
            
            // Kernel 2
            public BufferHandle WorkingDeferredBlendLocationList;
            public BufferHandle WorkingExecuteIndirectBuffer;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var targetDesc = cameraData.cameraTargetDescriptor;

            var resX = targetDesc.width;
            var resY = targetDesc.height;

            using var builder = renderGraph.AddComputePass<PassData>("CMAA2", out var passData);
            passData.Compute = _compute;

            builder.UseTexture(resourceData.cameraColor);
            passData.FrameColor = resourceData.cameraColor;

            var uintUAVTextureDesc = new TextureDesc(resX, resY)
            {
                format = GraphicsFormat.R8_UInt,
                enableRandomWrite = true,
            };
            passData.WorkingEdges = builder.CreateTransientTexture(in uintUAVTextureDesc);
            passData.WorkingDeferredBlendItemListHeads = builder.CreateTransientTexture(in uintUAVTextureDesc);

            // Bufers
            int requiredCandidatePixels = resX * resY / 4 * m_TextureSampleCount;
            int requiredDeferredColorApplyBuffer = resX * resY / 2 * m_TextureSampleCount;
            int requiredListHeadsPixels = ( resX * resY + 3 ) / 6;
            
            // Create buffer for storing a list of all pixel candidates to process (potential AA shapes, both simple and complex)
            {
                var desc = new BufferDesc(requiredCandidatePixels, sizeof(uint), GraphicsBuffer.Target.Structured);
                passData.WorkingShapeCandidates = builder.CreateTransientBuffer(in desc);
            }

            // Create buffer for storing a list of coordinates of linked list heads quads, to allow for combined processing in the last step
            {
                var desc = new BufferDesc(requiredListHeadsPixels, sizeof(uint), GraphicsBuffer.Target.Structured);
                passData.WorkingDeferredBlendLocationList = builder.CreateTransientBuffer(desc);
            }
            
            // Control buffer (always the same size, doesn't need re-creating but oh well)
            {
                var desc = new BufferDesc(16, sizeof(uint), GraphicsBuffer.Target.Raw);
                passData.WorkingControlBuffer = builder.CreateTransientBuffer(in desc);
            }

            {
                var desc = new BufferDesc(4, sizeof(uint), GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments);
                passData.WorkingExecuteIndirectBuffer = builder.CreateTransientBuffer(in desc);
            }

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<PassData>(static (data, context) =>
            {
                data.Compute.EdgesColor2x2CS(
                    cmd: context.cmd,
                    inColor: data.FrameColor,
                    workingEdges: data.WorkingEdges,
                    workingShapeCandidates: data.WorkingShapeCandidates,
                    workingDeferredBlendItemListHeads: data.WorkingDeferredBlendItemListHeads,
                    workingControlBuffer: data.WorkingControlBuffer
                );

                data.Compute.ComputeDispatchArgsCS(
                    cmd: context.cmd,
                    workingDeferredBlendLocationList: data.WorkingDeferredBlendLocationList,
                    data.WorkingControlBuffer,
                    data.WorkingExecuteIndirectBuffer
                );
            });
        }
    }
}
