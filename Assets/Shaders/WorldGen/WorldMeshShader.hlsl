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
    float _TileBlendRadius;
    float _TileBlendSamples;
    float _ShadowOpacity;
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
            float id = round(SAMPLE_TEXTURE2D(_TileIDTex, sampler_TileIDTex, (tapTexel + 0.5) / texSize).r * 255.0);
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
            color += SAMPLE_TEXTURE2D_ARRAY(_TileTextures, sampler_TileTextures, detailUV, s) * (typeWeight[s] / totalWeight);
    }

    return color;
}

#endif
