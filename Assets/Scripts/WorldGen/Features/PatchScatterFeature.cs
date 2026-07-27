using System;
using System.Collections.Generic;
using UnityEngine;

// Scatters a random number of decorative ground-patch meshes (see TerrainPatchRegistry,
// looked up by PatchId) at random positions within the parent biome (or across the whole
// world if there is no parent BiomeFeature) — e.g. patches of dirt/moss/frost breaking up an
// otherwise uniform tile texture. Each patch's size is drawn uniformly from
// [PatchSizeMin, PatchSizeMax] independently, and the count is drawn once from
// [CountMin, CountMax], both via handler.Random so it stays deterministic across
// server/client, same as EntityClusterFeature.
//
// Usage — as a biome child:
//   new PatchScatterFeature
//   {
//       PatchId       = "Moss",
//       CountMin      = 8,
//       CountMax      = 14,
//       PatchSizeMin  = 3f,
//       PatchSizeMax  = 8f,
//       MinDistanceToOtherPatches = 8f,
//       AllowedTileTypes = new[] { TileType.Grass },
//   }
public class PatchScatterFeature : WorldGenFeature
{
    public readonly struct PlacedPatch
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Size;

        public PlacedPatch(float x, float y, float size)
        {
            X = x;
            Y = y;
            Size = size;
        }
    }

    public string PatchId;
    public int CountMin = 5;
    public int CountMax = 5;
    public float PatchSizeMin = 8f;
    public float PatchSizeMax = 8f;
    public TileType[] AllowedTileTypes = { TileType.Grass };

    // Forwarded to every SpawnMeshPatchAction this feature enqueues — lets several
    // PatchScatterFeatures layered over the same area control draw order via a small
    // vertical separation (see SpawnMeshPatchAction.YOffset).
    public float YOffset = 0f;

    // A candidate position is rejected (and re-rolled, up to MaxPlacementAttempts times) if
    // it's closer than this to any patch already placed by an earlier PatchScatterFeature —
    // gathered via handler.GetPreviousFeatures<PatchScatterFeature>(), so this sees patches
    // from every biome processed so far, not just this one. 0 disables the check.
    public float MinDistanceToOtherPatches = 0f;

    // A candidate tile is rejected if it's within this many tiles of the biome's own border
    // (see BorderSampleCount below) — patch meshes have physical extent, so a patch centred
    // right at a biome's edge would visibly poke into whatever neighbours it. 0 disables the
    // check (matches EntityClusterFeature's own "0 disables" convention for its distance
    // fields).
    public float MaxDistanceFromBiomeBorder = 0f;

    // Populated by Generate — later PatchScatterFeature instances (via
    // handler.GetPreviousFeatures<PatchScatterFeature>()) read this to avoid overlapping
    // already-placed patches when MinDistanceToOtherPatches is set.
    public IReadOnlyList<PlacedPatch> Patches { get; private set; } = Array.Empty<PlacedPatch>();

    // Cap on retries when a candidate position fails the minimum-distance check, so a dense
    // area can't hang generation — patches that can't find a valid spot are skipped instead.
    private const int MaxPlacementAttempts = 30;

    public override void Generate(WorldGenHandler handler)
    {
        var placed = new List<PlacedPatch>();

        if (string.IsNullOrEmpty(PatchId))
        {
            Patches = placed;
            return;
        }

        var rng = handler.Random;
        var biome = handler.GetPreviousFeature<BiomeFeature>();

        List<(ushort x, ushort y)> validTiles = GatherValidTiles(handler, biome);
        if (validTiles.Count == 0)
        {
            Patches = placed;
            return;
        }

        List<PlacedPatch> otherPatches = GatherOtherPatches(handler);

        int count = rng.Next(CountMin, CountMax + 1);
        for (int i = 0; i < count; i++)
        {
            if (!TryPickPosition(validTiles, rng, otherPatches, placed, out float x, out float y))
                continue;

            float size = (float)(rng.NextDouble() * (PatchSizeMax - PatchSizeMin) + PatchSizeMin);
            placed.Add(new PlacedPatch(x, y, size));
            handler.EnqueueAction(new SpawnMeshPatchAction { X = x, Y = y, PatchId = PatchId, Size = size, YOffset = YOffset });
        }

        Patches = placed;
    }

    private bool TryPickPosition(List<(ushort x, ushort y)> validTiles, System.Random rng,
        List<PlacedPatch> otherPatches, List<PlacedPatch> placed, out float x, out float y)
    {
        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            (ushort tx, ushort ty) = validTiles[rng.Next(validTiles.Count)];

            if (MinDistanceToOtherPatches <= 0f ||
                (!IsTooClose(tx, ty, otherPatches) && !IsTooClose(tx, ty, placed)))
            {
                x = tx;
                y = ty;
                return true;
            }
        }
        x = y = 0f;
        return false;
    }

    private bool IsTooClose(float x, float y, List<PlacedPatch> patches)
    {
        float minDist2 = MinDistanceToOtherPatches * MinDistanceToOtherPatches;
        foreach (var p in patches)
        {
            float dx = p.X - x, dy = p.Y - y;
            if (dx * dx + dy * dy < minDist2) return true;
        }
        return false;
    }

    private List<PlacedPatch> GatherOtherPatches(WorldGenHandler handler)
    {
        var result = new List<PlacedPatch>();
        foreach (var feature in handler.GetPreviousFeatures<PatchScatterFeature>())
            result.AddRange(feature.Patches);
        return result;
    }

    // Mirrors EntityClusterFeature.GatherValidTiles/IsAllowedTile exactly.
    private List<(ushort x, ushort y)> GatherValidTiles(WorldGenHandler handler, BiomeFeature biome)
    {
        var result = new List<(ushort, ushort)>();

        if (biome != null)
        {
            var noiseX = handler.GetWorldGenResource<NoiseGenerator>(biome.NoiseKeyX);
            var noiseY = handler.GetWorldGenResource<NoiseGenerator>(biome.NoiseKeyY);

            foreach ((ushort cx, ushort cy) in biome.BiomeChunks)
            {
                foreach ((ushort tx, ushort ty) in handler.GetChunk(cx, cy).IterateWorldTiles())
                {
                    if (!biome.IsInBiome(noiseX.Sample(tx, ty), noiseY.Sample(tx, ty))) continue;
                    if (!IsFarFromBiomeBorder(biome, noiseX, noiseY, tx, ty)) continue;
                    if (IsAllowedTile(handler.GetTileType(tx, ty)))
                        result.Add((tx, ty));
                }
            }
        }
        else
        {
            int total = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks;
            for (ushort tx = 0; tx < total; tx++)
                for (ushort ty = 0; ty < total; ty++)
                    if (IsAllowedTile(handler.GetTileType(tx, ty)))
                        result.Add((tx, ty));
        }

        return result;
    }

    // Biome borders come from an arbitrary noise threshold, not a tile-aligned rectangle, so
    // there's no cheap exact "distance to border" — instead this samples biome membership at
    // BorderSampleCount points on a ring of radius MaxDistanceFromBiomeBorder around the
    // candidate and rejects it if any sample lands outside the biome. Deliberately
    // conservative (a real, precise border could dip between two samples and still be
    // closer than MaxDistanceFromBiomeBorder) — that's the intended tradeoff for a "generous"
    // margin meant to stop patches bleeding into a neighbouring biome, not a tight one.
    private const int BorderSampleCount = 8;

    private bool IsFarFromBiomeBorder(BiomeFeature biome, NoiseGenerator noiseX, NoiseGenerator noiseY, float tx, float ty)
    {
        if (MaxDistanceFromBiomeBorder <= 0f) return true;

        for (int i = 0; i < BorderSampleCount; i++)
        {
            float angle = i / (float)BorderSampleCount * 2f * Mathf.PI;
            float sx = tx + Mathf.Cos(angle) * MaxDistanceFromBiomeBorder;
            float sy = ty + Mathf.Sin(angle) * MaxDistanceFromBiomeBorder;

            if (!biome.IsInBiome(noiseX.Sample(sx, sy), noiseY.Sample(sx, sy)))
                return false;
        }
        return true;
    }

    private bool IsAllowedTile(TileType type)
    {
        foreach (TileType allowed in AllowedTileTypes)
            if (type == allowed) return true;
        return false;
    }
}
