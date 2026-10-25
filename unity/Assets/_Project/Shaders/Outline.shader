// The rim around a selected or held object (TTS outlines them in the player's colour, G46), drawn on a copy of the
// object's mesh after the scene. Pass 1 marks the object's own silhouette in the stencil buffer; pass 2 draws the mesh
// pushed outward from its centre by _Width metres, only outside that mark and over whatever is there, so a rim shows
// around the silhouette from any angle (a card lying on the felt included). Pushing from the centre, not along normals,
// keeps boxes (cards, dice) closed at their corners. Kept in builds through RequiredShaders.
Shader "TableWorld/Outline"
{
    Properties
    {
        _BaseColor("Color", Color) = (1, 0.6, 0.1, 1)
        _Width("Width (m)", Float) = 0.003
        _Center("Centre (object space)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+100" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Width;
            float4 _Center;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings { float4 positionCS : SV_POSITION; };

        Varyings Pushed(Attributes input, float width)
        {
            Varyings output;
            float3 world = TransformObjectToWorld(input.positionOS.xyz);
            float3 away = world - TransformObjectToWorld(_Center.xyz);
            float length2 = dot(away, away);
            away = length2 > 1e-10 ? away * rsqrt(length2) : float3(0, 1, 0);
            output.positionCS = TransformWorldToHClip(world + away * width);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZTest Always
            ZWrite Off
            ColorMask 0
            Stencil { Ref 64 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            Varyings Vertex(Attributes input) { return Pushed(input, 0); }
            half4 Fragment(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "Rim"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZTest Always
            ZWrite Off
            Stencil { Ref 64 Comp NotEqual }
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            Varyings Vertex(Attributes input) { return Pushed(input, _Width); }
            half4 Fragment(Varyings input) : SV_Target { return _BaseColor; }
            ENDHLSL
        }
    }
}
