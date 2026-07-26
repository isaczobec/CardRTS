Shader "Custom/WorldMesh"
{
    Properties
    {
        [NoScaleOffset] _TileTextures ("Tile Texture Array", 2DArray) = "" {}
        [NoScaleOffset] _TileIDTex   ("Tile ID Texture",    2D)      = "white" {}
        _TileDetailTiling ("Detail Tiling", Float) = 8.0
        _TileBlendRadius ("Tile Blend Radius (ID texels)", Float) = 1.5
        [IntRange] _TileBlendSamples ("Tile Blend Samples (ring count: 1=3x3, 2=5x5, 3=7x7)", Range(1, 3)) = 1
        _ShadowOpacity ("Shadow Opacity", Range(0, 1)) = 0.75
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

            // Main-light shadow receiving. Terrain only ever needs the main directional
            // light's shadow (no additional per-light shadows), so that's all this pulls in.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            // Core.hlsl must come before WorldMeshShader.hlsl because it defines
            // the CBUFFER_START/END macros used in that file. Lighting.hlsl (via
            // RealtimeLights.hlsl) pulls in Shadows.hlsl for TransformWorldToShadowCoord
            // and MainLightRealtimeShadow.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Scripts/WorldGen/WorldMeshShader.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                half shadow = MainLightRealtimeShadow(shadowCoord);
                // _ShadowOpacity=0 leaves shadowed areas at full brightness; 1 is the previous
                // behavior (fully black in deep shadow). Lit areas (shadow=1) are unaffected
                // either way since lerp(1, 1, t) == 1.
                half shadowFactor = lerp(1.0h, shadow, (half)_ShadowOpacity);

                half4 color = SampleTerrain(IN.uv);
                color.rgb *= shadowFactor;
                return color;
            }

            ENDHLSL
        }
    }
}
