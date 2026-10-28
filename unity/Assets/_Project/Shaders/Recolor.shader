// Repaints a texture's skin in another colour (a chalker model in its seat's colour): texels whose hue lies within
// _Band of _FromHue take _Hue, their saturation and value scaled toward the new colour's, so shading and painted detail
// stay. Texels of other hues (eyes, mouth, tongue) and greys are left as they are. Used with Graphics.Blit into a
// RenderTexture once per colour change, never per frame. Kept in builds through RequiredShaders.
Shader "TableWorld/Recolor"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _FromHue("Hue to replace (0-1)", Float) = 0.3
        _Band("Hue band (0-0.5)", Float) = 0.15
        _Hue("New hue (0-1)", Float) = 0
        _SaturationScale("Saturation scale", Float) = 1
        _ValueScale("Value scale", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _FromHue;
                float _Band;
                float _Hue;
                float _SaturationScale;
                float _ValueScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float3 hsv = RgbToHsv(color.rgb);
                float distance = abs(frac(hsv.x - _FromHue + 0.5) - 0.5);
                float weight = saturate(1 - distance / _Band) * saturate((hsv.y - 0.08) / 0.1);
                float3 painted = HsvToRgb(float3(_Hue, saturate(hsv.y * _SaturationScale), saturate(hsv.z * _ValueScale)));
                return half4(lerp(color.rgb, painted, smoothstep(0, 0.6, weight)), color.a);
            }
            ENDHLSL
        }
    }
}
