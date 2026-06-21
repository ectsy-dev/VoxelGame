Shader "Custom/ChunkLit"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);       SAMPLER(sampler_MainTex);
            // Set once via Shader.SetGlobalTexture() in World.Start()
            TEXTURE2D(_DayLightmap);   SAMPLER(sampler_DayLightmap);
            TEXTURE2D(_NightLightmap); SAMPLER(sampler_NightLightmap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            // 0 = full night, 1 = full day — drive from sun angle for day/night cycle.
            float _GlobalLightLevel;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                // R = sky light  (0-1, maps to lightmap V)
                // G = block light(0-1, maps to lightmap U) — reserved, 0 until torches added
                // B = AO factor  (0.35-1.0)
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
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.uv         = IN.uv;
                OUT.color      = IN.color;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                // Lightmap UV: X = block light, Y = sky light.
                // Levels 0-15 map to 0/16 – 15/16 so each pixel = one light level.
                float2 lightUV   = float2(IN.color.g, 1.0 - IN.color.r) * (15.0 / 16.0);
                half4 dayLight   = SAMPLE_TEXTURE2D(_DayLightmap,   sampler_DayLightmap,   lightUV);
                half4 nightLight = SAMPLE_TEXTURE2D(_NightLightmap, sampler_NightLightmap, lightUV);
                half4 lightColor = lerp(nightLight, dayLight, _GlobalLightLevel);

                // diffuse (face shade) × light color from LUT × AO
                col.rgb *= IN.color.a * lightColor.rgb * IN.color.b;

                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
}
