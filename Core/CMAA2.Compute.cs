using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace CMAA2.Core
{
    public class CMAA2Compute
    {
        private ComputeShader _compute;
        private readonly int _edgesColor2x2CS;
        private readonly int _computeDispatchArgsCS;
        private readonly int _processCandidatesCS;
        private readonly int _deferredColorApply2x2CS;

        private ThreadGroupSizes _edgesColor2x2TreadGroupSize;

        public CMAA2Compute(ComputeShader compute)
        {
            _compute = compute;
            _edgesColor2x2CS = _compute.FindKernel("EdgesColor2x2CS");
            _compute.GetKernelThreadGroupSizes(_edgesColor2x2CS, out var x, out var y, out var z);
            _edgesColor2x2TreadGroupSize = new ThreadGroupSizes(x, y, z);

            _computeDispatchArgsCS = _compute.FindKernel("ComputeDispatchArgsCS");
            _processCandidatesCS = _compute.FindKernel("ProcessCandidatesCS");
            _deferredColorApply2x2CS = _compute.FindKernel("DeferredColorApply2x2CS");
        }

        public void EdgesColor2x2CS(
            IComputeCommandBuffer cmd,
            TextureHandle inColorTexture,
            Vector2Int textureResolution,
            TextureHandle workingEdges,
            SizedBufferHandle workingShapeCandidates,
            AtomicTextureHandle workingDeferredBlendItemListHeads,
            BufferHandle workingControlBuffer
        )
        {
            var kernelId = _edgesColor2x2CS;
            var sampleName = nameof(EdgesColor2x2CS);

            cmd.BeginSample(sampleName);

            Bind(cmd, kernelId, "g_inoutColorReadonly", inColorTexture);
            Bind(cmd, kernelId, "g_workingEdges", workingEdges);

            Bind(cmd, "g_workingShapeCandidates_Dim", workingShapeCandidates.Dimensions);
            Bind(cmd, kernelId, "g_workingShapeCandidates", workingShapeCandidates.Buffer);

            Bind(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);

            Bind(cmd, "g_workingDeferredBlendItemListHeads_Width", workingDeferredBlendItemListHeads.Width);
            workingDeferredBlendItemListHeads.Bind(cmd, _compute, kernelId, "g_workingDeferredBlendItemListHeads");

            // TODO: ThreadGroups count!
            int csOutputKernelSizeX = (int)(_edgesColor2x2TreadGroupSize.X - 2); // m_csInputKernelSizeX - 2;
            int csOutputKernelSizeY = (int)(_edgesColor2x2TreadGroupSize.Y - 2); // m_csInputKernelSizeY - 2;
            int threadGroupCountX = (textureResolution.x + csOutputKernelSizeX * 2 - 1) / (csOutputKernelSizeX * 2);
            int threadGroupCountY = (textureResolution.y + csOutputKernelSizeY * 2 - 1) / (csOutputKernelSizeY * 2);
            cmd.DispatchCompute(_compute, kernelId, threadGroupCountX, threadGroupCountY, 1);

            cmd.EndSample(sampleName);
        }

        public void ComputeDispatchArgsCS(
            IComputeCommandBuffer cmd,
            int threadGroupsX,
            int threadGroupsY,
            BufferHandle workingControlBuffer,
            SizedBufferHandle workingDeferredBlendLocationList,
            SizedBufferHandle workingShapeCandidates,
            BufferHandle workingExecuteIndirectBuffer
        )
        {
            int kernelId = _computeDispatchArgsCS;
            var sampleName = nameof(ComputeDispatchArgsCS);

            cmd.BeginSample(sampleName);

            Bind(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);

            // TODO: Remove passing unnecessary vectors!
            Bind(cmd, "g_workingDeferredBlendLocationList_Dim", workingDeferredBlendLocationList.Dimensions);
            Bind(cmd, kernelId, "g_workingDeferredBlendLocationList", workingDeferredBlendLocationList.Buffer);

            // TODO: Remove passing unnecessary vectors!
            Bind(cmd, "g_workingShapeCandidates_Dim", workingShapeCandidates.Dimensions);
            Bind(cmd, kernelId, "g_workingShapeCandidates", workingShapeCandidates.Buffer);

            // Out
            Bind(cmd, kernelId, "g_workingExecuteIndirectBuffer", workingExecuteIndirectBuffer);

            // TODO: ThreadGroups count!
            cmd.DispatchCompute(_compute, kernelId, threadGroupsX, threadGroupsY, 1);

            cmd.EndSample(sampleName);
        }

        // inColor : Texture2D<float4>
        // workingEdges : RWTexture2D<uint>
        // workingDeferredBlendItemListHeads
        // - MacOS|IOS : RWStructuredBuffer<uint>
        // - Windows   : RWTexture2D<uint>
        // workingShapeCandidates : RWStructuredBuffer<uint>
        // workingDeferredBlendLocationList : RWStructuredBuffer<uint>
        public void ProcessCandidatesCS(
            IComputeCommandBuffer cmd,
            BufferHandle workingExecuteDirectBuffer,
            TextureHandle inColor,
            TextureHandle workingEdges,
            AtomicTextureHandle workingDeferredBlendItemListHeads,
            BufferHandle workingControlBuffer,
            BufferHandle workingDeferredBlendItemList,
            SizedBufferHandle workingShapeCandidates,
            SizedBufferHandle workingDeferredBlendLocationList
        )
        {
            int kernelId = _processCandidatesCS;
            var sampleName = nameof(ProcessCandidatesCS);

            cmd.BeginSample(sampleName);

            Bind(cmd, kernelId, "g_inoutColorReadonly", inColor);
            Bind(cmd, kernelId, "g_workingEdges", workingEdges);

            // NOTE: Size only needed on platforms that don't support texture's atomics operations.
            Bind(cmd, "g_workingDeferredBlendItemListHeads_Width", workingDeferredBlendItemListHeads.Width);
            workingDeferredBlendItemListHeads.Bind(cmd, _compute, kernelId, "g_workingDeferredBlendItemListHeads");

            Bind(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);
            Bind(cmd, kernelId, "g_workingDeferredBlendItemList", workingDeferredBlendItemList);

            Bind(cmd, "g_workingDeferredBlendLocationList_Dim", workingDeferredBlendLocationList.Dimensions);
            Bind(cmd, kernelId, "g_workingDeferredBlendLocationList", workingDeferredBlendLocationList.Buffer);

            Bind(cmd, "g_workingShapeCandidates_Dim", workingShapeCandidates.Dimensions);
            Bind(cmd, kernelId, "g_workingShapeCandidates", workingShapeCandidates.Buffer);

            // TODO: ThreadGroups count!
            // cmd.DispatchCompute(_compute, kernelId, 1, 1, 1);
            cmd.DispatchCompute(_compute, kernelId, workingExecuteDirectBuffer, 0);
            cmd.EndSample(sampleName);
        }

        public void DeferredColorApply2x2CS(
            IComputeCommandBuffer cmd,
            BufferHandle workingExecuteIndirectBuffer,
            TextureHandle outColor,
            BufferHandle workingControlBuffer,
            BufferHandle workingDeferredBlendItemList,
            AtomicTextureHandle workingDeferredBlendItemListHeads,
            SizedBufferHandle workingDeferredBlendLocationList
        )
        {
            var kernelId = _deferredColorApply2x2CS;
            var sampleName = nameof(DeferredColorApply2x2CS);

            cmd.BeginSample(sampleName);

            Bind(cmd, kernelId, "g_inoutColorWriteonly", outColor);
            Bind(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);
            Bind(cmd, kernelId, "g_workingDeferredBlendItemList", workingDeferredBlendItemList);

            // NOTE: Size only needed on platforms that don't support texture's atomics operations.
            Bind(cmd, "g_workingDeferredBlendItemListHeads_Width", workingDeferredBlendItemListHeads.Width);
            workingDeferredBlendItemListHeads.Bind(cmd, _compute, kernelId, "g_workingDeferredBlendItemListHeads");

            Bind(cmd, "g_workingDeferredBlendLocationList_Dim", workingDeferredBlendLocationList.Dimensions);
            Bind(cmd, kernelId, "g_workingDeferredBlendLocationList", workingDeferredBlendLocationList.Buffer);

            cmd.DispatchCompute(_compute, kernelId, workingExecuteIndirectBuffer, 0);

            cmd.EndSample(sampleName);
        }

        private void Bind(IComputeCommandBuffer cmd, string name, int value)
        {
            cmd.SetComputeIntParam(_compute, name, value);
        }

        private void Bind(IComputeCommandBuffer cmd, string name, Vector4 vector)
        {
            cmd.SetComputeVectorParam(_compute, name, vector);
        }

        private void Bind(IComputeCommandBuffer cmd, int kernelId, string name, TextureHandle textureHandle)
        {
            cmd.SetComputeTextureParam(_compute, kernelId, name, textureHandle);
        }

        private void Bind(IComputeCommandBuffer cmd, int kernelId, string name, BufferHandle bufferHandle)
        {
            cmd.SetComputeBufferParam(_compute, kernelId, name, bufferHandle);
        }
    }

    struct ThreadGroupSizes
    {
        public uint X, Y, Z;

        public ThreadGroupSizes(uint x, uint y, uint z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
