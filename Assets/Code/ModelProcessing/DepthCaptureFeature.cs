using UnityEngine;
using UnityEngine.Rendering.Universal;

public class DepthCaptureFeature : ScriptableRendererFeature
{
    public static DepthCaptureFeature Instance { get; private set; }

    [SerializeField] ComputeShader _copyDepthCompute;

    private DepthCapturePass _pass;

    public RenderTexture OcclusionDepthTexture => _pass?.DepthTexture;

    public override void Create()
    {
        Instance = this;
        _pass    = new DepthCapturePass();

        if (_copyDepthCompute != null)
            _pass.Init(_copyDepthCompute);
        else
            Debug.LogError("[DepthCaptureFeature] CopyDepth compute shader not assigned!");
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
                                          ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game) return;
        if (_copyDepthCompute == null) return;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Cleanup();
    }
}

