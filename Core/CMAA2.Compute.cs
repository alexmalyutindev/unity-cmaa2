using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace CMAA2.Core
{
    public class CMAA2Compute
    {
        private ComputeShader _compute;
        private readonly int _edgesColor2X2CS;
        private readonly int _computeDispatchArgsCS;
        private readonly int _processCandidatesCS;

        public CMAA2Compute(ComputeShader compute)
        {
            _compute = compute;
            _edgesColor2X2CS = _compute.FindKernel("EdgesColor2x2CS");
            _computeDispatchArgsCS = _compute.FindKernel("ComputeDispatchArgsCS");
            _processCandidatesCS = _compute.FindKernel("ProcessCandidatesCS");
        }

        public void EdgesColor2x2CS(
            IComputeCommandBuffer cmd,
            TextureHandle inColor,
            TextureHandle workingEdges,
            BufferHandle workingShapeCandidates,
            AtomicTextureHandle workingDeferredBlendItemListHeads,
            BufferHandle workingControlBuffer
        )
        {
            var kernelId = _edgesColor2X2CS;
            var sampleName = "EdgesColor2x2CS";

            cmd.BeginSample(sampleName);

            Set(cmd, kernelId, "g_inoutColorReadonly", inColor);
            Set(cmd, kernelId, "g_workingEdges", workingEdges);

            Set(cmd, kernelId, "g_workingShapeCandidates", workingShapeCandidates);
            Set(cmd, kernelId, "g_workingDeferredBlendItemListHeads", workingDeferredBlendItemListHeads.Handle);
            Set(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);

            // TODO: ThreadGroups count!
            cmd.DispatchCompute(_compute, kernelId, 1, 1, 1);

            cmd.EndSample(sampleName);
        }

        public void ComputeDispatchArgsCS(
            IComputeCommandBuffer cmd,
            BufferHandle workingDeferredBlendLocationList,
            BufferHandle workingControlBuffer,
            BufferHandle workingExecuteIndirectBuffer
        )
        {
            int kernelId = _computeDispatchArgsCS;
            var sampleName = "ComputeDispatchArgsCS";

            cmd.BeginSample(sampleName);

            // TODO: Get size and stride!
            cmd.SetComputeVectorParam(_compute, "g_workingDeferredBlendLocationList_Dim", new Vector4());
            Set(cmd, kernelId, "g_workingDeferredBlendLocationList", workingDeferredBlendLocationList);
            Set(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);
            Set(cmd, kernelId, "g_workingExecuteIndirectBuffer", workingExecuteIndirectBuffer);

            // TODO: ThreadGroups count!
            cmd.DispatchCompute(_compute, kernelId, 1, 1, 1);

            cmd.EndSample(sampleName);
        }

        // inColor : Texture2D<float4>
        // workingEdges : RWTexture2D<uint>
        // workingDeferredBlendItemListHeads
        // - MacOS|IOS : RWStructuredBuffer<uint>
        // - Windows   : RWTexture2D<uint>
        public void ProcessCandidatesCS(
            IComputeCommandBuffer cmd,
            TextureHandle inColor,
            TextureHandle workingEdges,
            AtomicTextureHandle workingDeferredBlendItemListHeads,
            BufferHandle workingControlBuffer,
            BufferHandle workingDeferredBlendItemList,
            BufferHandle workingDeferredBlendLocationList,
            BufferHandle workingShapeCandidates
        )
        {
            int kernelId = _processCandidatesCS;
            var sampleName = "ProcessCandidatesCS";

            cmd.BeginSample(sampleName);

            Set(cmd, kernelId, "g_inoutColorReadonly", inColor);
            Set(cmd, kernelId, "g_workingEdges", workingEdges);

            Set(cmd, kernelId, "g_workingDeferredBlendItemListHeads", workingDeferredBlendItemListHeads.Handle);
            Set(
                cmd,
                "g_workingDeferredBlendItemListHeads_Size",
                new Vector4(workingDeferredBlendItemListHeads.Width, workingDeferredBlendItemListHeads.Height)
            );
            Set(cmd, kernelId, "g_workingControlBuffer", workingControlBuffer);
            Set(cmd, kernelId, "g_workingDeferredBlendItemList", workingDeferredBlendItemList);
            Set(cmd, kernelId, "g_workingDeferredBlendLocationList", workingDeferredBlendLocationList);
            Set(cmd, kernelId, "g_workingShapeCandidates", workingShapeCandidates);

            // TODO: ThreadGroups count!
            cmd.DispatchCompute(_compute, kernelId, 1, 1, 1);

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
}
