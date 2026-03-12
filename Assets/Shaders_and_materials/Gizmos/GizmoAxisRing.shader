Shader "CustomShaders/NegativeAxisRing"
{
   Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FillColor ("Inner Fill Color", Color) = (1,1,1,0.2)
        _RingColor ("Ring Color", Color) = (1,1,1,1)

        _Radius ("Outer Radius", Range(0,0.5)) = 0.45
        _Thickness ("Ring Thickness", Range(0.005,0.2)) = 0.05
        _EdgeSoftness ("Edge Softness", Range(0.001,0.05)) = 0.01
    }

    SubShader
    {
//        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Tags { "Queue"="Transparent+0" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite off
        Cull Off
        Lighting Off

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
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _FillColor;
            fixed4 _RingColor;
            float _Radius;
            float _Thickness;
            float _EdgeSoftness;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Center UV
                float2 p = i.uv - 0.5;
                float dist = length(p);

                float outerAlpha = smoothstep(
                    _Radius,
                    _Radius - _EdgeSoftness,
                    dist
                );

                float innerEdge = _Radius - _Thickness;

                float innerAlpha = smoothstep(
                    innerEdge,
                    innerEdge - _EdgeSoftness,
                    dist
                );

                // Ring mask
                float ringAlpha = outerAlpha - innerAlpha;

                // Fill mask (inside ring)
                float fillAlpha = innerAlpha;

                fixed4 col = 0;

                // Inner fill
                col = _FillColor;
                col.a *= fillAlpha;

                // Ring on top
                fixed4 ringCol = _RingColor;
                ringCol.a *= ringAlpha;

                col = lerp(col, ringCol, ringCol.a);

                return col;
            }
            ENDCG
        }
    }
}
