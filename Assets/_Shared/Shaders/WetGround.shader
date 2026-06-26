Shader "Custom/WetGround"
{
    Properties
    {
        _Color      ("Color",          Color)       = (1,1,1,1)
        _MainTex    ("Albedo (RGB)",   2D)          = "white" {}
        _BumpMap    ("Normal Map",     2D)          = "bump"  {}
        _BumpScale  ("Normal Scale",   Range(0,2))  = 0.2
        _Glossiness ("Wetness (Smoothness)", Range(0,1)) = 0.0
        _Metallic   ("Metallic",       Range(0,1))  = 0.0

        [Space(8)]
        [Header(Wet Patch Effect)]
        _WetPatchScale     ("Patch Scale (0.2=grande 5m  1.0=charco 1m)", Range(0.05, 2.0)) = 0.35
        _WetPatchVariation ("Patch Variation (0=uniforme  1=maximo)",      Range(0.0,  1.0)) = 0.60
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };

        half   _Glossiness;
        half   _Metallic;
        fixed4 _Color;
        half   _BumpScale;
        float  _WetPatchScale;
        float  _WetPatchVariation;

        // Ruido gradiente 2D (Perlin-like) — compatible con WebGL ES2 / #pragma target 3.0
        float2 WetHash2(float2 p)
        {
            float2 q = float2(dot(p, float2(127.1, 311.7)),
                              dot(p, float2(269.5, 183.3)));
            return -1.0 + 2.0 * frac(sin(q) * 43758.5453);
        }

        float WetGNoise(float2 p)
        {
            float2 pi = floor(p);
            float2 pf = frac(p);
            float2 u  = pf * pf * (3.0 - 2.0 * pf);

            float na = dot(WetHash2(pi + float2(0,0)), pf - float2(0,0));
            float nb = dot(WetHash2(pi + float2(1,0)), pf - float2(1,0));
            float nc = dot(WetHash2(pi + float2(0,1)), pf - float2(0,1));
            float nd = dot(WetHash2(pi + float2(1,1)), pf - float2(1,1));

            return lerp(lerp(na, nb, u.x), lerp(nc, nd, u.x), u.y);
        }

        // 2 octavas de FBM — genera manchas organicas suaves
        float WetPatchNoise(float2 p)
        {
            float n1 = WetGNoise(p)                             * 0.65;
            float n2 = WetGNoise(p * 2.1 + float2(1.73, 3.31)) * 0.35;
            // remap [-1,1] -> [0,1] con sesgo hacia humedo (rango ~0.15..1.0)
            return saturate((n1 + n2) * 0.5 + 0.55);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 col = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo   = col.rgb;
            o.Normal   = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);

            // Mancha en espacio mundial: independiente del tiling de UV
            float patch = WetPatchNoise(IN.worldPos.xz * _WetPatchScale);

            // patch=0 -> factor=(1-variation),  patch=1 -> factor=1.0
            // Variation=0 => brillo uniforme igual a _Glossiness
            // Variation=1 => rango completo entre seco y _Glossiness
            float factor    = lerp(1.0 - _WetPatchVariation, 1.0, patch);
            o.Smoothness    = _Glossiness * factor;
            o.Metallic      = _Metallic;
            o.Alpha         = col.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
