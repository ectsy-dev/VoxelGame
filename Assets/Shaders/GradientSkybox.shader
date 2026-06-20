Shader "Custom/GradientSkybox"
{
    Properties
    {
        _ZenithColor    ("Zenith Color",   Color) = (0.10, 0.35, 0.78, 1)
        _HorizonColor   ("Horizon Color",  Color) = (0.60, 0.80, 1.00, 1)
        _GroundColor    ("Ground Color",   Color) = (0.05, 0.05, 0.05, 1)
        _HorizonSharpness ("Horizon Sharpness", Range(0.5, 8.0)) = 2.5
        _GroundBlend    ("Ground Blend",   Range(1.0, 16.0)) = 6.0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _GroundColor;
                float _HorizonSharpness;
                float _GroundBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldDir   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.worldDir   = IN.positionOS.xyz;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.worldDir);

                // Sky: horizon → zenith based on upward angle
                float skyT = saturate(pow(max(dir.y, 0.0), _HorizonSharpness));
                half4 color = lerp(_HorizonColor, _ZenithColor, skyT);

                // Ground: horizon → ground color below the horizon
                float groundT = saturate(-dir.y * _GroundBlend);
                color = lerp(color, _GroundColor, groundT);

                return color;
            }
            ENDHLSL
        }
    }
}
