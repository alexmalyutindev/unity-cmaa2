using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CMAA2.Core
{
    public class CMAA2RenderFeature : ScriptableRendererFeature
    {
        public ComputeShader CMAA2Compute;

        private CMAA2RenderPass _pass;

        public override void Create()
        {
            _pass = new CMAA2RenderPass(CMAA2Compute)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_pass);
        }
    }
}
