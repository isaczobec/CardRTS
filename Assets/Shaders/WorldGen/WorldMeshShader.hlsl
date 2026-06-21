#ifndef WORLD_MESH_SHADER_INCLUDED
#define WORLD_MESH_SHADER_INCLUDED

// TEXTURE2D_ARRAY/SAMPLER/SAMPLE_* are Unity cross-platform macros (defined in Core.hlsl).
// The raw DX11 Texture2DArray.Sample() API doesn't translate to GLSL correctly.
TEXTURE2D_ARRAY(_TileTextures);
SAMPLER(sampler_TileTextures);

TEXTURE2D(_TileIDTex);
SAMPLER(sampler_TileIDTex);

CBUFFER_START(UnityPerMaterial)
    float _TileDetailTiling;
CBUFFER_END

static const float CHUNK_SIZE    = 16.0;
static const float TILE_TEX_SIZE = 17.0;

float4 SampleTerrain(float2 uv)
{
    float2 tileUV   = uv * CHUNK_SIZE;
    float2 tileBase = floor(tileUV);
    float2 tileFrac = frac(tileUV);

    // +0.5 shifts to texel centre — avoids precision-edge hits with Point filtering.
    float id00 = round(SAMPLE_TEXTURE2D(_TileIDTex, sampler_TileIDTex, (tileBase + float2(0.5, 0.5)) / TILE_TEX_SIZE).r * 255.0);
    float id10 = round(SAMPLE_TEXTURE2D(_TileIDTex, sampler_TileIDTex, (tileBase + float2(1.5, 0.5)) / TILE_TEX_SIZE).r * 255.0);
    float id01 = round(SAMPLE_TEXTURE2D(_TileIDTex, sampler_TileIDTex, (tileBase + float2(0.5, 1.5)) / TILE_TEX_SIZE).r * 255.0);
    float id11 = round(SAMPLE_TEXTURE2D(_TileIDTex, sampler_TileIDTex, (tileBase + float2(1.5, 1.5)) / TILE_TEX_SIZE).r * 255.0);

    float2 detailUV = uv * _TileDetailTiling;

    float4 c00 = SAMPLE_TEXTURE2D_ARRAY(_TileTextures, sampler_TileTextures, detailUV, id00);
    float4 c10 = SAMPLE_TEXTURE2D_ARRAY(_TileTextures, sampler_TileTextures, detailUV, id10);
    float4 c01 = SAMPLE_TEXTURE2D_ARRAY(_TileTextures, sampler_TileTextures, detailUV, id01);
    float4 c11 = SAMPLE_TEXTURE2D_ARRAY(_TileTextures, sampler_TileTextures, detailUV, id11);

    float2 blend = smoothstep(0.0, 1.0, tileFrac);
    return lerp(lerp(c00, c10, blend.x), lerp(c01, c11, blend.x), blend.y);
}

#endif
