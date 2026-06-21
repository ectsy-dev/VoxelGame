Shader "Custom/LiquidShader"
{
    // Water:  Alpha~0.75, EmissionStrength=0, gentle wave + scroll
    // Lava:   Alpha=1.0,  EmissionStrength>0 (orange glow), slow heavy wave + scroll
    Properties
    {
        _MainTex          ("Atlas",            2D)         = "white" {}
        _Color            ("Tint",             Color)      = (1, 1, 1, 1)
        [Space]
        _Alpha            ("Alpha",            Range(0,1)) = 0.75
        [Space]
        _ScrollSpeedX     ("Scroll Speed X",   Float)      = 0.05
        _ScrollSpeedY     ("Scroll Speed Y",   Float)      = 0.02
        [Space]
        _WaveHeight       ("Wave Height",      Float)      = 0.025
        _WaveSpeed        ("Wave Speed",       Float)      = 1.5
        _WaveFreq         ("Wave Frequency",   Float)      = 0.5
        [Space]
        [HDR] _EmissionColor    ("Emission Color",   Color)      = (0, 0, 0, 0)
        _EmissionStrength ("Emission Strength", Float)     = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);       SAMPLER(sampler_MainTex);
            TEXTURE2D(_DayLightmap);   SAMPLER(sampler_DayLightmap);
            TEXTURE2D(_NightLightmap); SAMPLER(sampler_NightLightmap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _Alpha;
                float  _ScrollSpeedX;
                float  _ScrollSpeedY;
                float  _WaveHeight;
                float  _WaveSpeed;
                float  _WaveFreq;
                half4  _EmissionColor;
                float  _EmissionStrength;
            CBUFFER_END

            float _GlobalLightLevel;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                // R = wave weight (0 = pinned, 1 = free to wave) — top face only
                // G = sky light   (0-1)
                // B = AO factor   (0.35-1.0)
                // A = face brightness / diffuse (0.4-1.0)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float  fogFactor  : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posOS = IN.positionOS.xyz;
                float3 posWS = TransformObjectToWorld(posOS);

                // Wave — R channel encodes wave eligibility per vertex.
                // Base offset lowers the top face so waves always stay below the block
                // top (y+1), preventing the gap between the waving top and pinned sides.
                posOS.y -= 0.1 * IN.color.r;
                float wave = sin(_Time.y * _WaveSpeed + posWS.x * _WaveFreq + posWS.z * _WaveFreq) * _WaveHeight;
                posOS.y   += wave * IN.color.r;

                VertexPositionInputs pos = GetVertexPositionInputs(posOS);
                OUT.positionCS = pos.positionCS;
                OUT.uv         = IN.uv;
                OUT.color      = IN.color;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Scroll UV within the atlas tile
                const float tileSize = 1.0 / 32.0;
                float2 tileOrigin    = floor(IN.uv / tileSize) * tileSize;
                float2 localUV       = (IN.uv - tileOrigin) / tileSize;
                float2 scrolled      = frac(localUV + float2(_Time.y * _ScrollSpeedX, _Time.y * _ScrollSpeedY));
                float2 finalUV       = tileOrigin + scrolled * tileSize;

                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, finalUV) * _Color;

                // Sky light in G, block light = 0 until torches added
                float2 lightUV   = float2(0.0, 1.0 - IN.color.g) * (15.0 / 16.0);
                half4 dayLight   = SAMPLE_TEXTURE2D(_DayLightmap,   sampler_DayLightmap,   lightUV);
                half4 nightLight = SAMPLE_TEXTURE2D(_NightLightmap, sampler_NightLightmap, lightUV);
                half4 lightColor = lerp(nightLight, dayLight, _GlobalLightLevel);

                col.rgb *= IN.color.a * lightColor.rgb * IN.color.b;

                col.rgb += _EmissionColor.rgb * _EmissionStrength;
                col.a    = _Alpha;
                col.rgb  = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
}
