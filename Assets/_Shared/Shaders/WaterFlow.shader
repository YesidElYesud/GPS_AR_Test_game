Shader "Custom/WaterFlow"
{
    Properties
    {
        _MainTex      ("Textura base agua", 2D)       = "white" {}
        _FoamTex      ("Textura espuma / ondas", 2D)  = "white" {}
        _BumpMap      ("Normal map (ondas)", 2D)       = "bump"  {}

        _Color        ("Color del agua",   Color)     = (0.18, 0.48, 0.75, 0.75)
        _FoamColor    ("Color espuma",     Color)     = (0.85, 0.92, 1.0, 0.5)

        [Header(Flujo)]
        _FlowDirection ("Dirección XY (se normaliza)", Vector) = (1, 0, 0, 0)
        _FlowSpeed     ("Velocidad flujo principal",   Float)  = 0.08
        _FoamSpeed     ("Velocidad espuma (paralela)", Float)  = 0.04
        _FoamOffset    ("Offset angular espuma (0-1)", Range(0,1)) = 0.15

        [Header(Apariencia)]
        _FoamBlend    ("Mezcla espuma",   Range(0,1)) = 0.35
        _BumpStrength ("Fuerza ondas",    Range(0,3)) = 0.8
        _Glossiness   ("Brillo",          Range(0,1)) = 0.85
        _Metallic     ("Metallic",        Range(0,1)) = 0.0
        _Alpha        ("Transparencia",   Range(0,1)) = 0.75

        [Header(Desplazamiento de vertices)]
        _WaveAmplitude ("Amplitud olas (m)",    Range(0, 0.5)) = 0.0
        _WaveFrequency ("Frecuencia espacial",  Range(0, 20))  = 6.0
        _WaveSpeed     ("Velocidad olas",       Range(0, 5))   = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        CGPROGRAM
        // vertex:vert activa desplazamiento geométrico por etapa
        #pragma surface surf Standard vertex:vert fullforwardshadows alpha:fade keepalpha
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _FoamTex;
        sampler2D _BumpMap;

        fixed4  _Color;
        fixed4  _FoamColor;

        float4  _FlowDirection;
        float   _FlowSpeed;
        float   _FoamSpeed;
        float   _FoamOffset;
        float   _FoamBlend;
        float   _BumpStrength;
        half    _Glossiness;
        half    _Metallic;
        half    _Alpha;

        float   _WaveAmplitude;
        float   _WaveFrequency;
        float   _WaveSpeed;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_FoamTex;
            float2 uv_BumpMap;
        };

        // ── Desplazamiento de vértices ────────────────────────────────────────────
        void vert(inout appdata_full v)
        {
            float2 flowDir = normalize(_FlowDirection.xy + float2(0.0001, 0.0));
            float2 perpDir = float2(-flowDir.y, flowDir.x);
            float  t       = _Time.y * _WaveSpeed;
            float2 pos     = v.vertex.xz;

            // Tres capas de seno en direcciones distintas para simular turbulencia
            // Capa 1: a lo largo de la corriente (ola dominante)
            float w1 = sin(dot(pos, flowDir) * _WaveFrequency + t * 1.30);
            // Capa 2: transversal a la corriente (cruce de olas)
            float w2 = sin(dot(pos, perpDir) * _WaveFrequency * 1.4 + t * 0.90) * 0.45;
            // Capa 3: diagonal caótica (turbulencia)
            float w3 = sin((pos.x * 1.7 + pos.y * 0.8) * _WaveFrequency * 0.55 + t * 1.70) * 0.25;

            v.vertex.y += (w1 + w2 + w3) * _WaveAmplitude;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Dirección principal (normalizada en shader para mayor robustez)
            float2 flowDir = normalize(_FlowDirection.xy + float2(0.0001, 0));

            // Dirección espuma: misma corriente + pequeño ángulo lateral
            float angle    = _FoamOffset * 0.5;  // max ~28°
            float cosA     = cos(angle);
            float sinA     = sin(angle);
            float2 foamDir = float2(
                flowDir.x * cosA - flowDir.y * sinA,
                flowDir.x * sinA + flowDir.y * cosA
            );

            // Offsets animados por tiempo
            float t = _Time.y;
            float2 mainUV = IN.uv_MainTex + flowDir * t * _FlowSpeed;
            float2 foamUV = IN.uv_FoamTex + foamDir * t * _FoamSpeed;
            float2 bumpUV = IN.uv_BumpMap  + flowDir * t * _FlowSpeed * 0.6;

            // Muestreo de texturas
            fixed4 mainSample = tex2D(_MainTex, mainUV);
            fixed4 foamSample = tex2D(_FoamTex, foamUV);

            // Mezcla base + espuma tintada
            fixed3 baseColor = mainSample.rgb * _Color.rgb;
            fixed3 foam      = foamSample.rgb  * _FoamColor.rgb;
            fixed3 blended   = lerp(baseColor, foam, _FoamBlend * foamSample.a);

            // Normal map para efecto de ondas superficiales
            fixed3 normal    = UnpackNormal(tex2D(_BumpMap, bumpUV));
            normal.xy       *= _BumpStrength;
            o.Normal         = normalize(normal);

            o.Albedo     = blended;
            o.Metallic   = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha      = _Alpha * _Color.a;
        }
        ENDCG
    }

    // Fallback para dispositivos muy limitados
    FallBack "Transparent/Diffuse"
}
