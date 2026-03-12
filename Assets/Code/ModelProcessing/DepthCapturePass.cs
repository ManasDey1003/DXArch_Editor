using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class DepthCapturePass : ScriptableRenderPass
{
    private static readonly int OcclusionDepthID =
        Shader.PropertyToID("_OcclusionDepthTexture");

    private RenderTexture _depthCopy;
    private ComputeShader _copyCompute;
    private int           _kernel;
    private int           _width, _height;

    // Inline compute shader - copies depth texture into RWTexture2D
    private const string CopyComputeSrc = @"
#pragma kernel CopyDepth

Texture2D<float>    _SourceDepth;
RWTexture2D<float>  _DestDepth;
int2                _Size;

[numthreads(8,8,1)]
void CopyDepth(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_Size.x || id.y >= (uint)_Size.y) return;
    _DestDepth[id.xy] = _SourceDepth.Load(int3(id.xy, 0));
}";

    public DepthCapturePass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }



    private void EnsureRT(int w, int h)
    {
        if (_depthCopy != null && _depthCopy.width == w && _depthCopy.height == h)
            return;
        _depthCopy?.Release();
        _depthCopy = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat)
        {
            name              = "_OcclusionDepthTexture",
            filterMode        = FilterMode.Point,
            wrapMode          = TextureWrapMode.Clamp,
            enableRandomWrite = true
        };
        _depthCopy.Create();
        Debug.Log($"[DepthCapture] RT {w}x{h}");
    }

    public void Init(ComputeShader copyCompute)
    {
        _copyCompute = copyCompute;
        _kernel      = _copyCompute.FindKernel("CopyDepth");
    }

    // ── RenderGraph path ──────────────────────────────────────────────────

    private class PassData
    {
        public TextureHandle  sourceDepth;
        public RenderTexture  destRT;
        public ComputeShader  compute;
        public int            kernel;
        public int            width, height;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph,
                                            ContextContainer frameData)
    {
        if (_copyCompute == null) return;

        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData   = frameData.Get<UniversalCameraData>();
        if (cameraData.cameraType != CameraType.Game) return;

        int w = cameraData.cameraTargetDescriptor.width;
        int h = cameraData.cameraTargetDescriptor.height;
        EnsureRT(w, h);

        using (var builder = renderGraph.AddUnsafePass<PassData>(
            "CopyDepthToRFloat", out var passData))
        {
            passData.sourceDepth = resourceData.cameraDepthTexture;
            passData.destRT      = _depthCopy;
            passData.compute     = _copyCompute;
            passData.kernel      = _kernel;
            passData.width       = w;
            passData.height      = h;

            builder.UseTexture(passData.sourceDepth, AccessFlags.Read);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, UnsafeGraphContext ctx) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);

                cmd.SetComputeTextureParam(data.compute, data.kernel,
                    "_SourceDepth", data.sourceDepth);
                cmd.SetComputeTextureParam(data.compute, data.kernel,
                    "_DestDepth", data.destRT);
                cmd.SetComputeIntParams(data.compute, "_Size",
                    data.width, data.height);

                int gx = Mathf.CeilToInt(data.width  / 8f);
                int gy = Mathf.CeilToInt(data.height / 8f);
                cmd.DispatchCompute(data.compute, data.kernel, gx, gy, 1);

                cmd.SetGlobalTexture(OcclusionDepthID, data.destRT);
            });
        }
    }

    // ── Compatibility path ────────────────────────────────────────────────

    public override void Execute(ScriptableRenderContext context,
                                  ref RenderingData renderingData)
    {
        if (_copyCompute == null) return;
        if (renderingData.cameraData.cameraType != CameraType.Game) return;

        var desc = renderingData.cameraData.cameraTargetDescriptor;
        EnsureRT(desc.width, desc.height);

        CommandBuffer cmd = CommandBufferPool.Get("CopyDepthToRFloat");

        var renderer    = renderingData.cameraData.renderer;
        var depthHandle = renderer.cameraDepthTargetHandle;

        if (depthHandle?.rt != null)
        {
            cmd.SetComputeTextureParam(_copyCompute, _kernel,
                "_SourceDepth", depthHandle.rt);
            cmd.SetComputeTextureParam(_copyCompute, _kernel,
                "_DestDepth", _depthCopy);
            cmd.SetComputeIntParams(_copyCompute, "_Size",
                desc.width, desc.height);

            int gx = Mathf.CeilToInt(desc.width  / 8f);
            int gy = Mathf.CeilToInt(desc.height / 8f);
            cmd.DispatchCompute(_copyCompute, _kernel, gx, gy, 1);

            cmd.SetGlobalTexture(OcclusionDepthID, _depthCopy);
        }
        else
        {
            Debug.LogWarning("[DepthCapture] No depth handle.");
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public void Cleanup()
    {
        _depthCopy?.Release();
        _depthCopy = null;
    }

    public RenderTexture DepthTexture => _depthCopy;
}