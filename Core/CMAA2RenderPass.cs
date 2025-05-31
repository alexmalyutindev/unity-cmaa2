using System;
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
            public TextureHandle ActualFrameColor;

            public Vector2Int FrameBufferSize;
            public TextureHandle ColorBackBuffer; // RWTexture2D<float4> : u0
            public TextureHandle WorkingEdges; // RWTexture2D<uint> : u1
            public SizedBufferHandle WorkingShapeCandidates; // RWStructuredBuffer<uint> : u2
            public SizedBufferHandle WorkingDeferredBlendLocationList; // RWStructuredBuffer<uint> : u3
            public BufferHandle WorkingDeferredBlendItemList; // RWStructuredBuffer<uint2> : u4
            public AtomicTextureHandle WorkingDeferredBlendItemListHeads; // [RWTexture2D|RWStructuredBuffer]<uint> : u5
            public BufferHandle WorkingControlBuffer; // RWByteAddressBuffer : u6
            public BufferHandle WorkingExecuteIndirectBuffer; // RWByteAddressBuffer : u7
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var targetDesc = cameraData.cameraTargetDescriptor;

            var resX = targetDesc.width;
            var resY = targetDesc.height;

            using var builder = renderGraph.AddUnsafePass<PassData>(passName: "CMAA2", passData: out var passData);
            passData.Compute = _compute;

            passData.FrameBufferSize = new Vector2Int(resX, resY);
            passData.ActualFrameColor = resourceData.activeColorTexture;
            builder.UseTexture(input: resourceData.activeColorTexture);

            var colorBackBufferDesc = new TextureDesc(resX, resY)
            {
                name = "_ColorBackBufferRW",
                format = GraphicsFormatUtility.GetGraphicsFormat(
                    RenderTextureFormat.ARGBFloat,
                    RenderTextureReadWrite.Linear
                ),
                enableRandomWrite = true,
            };
            passData.ColorBackBuffer = builder.CreateTransientTexture(colorBackBufferDesc);

            // create all temporary storage buffers
            {
                int edgesResX = resX;
                if (m_TextureSampleCount == 1) edgesResX = (resX + 1) / 2;
                var graphicsFormat = m_TextureSampleCount switch
                {
                    1 or 2 => GraphicsFormat.R8_UInt,
                    4 => GraphicsFormat.R16_UInt,
                    8 => GraphicsFormat.R32_UInt,
                    _ => GraphicsFormat.R8_UInt,
                };
                var uintUAVTextureDesc = new TextureDesc(width: edgesResX, height: resY)
                {
                    format = graphicsFormat,
                    enableRandomWrite = true,
                };
                passData.WorkingEdges = builder.CreateTransientTexture(desc: in uintUAVTextureDesc);


                passData.WorkingDeferredBlendItemListHeads = AtomicTextureHandle.CreateTransientUint(
                    builder,
                    (resX + 1) / 2,
                    (resY + 1) / 2
                );
            }

            // Bufers
            int requiredCandidatePixels = resX * resY / 4 * m_TextureSampleCount;
            int requiredDeferredColorApplyBuffer = resX * resY / 2 * m_TextureSampleCount;
            int requiredListHeadsPixels = (resX * resY + 3) / 6;

            // Create buffer for storing a list of all pixel candidates to process (potential AA shapes, both simple and complex)
            {
                var desc = new BufferDesc(
                    count: requiredCandidatePixels,
                    stride: sizeof(uint),
                    target: GraphicsBuffer.Target.Structured
                );
                passData.WorkingShapeCandidates = new SizedBufferHandle(
                    builder.CreateTransientBuffer(desc: in desc),
                    desc.count
                );
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

            // Create buffer for storing a list of coordinates of linked list heads quads, to allow for combined processing in the last step
            {
                var desc = new BufferDesc(
                    count: requiredListHeadsPixels,
                    stride: sizeof(uint),
                    target: GraphicsBuffer.Target.Structured
                );
                passData.WorkingDeferredBlendLocationList = new SizedBufferHandle(
                    builder.CreateTransientBuffer(desc),
                    desc.count
                );
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

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<PassData>(Render);
        }

        private static void Render(PassData data, UnsafeGraphContext context)
        {
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            nativeCmd.Blit(data.ActualFrameColor, data.ColorBackBuffer);

            // first pass edge detect
            data.Compute.EdgesColor2x2CS(
                cmd: context.cmd,
                inColorTexture: data.ColorBackBuffer,
                textureResolution: data.FrameBufferSize,
                workingEdges: data.WorkingEdges,
                workingShapeCandidates: data.WorkingShapeCandidates,
                workingDeferredBlendItemListHeads: data.WorkingDeferredBlendItemListHeads,
                workingControlBuffer: data.WorkingControlBuffer);

            // Set up for the first DispatchIndirect
            data.Compute.ComputeDispatchArgsCS(
                cmd: context.cmd,
                threadGroupsX: 2,
                threadGroupsY: 1,
                workingShapeCandidates: data.WorkingShapeCandidates,
                workingDeferredBlendLocationList: data.WorkingDeferredBlendLocationList,
                workingControlBuffer: data.WorkingControlBuffer,
                workingExecuteIndirectBuffer: data.WorkingExecuteIndirectBuffer
            );

            // Process shape candidates DispatchIndirect
            data.Compute.ProcessCandidatesCS(
                cmd: context.cmd,
                workingExecuteDirectBuffer: data.WorkingExecuteIndirectBuffer,
                inColor: data.ColorBackBuffer,
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
                threadGroupsX: 1,
                threadGroupsY: 2,
                workingShapeCandidates: data.WorkingShapeCandidates,
                workingDeferredBlendLocationList: data.WorkingDeferredBlendLocationList,
                workingControlBuffer: data.WorkingControlBuffer,
                workingExecuteIndirectBuffer: data.WorkingExecuteIndirectBuffer
            );

            // Resolve & apply blended colors
            data.Compute.DeferredColorApply2x2CS(
                context.cmd,
                workingExecuteIndirectBuffer: data.WorkingExecuteIndirectBuffer,
                outColor: data.ColorBackBuffer,
                workingControlBuffer: data.WorkingControlBuffer,
                workingDeferredBlendItemList: data.WorkingDeferredBlendItemList,
                workingDeferredBlendItemListHeads: data.WorkingDeferredBlendItemListHeads,
                workingDeferredBlendLocationList: data.WorkingDeferredBlendLocationList
            );

            nativeCmd.Blit(data.ColorBackBuffer, data.ActualFrameColor);
        }
    }

    public struct SizedBufferHandle
    {
        public Vector4 Dimensions => new Vector4(Size, 0);

        public readonly int Size;
        public readonly BufferHandle Buffer;

        public SizedBufferHandle(BufferHandle bufferHandle, int size)
        {
            Buffer = bufferHandle;
            Size = size;
        }
    }
}
