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

            // Decals' Screen-Space technique (unlike DBuffer) expects rendering-layer
            // data written as a second render target straight from the main opaque
            // pass, rather than from a DepthNormalsOnly prepass. Without this, this
            // mesh's Rendering Layer Mask is never visible to the decal system and
            // rendering-layer-filtered decals silently fail to project onto it.
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
            #pragma target 4.5 _WRITE_RENDERING_LAYERS

            // Core.hlsl must come first — it defines TEXTURE2D_ARRAY, SAMPLER,
            // SAMPLE_TEXTURE2D, CBUFFER_START, and TransformObjectToHClip.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/WorldGen/WorldMeshShader.hlsl"

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

            void frag(
                Varyings IN
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                outColor = SampleTerrain(IN.uv);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }

            ENDHLSL
        }
    }
}
