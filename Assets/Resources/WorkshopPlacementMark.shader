Shader "Monlit/Workshop Placement Mark"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _DepthTest ("Depth test", Float) = 4
        [HideInInspector] _Obscured ("Obscured edge", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest [_DepthTest]
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Obscured;
                float _DepthTest;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float dash : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                    output.color.rgb = SRGBToLinear(output.color.rgb);
                #endif
                output.dash = (input.positionOS.x + input.positionOS.z) * 5.8;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_Obscured > 0.5)
                {
                    // Keep the rear edge legible through a raised casting. Leave its face visible.
                    float width = max(fwidth(input.dash), 0.001);
                    float dash = 1.0 - smoothstep(0.25 - width * 0.5, 0.25 + width * 0.5,
                                                   abs(frac(input.dash) - 0.5));
                    return half4(lerp(half3(.012, .039, .043), input.color.rgb, dash),
                                 lerp(.32, .66, dash));
                }
                return input.color;
            }
            ENDHLSL
        }
    }
}
