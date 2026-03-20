Shader "Hidden/ObjectIDVisualizer"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MaxID ("Max ID", Float) = 100
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float _MaxID;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Read the integer ID
                float id = tex2D(_MainTex, i.uv).r;
                
                if (id <= 0)
                    return fixed4(0, 0, 0, 1); // Black for background
                
                // Convert ID to hue (rainbow colors)
                float hue = frac(id / _MaxID);
                
                // HSV to RGB conversion
                float3 rgb;
                float h = hue * 6.0;
                float f = frac(h);
                float p = 0.0;
                float q = 1.0 - f;
                float t = f;
                
                if (h < 1.0) rgb = float3(1.0, t, p);
                else if (h < 2.0) rgb = float3(q, 1.0, p);
                else if (h < 3.0) rgb = float3(p, 1.0, t);
                else if (h < 4.0) rgb = float3(p, q, 1.0);
                else if (h < 5.0) rgb = float3(t, p, 1.0);
                else rgb = float3(1.0, p, q);
                
                return fixed4(rgb, 1.0);
            }
            ENDCG
        }
    }
}