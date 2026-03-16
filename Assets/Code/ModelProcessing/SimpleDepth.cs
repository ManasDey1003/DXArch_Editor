using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class DepthDebugDisplay : MonoBehaviour
{
    public bool show = true;
    public float nearDisplay = 0.1f;
    public float farDisplay = 500f;

    private Material _mat;
    private Camera _cam;

    private const string ShaderSrc = @"
Shader ""Hidden/DepthDebug""
{
    Properties { _MainTex("""", 2D) = ""white""{} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include ""UnityCG.cginc""

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            float _Near;
            float _Far;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                d = LinearEyeDepth(d);
                d = saturate((d - _Near) / (_Far - _Near));
                return fixed4(d, d, d, 1);
            }
            ENDCG
        }
    }
}";

    void Start()
    {
        // _cam = GetComponent<Camera>();
        // _cam.depthTextureMode = DepthTextureMode.Depth;

        // var shader = ShaderUtil.CreateShaderAsset(ShaderSrc, false);
        // _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (!show || _mat == null)
        {
            Graphics.Blit(src, dst);
            return;
        }

        _mat.SetFloat("_Near", nearDisplay);
        _mat.SetFloat("_Far", farDisplay);
        Graphics.Blit(src, dst, _mat);
    }
}