Shader "Custom/WorldMesh"
{
    Properties
    {
        [NoScaleOffset] _TileTextures ("Tile Texture Array", 2DArray) = "" {}
        [NoScaleOffset] _TileIDTex   ("Tile ID Texture",    2D)      = "white" {}
        _TileDetailTiling ("Detail Tiling", Float) = 8.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray

            // Core.hlsl must come before WorldMeshShader.hlsl because it defines
            // the CBUFFER_START/END macros used in that file.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Scripts/WorldGen/WorldMeshShader.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                return SampleTerrain(IN.uv);
            }

            ENDHLSL
        }
    }
}
