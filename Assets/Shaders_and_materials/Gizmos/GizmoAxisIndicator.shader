Shader "CustomShaders/GizmoAxisIndicator"
{
   Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _CircleColor ("Circle Color", Color) = (1,1,1,1)
        _LetterColor ("Letter Color", Color) = (0,0,0,1)

        _Radius ("Circle Radius", Range(0,0.5)) = 0.45
        _EdgeSoftness ("Edge Softness", Range(0.001,0.05)) = 0.01

        [Enum(X,0,Y,1,Z,2)]
        _Letter ("Letter", Int) = 0

        _LetterThickness ("Letter Thickness", Range(0.01,0.1)) = 0.05
        _LetterScale ("Letter Size", Range(0.5,2.0)) = 1.0
    }

    SubShader
    {
//        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" }
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

            fixed4 _CircleColor;
            fixed4 _LetterColor;
            float _Radius;
            float _EdgeSoftness;
            float _LetterThickness;
            float _LetterScale;
            int _Letter;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Distance to line segment
            float sdLine(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            float letterX(float2 p)
            {
                float d1 = sdLine(p, float2(-0.25, -0.25), float2(0.25, 0.25));
                float d2 = sdLine(p, float2(-0.25,  0.25), float2(0.25, -0.25));
                return min(d1, d2);
            }

            float letterY(float2 p)
            {
                float d1 = sdLine(p, float2(-0.25, 0.25), float2(0.0, 0.0));
                float d2 = sdLine(p, float2( 0.25, 0.25), float2(0.0, 0.0));
                float d3 = sdLine(p, float2( 0.0, 0.0), float2(0.0, -0.3));
                return min(min(d1, d2), d3);
            }

            float letterZ(float2 p)
            {
                float d1 = sdLine(p, float2(-0.25,  0.25), float2(0.25,  0.25));
                float d2 = sdLine(p, float2( 0.25,  0.25), float2(-0.25, -0.25));
                float d3 = sdLine(p, float2(-0.25, -0.25), float2(0.25, -0.25));
                return min(min(d1, d2), d3);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Center UV
                float2 p = i.uv - 0.5;

                // Circle mask
                float dist = length(p);
                float circleAlpha = smoothstep(_Radius, _Radius - _EdgeSoftness, dist);

                fixed4 col = _CircleColor;
                col.a *= circleAlpha;

                // Scale letter space
                float2 lp = p / _LetterScale;

                float letterDist = 1.0;
                if (_Letter == 0) letterDist = letterX(lp);
                else if (_Letter == 1) letterDist = letterY(lp);
                else if (_Letter == 2) letterDist = letterZ(lp);

                float letterAlpha = smoothstep(
                    _LetterThickness,
                    _LetterThickness * 0.5,
                    letterDist
                );

                fixed4 letterCol = _LetterColor;
                letterCol.a *= letterAlpha;

                return lerp(col, letterCol, letterCol.a);
            }
            ENDCG
        }
    }
}
