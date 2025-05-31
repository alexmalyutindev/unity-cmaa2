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

            public Vector2Int FrameBufferSize;
            public TextureHandle FrameColor;

            // EdgesColor2x2CS
            public TextureHandle WorkingEdges; // RWTexture2D<uint> : u1
            public BufferHandle WorkingShapeCandidates; // RWStructuredBuffer<uint> : u2
            public AtomicTextureHandle WorkingDeferredBlendItemListHeads; // [RWTexture2D|RWStructuredBuffer]<uint> : u5
            public BufferHandle WorkingControlBuffer; // RWByteAddressBuffer : u6

            // ComputeDispatchArgsCS
            public BufferHandle WorkingDeferredBlendLocationList; // RWStructuredBuffer<uint> : u3
            public BufferHandle WorkingExecuteIndirectBuffer; // RWByteAddressBuffer : u7

            // ProcessCandidatesCS
            public BufferHandle WorkingDeferredBlendItemList; // RWStructuredBuffer : u4

            // DeferredColorApply2x2CS
            public TextureHandle OutColor;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var targetDesc = cameraData.cameraTargetDescriptor;

            var resX = targetDesc.width;
            var resY = targetDesc.height;

            using var builder = renderGraph.AddComputePass<PassData>(passName: "CMAA2", passData: out var passData);
            passData.Compute = _compute;

            passData.FrameBufferSize = new Vector2Int(resX, resY);
            builder.UseTexture(input: resourceData.cameraColor);
            passData.FrameColor = resourceData.cameraColor;

            var tempTargetDesc = new TextureDesc(targetDesc.width, targetDesc.height)
            {
                enableRandomWrite = true,
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(
                    RenderTextureFormat.RGB565,
                    RenderTextureReadWrite.Linear)
            };
            passData.OutColor = builder.CreateTransientTexture(tempTargetDesc);

            var uintUAVTextureDesc = new TextureDesc(width: resX, height: resY)
            {
                format = GraphicsFormat.R8_UInt,
                enableRandomWrite = true,
            };
            passData.WorkingEdges = builder.CreateTransientTexture(desc: in uintUAVTextureDesc);
            passData.WorkingDeferredBlendItemListHeads = AtomicTextureHandle.CreateTransientUint(
                builder,
                uintUAVTextureDesc.width,
                uintUAVTextureDesc.height
            );

            // Bufers
            int requiredCandidatePixels = resX * resY / 4 * m_TextureSampleCount;
            int requiredDeferredColorApplyBuffer = resX * resY / 2 * m_TextureSampleCount;
            int requiredListHeadsPixels = (resX * resY + 3) / 6;

            // Create buffer for storing a list of all pixel candidates to process (potential AA shapes, both simple and complex)
            {
                var desc = new BufferDesc(
                    count: requiredCandidatePixels,
                    stride: sizeof(uint),
                    target: GraphicsBuffer.Target.Structured);
                passData.WorkingShapeCandidates = builder.CreateTransientBuffer(desc: in desc);
            }

            // Create buffer for storing a list of coordinates of linked list heads quads, to allow for combined processing in the last step
            {
                var desc = new BufferDesc(
                    count: requiredListHeadsPixels,
                    stride: sizeof(uint),
                    target: GraphicsBuffer.Target.Structured);
                passData.WorkingDeferredBlendLocationList = builder.CreateTransientBuffer(desc: desc);
            }

            // Control buffer (always the same size, doesn't need re-creating but oh well)
            {
                var desc = new BufferDesc(count: 16, stride: sizeof(uint), target: GraphicsBuffer.Target.Raw);
                passData.WorkingControlBuffer = builder.CreateTransientBuffer(desc: in desc);
            }

            // Control buffer (always the same size, doesn't need re-creating but oh well)
            {
                var desc = new BufferDesc(
                    count: 4,
                    stride: sizeof(uint),
                    target: GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments);
                passData.WorkingExecuteIndirectBuffer = builder.CreateTransientBuffer(desc: in desc);
            }

            // Create buffer for storing linked list of all output values to blend
            {
                var desc = new BufferDesc(
                    requiredDeferredColorApplyBuffer,
                    sizeof(uint) * 2,
                    GraphicsBuffer.Target.Structured
                );
                passData.WorkingDeferredBlendItemList = builder.CreateTransientBuffer(desc);
            }

            builder.AllowPassCulling(value: false);
            builder.SetRenderFunc<PassData>(
                renderFunc: static (data, context) =>
                {
                    // first pass edge detect
                    data.Compute.EdgesColor2x2CS(
                        cmd: context.cmd,
                        inColorTexture: data.FrameColor,
                        textureResolution: data.FrameBufferSize,
                        workingEdges: data.WorkingEdges,
                        workingShapeCandidates: data.WorkingShapeCandidates,
                        workingDeferredBlendItemListHeads: data.WorkingDeferredBlendItemListHeads,
                        workingControlBuffer: data.WorkingControlBuffer
                    );

                    // Set up for the first DispatchIndirect
                    data.Compute.ComputeDispatchArgsCS(
                        cmd: context.cmd,
                        threadGroupsX:2,
                        threadGroupsY:1,
                        workingDeferredBlendLocationList: data.WorkingDeferredBlendLocationList,
                        workingControlBuffer: data.WorkingControlBuffer,
                        workingExecuteIndirectBuffer: data.WorkingExecuteIndirectBuffer
                    );

                    // Process shape candidates DispatchIndirect
                    data.Compute.ProcessCandidatesCS(
                        cmd: context.cmd,
                        workingExecuteDirectBuffer: data.WorkingExecuteIndirectBuffer,
                        inColor: data.FrameColor,
                        workingEdges: data.WorkingEdges,
                        workingDeferredBlendItemListHeads: data.WorkingDeferredBlendItemListHeads,
                        workingControlBuffer: data.WorkingControlBuffer,
                        workingDeferredBlendItemList: data.WorkingDeferredBlendItemList,
                        workingShapeCandidates: data.WorkingShapeCandidates,
                        workingDeferredBlendLocationList: data.WorkingDeferredBlendLocationList
                    );
                    
                    // Set up for the second DispatchIndirect
                    data.Compute.ComputeDispatchArgsCS(
                        cmd: context.cmd,
                        threadGroupsX:1,
                        threadGroupsY:2,
                        workingDeferredBlendLocationList: data.WorkingDeferredBlendLocationList,
                        workingControlBuffer: data.WorkingControlBuffer,
                        workingExecuteIndirectBuffer: data.WorkingExecuteIndirectBuffer
                    );

                    // Resolve & apply blended colors
                    data.Compute.DeferredColorApply2x2CS(
                        context.cmd,
                        workingExecuteIndirectBuffer: data.WorkingExecuteIndirectBuffer,
                        outColor: data.OutColor,
                        workingControlBuffer: data.WorkingControlBuffer,
                        workingDeferredBlendItemList: data.WorkingDeferredBlendItemList,
                        workingDeferredBlendItemListHeads: data.WorkingDeferredBlendItemListHeads,
                        workingDeferredBlendLocationList: data.WorkingDeferredBlendLocationList
                    );
                });
        }
    }
}
