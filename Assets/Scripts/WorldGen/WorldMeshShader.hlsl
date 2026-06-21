#ifndef WORLD_MESH_SHADER_INCLUDED
#define WORLD_MESH_SHADER_INCLUDED

// Textures and samplers must live outside the CBUFFER.
Texture2DArray<float4> _TileTextures;
SamplerState           sampler_TileTextures;

Texture2D<float4> _TileIDTex;
SamplerState      sampler_TileIDTex;

// Per-material uniforms must be in UnityPerMaterial for the SRP batcher.
// CBUFFER_START/END are macros from Core.hlsl, included before this file.
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
    float id00 = round(_TileIDTex.Sample(sampler_TileIDTex, (tileBase + float2(0.5, 0.5)) / TILE_TEX_SIZE).r * 255.0);
    float id10 = round(_TileIDTex.Sample(sampler_TileIDTex, (tileBase + float2(1.5, 0.5)) / TILE_TEX_SIZE).r * 255.0);
    float id01 = round(_TileIDTex.Sample(sampler_TileIDTex, (tileBase + float2(0.5, 1.5)) / TILE_TEX_SIZE).r * 255.0);
    float id11 = round(_TileIDTex.Sample(sampler_TileIDTex, (tileBase + float2(1.5, 1.5)) / TILE_TEX_SIZE).r * 255.0);

    float2 detailUV = uv * _TileDetailTiling;

    float4 c00 = _TileTextures.Sample(sampler_TileTextures, float3(detailUV, id00));
    float4 c10 = _TileTextures.Sample(sampler_TileTextures, float3(detailUV, id10));
    float4 c01 = _TileTextures.Sample(sampler_TileTextures, float3(detailUV, id01));
    float4 c11 = _TileTextures.Sample(sampler_TileTextures, float3(detailUV, id11));

    float2 blend = smoothstep(0.0, 1.0, tileFrac);
    return lerp(lerp(c00, c10, blend.x), lerp(c01, c11, blend.x), blend.y);
}

#endif
