using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class ShipNavGraph
    {
        public sealed class RoomInfo
        {
            public int Id;
            public float MinX, MaxX, MinZ, MaxZ;
            public float FloorY, CeilY;
            public float CenterX => (MinX + MaxX) * 0.5f;
            public float CenterZ => (MinZ + MaxZ) * 0.5f;
        }

        public sealed class PortalInfo
        {
            public int OpeningId;
            public float X, Y, Z;
        }

        readonly CompartmentGraph _graph;
        readonly Dictionary<int, RoomInfo> _rooms = new();
        readonly Dictionary<int, PortalInfo> _portals = new();

        const float ClosedDoorCost = 0.5f;
        const float WaterCostWeight = 6f;
        const float FireCostWeight = 10f;
        const float DangerousDoorWaterDelta = 0.4f;
        const float DefaultHopDistance = 6f;

        public ShipNavGraph(CompartmentGraph graph)
        {
            _graph = graph;
        }

        public void RegisterRoom(int compartmentId, float minX, float maxX,
                                 float minZ, float maxZ, float floorY, float ceilY)
        {
            _rooms[compartmentId] = new RoomInfo
            {
                Id = compartmentId,
                MinX = minX, MaxX = maxX,
                MinZ = minZ, MaxZ = maxZ,
                FloorY = floorY, CeilY = ceilY,
            };
        }

        public void RegisterPortal(int openingId, float x, float y, float z)
        {
            _portals[openingId] = new PortalInfo { OpeningId = openingId, X = x, Y = y, Z = z };
        }

        public bool TryGetPortal(int openingId, out PortalInfo p)
        {
            return _portals.TryGetValue(openingId, out p);
        }

        public RoomInfo GetRoom(int compartmentId)
        {
            return _rooms.TryGetValue(compartmentId, out var r) ? r : null;
        }

        public int FindCompartmentAt(float x, float y, float z)
        {
            foreach (var kvp in _rooms)
            {
                var r = kvp.Value;
                if (x >= r.MinX && x <= r.MaxX &&
                    y >= r.FloorY && y < r.CeilY &&
                    z >= r.MinZ && z <= r.MaxZ)
                    return r.Id;
            }
            return -1;
        }

        public bool FindPath(int fromCompartment, int toCompartment,
                             FireSystem fire, List<int> resultCompartments, List<int> resultOpenings)
        {
            resultCompartments.Clear();
            resultOpenings.Clear();

            if (fromCompartment == toCompartment)
            {
                resultCompartments.Add(fromCompartment);
                return true;
            }

            int n = _graph.Compartments.Count;
            if (n == 0) return false;

            var dist = new float[n];
            var prevComp = new int[n];
            var prevOpening = new int[n];
            var visited = new bool[n];

            for (int i = 0; i < n; i++)
            {
                dist[i] = float.MaxValue;
                prevComp[i] = -1;
                prevOpening[i] = -1;
            }

            if (fromCompartment < 0 || fromCompartment >= n) return false;
            if (toCompartment < 0 || toCompartment >= n) return false;
            dist[fromCompartment] = 0f;

            for (int step = 0; step < n; step++)
            {
                int u = -1;
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (!visited[i] && dist[i] < best)
                    {
                        best = dist[i];
                        u = i;
                    }
                }
                if (u < 0) break;
                if (u == toCompartment) break;
                visited[u] = true;

                var openings = _graph.Openings;
                for (int oi = 0; oi < openings.Count; oi++)
                {
                    var o = openings[oi];
                    if (o.Kind == OpeningKind.Breach) continue;

                    int neighbor;
                    if (o.CompartmentA == u && o.CompartmentB >= 0) neighbor = o.CompartmentB;
                    else if (o.CompartmentB == u && o.CompartmentA >= 0) neighbor = o.CompartmentA;
                    else continue;

                    if (visited[neighbor]) continue;

                    float cost = EdgeCost(o, u, neighbor, fire);
                    if (cost >= float.MaxValue) continue;

                    float newDist = dist[u] + cost;
                    if (newDist < dist[neighbor])
                    {
                        dist[neighbor] = newDist;
                        prevComp[neighbor] = u;
                        prevOpening[neighbor] = o.Id;
                    }
                }
            }

            if (dist[toCompartment] >= float.MaxValue) return false;

            var path = new List<int>();
            var portals = new List<int>();
            int cur = toCompartment;
            while (cur != fromCompartment)
            {
                path.Add(cur);
                portals.Add(prevOpening[cur]);
                cur = prevComp[cur];
            }
            path.Add(fromCompartment);
            path.Reverse();
            portals.Reverse();

            resultCompartments.AddRange(path);
            resultOpenings.AddRange(portals);
            return true;
        }

        public float PathCost(int fromCompartment, int toCompartment, FireSystem fire)
        {
            if (fromCompartment == toCompartment) return 0f;
            var rc = new List<int>();
            var ro = new List<int>();
            if (!FindPath(fromCompartment, toCompartment, fire, rc, ro))
                return float.MaxValue;

            float total = 0f;
            for (int i = 0; i < ro.Count; i++)
            {
                var o = _graph.Openings[ro[i]];
                int from = rc[i];
                int to = rc[i + 1];
                total += EdgeCost(o, from, to, fire);
            }
            return total;
        }

        float EdgeCost(Opening o, int from, int to, FireSystem fire)
        {
            var compFrom = _graph.GetCompartment(from);
            var compTo = _graph.GetCompartment(to);

            float wfFrom = compFrom.WaterFraction;
            float wfTo = compTo.WaterFraction;

            if (!o.IsOpen && System.Math.Abs(wfFrom - wfTo) > DangerousDoorWaterDelta)
                return float.MaxValue;

            float baseCost = DefaultHopDistance;
            if (_portals.TryGetValue(o.Id, out var portal))
            {
                var roomFrom = GetRoom(from);
                var roomTo = GetRoom(to);
                if (roomFrom != null && roomTo != null)
                {
                    float dx = roomTo.CenterX - roomFrom.CenterX;
                    float dy = roomTo.FloorY - roomFrom.FloorY;
                    float dz = roomTo.CenterZ - roomFrom.CenterZ;
                    float d = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d > 0.1f) baseCost = d;
                }
            }

            float cost = baseCost;
            if (!o.IsOpen) cost += ClosedDoorCost;
            cost += WaterCostWeight * wfTo;
            if (fire != null) cost += FireCostWeight * fire.GetFireIntensity(to);

            return cost;
        }
    }
}
