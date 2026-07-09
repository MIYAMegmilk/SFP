using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class ProceduralMapData
    {
        public readonly int Seed;
        public float SpawnX { get; }
        public float SpawnZ { get; }
        public int MaxCachedChunks = 256;

        readonly Dictionary<long, LinkedListNode<TerrainChunk>> _chunkCache = new();
        readonly LinkedList<TerrainChunk> _chunkLru = new();

        readonly Dictionary<long, List<(float X, float Z, float Depth)>> _mineCache = new();
        const int MaxCachedMines = 1024;

        public ProceduralMapData(int seed)
        {
            Seed = seed;
            var spawn = MapGenerator.GetSpawnPoint(seed);
            SpawnX = spawn.X;
            SpawnZ = spawn.Z;
        }

        public TerrainChunk GetChunk(ChunkCoord coord)
        {
            long key = coord.Key;

            if (_chunkCache.TryGetValue(key, out var node))
            {
                _chunkLru.Remove(node);
                _chunkLru.AddFirst(node);
                return node.Value;
            }

            var chunk = MapGenerator.GenerateChunk(Seed, coord);
            var newNode = _chunkLru.AddFirst(chunk);
            _chunkCache[key] = newNode;

            while (_chunkCache.Count > MaxCachedChunks)
            {
                var last = _chunkLru.Last;
                _chunkLru.RemoveLast();
                _chunkCache.Remove(last.Value.Coord.Key);
            }

            return chunk;
        }

        public float GetFloorDepthAt(float x, float z)
        {
            var coord = ChunkCoord.FromWorld(x, z);
            var chunk = GetChunk(coord);

            float localX = (x - coord.OriginX) / MapConstants.CellSize;
            float localZ = (z - coord.OriginZ) / MapConstants.CellSize;

            int maxCell = MapConstants.ChunkCells;
            if (localX < 0f) localX = 0f;
            else if (localX > maxCell) localX = maxCell;
            if (localZ < 0f) localZ = 0f;
            else if (localZ > maxCell) localZ = maxCell;

            int x0 = (int)localX;
            int z0 = (int)localZ;
            if (x0 >= maxCell) x0 = maxCell - 1;
            if (z0 >= maxCell) z0 = maxCell - 1;
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            float tx = localX - x0;
            float tz = localZ - z0;

            int vps = TerrainChunk.VertsPerSide;
            float d00 = chunk.FloorDepth[z0 * vps + x0];
            float d10 = chunk.FloorDepth[z0 * vps + x1];
            float d01 = chunk.FloorDepth[z1 * vps + x0];
            float d11 = chunk.FloorDepth[z1 * vps + x1];

            float dx0 = d00 + (d10 - d00) * tx;
            float dx1 = d01 + (d11 - d01) * tx;
            return dx0 + (dx1 - dx0) * tz;
        }

        public float GetCeilingDepthAt(float x, float z)
        {
            var coord = ChunkCoord.FromWorld(x, z);
            var chunk = GetChunk(coord);

            float localX = (x - coord.OriginX) / MapConstants.CellSize;
            float localZ = (z - coord.OriginZ) / MapConstants.CellSize;

            int maxCell = MapConstants.ChunkCells;
            if (localX < 0f) localX = 0f;
            else if (localX > maxCell) localX = maxCell;
            if (localZ < 0f) localZ = 0f;
            else if (localZ > maxCell) localZ = maxCell;

            int x0 = (int)localX;
            int z0 = (int)localZ;
            if (x0 >= maxCell) x0 = maxCell - 1;
            if (z0 >= maxCell) z0 = maxCell - 1;
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            float tx = localX - x0;
            float tz = localZ - z0;

            int vps = TerrainChunk.VertsPerSide;
            float d00 = chunk.CeilingDepth[z0 * vps + x0];
            float d10 = chunk.CeilingDepth[z0 * vps + x1];
            float d01 = chunk.CeilingDepth[z1 * vps + x0];
            float d11 = chunk.CeilingDepth[z1 * vps + x1];

            float dx0 = d00 + (d10 - d00) * tx;
            float dx1 = d01 + (d11 - d01) * tx;
            return dx0 + (dx1 - dx0) * tz;
        }

        public void GetMinesForChunk(ChunkCoord coord, List<(float X, float Z, float Depth)> results)
        {
            long key = coord.Key;

            if (_mineCache.TryGetValue(key, out var cached))
            {
                results.Clear();
                results.AddRange(cached);
                return;
            }

            MapGenerator.GenerateMinesForChunk(Seed, coord, this, results);

            var copy = new List<(float X, float Z, float Depth)>(results);
            _mineCache[key] = copy;

            if (_mineCache.Count > MaxCachedMines)
            {
                // Evict oldest half by clearing and rebuilding
                _mineCache.Clear();
                _mineCache[key] = copy;
            }
        }

        public (float X, float Z) GetChannelNode(int sx, int sz)
        {
            return ChannelNetwork.GetNode(sx, sz, Seed);
        }

        public void GetNearbyChannelNodes(float x, float z, List<(float X, float Z)> results)
        {
            ChannelNetwork.GetNearbyNodes(x, z, Seed, results);
        }
    }
}
