using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DepthProbeFeature : ScriptableRendererFeature
{
    class DepthProbePass : ScriptableRenderPass
    {
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var depthHandle = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            var depthTex    = Shader.GetGlobalTexture("_CameraDepthTexture");

            string handleInfo = "NULL";
            if (depthHandle?.rt != null)
                handleInfo = $"{depthHandle.rt.width}x{depthHandle.rt.height} fmt={depthHandle.rt.format}";
            else if (depthHandle != null)
                handleInfo = $"handle exists but rt is null (nameID={depthHandle.nameID})";

            string texInfo = "NULL";
            if (depthTex != null)
                texInfo = $"{depthTex.width}x{depthTex.height} type={depthTex.GetType().Name}";

            Debug.Log($"[DepthProbe] cameraDepthHandle: {handleInfo}");
            Debug.Log($"[DepthProbe] _CameraDepthTexture: {texInfo}");
        }

        public override void Execute(ScriptableRenderContext context,
                                     ref RenderingData renderingData) { }
    }

    private DepthProbePass _pass;

    public override void Create()
    {
        _pass = new DepthProbePass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
                                          ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game) return;
        renderer.EnqueuePass(_pass);
    }
}