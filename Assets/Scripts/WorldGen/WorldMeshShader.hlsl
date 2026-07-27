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
    float _TileBlendRadius;
    float _TileBlendSamples;
    float _ShadowOpacity;
    float _TileBreakupStrength;
    float _TileBreakupNoiseScale;
CBUFFER_END

// Upper bound on TileType's byte range (currently 7 values, Water..Ice) — bump this if
// TileType ever grows past 16. Local HLSL arrays need a compile-time size, so this is a
// generous fixed ceiling rather than the exact live count; unused slots just stay at
// zero weight and are skipped before sampling.
#define MAX_TILE_TYPES 16

// Largest supported kernel: a 7x7 grid (ring count 3). _TileBlendSamples (1-3) picks how many
// rings of that grid actually contribute; ring 1 = the innermost 3x3 (9 taps), ring 2 = 5x5
// (25 taps), ring 3 = the full 7x7 (49 taps). Weights are a real 2D Gaussian over tap-grid
// distance (sigma = 1 tap spacing), so adding rings extends the same falloff curve further out
// rather than changing its shape — more rings just represent more of that curve's tail instead
// of leaving it truncated, which is what reduces aliasing/faceting on the transition.
#define MAX_BLEND_RING 3

// Rotates a continuous (unbounded, still-tiling) UV coordinate around the origin.
float2 RotateUV(float2 uv, float angleRad)
{
    float s, c;
    sincos(angleRad, s, c);
    return float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
}

// Cheap hash-based value noise (2D -> [0,1]). Only used as a slow-varying blend
// mask for tile breakup below, so its low quality doesn't matter.
float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float a = Hash21(i);
    float b = Hash21(i + float2(1.0, 0.0));
    float c = Hash21(i + float2(0.0, 1.0));
    float d = Hash21(i + float2(1.0, 1.0));
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Samples a single tile type's detail texture up to three times — the plain
// UV, plus two copies rotated and rescaled by irrational-ish amounts so their
// tiling grids never realign with each other or the base copy — then blends
// between them with a low-frequency noise mask. Squaring the noise weights
// sharpens the mask so one copy tends to dominate each patch (soft transitions
// at the boundaries) instead of a constant three-way blur, which is what
// actually breaks up the repeating pattern rather than just softening it.
float4 SampleTileDetail(float2 uv, float2 detailUV, int layer)
{
    float4 baseColor = _TileTextures.Sample(sampler_TileTextures, float3(detailUV, layer));
    if (_TileBreakupStrength <= 0.0) return baseColor;

    float4 colorB = _TileTextures.Sample(sampler_TileTextures, float3(RotateUV(detailUV, 1.3) * 1.71, layer));
    float4 colorC = _TileTextures.Sample(sampler_TileTextures, float3(RotateUV(detailUV, -2.1) * 1.37, layer));

    float3 n = float3(
        ValueNoise(uv * _TileBreakupNoiseScale),
        ValueNoise(uv * _TileBreakupNoiseScale + float2(19.19, 7.3)),
        ValueNoise(uv * _TileBreakupNoiseScale + float2(3.7, 41.1)));
    n *= n;
    float3 w = n / max(n.x + n.y + n.z, 1e-5);

    float4 breakup = baseColor * w.x + colorB * w.y + colorC * w.z;
    return lerp(baseColor, breakup, _TileBreakupStrength);
}

float4 SampleTerrain(float2 uv)
{
    // Read the ID texture's own resolution rather than assuming a fixed grid: uniform
    // chunks ship a 1-texel-per-tile-corner grid, chunks with multiple tile types ship a
    // supersampled + smoothed grid (see WorldMeshGenerator.BuildSmoothedTileTexture).
    uint texW, texH;
    _TileIDTex.GetDimensions(texW, texH);
    float2 gridCells = float2(texW, texH) - 1.0;
    float2 texSize   = gridCells + 1.0;
    float2 gridUV    = uv * gridCells;

    // Weighted vote over an NxN tap pattern — the same categorical-smoothing idea used at
    // world-gen time (BuildSmoothedTileTexture), just done live per-pixel so it can react to
    // _TileBlendRadius/_TileBlendSamples. This is radially symmetric, unlike a 4-corner
    // bilinear lerp, so it doesn't reintroduce a diamond-shaped artifact as the radius grows.
    float typeWeight[MAX_TILE_TYPES];
    [unroll]
    for (int i = 0; i < MAX_TILE_TYPES; i++) typeWeight[i] = 0.0;

    // _TileBlendSamples is a per-material uniform, so this comparison is the same for every
    // pixel in the draw — a coherent (non-divergent) branch, not a per-pixel one.
    int ring = clamp((int)_TileBlendSamples, 1, MAX_BLEND_RING);

    [unroll]
    for (int oy = -MAX_BLEND_RING; oy <= MAX_BLEND_RING; oy++)
    {
        [unroll]
        for (int ox = -MAX_BLEND_RING; ox <= MAX_BLEND_RING; ox++)
        {
            if (max(abs(ox), abs(oy)) > ring) continue;

            float2 offset = float2(ox, oy);
            float weight = exp(-dot(offset, offset) * 0.5);

            float2 tapTexel = clamp(gridUV + offset * _TileBlendRadius, 0.0, gridCells);
            // +0.5 shifts to texel centre — avoids precision-edge hits with Point filtering.
            float id = round(_TileIDTex.Sample(sampler_TileIDTex, (tapTexel + 0.5) / texSize).r * 255.0);
            int idx = min((int)id, MAX_TILE_TYPES - 1);
            typeWeight[idx] += weight;
        }
    }

    float totalWeight = 0.0;
    [unroll]
    for (int t = 0; t < MAX_TILE_TYPES; t++) totalWeight += typeWeight[t];

    float2 detailUV = uv * _TileDetailTiling;
    float4 color = float4(0.0, 0.0, 0.0, 0.0);

    [unroll]
    for (int s = 0; s < MAX_TILE_TYPES; s++)
    {
        if (typeWeight[s] > 0.0)
            color += SampleTileDetail(uv, detailUV, s) * (typeWeight[s] / totalWeight);
    }

    return color;
}

#endif
