using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class ShallowWaterSystem
    {
        public float CellSize { get; }
        public float Damping { get; set; } = 0.8f;
        public int SubSteps { get; set; } = 3;

        readonly List<ShallowWaterGrid> _grids = new();
        readonly List<GridConnection> _connections = new();
        readonly List<BreachSource> _breaches = new();
        readonly CompartmentGraph _graph;

        struct GridConnection
        {
            public int GridA, GridB;
            public int[] CellsA;
            public int[] CellsB;
            public float SillY;
            public Opening Opening;
            public bool IsVertical;
        }

        struct BreachSource
        {
            public int GridId;
            public int CellX, CellZ;
            public float BreachY;
            public Opening Opening;
        }

        public ShallowWaterSystem(CompartmentGraph graph, float cellSize = 0.25f)
        {
            _graph = graph;
            CellSize = cellSize;
        }

        public ShallowWaterGrid GetGrid(int compartmentId) => _grids[compartmentId];
        public IReadOnlyList<ShallowWaterGrid> Grids => _grids;

        public ShallowWaterGrid AddGrid(int compartmentId, float floorY, float roomHeight,
            float originX, float originZ, float lengthX, float widthZ)
        {
            var grid = new ShallowWaterGrid(compartmentId, floorY, roomHeight,
                originX, originZ, lengthX, widthZ, CellSize);

            while (_grids.Count <= compartmentId)
                _grids.Add(null);
            _grids[compartmentId] = grid;
            return grid;
        }

        public void AddConnection(Opening opening, int gridA, int gridB,
            float posX, float posZ, float openingWidth, float sillY, bool isVertical)
        {
            var gA = _grids[gridA];
            var gB = _grids[gridB];

            var cellsA = new List<int>();
            var cellsB = new List<int>();

            if (isVertical)
            {
                int cx = (int)Math.Round((posX - gA.OriginX) / gA.CellSize);
                int cz = (int)Math.Round((posZ - gA.OriginZ) / gA.CellSize);
                float halfSide = openingWidth * 0.5f;
                int halfCells = Math.Max(1, (int)Math.Ceiling(halfSide / gA.CellSize));

                int cxB = (int)Math.Round((posX - gB.OriginX) / gB.CellSize);
                int czB = (int)Math.Round((posZ - gB.OriginZ) / gB.CellSize);

                for (int dx = -halfCells; dx <= halfCells; dx++)
                {
                    for (int dz = -halfCells; dz <= halfCells; dz++)
                    {
                        int ax = Math.Clamp(cx + dx, 0, gA.ResX - 1);
                        int az = Math.Clamp(cz + dz, 0, gA.ResZ - 1);
                        int bx = Math.Clamp(cxB + dx, 0, gB.ResX - 1);
                        int bz = Math.Clamp(czB + dz, 0, gB.ResZ - 1);
                        cellsA.Add(gA.Idx(ax, az));
                        cellsB.Add(gB.Idx(bx, bz));
                    }
                }
            }
            else
            {
                float halfW = openingWidth * 0.5f;
                float zMin = posZ - halfW;
                float zMax = posZ + halfW;

                bool aIsLeft = gA.OriginX + gA.ResX * gA.CellSize <= gB.OriginX + 0.1f;

                for (float z = zMin; z < zMax; z += gA.CellSize)
                {
                    int zA = (int)Math.Round((z - gA.OriginZ) / gA.CellSize);
                    int zB = (int)Math.Round((z - gB.OriginZ) / gB.CellSize);
                    zA = Math.Clamp(zA, 0, gA.ResZ - 1);
                    zB = Math.Clamp(zB, 0, gB.ResZ - 1);

                    int xA = aIsLeft ? gA.ResX - 1 : 0;
                    int xB = aIsLeft ? 0 : gB.ResX - 1;

                    cellsA.Add(gA.Idx(xA, zA));
                    cellsB.Add(gB.Idx(xB, zB));
                }

                if (cellsA.Count == 0)
                {
                    int zA = Math.Clamp((int)Math.Round((posZ - gA.OriginZ) / gA.CellSize), 0, gA.ResZ - 1);
                    int zB = Math.Clamp((int)Math.Round((posZ - gB.OriginZ) / gB.CellSize), 0, gB.ResZ - 1);
                    int xA = aIsLeft ? gA.ResX - 1 : 0;
                    int xB = aIsLeft ? 0 : gB.ResX - 1;
                    cellsA.Add(gA.Idx(xA, zA));
                    cellsB.Add(gB.Idx(xB, zB));
                }
            }

            var arrA = cellsA.ToArray();
            var arrB = cellsB.ToArray();

            _connections.Add(new GridConnection
            {
                GridA = gridA,
                GridB = gridB,
                CellsA = arrA,
                CellsB = arrB,
                SillY = sillY,
                Opening = opening,
                IsVertical = isVertical
            });

            if (opening.Kind == OpeningKind.Hatch || opening.Kind == OpeningKind.Breach)
            {
                foreach (int idx in arrA)
                {
                    int ax = idx / gA.ResZ, az = idx % gA.ResZ;
                    gA.MarkOpeningCell(ax, az);
                }
                foreach (int idx in arrB)
                {
                    int bx = idx / gB.ResZ, bz = idx % gB.ResZ;
                    gB.MarkOpeningCell(bx, bz);
                }
            }
        }

        public void AddBreachSource(Opening opening, int gridId, float worldX, float worldZ, float breachY)
        {
            var g = _grids[gridId];
            int cx = Math.Clamp((int)Math.Round((worldX - g.OriginX) / g.CellSize), 0, g.ResX - 1);
            int cz = Math.Clamp((int)Math.Round((worldZ - g.OriginZ) / g.CellSize), 0, g.ResZ - 1);
            _breaches.Add(new BreachSource
            {
                GridId = gridId,
                CellX = cx,
                CellZ = cz,
                BreachY = breachY,
                Opening = opening
            });

            g.MarkOpeningCell(cx, cz);
        }

        public void Tick(float dt)
        {
            int steps = Math.Max(1, SubSteps);
            float subDt = dt / steps;

            for (int s = 0; s < steps; s++)
            {
                for (int i = 0; i < _grids.Count; i++)
                {
                    if (_grids[i] == null) continue;
                    _grids[i].UpdateFluxInternal(subDt, Damping);
                }

                UpdateConnectionFlux(subDt);

                for (int i = 0; i < _grids.Count; i++)
                {
                    if (_grids[i] == null) continue;
                    _grids[i].ScaleFlux(subDt);
                }

                for (int i = 0; i < _grids.Count; i++)
                {
                    if (_grids[i] == null) continue;
                    _grids[i].ApplyFlux(subDt);
                }

                ApplyBreachSources(subDt);
                EqualizeBoundary();
            }

            for (int i = 0; i < _grids.Count; i++)
            {
                if (_grids[i] == null) continue;
                _grids[i].ComputeVelocity();
            }

            SyncCompartmentVolumes();
            SyncOpeningFlow(dt);
        }

        void UpdateConnectionFlux(float dt)
        {
            const float gravity = 9.81f;
            const float doorFlowMult = 30f;
            const float hatchFlowMult = 10f;

            for (int c = 0; c < _connections.Count; c++)
            {
                var conn = _connections[c];
                if (!conn.Opening.IsOpen) continue;

                float mult = conn.Opening.Kind == OpeningKind.Door ? doorFlowMult : hatchFlowMult;

                var gA = _grids[conn.GridA];
                var gB = _grids[conn.GridB];

                bool isVertical = conn.IsVertical;
                float cellArea = gA.CellSize * gA.CellSize;

                for (int i = 0; i < conn.CellsA.Length; i++)
                {
                    int idxA = conn.CellsA[i];
                    int idxB = conn.CellsB[i];

                    float absYA = gA.FloorY + gA.H[idxA];
                    float absYB = gB.FloorY + gB.H[idxB];

                    float sill = conn.SillY;
                    if (absYA <= sill && absYB <= sill) continue;

                    float flow;

                    if (isVertical)
                    {
                        bool aIsUpper = gA.FloorY >= gB.FloorY;
                        var gUp = aIsUpper ? gA : gB;
                        var gDn = aIsUpper ? gB : gA;
                        int idxUp = aIsUpper ? idxA : idxB;
                        int idxDn = aIsUpper ? idxB : idxA;
                        float hUp = gUp.H[idxUp];
                        float hDn = gDn.H[idxDn];

                        float headDn = Math.Max(0f, gDn.FloorY + hDn - conn.SillY);
                        float headUp = hUp;
                        float netHead = headDn - headUp;

                        if (Math.Abs(netHead) < 0.005f) continue;

                        const float cd = 0.6f;
                        float rate = cd * cellArea * (float)Math.Sqrt(2f * gravity * Math.Abs(netHead));
                        float transfer = rate * dt;

                        if (netHead > 0f)
                        {
                            float maxRemove = headDn * cellArea * 0.5f;
                            float maxAccept = (gUp.RoomHeight - hUp) * cellArea;
                            transfer = Math.Min(transfer, Math.Min(maxRemove, maxAccept));
                            if (transfer > 1e-6f)
                            {
                                RemoveWater(gDn, idxDn, transfer);
                                AddWaterSpread(gUp, idxUp, transfer);
                            }
                        }
                        else
                        {
                            float maxRemove = hUp * cellArea * 0.8f;
                            float maxAccept = (gDn.RoomHeight - hDn) * cellArea;
                            transfer = Math.Min(transfer, Math.Min(maxRemove, maxAccept));
                            if (transfer > 1e-6f)
                            {
                                RemoveWater(gUp, idxUp, transfer);
                                AddWaterSpread(gDn, idxDn, transfer);
                            }
                        }
                        continue;
                    }
                    else
                    {
                        float effectiveA = Math.Max(0f, absYA - sill);
                        float effectiveB = Math.Max(0f, absYB - sill);
                        float dh = effectiveA - effectiveB;

                        if (Math.Abs(dh) < 0.005f) continue;

                        float hm = (effectiveA + effectiveB) * 0.5f;
                        flow = gravity * dh * hm * mult * dt;
                        float maxFlow = Math.Abs(dh) * 0.4f * cellArea;
                        flow = Math.Clamp(flow, -maxFlow, maxFlow);
                    }

                    if (flow > 0f)
                    {
                        float maxTransfer = gA.H[idxA] * cellArea * 0.5f;
                        flow = Math.Min(flow, maxTransfer);
                        RemoveWater(gA, idxA, flow);
                        AddWaterSpread(gB, idxB, flow);
                    }
                    else if (flow < 0f)
                    {
                        flow = -flow;
                        float maxTransfer = gB.H[idxB] * cellArea * 0.5f;
                        flow = Math.Min(flow, maxTransfer);
                        RemoveWater(gB, idxB, flow);
                        AddWaterSpread(gA, idxA, flow);
                    }
                }
            }
        }

        void ApplyBreachSources(float dt)
        {
            const float gravity = 9.81f;
            const float cd = 0.6f;

            for (int b = 0; b < _breaches.Count; b++)
            {
                var src = _breaches[b];
                if (!src.Opening.IsOpen) continue;

                var g = _grids[src.GridId];
                int idx = g.Idx(src.CellX, src.CellZ);

                float waterY = g.FloorY + g.H[idx];
                float seaY = _graph.SeaLevelY;
                float dh = seaY - Math.Max(waterY, src.BreachY);
                if (dh <= 0f) continue;

                float area = src.Opening.Area;
                float q = cd * area * (float)Math.Sqrt(2f * gravity * dh);
                float volume = q * dt;

                int spread = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(area) / g.CellSize));
                int cellCount = 0;
                for (int dx = -spread; dx <= spread; dx++)
                {
                    for (int dz = -spread; dz <= spread; dz++)
                    {
                        int nx = src.CellX + dx;
                        int nz = src.CellZ + dz;
                        if (nx >= 0 && nx < g.ResX && nz >= 0 && nz < g.ResZ)
                            cellCount++;
                    }
                }

                float perCell = volume / Math.Max(1, cellCount);
                float cellArea = g.CellSize * g.CellSize;
                for (int dx = -spread; dx <= spread; dx++)
                {
                    for (int dz = -spread; dz <= spread; dz++)
                    {
                        int nx = src.CellX + dx;
                        int nz = src.CellZ + dz;
                        if (nx >= 0 && nx < g.ResX && nz >= 0 && nz < g.ResZ)
                        {
                            int ni = g.Idx(nx, nz);
                            g.H[ni] = Math.Min(g.RoomHeight, g.H[ni] + perCell / cellArea);
                        }
                    }
                }

                src.Opening.FlowQ = q;
                src.Opening.FlowVelocity = q / Math.Max(area, 0.01f);
            }
        }

        static void RemoveWater(ShallowWaterGrid g, int idx, float volume)
        {
            float cellArea = g.CellSize * g.CellSize;
            g.H[idx] = Math.Max(0f, g.H[idx] - volume / cellArea);
        }

        static void AddWaterSpread(ShallowWaterGrid g, int idx, float volume)
        {
            float cellArea = g.CellSize * g.CellSize;
            int x = idx / g.ResZ;
            int z = idx % g.ResZ;

            const int spread = 2;
            int count = 0;
            for (int dx = -spread; dx <= spread; dx++)
            {
                for (int dz = -spread; dz <= spread; dz++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx >= 0 && nx < g.ResX && nz >= 0 && nz < g.ResZ)
                        count++;
                }
            }

            float perCell = volume / Math.Max(1, count) / cellArea;
            for (int dx = -spread; dx <= spread; dx++)
            {
                for (int dz = -spread; dz <= spread; dz++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx >= 0 && nx < g.ResX && nz >= 0 && nz < g.ResZ)
                        g.H[g.Idx(nx, nz)] = Math.Min(g.RoomHeight, g.H[g.Idx(nx, nz)] + perCell);
                }
            }
        }

        void EqualizeBoundary()
        {
            for (int c = 0; c < _connections.Count; c++)
            {
                var conn = _connections[c];
                if (!conn.Opening.IsOpen) continue;
                if (conn.IsVertical) continue;

                var gA = _grids[conn.GridA];
                var gB = _grids[conn.GridB];

                for (int i = 0; i < conn.CellsA.Length; i++)
                {
                    int idxA = conn.CellsA[i];
                    int idxB = conn.CellsB[i];

                    float absA = gA.FloorY + gA.H[idxA];
                    float absB = gB.FloorY + gB.H[idxB];
                    float avg = (absA + absB) * 0.5f;

                    gA.H[idxA] = Math.Max(0f, avg - gA.FloorY);
                    gB.H[idxB] = Math.Max(0f, avg - gB.FloorY);
                }
            }
        }

        const float DrainSnapHeight = 0.01f;
        float[] _prevVolumes;

        void SyncCompartmentVolumes()
        {
            if (_prevVolumes == null || _prevVolumes.Length < _grids.Count)
                _prevVolumes = new float[_grids.Count];

            for (int i = 0; i < _grids.Count; i++)
            {
                if (_grids[i] == null) continue;
                var grid = _grids[i];
                float vol = grid.TotalVolume();
                bool decreasing = vol < _prevVolumes[i] - 1e-6f;

                if (decreasing && grid.MaxHeight() < DrainSnapHeight)
                {
                    grid.ZeroAll();
                    vol = 0f;
                }

                _prevVolumes[i] = vol;
                var comp = _graph.GetCompartment(i);
                comp.WaterVolume = vol;
            }
        }

        void SyncOpeningFlow(float dt)
        {
            for (int c = 0; c < _connections.Count; c++)
            {
                var conn = _connections[c];
                var gA = _grids[conn.GridA];
                var gB = _grids[conn.GridB];

                float totalFlow = 0f;
                for (int i = 0; i < conn.CellsA.Length; i++)
                {
                    float absYA = gA.FloorY + gA.H[conn.CellsA[i]];
                    float absYB = gB.FloorY + gB.H[conn.CellsB[i]];
                    totalFlow += absYA - absYB;
                }

                float avgDiff = conn.CellsA.Length > 0 ? totalFlow / conn.CellsA.Length : 0f;
                float area = conn.Opening.Area;
                float q = avgDiff * area * 2f;

                conn.Opening.FlowQ = q;
                conn.Opening.FlowVelocity = Math.Abs(q) / Math.Max(area, 0.01f);
            }
        }
    }
}
