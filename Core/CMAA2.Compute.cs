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
            BufferHandle workingShapeCandidates,
            AtomicTextureHandle workingDeferredBlendItemListHeads,
            BufferHandle workingControlBuffer
        )
        {
            var kernelId = _edgesColor2x2CS;
            var sampleName = nameof(EdgesColor2x2CS);

            cmd.BeginSample(sampleName);

            Set(cmd, kernelId, "g_inoutColorReadonly", inColorTexture);
            Set(cmd, kernelId, "g_workingEdges", workingEdges);

            Set(cmd, kernelId, "g_workingShapeCandidates", workingShapeCandidates);
            Set(cmd, kernelId, "g_workingDeferredBlendItemListHeads", workingDeferredBlendItemListHeads.Handle);
            Set(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);

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
            BufferHandle workingDeferredBlendLocationList,
            int workingDeferredBlendLocationListSize,
            BufferHandle workingShapeCandidates,
            int workingShapeCandidatesSize,
            BufferHandle workingExecuteIndirectBuffer
        )
        {
            int kernelId = _computeDispatchArgsCS;
            var sampleName = nameof(ComputeDispatchArgsCS);

            cmd.BeginSample(sampleName);

            Set(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);

            // TODO: Remove passing unnecessary vectors!
            Set(cmd, "g_workingDeferredBlendLocationList_Dim", new Vector4(workingDeferredBlendLocationListSize, 0));
            Set(cmd, kernelId, "g_workingDeferredBlendLocationList", workingDeferredBlendLocationList);

            // TODO: Remove passing unnecessary vectors!
            Set(cmd, "g_workingShapeCandidates_Dim", new Vector4(workingShapeCandidatesSize, 0));
            Set(cmd, kernelId, "g_workingShapeCandidates", workingExecuteIndirectBuffer);

            // Out
            Set(cmd, kernelId, "g_workingExecuteIndirectBuffer", workingExecuteIndirectBuffer);

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
            BufferHandle workingShapeCandidates,
            BufferHandle workingDeferredBlendLocationList
        )
        {
            int kernelId = _processCandidatesCS;
            var sampleName = nameof(ProcessCandidatesCS);

            cmd.BeginSample(sampleName);

            Set(cmd, kernelId, "g_inoutColorReadonly", inColor);
            Set(cmd, kernelId, "g_workingEdges", workingEdges);

            Set(cmd, kernelId, "g_workingDeferredBlendItemListHeads", workingDeferredBlendItemListHeads.Handle);
            // NOTE: Size only needed on platforms that don't support texture's atomics operations.
            Set(cmd, "g_workingDeferredBlendItemListHeads_Size", workingDeferredBlendItemListHeads.Size);

            Set(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);
            Set(cmd, kernelId, "g_workingDeferredBlendItemList", workingDeferredBlendItemList);
            Set(cmd, kernelId, "g_workingDeferredBlendLocationList", workingDeferredBlendLocationList);
            Set(cmd, kernelId, "g_workingShapeCandidates", workingShapeCandidates);

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
            BufferHandle workingDeferredBlendLocationList
        )
        {
            var kernelId = _deferredColorApply2x2CS;
            var sampleName = nameof(DeferredColorApply2x2CS);

            cmd.BeginSample(sampleName);

            Set(cmd, kernelId, "g_inoutColorWriteonly", outColor);
            Set(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);
            Set(cmd, kernelId, "g_workingDeferredBlendItemList", workingDeferredBlendItemList);
            Set(cmd, kernelId, "g_workingDeferredBlendItemListHeads", workingDeferredBlendItemListHeads);
            Set(cmd, kernelId, "g_workingDeferredBlendLocationList", workingDeferredBlendLocationList);
            cmd.DispatchCompute(_compute, kernelId, workingExecuteIndirectBuffer, 0);

            cmd.EndSample(sampleName);
        }

        private void Set(IComputeCommandBuffer cmd, string name, Vector4 vector)
        {
            cmd.SetComputeVectorParam(_compute, name, vector);
        }

        private void Set(IComputeCommandBuffer cmd, int kernelId, string name, TextureHandle textureHandle)
        {
            cmd.SetComputeTextureParam(_compute, kernelId, name, textureHandle);
        }

        private void Set(IComputeCommandBuffer cmd, int kernelId, string name, BufferHandle bufferHandle)
        {
            cmd.SetComputeBufferParam(_compute, kernelId, name, bufferHandle);
        }

        private void Set(IComputeCommandBuffer cmd, int kernelId, string name, AtomicTextureHandle bufferHandle)
        {
            cmd.SetComputeBufferParam(_compute, kernelId, name, bufferHandle.Handle);
            // TODO: Fix string allocation!
            cmd.SetComputeVectorParam(_compute, name + "_Size", new Vector4(bufferHandle.Width, bufferHandle.Height));
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
