Shader "Custom/ChunkTransparent"
{
    Properties
    {
        _MainTex ("Atlas", 2D)         = "white" {}
        _Color   ("Tint",  Color)      = (1, 1, 1, 1)
        _Alpha   ("Alpha", Range(0,1)) = 1.0
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
            CBUFFER_END

            float _GlobalLightLevel;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                // R = sky light, G = block light, B = AO factor, A = face brightness
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
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color;
                col.a *= _Alpha;

                float2 lightUV   = float2(IN.color.g, 1.0 - IN.color.r) * (15.0 / 16.0);
                half4 dayLight   = SAMPLE_TEXTURE2D(_DayLightmap,   sampler_DayLightmap,   lightUV);
                half4 nightLight = SAMPLE_TEXTURE2D(_NightLightmap, sampler_NightLightmap, lightUV);
                half4 lightColor = lerp(nightLight, dayLight, _GlobalLightLevel);

                col.rgb *= IN.color.a * lightColor.rgb * IN.color.b;

                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
}
