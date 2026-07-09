using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class ChunkStreamingManager : MonoBehaviour
    {
        public Material RockMaterial;
        public int LoadRadius = 2;
        public int MaxActiveChunks = 40;
        public int ChunkBuildsPerFrame = 1;

        sealed class ChunkSlot
        {
            public GameObject Root;
            public MeshFilter FloorFilter;
            public MeshRenderer FloorRenderer;
            public MeshCollider FloorCollider;
            public Mesh FloorMesh;
            public MeshFilter CeilingFilter;
            public MeshRenderer CeilingRenderer;
            public MeshCollider CeilingCollider;
            public Mesh CeilingMesh;
            public GameObject CeilingGo;
        }

        readonly Dictionary<long, ChunkSlot> _activeSlots = new();
        readonly Stack<ChunkSlot> _pool = new();
        readonly List<ChunkCoord> _loadQueue = new();

        ChunkCoord _lastCenter;
        bool _hasCenter;

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            var sub = bridge != null ? bridge.SubState : null;
            if (sub == null || bridge.Map == null) return;

            var center = ChunkCoord.FromWorld(sub.PositionX, sub.PositionZ);

            if (!_hasCenter || center != _lastCenter)
            {
                _hasCenter = true;
                _lastCenter = center;
                RebuildLoadQueue(center, bridge.Map);
            }

            int built = 0;
            while (built < ChunkBuildsPerFrame && _loadQueue.Count > 0)
            {
                var coord = _loadQueue[0];
                _loadQueue.RemoveAt(0);

                if (_activeSlots.ContainsKey(coord.Key)) continue;

                var tile = bridge.Map.GetChunk(coord);
                var slot = AcquireSlot();

                TerrainChunkMeshBuilder.FillFloorMesh(bridge.Map, tile, slot.FloorMesh);
                slot.FloorCollider.sharedMesh = null;
                slot.FloorCollider.sharedMesh = slot.FloorMesh;

                if (tile.HasCeiling)
                {
                    TerrainChunkMeshBuilder.FillCeilingMesh(bridge.Map, tile, slot.CeilingMesh);
                    slot.CeilingCollider.sharedMesh = null;
                    slot.CeilingCollider.sharedMesh = slot.CeilingMesh;
                    slot.CeilingGo.SetActive(true);
                }
                else
                {
                    slot.CeilingMesh.Clear();
                    slot.CeilingGo.SetActive(false);
                }

                // Vertices are already in world coords
                slot.Root.transform.position = Vector3.zero;
                slot.Root.SetActive(true);

                _activeSlots[coord.Key] = slot;
                built++;
            }
        }

        void RebuildLoadQueue(ChunkCoord center, ProceduralMapData map)
        {
            // Compute desired set within Chebyshev radius
            var desired = new List<(ChunkCoord Coord, int DistSq)>();
            for (int dz = -LoadRadius; dz <= LoadRadius; dz++)
            {
                for (int dx = -LoadRadius; dx <= LoadRadius; dx++)
                {
                    var coord = new ChunkCoord(center.X + dx, center.Z + dz);
                    int distSq = dx * dx + dz * dz;
                    desired.Add((coord, distSq));
                }
            }

            // Sort by distance, truncate to MaxActiveChunks
            desired.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
            if (desired.Count > MaxActiveChunks)
                desired.RemoveRange(MaxActiveChunks, desired.Count - MaxActiveChunks);

            var desiredKeys = new HashSet<long>();
            foreach (var d in desired)
                desiredKeys.Add(d.Coord.Key);

            // Unload active slots not in desired set
            var toRemove = new List<long>();
            foreach (var kvp in _activeSlots)
            {
                if (!desiredKeys.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var key in toRemove)
            {
                var slot = _activeSlots[key];
                slot.Root.SetActive(false);
                _pool.Push(slot);
                _activeSlots.Remove(key);
            }

            // Build load queue = desired minus already active, nearest first
            _loadQueue.Clear();
            foreach (var d in desired)
            {
                if (!_activeSlots.ContainsKey(d.Coord.Key))
                    _loadQueue.Add(d.Coord);
            }
        }

        ChunkSlot AcquireSlot()
        {
            if (_pool.Count > 0)
                return _pool.Pop();

            var slot = new ChunkSlot();
            slot.Root = new GameObject("TerrainChunkSlot");
            slot.Root.transform.SetParent(transform, false);

            slot.FloorMesh = new Mesh { name = "ChunkFloor" };
            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(slot.Root.transform, false);
            slot.FloorFilter = floorGo.AddComponent<MeshFilter>();
            slot.FloorFilter.sharedMesh = slot.FloorMesh;
            slot.FloorRenderer = floorGo.AddComponent<MeshRenderer>();
            slot.FloorRenderer.sharedMaterial = RockMaterial;
            slot.FloorCollider = floorGo.AddComponent<MeshCollider>();

            slot.CeilingMesh = new Mesh { name = "ChunkCeiling" };
            slot.CeilingGo = new GameObject("Ceiling");
            slot.CeilingGo.transform.SetParent(slot.Root.transform, false);
            slot.CeilingFilter = slot.CeilingGo.AddComponent<MeshFilter>();
            slot.CeilingFilter.sharedMesh = slot.CeilingMesh;
            slot.CeilingRenderer = slot.CeilingGo.AddComponent<MeshRenderer>();
            slot.CeilingRenderer.sharedMaterial = RockMaterial;
            slot.CeilingCollider = slot.CeilingGo.AddComponent<MeshCollider>();

            return slot;
        }
    }
}
