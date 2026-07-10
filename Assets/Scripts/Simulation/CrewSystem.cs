using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class CrewSystem
    {
        readonly CompartmentGraph _graph;
        readonly ShipNavGraph _nav;
        readonly int _seed;
        readonly List<CrewMemberState> _crew = new();
        readonly Queue<CrewEvent> _events = new();
        readonly List<PumpRef> _pumps = new();
        readonly List<StationRef> _reactorStations = new();
        readonly List<TaskCandidate> _candidates = new();
        readonly Dictionary<long, int> _claims = new();
        int _tickCounter;

        const float WalkSpeed = 3f;
        const float SwimSpeed = 2f;
        const float ClimbSpeed = 1.5f;
        const float WaypointTolerance = 0.3f;
        const float WorkRange = 1.5f;
        const float DeviceRange = 1.2f;
        const float DoorOpenRange = 1.5f;
        const float DoorOperateTime = 0.7f;
        const float RescoreInterval = 0.5f;
        const float HysteresisFactor = 1.2f;
        const float HysteresisBias = 5f;
        const float DistanceWeight = 2f;
        const float ExtinguishRate = 0.8f;
        const float ExtinguisherRechargeRate = 5f;
        const float CrewPatchRate = 0.35f;
        const float CrewSealRate = 0.015f;
        const float UnderwaterWorkScale = 0.5f;
        const float MaxOxygenSeconds = 30f;
        const float OxygenRecoveryRate = 10f;
        const float AsphyxiaDamageRate = 4f;
        const float HealthRegenRate = 0.5f;
        const float ReactorAdjustRate = 25f;
        const float ReactorStableHold = 8f;
        const float FleeHealthThreshold = 40f;
        const float AbortOxygenThreshold = 10f;
        const float EyeHeight = 1.6f;
        const float TaskCooldownSeconds = 5f;
        const int TaskCooldownTicks = 150;
        const float SeparationDist = 0.8f;
        const float SeparationStrength = 0.3f;

        struct PumpRef
        {
            public BilgePumpState Pump;
            public float X, Y, Z;
        }

        struct StationRef
        {
            public int ReactorIndex;
            public int CompartmentId;
            public float X, Y, Z;
        }

        struct TaskCandidate
        {
            public CrewTaskKind Kind;
            public int TargetId;
            public int CompartmentId;
            public float Urgency;
            public int MaxWorkers;
        }

        public CrewSystem(CompartmentGraph graph, ShipNavGraph nav, int seed)
        {
            _graph = graph;
            _nav = nav;
            _seed = seed;
        }

        public IReadOnlyList<CrewMemberState> Crew => _crew;
        public ShipNavGraph Nav => _nav;

        public CrewMemberState SpawnCrew(float x, float y, float z, CrewJobKind job = CrewJobKind.Captain)
        {
            var c = new CrewMemberState
            {
                Id = _crew.Count,
                Job = job,
                X = x, Y = y, Z = z,
                RescoreTimer = _crew.Count * 0.1f,
            };
            c.CompartmentId = _nav.FindCompartmentAt(x, y, z);
            _crew.Add(c);
            return c;
        }

        public void RegisterPump(BilgePumpState pump, float x, float y, float z)
        {
            _pumps.Add(new PumpRef { Pump = pump, X = x, Y = y, Z = z });
        }

        public void RegisterReactorStation(int reactorIndex, int compartmentId, float x, float y, float z)
        {
            _reactorStations.Add(new StationRef
            {
                ReactorIndex = reactorIndex,
                CompartmentId = compartmentId,
                X = x, Y = y, Z = z,
            });
        }

        public bool TryDequeueEvent(out CrewEvent evt)
        {
            if (_events.Count > 0) { evt = _events.Dequeue(); return true; }
            evt = default;
            return false;
        }

        public void IssueOrder(int crewId, CrewOrderKind kind, int targetId, float x, float y, float z)
        {
            if (crewId < 0 || crewId >= _crew.Count) return;
            var c = _crew[crewId];
            if (c.IsDead) return;
            c.Order = kind;
            c.OrderTargetId = targetId;
            c.OrderX = x; c.OrderY = y; c.OrderZ = z;
            c.RescoreTimer = 0f;
        }

        public void CancelOrder(int crewId)
        {
            if (crewId < 0 || crewId >= _crew.Count) return;
            var c = _crew[crewId];
            c.Order = CrewOrderKind.None;
            c.OrderTargetId = -1;
            c.RescoreTimer = 0f;
            if (c.Task == CrewTaskKind.MoveTo)
                EndTask(c, null);
        }

        // ----------------------------------------------------------------
        // Main tick
        // ----------------------------------------------------------------

        public void Tick(float dt, DamageSystem damage, FireSystem fire,
                         AtmosphereSystem atmosphere, TemperatureSystem temperature,
                         PowerGrid power)
        {
            _tickCounter++;
            BuildCandidates(damage, fire, power);

            for (int i = 0; i < _crew.Count; i++)
            {
                var c = _crew[i];
                if (c.IsDead) continue;

                UpdateCompartment(c);
                TickSurvival(c, dt, fire, atmosphere, temperature);
                if (c.IsDead)
                {
                    OnCrewDied(c, damage);
                    continue;
                }

                CheckEmergency(c, fire);

                c.RescoreTimer -= dt;
                if (c.RescoreTimer <= 0f)
                {
                    SelectTask(c, fire);
                    c.RescoreTimer = RescoreInterval;
                }

                if (!ValidateTask(c, damage, fire, power))
                    EndTask(c, damage);

                Navigate(c, dt, damage, fire);

                if (IsAtWorkPosition(c))
                    ExecuteWork(c, dt, damage, fire, power);
            }

            ApplySeparation();
        }

        // ----------------------------------------------------------------
        // Compartment detection
        // ----------------------------------------------------------------

        void UpdateCompartment(CrewMemberState c)
        {
            int id = _nav.FindCompartmentAt(c.X, c.Y, c.Z);
            if (id >= 0) c.CompartmentId = id;
        }

        // ----------------------------------------------------------------
        // Collision avoidance
        // ----------------------------------------------------------------

        void ApplySeparation()
        {
            for (int i = 0; i < _crew.Count; i++)
            {
                var a = _crew[i];
                if (a.IsDead) continue;

                for (int j = i + 1; j < _crew.Count; j++)
                {
                    var b = _crew[j];
                    if (b.IsDead) continue;
                    if (Math.Abs(a.Y - b.Y) > 2f) continue;

                    float dx = b.X - a.X;
                    float dz = b.Z - a.Z;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);

                    if (dist >= SeparationDist) continue;

                    if (dist > 0.001f)
                    {
                        float overlap = (SeparationDist - dist) * SeparationStrength;
                        float nx = dx / dist;
                        float nz = dz / dist;
                        a.X -= nx * overlap;
                        a.Z -= nz * overlap;
                        b.X += nx * overlap;
                        b.Z += nz * overlap;
                    }
                    else
                    {
                        float hash = SimHash.HashToFloat(a.Id, b.Id, _seed + 2003);
                        float angle = hash * 6.2832f;
                        float push = SeparationDist * 0.5f;
                        a.X -= (float)Math.Cos(angle) * push;
                        a.Z -= (float)Math.Sin(angle) * push;
                        b.X += (float)Math.Cos(angle) * push;
                        b.Z += (float)Math.Sin(angle) * push;
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // Survival
        // ----------------------------------------------------------------

        void TickSurvival(CrewMemberState c, float dt, FireSystem fire,
                          AtmosphereSystem atmosphere, TemperatureSystem temperature)
        {
            if (c.CompartmentId < 0) return;
            var comp = _graph.GetCompartment(c.CompartmentId);

            bool headSubmerged = (c.Y + EyeHeight) <= comp.WaterLevelY;

            if (headSubmerged)
            {
                c.Oxygen -= dt;
            }
            else
            {
                float o2 = atmosphere?.GetOxygenLevel(c.CompartmentId) ?? 1f;
                float co2 = atmosphere?.GetCo2Level(c.CompartmentId) ?? 0f;
                float pressure = comp.AirPressureAtm;

                if (o2 < 0.15f)
                {
                    c.Oxygen -= (1f - o2 * 5f) * dt;
                }
                else
                {
                    float recovery = OxygenRecoveryRate * o2;
                    if (pressure > 2f) recovery *= 0.5f;
                    if (co2 > 0.05f) recovery *= 0.5f;
                    c.Oxygen += recovery * dt;
                    if (c.Oxygen > MaxOxygenSeconds) c.Oxygen = MaxOxygenSeconds;
                }

                if (pressure > 4f) c.Oxygen -= (pressure - 4f) * 0.5f * dt;
                if (pressure > 6f) c.Oxygen -= (2f + (pressure - 6f)) * dt;
                if (co2 > 0.10f) c.Oxygen -= dt;
                if (co2 > 0.15f) c.Oxygen -= 4f * dt;

                // crew breathe
                atmosphere?.AddOxygen(c.CompartmentId, -0.002f * dt);
                if (atmosphere != null)
                {
                    float airVol = comp.AirVolume;
                    if (airVol > 0f)
                    {
                        float co2Add = atmosphere.Co2ProductionRate * atmosphere.Co2GameplayScale / airVol * dt;
                        atmosphere.AddCo2(c.CompartmentId, co2Add);
                    }
                }
            }

            if (fire != null)
            {
                float heat = fire.GetHeatDamageRate(c.CompartmentId);
                if (heat > 0f) c.Health -= heat * dt;
            }

            if (temperature != null)
            {
                float tempK = temperature.GetTemperatureK(c.CompartmentId);
                if (tempK > 318f) c.Health -= (tempK - 318f) / 15f * dt;
            }

            if (c.Oxygen <= 0f)
            {
                c.Oxygen = 0f;
                c.Health -= AsphyxiaDamageRate * dt;
            }

            // regen when safe
            if (c.Health < 100f && c.Oxygen > 10f)
            {
                bool safe = true;
                if (fire != null && fire.GetFireIntensity(c.CompartmentId) > 0.05f) safe = false;
                if (comp.WaterFraction > 0.3f) safe = false;
                if (safe) c.Health += HealthRegenRate * dt;
                if (c.Health > 100f) c.Health = 100f;
            }

            if (c.Health <= 0f)
            {
                c.Health = 0f;
                c.IsDead = true;
            }
        }

        void OnCrewDied(CrewMemberState c, DamageSystem damage)
        {
            if (c.RepairingOpeningId >= 0)
            {
                damage?.SetRepairing(c.RepairingOpeningId, false);
                c.RepairingOpeningId = -1;
            }
            ReleaseClaim(c);
            _events.Enqueue(new CrewEvent { Kind = CrewEventKind.CrewDied, CrewId = c.Id, TargetId = -1 });
        }

        // ----------------------------------------------------------------
        // Emergency check
        // ----------------------------------------------------------------

        void CheckEmergency(CrewMemberState c, FireSystem fire)
        {
            if (c.CompartmentId < 0) return;
            var comp = _graph.GetCompartment(c.CompartmentId);

            bool shouldFlee = false;

            if (fire != null && fire.GetFireIntensity(c.CompartmentId) > 0.5f
                && c.Task != CrewTaskKind.FightFire)
                shouldFlee = true;

            if (comp.WaterFraction > 0.6f && c.Task != CrewTaskKind.RepairBreach
                && c.Task != CrewTaskKind.OperatePump)
                shouldFlee = true;

            if (c.Oxygen < AbortOxygenThreshold && (c.Y + EyeHeight) < comp.WaterLevelY)
                shouldFlee = true;

            if (c.Health < FleeHealthThreshold
                && (fire != null && fire.GetFireIntensity(c.CompartmentId) > 0.1f
                    || comp.WaterFraction > 0.3f))
                shouldFlee = true;

            if (shouldFlee && c.Task != CrewTaskKind.Flee)
            {
                EndTask(c, null);
                c.Task = CrewTaskKind.Flee;
                c.TaskScore = 2000f;
                c.TaskTargetId = FindSafeRoom(c, fire);
            }
        }

        int FindSafeRoom(CrewMemberState c, FireSystem fire)
        {
            int best = c.CompartmentId;
            float bestCost = float.MaxValue;
            for (int i = 0; i < _graph.Compartments.Count; i++)
            {
                var comp = _graph.Compartments[i];
                if (fire != null && fire.GetFireIntensity(comp.Id) >= 0.1f) continue;
                if (comp.WaterFraction >= 0.3f) continue;
                float cost = _nav.PathCost(c.CompartmentId, comp.Id, fire);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = comp.Id;
                }
            }
            return best;
        }

        // ----------------------------------------------------------------
        // Task candidate building
        // ----------------------------------------------------------------

        void BuildCandidates(DamageSystem damage, FireSystem fire, PowerGrid power)
        {
            _candidates.Clear();

            // Fire tasks
            if (fire != null)
            {
                for (int i = 0; i < _graph.Compartments.Count; i++)
                {
                    var comp = _graph.Compartments[i];
                    float intensity = fire.GetFireIntensity(comp.Id);
                    if (intensity <= 0.05f) continue;
                    _candidates.Add(new TaskCandidate
                    {
                        Kind = CrewTaskKind.FightFire,
                        TargetId = comp.Id,
                        CompartmentId = comp.Id,
                        Urgency = 60f + 40f * intensity,
                        MaxWorkers = intensity > 0.6f ? 2 : 1,
                    });
                }
            }

            // Breach repair tasks
            if (damage != null)
            {
                var openings = _graph.Openings;
                for (int i = 0; i < openings.Count; i++)
                {
                    var o = openings[i];
                    if (o.Kind != OpeningKind.Breach) continue;
                    if (o.Area <= 0.005f) continue;
                    if (o.CompartmentA != Opening.Sea && o.CompartmentB != Opening.Sea) continue;

                    int roomId = o.CompartmentA == Opening.Sea ? o.CompartmentB : o.CompartmentA;
                    if (roomId < 0) continue;

                    var comp = _graph.GetCompartment(roomId);
                    float areaFactor = o.Area / 0.15f;
                    if (areaFactor > 1f) areaFactor = 1f;
                    float urgency = 45f + 35f * areaFactor + 15f * comp.WaterFraction;

                    var stage = damage.GetRepairStage(o.Id);
                    if (stage == BreachRepairStage.Patched) urgency *= 0.6f;

                    _candidates.Add(new TaskCandidate
                    {
                        Kind = CrewTaskKind.RepairBreach,
                        TargetId = o.Id,
                        CompartmentId = roomId,
                        Urgency = urgency,
                        MaxWorkers = 1,
                    });
                }
            }

            // Pump tasks
            for (int i = 0; i < _pumps.Count; i++)
            {
                var pr = _pumps[i];
                if (pr.Pump.IsActive || !pr.Pump.IsFunctional) continue;
                if (pr.Pump.CompartmentId < 0) continue;
                var comp = _graph.GetCompartment(pr.Pump.CompartmentId);
                if (comp.WaterFraction <= 0.03f) continue;
                _candidates.Add(new TaskCandidate
                {
                    Kind = CrewTaskKind.OperatePump,
                    TargetId = i,
                    CompartmentId = pr.Pump.CompartmentId,
                    Urgency = 30f + 40f * comp.WaterFraction,
                    MaxWorkers = 1,
                });
            }

            // Reactor tasks
            if (power != null)
            {
                for (int i = 0; i < _reactorStations.Count; i++)
                {
                    var st = _reactorStations[i];
                    if (st.ReactorIndex >= power.Reactors.Count) continue;
                    var reactor = power.Reactors[st.ReactorIndex];
                    if (reactor.HasExploded || reactor.FuelRemaining <= 0f || reactor.Condition <= 0f)
                        continue;

                    bool needsAttention = reactor.IsMeltingDown
                        || reactor.Temperature > reactor.OptimalTemperature * 1.4f
                        || power.GridVoltage < 0.8f;
                    if (!needsAttention) continue;

                    float urgency = reactor.IsMeltingDown ? 95f : 40f;
                    int compId = reactor.CompartmentId >= 0 ? reactor.CompartmentId : 0;

                    _candidates.Add(new TaskCandidate
                    {
                        Kind = CrewTaskKind.OperateReactor,
                        TargetId = i,
                        CompartmentId = compId,
                        Urgency = urgency,
                        MaxWorkers = 1,
                    });
                }
            }
        }

        // ----------------------------------------------------------------
        // Task selection
        // ----------------------------------------------------------------

        void SelectTask(CrewMemberState c, FireSystem fire)
        {
            // player order pins task
            if (c.Order != CrewOrderKind.None)
            {
                var mapped = MapOrderToTask(c.Order);
                if (c.Task != mapped || c.TaskTargetId != c.OrderTargetId)
                {
                    c.Task = mapped;
                    c.TaskTargetId = c.OrderTargetId;
                    c.TaskScore = 1000f;
                    ClearPath(c);
                }
                return;
            }

            float bestScore = -1f;
            int bestIdx = -1;

            for (int i = 0; i < _candidates.Count; i++)
            {
                var cand = _candidates[i];

                long cooldownKey = CooldownKey(c.Id, cand.Kind, cand.TargetId);
                if (IsCooldown(cooldownKey)) continue;

                int currentClaim = GetClaimCount(cand.Kind, cand.TargetId);
                bool isMine = c.Task == cand.Kind && c.TaskTargetId == cand.TargetId;
                if (!isMine && currentClaim >= cand.MaxWorkers) continue;

                float pathCost = _nav.PathCost(c.CompartmentId, cand.CompartmentId, fire);
                if (pathCost >= float.MaxValue) continue;

                float proficiency = CrewJob.GetProficiency(c.Job, cand.Kind);
                float score = cand.Urgency * proficiency - DistanceWeight * pathCost;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
            {
                var cand = _candidates[bestIdx];
                bool shouldSwitch = c.Task == CrewTaskKind.None || c.Task == CrewTaskKind.Idle
                    || bestScore > c.TaskScore * HysteresisFactor + HysteresisBias;

                if (shouldSwitch && !(c.Task == cand.Kind && c.TaskTargetId == cand.TargetId))
                {
                    ReleaseClaim(c);
                    c.Task = cand.Kind;
                    c.TaskTargetId = cand.TargetId;
                    c.TaskScore = bestScore;
                    c.StableTimer = 0f;
                    AddClaim(c);
                    ClearPath(c);
                }
            }
            else if (c.Task == CrewTaskKind.None || c.Task == CrewTaskKind.Flee)
            {
                c.Task = CrewTaskKind.Idle;
                c.TaskTargetId = c.CompartmentId;
                c.TaskScore = 1f;
            }
        }

        static CrewTaskKind MapOrderToTask(CrewOrderKind order)
        {
            switch (order)
            {
                case CrewOrderKind.MoveTo: return CrewTaskKind.MoveTo;
                case CrewOrderKind.RepairBreach: return CrewTaskKind.RepairBreach;
                case CrewOrderKind.FightFire: return CrewTaskKind.FightFire;
                case CrewOrderKind.OperatePump: return CrewTaskKind.OperatePump;
                case CrewOrderKind.OperateReactor: return CrewTaskKind.OperateReactor;
                default: return CrewTaskKind.Idle;
            }
        }

        // ----------------------------------------------------------------
        // Task validation
        // ----------------------------------------------------------------

        bool ValidateTask(CrewMemberState c, DamageSystem damage, FireSystem fire, PowerGrid power)
        {
            switch (c.Task)
            {
                case CrewTaskKind.FightFire:
                    return fire != null && fire.GetFireIntensity(c.TaskTargetId) > 0.01f;

                case CrewTaskKind.RepairBreach:
                    if (c.TaskTargetId < 0 || c.TaskTargetId >= _graph.Openings.Count) return false;
                    var o = _graph.Openings[c.TaskTargetId];
                    return o.Kind == OpeningKind.Breach && o.Area > 0.005f;

                case CrewTaskKind.OperatePump:
                    if (c.TaskTargetId < 0 || c.TaskTargetId >= _pumps.Count) return false;
                    var pr = _pumps[c.TaskTargetId];
                    return !pr.Pump.IsActive && pr.Pump.IsFunctional
                        && _graph.GetCompartment(pr.Pump.CompartmentId).WaterFraction > 0.02f;

                case CrewTaskKind.OperateReactor:
                    if (c.TaskTargetId < 0 || c.TaskTargetId >= _reactorStations.Count) return false;
                    var st = _reactorStations[c.TaskTargetId];
                    if (st.ReactorIndex >= (power?.Reactors.Count ?? 0)) return false;
                    var reactor = power.Reactors[st.ReactorIndex];
                    return !reactor.HasExploded && reactor.FuelRemaining > 0f && reactor.Condition > 0f;

                case CrewTaskKind.Flee:
                    return c.CompartmentId != c.TaskTargetId;

                case CrewTaskKind.MoveTo:
                    return true;

                case CrewTaskKind.Idle:
                    return true;

                default:
                    return false;
            }
        }

        // ----------------------------------------------------------------
        // Navigation
        // ----------------------------------------------------------------

        void Navigate(CrewMemberState c, float dt, DamageSystem damage, FireSystem fire)
        {
            int targetCompartment = GetTargetCompartment(c);

            if (c.Task == CrewTaskKind.Idle)
            {
                NavigateIdle(c, dt);
                return;
            }

            // need to path to target
            if (targetCompartment >= 0 && c.CompartmentId != targetCompartment
                && c.PathCompartments.Count == 0)
            {
                if (!_nav.FindPath(c.CompartmentId, targetCompartment, fire,
                                   c.PathCompartments, c.PathOpenings))
                {
                    SetCooldown(CooldownKey(c.Id, c.Task, c.TaskTargetId));
                    EndTask(c, damage);
                    return;
                }
                c.PathIndex = 0;
                SetNextWaypoint(c);
            }

            if (c.HasWaypoint)
                MoveToward(c, dt);

            // advance path
            if (c.HasWaypoint && DistXZ(c.X, c.Z, c.WaypointX, c.WaypointZ) < WaypointTolerance
                && c.MoveMode != CrewMoveMode.Climbing)
            {
                c.PathIndex++;
                if (c.PathIndex < c.PathOpenings.Count)
                    SetNextWaypoint(c);
                else
                {
                    c.HasWaypoint = false;
                    ClearPath(c);
                    // set waypoint to work position
                    SetWorkPositionWaypoint(c);
                }
            }
        }

        void NavigateIdle(CrewMemberState c, float dt)
        {
            c.IdleTimer -= dt;
            if (c.IdleTimer <= 0f)
            {
                var room = _nav.GetRoom(c.CompartmentId);
                if (room != null)
                {
                    float rx = SimHash.HashToFloat(c.Id, _tickCounter, _seed + 811);
                    float rz = SimHash.HashToFloat(c.Id, _tickCounter, _seed + 937);
                    c.WaypointX = room.MinX + rx * (room.MaxX - room.MinX);
                    c.WaypointZ = room.MinZ + rz * (room.MaxZ - room.MinZ);
                    c.WaypointY = room.FloorY;
                    c.HasWaypoint = true;
                }
                float interval = 4f + 3f * SimHash.HashToFloat(c.Id, _tickCounter, _seed + 1073);
                c.IdleTimer = interval;
            }

            if (c.HasWaypoint)
            {
                MoveToward(c, dt);
                if (DistXZ(c.X, c.Z, c.WaypointX, c.WaypointZ) < WaypointTolerance)
                    c.HasWaypoint = false;
            }
        }

        int GetTargetCompartment(CrewMemberState c)
        {
            switch (c.Task)
            {
                case CrewTaskKind.FightFire:
                    return c.TaskTargetId;

                case CrewTaskKind.RepairBreach:
                    if (c.TaskTargetId >= 0 && c.TaskTargetId < _graph.Openings.Count)
                    {
                        var o = _graph.Openings[c.TaskTargetId];
                        return o.CompartmentA == Opening.Sea ? o.CompartmentB : o.CompartmentA;
                    }
                    return -1;

                case CrewTaskKind.OperatePump:
                    if (c.TaskTargetId >= 0 && c.TaskTargetId < _pumps.Count)
                        return _pumps[c.TaskTargetId].Pump.CompartmentId;
                    return -1;

                case CrewTaskKind.OperateReactor:
                    if (c.TaskTargetId >= 0 && c.TaskTargetId < _reactorStations.Count)
                        return _reactorStations[c.TaskTargetId].CompartmentId;
                    return -1;

                case CrewTaskKind.Flee:
                    return c.TaskTargetId;

                case CrewTaskKind.MoveTo:
                    return _nav.FindCompartmentAt(c.OrderX, c.OrderY, c.OrderZ);

                default:
                    return c.CompartmentId;
            }
        }

        void SetNextWaypoint(CrewMemberState c)
        {
            if (c.PathIndex >= c.PathOpenings.Count)
            {
                c.HasWaypoint = false;
                return;
            }

            int openingId = c.PathOpenings[c.PathIndex];
            if (_nav.TryGetPortal(openingId, out var portal))
            {
                c.WaypointX = portal.X;
                c.WaypointY = portal.Y;
                c.WaypointZ = portal.Z;
                c.HasWaypoint = true;
            }
            else
            {
                c.HasWaypoint = false;
            }
        }

        void SetWorkPositionWaypoint(CrewMemberState c)
        {
            switch (c.Task)
            {
                case CrewTaskKind.RepairBreach:
                    if (_nav.TryGetPortal(c.TaskTargetId, out var bp))
                    {
                        c.WaypointX = bp.X; c.WaypointY = bp.Y; c.WaypointZ = bp.Z;
                        c.HasWaypoint = true;
                    }
                    break;

                case CrewTaskKind.OperatePump:
                    if (c.TaskTargetId >= 0 && c.TaskTargetId < _pumps.Count)
                    {
                        var pr = _pumps[c.TaskTargetId];
                        c.WaypointX = pr.X; c.WaypointY = pr.Y; c.WaypointZ = pr.Z;
                        c.HasWaypoint = true;
                    }
                    break;

                case CrewTaskKind.OperateReactor:
                    if (c.TaskTargetId >= 0 && c.TaskTargetId < _reactorStations.Count)
                    {
                        var st = _reactorStations[c.TaskTargetId];
                        c.WaypointX = st.X; c.WaypointY = st.Y; c.WaypointZ = st.Z;
                        c.HasWaypoint = true;
                    }
                    break;

                case CrewTaskKind.FightFire:
                    var room = _nav.GetRoom(c.TaskTargetId);
                    if (room != null)
                    {
                        c.WaypointX = room.CenterX; c.WaypointY = room.FloorY; c.WaypointZ = room.CenterZ;
                        c.HasWaypoint = true;
                    }
                    break;

                case CrewTaskKind.MoveTo:
                    c.WaypointX = c.OrderX; c.WaypointY = c.OrderY; c.WaypointZ = c.OrderZ;
                    c.HasWaypoint = true;
                    break;
            }
        }

        void MoveToward(CrewMemberState c, float dt)
        {
            // check for closed doors in path
            if (c.PathIndex < c.PathOpenings.Count)
            {
                int oid = c.PathOpenings[c.PathIndex];
                var opening = _graph.Openings[oid];
                if (!opening.IsOpen)
                {
                    float dxDoor = 0f, dzDoor = 0f;
                    if (_nav.TryGetPortal(oid, out var dp))
                    {
                        dxDoor = dp.X - c.X;
                        dzDoor = dp.Z - c.Z;
                    }
                    float doorDist = (float)Math.Sqrt(dxDoor * dxDoor + dzDoor * dzDoor);
                    if (doorDist < DoorOpenRange)
                    {
                        c.DoorTimer += dt;
                        if (c.DoorTimer >= DoorOperateTime)
                        {
                            opening.IsOpen = true;
                            c.DoorTimer = 0f;
                        }
                        // stand still while opening
                        c.VelX = 0f; c.VelY = 0f; c.VelZ = 0f;
                        return;
                    }
                }
                else
                {
                    c.DoorTimer = 0f;
                }
            }

            // climbing through hatches
            if (c.MoveMode == CrewMoveMode.Climbing)
            {
                float dy = c.WaypointY - c.Y;
                if (Math.Abs(dy) < 0.3f)
                {
                    c.Y = c.WaypointY;
                    c.MoveMode = CrewMoveMode.Walking;
                    c.VelX = 0f; c.VelY = 0f; c.VelZ = 0f;
                    return;
                }
                float vy = Math.Sign(dy) * ClimbSpeed;
                c.Y += vy * dt;
                c.VelX = 0f; c.VelY = vy; c.VelZ = 0f;
                return;
            }

            // check if we need to climb (hatch transition)
            if (c.PathIndex < c.PathOpenings.Count)
            {
                int oid = c.PathOpenings[c.PathIndex];
                var opening = _graph.Openings[oid];
                if (opening.Kind == OpeningKind.Hatch && opening.IsOpen)
                {
                    float dxH = c.WaypointX - c.X;
                    float dzH = c.WaypointZ - c.Z;
                    float hDist = (float)Math.Sqrt(dxH * dxH + dzH * dzH);
                    if (hDist < WaypointTolerance)
                    {
                        // start climbing
                        c.MoveMode = CrewMoveMode.Climbing;
                        int nextRoom = c.PathIndex + 1 < c.PathCompartments.Count
                            ? c.PathCompartments[c.PathIndex + 1] : c.CompartmentId;
                        var destRoom = _nav.GetRoom(nextRoom);
                        if (destRoom != null)
                            c.WaypointY = destRoom.FloorY;
                        c.X = c.WaypointX;
                        c.Z = c.WaypointZ;
                        return;
                    }
                }
            }

            // swimming check
            if (c.CompartmentId >= 0)
            {
                var comp = _graph.GetCompartment(c.CompartmentId);
                if (comp.WaterLevelY > c.Y + 1.2f)
                {
                    c.MoveMode = CrewMoveMode.Swimming;
                    float surfaceY = comp.WaterLevelY - 1.5f;
                    if (surfaceY < comp.FloorY) surfaceY = comp.FloorY;
                    float ceilY = comp.FloorY + comp.Height;
                    if (surfaceY + EyeHeight > ceilY)
                        surfaceY = ceilY - EyeHeight;
                    c.Y = surfaceY;
                }
                else
                {
                    if (c.MoveMode == CrewMoveMode.Swimming)
                        c.MoveMode = CrewMoveMode.Walking;
                    c.Y = comp.FloorY;
                }
            }

            float speed = c.MoveMode == CrewMoveMode.Swimming ? SwimSpeed : WalkSpeed;
            float dx = c.WaypointX - c.X;
            float dz = c.WaypointZ - c.Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);

            if (dist < 0.01f)
            {
                c.VelX = 0f; c.VelZ = 0f;
                return;
            }

            float nx = dx / dist;
            float nz = dz / dist;
            float step = speed * dt;
            if (step > dist) step = dist;
            c.X += nx * step;
            c.Z += nz * step;
            c.VelX = nx * speed;
            c.VelY = 0f;
            c.VelZ = nz * speed;
        }

        // ----------------------------------------------------------------
        // Work execution
        // ----------------------------------------------------------------

        bool IsAtWorkPosition(CrewMemberState c)
        {
            switch (c.Task)
            {
                case CrewTaskKind.FightFire:
                    return c.CompartmentId == c.TaskTargetId;

                case CrewTaskKind.RepairBreach:
                    if (_nav.TryGetPortal(c.TaskTargetId, out var rp))
                        return DistXZ(c.X, c.Z, rp.X, rp.Z) < WorkRange;
                    return false;

                case CrewTaskKind.OperatePump:
                    if (c.TaskTargetId >= 0 && c.TaskTargetId < _pumps.Count)
                    {
                        var pr = _pumps[c.TaskTargetId];
                        return DistXZ(c.X, c.Z, pr.X, pr.Z) < DeviceRange;
                    }
                    return false;

                case CrewTaskKind.OperateReactor:
                    if (c.TaskTargetId >= 0 && c.TaskTargetId < _reactorStations.Count)
                    {
                        var st = _reactorStations[c.TaskTargetId];
                        return DistXZ(c.X, c.Z, st.X, st.Z) < DeviceRange;
                    }
                    return false;

                case CrewTaskKind.MoveTo:
                    return DistXZ(c.X, c.Z, c.OrderX, c.OrderZ) < WaypointTolerance;

                case CrewTaskKind.Flee:
                    return c.CompartmentId == c.TaskTargetId;

                default:
                    return false;
            }
        }

        void ExecuteWork(CrewMemberState c, float dt, DamageSystem damage,
                         FireSystem fire, PowerGrid power)
        {
            switch (c.Task)
            {
                case CrewTaskKind.FightFire:
                    ExecuteFightFire(c, dt, fire);
                    break;

                case CrewTaskKind.RepairBreach:
                    ExecuteRepairBreach(c, dt, damage);
                    break;

                case CrewTaskKind.OperatePump:
                    ExecuteOperatePump(c, dt);
                    break;

                case CrewTaskKind.OperateReactor:
                    ExecuteOperateReactor(c, dt, power);
                    break;

                case CrewTaskKind.MoveTo:
                    // arrived; keep order as guard duty
                    break;

                case CrewTaskKind.Flee:
                    EndTask(c, damage);
                    c.Task = CrewTaskKind.Idle;
                    c.TaskTargetId = c.CompartmentId;
                    c.TaskScore = 1f;
                    break;
            }
        }

        void ExecuteFightFire(CrewMemberState c, float dt, FireSystem fire)
        {
            if (fire == null) return;

            float prof = CrewJob.GetProficiency(c.Job, CrewTaskKind.FightFire);

            if (c.ExtinguisherCharge > 0f)
            {
                fire.Extinguish(c.TaskTargetId, ExtinguishRate * prof * dt);
                c.ExtinguisherCharge -= 1.6f * dt;
                if (c.ExtinguisherCharge < 0f) c.ExtinguisherCharge = 0f;
            }
            else
            {
                c.ExtinguisherCharge += ExtinguisherRechargeRate * dt;
                if (c.ExtinguisherCharge > 100f) c.ExtinguisherCharge = 100f;
            }
        }

        void ExecuteRepairBreach(CrewMemberState c, float dt, DamageSystem damage)
        {
            if (damage == null) return;
            if (c.TaskTargetId < 0 || c.TaskTargetId >= _graph.Openings.Count) return;

            if (c.RepairingOpeningId != c.TaskTargetId)
            {
                if (c.RepairingOpeningId >= 0)
                    damage.SetRepairing(c.RepairingOpeningId, false);
                damage.SetRepairing(c.TaskTargetId, true);
                c.RepairingOpeningId = c.TaskTargetId;
            }

            float prof = CrewJob.GetProficiency(c.Job, CrewTaskKind.RepairBreach);

            // underwater work penalty
            float ws = 1f;
            if (c.CompartmentId >= 0)
            {
                var comp = _graph.GetCompartment(c.CompartmentId);
                if ((c.Y + EyeHeight) < comp.WaterLevelY) ws = UnderwaterWorkScale;
            }

            var stage = damage.GetRepairStage(c.TaskTargetId);
            if (stage == BreachRepairStage.None)
            {
                damage.AddPatchProgress(c.TaskTargetId, CrewPatchRate * prof * ws * dt);
            }
            else if (stage == BreachRepairStage.Patched)
            {
                var o = _graph.Openings[c.TaskTargetId];
                o.Area -= CrewSealRate * prof * ws * dt;
                if (o.Area <= 0f)
                {
                    o.Area = 0f;
                    damage.SetRepairing(c.TaskTargetId, false);
                    damage.UnregisterBreach(c.TaskTargetId);
                    c.RepairingOpeningId = -1;
                    _events.Enqueue(new CrewEvent
                    {
                        Kind = CrewEventKind.BreachSealed,
                        CrewId = c.Id,
                        TargetId = c.TaskTargetId,
                    });
                    EndTask(c, damage);
                }
            }
        }

        void ExecuteOperatePump(CrewMemberState c, float dt)
        {
            if (c.TaskTargetId < 0 || c.TaskTargetId >= _pumps.Count) return;
            var pr = _pumps[c.TaskTargetId];

            c.DoorTimer += dt; // reuse as interact timer
            if (c.DoorTimer >= 1.0f)
            {
                pr.Pump.IsActive = true;
                c.DoorTimer = 0f;
                EndTask(c, null);
            }
        }

        void ExecuteOperateReactor(CrewMemberState c, float dt, PowerGrid power)
        {
            if (power == null) return;
            if (c.TaskTargetId < 0 || c.TaskTargetId >= _reactorStations.Count) return;

            var st = _reactorStations[c.TaskTargetId];
            if (st.ReactorIndex >= power.Reactors.Count) return;
            var reactor = power.Reactors[st.ReactorIndex];

            float prof = CrewJob.GetProficiency(c.Job, CrewTaskKind.OperateReactor);
            float rate = ReactorAdjustRate * prof * dt;

            if (reactor.Temperature > reactor.MeltdownTemperature * 0.9f)
            {
                // scram: drop fission
                reactor.FissionRate -= rate;
                if (reactor.FissionRate < 0f) reactor.FissionRate = 0f;
                reactor.TurbineOutput += rate;
                if (reactor.TurbineOutput > 100f) reactor.TurbineOutput = 100f;
            }
            else
            {
                // target turbine for consumption
                float totalConsumption = power.TotalConsumption;
                float targetTurbine = totalConsumption / reactor.MaxPowerOutput * 100f;
                if (targetTurbine > 100f) targetTurbine = 100f;
                if (targetTurbine < 10f) targetTurbine = 10f;

                if (reactor.TurbineOutput < targetTurbine)
                    reactor.TurbineOutput = Math.Min(reactor.TurbineOutput + rate, targetTurbine);
                else if (reactor.TurbineOutput > targetTurbine + 5f)
                    reactor.TurbineOutput = Math.Max(reactor.TurbineOutput - rate, targetTurbine);

                // fission bang-bang around optimal temp
                if (reactor.Temperature < reactor.OptimalTemperature - 5f)
                {
                    reactor.FissionRate += rate;
                    if (reactor.FissionRate > 100f) reactor.FissionRate = 100f;
                }
                else if (reactor.Temperature > reactor.OptimalTemperature + 5f)
                {
                    reactor.FissionRate -= rate;
                    if (reactor.FissionRate < 0f) reactor.FissionRate = 0f;
                }
            }

            // check stability
            bool stable = !reactor.IsMeltingDown
                && power.GridVoltage >= 0.95f && power.GridVoltage <= 1.1f
                && Math.Abs(reactor.Temperature - reactor.OptimalTemperature) < 10f;

            if (stable)
            {
                c.StableTimer += dt;
                if (c.StableTimer >= ReactorStableHold)
                    EndTask(c, null);
            }
            else
            {
                c.StableTimer = 0f;
            }
        }

        // ----------------------------------------------------------------
        // Task lifecycle
        // ----------------------------------------------------------------

        void EndTask(CrewMemberState c, DamageSystem damage)
        {
            if (c.RepairingOpeningId >= 0 && damage != null)
            {
                damage.SetRepairing(c.RepairingOpeningId, false);
                c.RepairingOpeningId = -1;
            }
            ReleaseClaim(c);
            ClearPath(c);
            c.Task = CrewTaskKind.None;
            c.TaskTargetId = -1;
            c.TaskScore = 0f;
            c.StableTimer = 0f;
            c.DoorTimer = 0f;

            // clear completed orders (except MoveTo which persists as guard)
            if (c.Order != CrewOrderKind.None && c.Order != CrewOrderKind.MoveTo)
            {
                c.Order = CrewOrderKind.None;
                c.OrderTargetId = -1;
            }
        }

        void ClearPath(CrewMemberState c)
        {
            c.PathCompartments.Clear();
            c.PathOpenings.Clear();
            c.PathIndex = 0;
            c.HasWaypoint = false;
        }

        // ----------------------------------------------------------------
        // Claim management
        // ----------------------------------------------------------------

        long ClaimKey(CrewTaskKind kind, int targetId)
        {
            return ((long)kind << 32) | (uint)targetId;
        }

        void AddClaim(CrewMemberState c)
        {
            long key = ClaimKey(c.Task, c.TaskTargetId);
            _claims.TryGetValue(key, out int count);
            _claims[key] = count + 1;
        }

        void ReleaseClaim(CrewMemberState c)
        {
            if (c.Task == CrewTaskKind.None || c.Task == CrewTaskKind.Idle) return;
            long key = ClaimKey(c.Task, c.TaskTargetId);
            if (_claims.TryGetValue(key, out int count))
            {
                count--;
                if (count <= 0) _claims.Remove(key);
                else _claims[key] = count;
            }
        }

        int GetClaimCount(CrewTaskKind kind, int targetId)
        {
            long key = ClaimKey(kind, targetId);
            return _claims.TryGetValue(key, out int count) ? count : 0;
        }

        // ----------------------------------------------------------------
        // Cooldown management
        // ----------------------------------------------------------------

        long CooldownKey(int crewId, CrewTaskKind kind, int targetId)
        {
            return ((long)crewId << 40) | ((long)kind << 32) | (uint)targetId;
        }

        bool IsCooldown(long key)
        {
            if (_claims.TryGetValue(key + long.MinValue, out int until))
                return _tickCounter < until;
            return false;
        }

        void SetCooldown(long key)
        {
            _claims[key + long.MinValue] = _tickCounter + TaskCooldownTicks;
        }

        // ----------------------------------------------------------------
        // Utility
        // ----------------------------------------------------------------

        static float DistXZ(float x1, float z1, float x2, float z2)
        {
            float dx = x2 - x1;
            float dz = z2 - z1;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        static float Math_Sign(float v)
        {
            if (v > 0f) return 1f;
            if (v < 0f) return -1f;
            return 0f;
        }
    }
}
