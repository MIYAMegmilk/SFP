using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public enum BreachRepairStage { None, Patched, Sealed }

    public sealed class DamageSystem
    {
        readonly CompartmentGraph _graph;
        readonly SubmarineState _sub;
        readonly List<HullSection> _sections = new();
        readonly List<BreachRecord> _breaches = new();
        readonly Queue<DamageEvent> _pendingEvents = new();
        readonly Random _rng = new Random(12345);

        // breach growth
        public float BaseGrowthRate = 0.004f;
        public float ReferenceDepth = 200f;

        // crush stress
        public bool CrushStressEnabled = true;
        public float HullStress { get; private set; }
        public float StressBuildRate = 0.02f;
        public float StressRecoveryRate = 0.05f;
        public float CrushDamageRate = 3f;
        public float CrushBreachArea = 0.04f;
        public float CrushBreachMaxArea = 0.25f;

        // 2-stage repair
        public float PatchWorkRequired = 1f;

        // fire weakening
        public float FireWeakenRate = 0.6f;

        // explosion
        public float ExplosionIntegrityDamage = 80f;

        // water short
        public float WaterShortThreshold = 0.25f;
        public float WaterShortDamageRate = 5f;

        // collision
        public TerrainModel Terrain;
        public float MinImpactSpeed = 1.5f;
        public float ImpactIntegrityScale = 12f;
        public float CollisionBreachArea = 0.05f;
        public float WallStepThreshold = 8f;

        // Hull half-extents in ship axes (m): authored hull is 24 × 18 × 6. Terrain collision
        // samples this footprint so the whole boat collides, not just its center point.
        public float HullHalfLength = 12f;
        public float HullHalfWidth = 3f;
        public float HullHalfHeight = 9f;
        bool _grounded;
        bool _ceilingContact;

        // equipment damage
        public EngineState Engine;
        public float EquipWaterDamageRate = 2f;
        public float EquipFireDamageRate = 3f;

        // creature attacks
        public float CreatureBiteIntegrityScale = 10f;
        public float CreatureBiteBreachArea = 0.05f;
        int _creatureBiteCounter;

        // fire ignition from damage
        public FireSystem Fire { get; set; }
        public float CollisionFireMinSpeed = 3f;
        int _fireIgnitionCounter;
        const int FireIgnitionSeed = 555113;

        public IReadOnlyList<HullSection> Sections => _sections;

        struct BreachRecord
        {
            public int OpeningId;
            public float MaxArea;
            public bool IsRepairing;
            public BreachRepairStage Stage;
            public float PatchProgress;
            public int SectionId;
        }

        public DamageSystem(CompartmentGraph graph, SubmarineState sub)
        {
            _graph = graph;
            _sub = sub;
        }

        // --- Hull section registration ---

        public HullSection RegisterHullSection(int compartmentId, HullFace face, bool isExterior)
        {
            var s = new HullSection
            {
                Id = _sections.Count,
                CompartmentId = compartmentId,
                Face = face,
                IsExterior = isExterior,
                WeaknessFactor = 0.8f + 0.4f * (float)_rng.NextDouble(),
            };
            _sections.Add(s);
            return s;
        }

        public HullSection GetSection(int id)
        {
            return id >= 0 && id < _sections.Count ? _sections[id] : null;
        }

        public HullSection FindSection(int compartmentId, HullFace face)
        {
            for (int i = 0; i < _sections.Count; i++)
            {
                var s = _sections[i];
                if (s.CompartmentId == compartmentId && s.Face == face) return s;
            }
            return null;
        }

        // --- Breach management ---

        public void RegisterBreach(int openingId, float maxArea, int sectionId = -1)
        {
            for (int i = 0; i < _breaches.Count; i++)
                if (_breaches[i].OpeningId == openingId) return;

            _breaches.Add(new BreachRecord
            {
                OpeningId = openingId,
                MaxArea = maxArea,
                SectionId = sectionId,
            });

            if (sectionId >= 0 && sectionId < _sections.Count)
            {
                var s = _sections[sectionId];
                s.ActiveOpeningId = openingId;
                s.BreachPending = false;
                if (s.Integrity > 30f) s.Integrity = 30f;
            }
        }

        public void UnregisterBreach(int openingId)
        {
            for (int i = _breaches.Count - 1; i >= 0; i--)
            {
                if (_breaches[i].OpeningId != openingId) continue;
                int sid = _breaches[i].SectionId;
                _breaches.RemoveAt(i);

                if (sid >= 0 && sid < _sections.Count)
                {
                    var s = _sections[sid];
                    s.ActiveOpeningId = -1;
                    if (s.Integrity < 50f) s.Integrity = 50f;
                }

                var opening = _graph.Openings[openingId];
                opening.FlowScale = 1f;
                return;
            }
        }

        public void SetRepairing(int openingId, bool repairing)
        {
            for (int i = 0; i < _breaches.Count; i++)
            {
                if (_breaches[i].OpeningId != openingId) continue;
                var b = _breaches[i];
                b.IsRepairing = repairing;
                _breaches[i] = b;
                return;
            }
        }

        public BreachRepairStage GetRepairStage(int openingId)
        {
            for (int i = 0; i < _breaches.Count; i++)
                if (_breaches[i].OpeningId == openingId) return _breaches[i].Stage;
            return BreachRepairStage.None;
        }

        public float GetPatchProgress(int openingId)
        {
            for (int i = 0; i < _breaches.Count; i++)
                if (_breaches[i].OpeningId == openingId) return _breaches[i].PatchProgress;
            return 0f;
        }

        public bool AddPatchProgress(int openingId, float delta)
        {
            for (int i = 0; i < _breaches.Count; i++)
            {
                if (_breaches[i].OpeningId != openingId) continue;
                var b = _breaches[i];
                b.PatchProgress += delta / PatchWorkRequired;
                if (b.PatchProgress >= 1f)
                {
                    b.PatchProgress = 1f;
                    b.Stage = BreachRepairStage.Patched;
                    _graph.Openings[openingId].FlowScale = 0.5f;
                    _breaches[i] = b;
                    return true;
                }
                _breaches[i] = b;
                return false;
            }
            return false;
        }

        // --- HUD queries ---

        public int BreachCount => _breaches.Count;

        public int PatchedBreachCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _breaches.Count; i++)
                    if (_breaches[i].Stage == BreachRepairStage.Patched) n++;
                return n;
            }
        }

        public float GetHullIntegrityFraction()
        {
            if (_sections.Count == 0) return 1f;
            int count = 0;
            float sum = 0f;
            for (int i = 0; i < _sections.Count; i++)
            {
                if (!_sections[i].IsExterior) continue;
                sum += _sections[i].IntegrityFraction;
                count++;
            }
            return count > 0 ? sum / count : 1f;
        }

        public HullSection GetWorstExteriorSection()
        {
            HullSection worst = null;
            for (int i = 0; i < _sections.Count; i++)
            {
                var s = _sections[i];
                if (!s.IsExterior) continue;
                if (worst == null || s.Integrity < worst.Integrity) worst = s;
            }
            return worst;
        }

        // --- Event queue ---

        public void EnqueueEvent(DamageEvent evt)
        {
            _pendingEvents.Enqueue(evt);
        }

        public bool TryDequeueEvent(out DamageEvent evt)
        {
            if (_pendingEvents.Count > 0)
            { evt = _pendingEvents.Dequeue(); return true; }
            evt = default;
            return false;
        }

        // --- Main tick ---

        public void Tick(float dt, FireSystem fire, PowerGrid power)
        {
            TickCollision(dt);
            TickCrushStress(dt);
            TickBreachGrowth(dt);
            TickFireWeakening(dt, fire);
            TickReactorMeltdown(dt, fire, power);
            TickWaterShort(dt, fire, power);
            TickEquipmentDamage(dt, fire, power);
        }

        void TickBreachGrowth(float dt)
        {
            float depth = _sub.Depth;
            float pressureFactor = 0.5f + depth / ReferenceDepth;

            for (int i = _breaches.Count - 1; i >= 0; i--)
            {
                var b = _breaches[i];
                if (b.IsRepairing) continue;
                if (b.Stage != BreachRepairStage.None) continue;

                var opening = _graph.Openings[b.OpeningId];
                if (opening.Area <= 0f || !opening.IsOpen) continue;
                if (opening.Area >= b.MaxArea) continue;

                float sizeFactor = 1f + opening.Area / b.MaxArea;
                float growth = BaseGrowthRate * pressureFactor * sizeFactor * dt;
                opening.Area += growth;
                if (opening.Area > b.MaxArea)
                    opening.Area = b.MaxArea;
            }
        }

        void TickCrushStress(float dt)
        {
            if (!CrushStressEnabled) return;

            float excess = Math.Max(0f, (_sub.Depth - _sub.CrushDepth) / _sub.CrushDepth);
            bool inPreCreak = _sub.Depth > _sub.CrushDepth * 0.9f;

            if (excess > 0f || inPreCreak)
                HullStress += StressBuildRate * (0.25f + 4f * excess) * dt;
            else
                HullStress -= StressRecoveryRate * dt;

            HullStress = Math.Clamp(HullStress, 0f, 1f);

            if (HullStress <= 0.5f || excess <= 0f) return;

            float damageScale = CrushDamageRate * (HullStress - 0.5f) * 2f * (1f + 2f * excess);
            for (int i = 0; i < _sections.Count; i++)
            {
                var s = _sections[i];
                if (!s.IsExterior) continue;
                if (s.HasBreach || s.BreachPending) continue;

                s.Integrity -= damageScale * s.WeaknessFactor * dt;
                if (s.Integrity <= 0f)
                {
                    s.Integrity = 0f;
                    s.BreachPending = true;
                    EnqueueEvent(new DamageEvent
                    {
                        Kind = DamageEventKind.BreachCreated,
                        CompartmentId = s.CompartmentId,
                        Face = s.Face,
                        Magnitude = CrushBreachArea,
                        SectionId = s.Id,
                    });
                }
            }
        }

        void TickFireWeakening(float dt, FireSystem fire)
        {
            if (fire == null) return;
            for (int i = 0; i < _sections.Count; i++)
            {
                var s = _sections[i];
                float fi = fire.GetFireIntensity(s.CompartmentId);
                if (fi <= 0.1f) continue;
                if (s.HasBreach || s.BreachPending) continue;

                s.Integrity -= FireWeakenRate * fi * s.WeaknessFactor * dt;
                if (s.Integrity <= 0f)
                {
                    s.Integrity = 0f;
                    s.BreachPending = true;
                    if (s.IsExterior)
                    {
                        EnqueueEvent(new DamageEvent
                        {
                            Kind = DamageEventKind.BreachCreated,
                            CompartmentId = s.CompartmentId,
                            Face = s.Face,
                            Magnitude = 0.03f,
                            SectionId = s.Id,
                        });
                    }
                }
            }
        }

        void TickReactorMeltdown(float dt, FireSystem fire, PowerGrid power)
        {
            if (power == null) return;
            for (int i = 0; i < power.Reactors.Count; i++)
            {
                var r = power.Reactors[i];
                if (r.HasExploded) continue;
                if (r.IsMeltingDown)
                    r.MeltdownProgress += dt;
                else
                    r.MeltdownProgress = Math.Max(0f, r.MeltdownProgress - 2f * dt);

                if (r.MeltdownProgress >= r.MeltdownFuseSeconds)
                {
                    r.HasExploded = true;
                    r.FissionRate = 0f;
                    r.FuelRemaining = 0f;
                    r.Condition = 0f;
                    ApplyExplosion(r.CompartmentId, 1f, fire, power);
                }
            }
        }

        public void ApplyExplosion(int compartmentId, float magnitude, FireSystem fire, PowerGrid power)
        {
            for (int i = 0; i < _sections.Count; i++)
            {
                var s = _sections[i];
                if (s.CompartmentId != compartmentId) continue;
                s.Integrity -= ExplosionIntegrityDamage * magnitude * s.WeaknessFactor;
                if (s.Integrity <= 0f && !s.HasBreach && !s.BreachPending)
                {
                    s.Integrity = 0f;
                    s.BreachPending = true;
                    if (s.IsExterior)
                    {
                        EnqueueEvent(new DamageEvent
                        {
                            Kind = DamageEventKind.BreachCreated,
                            CompartmentId = s.CompartmentId,
                            Face = s.Face,
                            Magnitude = 0.12f * magnitude,
                            SectionId = s.Id,
                        });
                    }
                }
            }

            // Adjacent compartments: half damage
            var adjacentIds = new HashSet<int>();
            for (int i = 0; i < _graph.Openings.Count; i++)
            {
                var o = _graph.Openings[i];
                if (o.CompartmentA == compartmentId && o.CompartmentB >= 0)
                    adjacentIds.Add(o.CompartmentB);
                if (o.CompartmentB == compartmentId && o.CompartmentA >= 0)
                    adjacentIds.Add(o.CompartmentA);
            }
            foreach (int adjId in adjacentIds)
            {
                for (int i = 0; i < _sections.Count; i++)
                {
                    var s = _sections[i];
                    if (s.CompartmentId != adjId) continue;
                    s.Integrity -= ExplosionIntegrityDamage * 0.5f * magnitude * s.WeaknessFactor;
                    if (s.Integrity <= 0f && !s.HasBreach && !s.BreachPending)
                    {
                        s.Integrity = 0f;
                        s.BreachPending = true;
                        if (s.IsExterior)
                        {
                            EnqueueEvent(new DamageEvent
                            {
                                Kind = DamageEventKind.BreachCreated,
                                CompartmentId = s.CompartmentId,
                                Face = s.Face,
                                Magnitude = 0.03f * magnitude,
                                SectionId = s.Id,
                            });
                        }
                    }
                }
            }

            fire?.StartFire(compartmentId, magnitude);
            foreach (int adjId in adjacentIds)
                fire?.StartFire(adjId, 0.4f * magnitude);

            // Equipment damage
            if (power != null)
            {
                for (int i = 0; i < power.Reactors.Count; i++)
                    if (power.Reactors[i].CompartmentId == compartmentId)
                        power.Reactors[i].Condition -= 60f * magnitude;
                    else if (adjacentIds.Contains(power.Reactors[i].CompartmentId))
                        power.Reactors[i].Condition -= 25f * magnitude;

                for (int i = 0; i < power.Junctions.Count; i++)
                    if (power.Junctions[i].CompartmentId == compartmentId)
                        power.Junctions[i].Condition -= 60f * magnitude;
                    else if (adjacentIds.Contains(power.Junctions[i].CompartmentId))
                        power.Junctions[i].Condition -= 25f * magnitude;
            }

            if (Engine != null && Engine.CompartmentId == compartmentId)
                Engine.Condition -= 60f * magnitude;
            else if (Engine != null && adjacentIds.Contains(Engine.CompartmentId))
                Engine.Condition -= 25f * magnitude;

            EnqueueEvent(new DamageEvent
            {
                Kind = DamageEventKind.Explosion,
                CompartmentId = compartmentId,
                Magnitude = magnitude,
                SectionId = -1,
            });
        }

        void TickWaterShort(float dt, FireSystem fire, PowerGrid power)
        {
            if (power == null) return;
            for (int i = 0; i < power.Junctions.Count; i++)
            {
                var j = power.Junctions[i];
                if (j.CompartmentId < 0) continue;
                float wf = _graph.GetCompartment(j.CompartmentId).WaterFraction;

                if (wf > WaterShortThreshold && j.Condition > 0f)
                {
                    j.Condition -= WaterShortDamageRate * ((wf - WaterShortThreshold) / (1f - WaterShortThreshold)) * dt;
                    if (j.Condition < 0f) j.Condition = 0f;

                    if (!j.IsShortedByWater)
                    {
                        j.IsShortedByWater = true;
                        EnqueueEvent(new DamageEvent
                        {
                            Kind = DamageEventKind.ElectricalShort,
                            CompartmentId = j.CompartmentId,
                            Magnitude = wf,
                            SectionId = -1,
                        });
                        if (wf < 0.5f)
                            fire?.StartFire(j.CompartmentId, 0.2f);
                    }
                }
                else if (wf < 0.15f)
                {
                    j.IsShortedByWater = false;
                }
            }
        }

        void TickCollision(float dt)
        {
            if (Terrain == null) return;

            // Sample a 3×3 grid over the hull footprint (bow/stern × port/starboard, rotated
            // by heading) instead of just the center point, so a wingtip of the hull hits rock
            // even when the center is over clear water. Vertical checks then reference the
            // keel/top line via HullHalfHeight, not the hull center.
            float rad = _sub.Heading * ((float)Math.PI / 180f);
            float fwdX = (float)Math.Sin(rad), fwdZ = (float)Math.Cos(rad);
            float latX = fwdZ, latZ = -fwdX;

            float minFloor = float.MaxValue;
            float maxCeiling = 0f;
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    float sx = _sub.PositionX + fwdX * i * HullHalfLength + latX * j * HullHalfWidth;
                    float sz = _sub.PositionZ + fwdZ * i * HullHalfLength + latZ * j * HullHalfWidth;
                    float f = Terrain.GetFloorDepthAt(sx, sz);
                    if (f < minFloor) minFloor = f;
                    float c = Terrain.GetCeilingDepthAt(sx, sz);
                    if (c > maxCeiling) maxCeiling = c;
                }
            }

            float hullBottom = _sub.Depth + HullHalfHeight;
            float hullTop = _sub.Depth - HullHalfHeight;

            bool floorBlocked = hullBottom >= minFloor && minFloor < hullBottom - WallStepThreshold;
            bool ceilingBlocked = maxCeiling > 0f && hullTop <= maxCeiling && maxCeiling >= hullTop + WallStepThreshold;

            if (floorBlocked || ceilingBlocked)
            {
                // Lateral wall hit: steep terrain ahead, don't clamp Depth (would teleport up/down).
                float impactSpeed = _sub.HorizontalSpeedMagnitude;
                HullFace face = _sub.HorizontalSpeed > 0f ? HullFace.East : HullFace.West;
                _sub.RevertHorizontal();
                _sub.ScaleHorizontalVelocity(-0.1f);
                ApplyImpactDamage(impactSpeed, face);
                _grounded = false;
            }
            else if (hullBottom >= minFloor)
            {
                float vertSpeed = Math.Max(0f, _sub.Velocity);
                float horizSpeed = _sub.HorizontalSpeedMagnitude;
                float impactSpeed = (float)Math.Sqrt(vertSpeed * vertSpeed + horizSpeed * horizSpeed);
                _sub.Depth = minFloor - HullHalfHeight;
                if (_sub.Velocity > 0f) _sub.Velocity = 0f;
                _sub.ScaleHorizontalVelocity(0.2f);

                if (!_grounded) ApplyImpactDamage(impactSpeed, HullFace.Floor);
                _grounded = true;
            }
            else
            {
                _grounded = false;
            }

            // Vertical ceiling bump: rock hangs down further than the top of the sail.
            if (maxCeiling > 0f && hullTop <= maxCeiling)
            {
                float riseSpeed = Math.Max(0f, -_sub.Velocity);
                float ceilHorizSpeed = _sub.HorizontalSpeedMagnitude;
                float impactSpeed = (float)Math.Sqrt(riseSpeed * riseSpeed + ceilHorizSpeed * ceilHorizSpeed);
                _sub.Depth = maxCeiling + HullHalfHeight;
                if (_sub.Velocity < 0f) _sub.Velocity = 0f;

                if (!_ceilingContact) ApplyImpactDamage(impactSpeed, HullFace.Ceiling);
                _ceilingContact = true;
            }
            else
            {
                _ceilingContact = false;
            }

            if (Terrain.Map != null)
            {
                _sub.PositionX = Math.Clamp(_sub.PositionX, 10f, Terrain.Map.WorldSizeX - 10f);
                _sub.PositionZ = Math.Clamp(_sub.PositionZ, 10f, Terrain.Map.WorldSizeZ - 10f);
            }
        }

        void ApplyImpactDamage(float impactSpeed, HullFace face)
        {
            if (impactSpeed <= MinImpactSpeed) return;

            float over = impactSpeed - MinImpactSpeed;
            float damage = ImpactIntegrityScale * over * over;
            int fireCompartment = -1;
            for (int i = 0; i < _sections.Count; i++)
            {
                var s = _sections[i];
                if (!s.IsExterior || s.Face != face) continue;
                if (s.HasBreach || s.BreachPending) continue;

                s.Integrity -= damage * s.WeaknessFactor;
                if (fireCompartment < 0) fireCompartment = s.CompartmentId;
                if (s.Integrity <= 0f)
                {
                    s.Integrity = 0f;
                    s.BreachPending = true;
                    float clampedOver = Math.Clamp(over, 1f, 4f);
                    EnqueueEvent(new DamageEvent
                    {
                        Kind = DamageEventKind.BreachCreated,
                        CompartmentId = s.CompartmentId,
                        Face = s.Face,
                        Magnitude = CollisionBreachArea * clampedOver,
                        SectionId = s.Id,
                    });
                }
            }

            // High-speed ground/wall impacts have a small chance to spark a fire.
            if (impactSpeed > CollisionFireMinSpeed)
                TryIgniteFromDamage(fireCompartment, (impactSpeed - CollisionFireMinSpeed) * 0.1f);

            EnqueueEvent(new DamageEvent
            {
                Kind = DamageEventKind.Collision,
                CompartmentId = -1,
                Magnitude = impactSpeed,
                SectionId = -1,
            });
        }

        public void ApplyMineExplosion(float distance)
        {
            float magnitude = Math.Clamp(3.5f - distance * 0.08f, 1f, 3.5f);
            float over = magnitude;
            float damage = ImpactIntegrityScale * over * over;

            int hit = 0;
            int fireCompartment = -1;
            for (int i = 0; i < _sections.Count && hit < 2; i++)
            {
                var s = _sections[i];
                if (!s.IsExterior) continue;
                if (s.Face != HullFace.North && s.Face != HullFace.South && s.Face != HullFace.East && s.Face != HullFace.West) continue;
                if (s.HasBreach || s.BreachPending) continue;

                s.Integrity -= damage * s.WeaknessFactor;
                if (fireCompartment < 0) fireCompartment = s.CompartmentId;
                if (s.Integrity <= 0f)
                {
                    s.Integrity = 0f;
                    s.BreachPending = true;
                    float clampedOver = Math.Clamp(over, 1f, 4f);
                    EnqueueEvent(new DamageEvent
                    {
                        Kind = DamageEventKind.BreachCreated,
                        CompartmentId = s.CompartmentId,
                        Face = s.Face,
                        Magnitude = CollisionBreachArea * clampedOver,
                        SectionId = s.Id,
                    });
                }
                hit++;
            }

            TryIgniteFromDamage(fireCompartment, magnitude);

            EnqueueEvent(new DamageEvent
            {
                Kind = DamageEventKind.MineExplosion,
                CompartmentId = -1,
                Magnitude = magnitude,
                SectionId = -1,
            });
        }

        public void ApplyCreatureBite(float magnitude)
        {
            HullSection target = null;
            int n = _sections.Count;
            if (n > 0)
            {
                for (int step = 0; step < n; step++)
                {
                    int idx = (_creatureBiteCounter + step) % n;
                    var s = _sections[idx];
                    if (!s.IsExterior) continue;
                    if (s.Face != HullFace.North && s.Face != HullFace.South && s.Face != HullFace.East && s.Face != HullFace.West) continue;
                    if (s.HasBreach || s.BreachPending) continue;

                    target = s;
                    _creatureBiteCounter = (idx + 1) % n;
                    break;
                }
            }

            if (target != null)
            {
                target.Integrity -= magnitude * CreatureBiteIntegrityScale * target.WeaknessFactor;
                if (target.Integrity <= 0f)
                {
                    target.Integrity = 0f;
                    target.BreachPending = true;
                    EnqueueEvent(new DamageEvent
                    {
                        Kind = DamageEventKind.BreachCreated,
                        CompartmentId = target.CompartmentId,
                        Face = target.Face,
                        Magnitude = CreatureBiteBreachArea * magnitude,
                        SectionId = target.Id,
                    });
                }

                TryIgniteFromDamage(target.CompartmentId, magnitude);
            }

            EnqueueEvent(new DamageEvent
            {
                Kind = DamageEventKind.CreatureAttack,
                CompartmentId = target?.CompartmentId ?? -1,
                Magnitude = magnitude,
                SectionId = target?.Id ?? -1,
            });
        }

        void TickEquipmentDamage(float dt, FireSystem fire, PowerGrid power)
        {
            if (power == null) return;

            for (int i = 0; i < power.Reactors.Count; i++)
            {
                var r = power.Reactors[i];
                if (r.CompartmentId < 0 || r.Condition <= 0f) continue;
                float rate = ComputeEquipDamageRate(r.CompartmentId, fire);
                if (rate > 0f) r.Condition = Math.Max(0f, r.Condition - rate * dt);
            }

            for (int i = 0; i < power.Junctions.Count; i++)
            {
                var j = power.Junctions[i];
                if (j.CompartmentId < 0 || j.Condition <= 0f) continue;
                float rate = ComputeEquipDamageRate(j.CompartmentId, fire);
                if (rate > 0f) j.Condition = Math.Max(0f, j.Condition - rate * dt);
            }

            if (Engine != null && Engine.CompartmentId >= 0 && Engine.Condition > 0f)
            {
                float rate = ComputeEquipDamageRate(Engine.CompartmentId, fire);
                if (rate > 0f) Engine.Condition = Math.Max(0f, Engine.Condition - rate * dt);
            }
        }

        float ComputeEquipDamageRate(int compartmentId, FireSystem fire)
        {
            var comp = _graph.GetCompartment(compartmentId);
            float rate = 0f;
            if (comp.WaterFraction > 0.6f)
                rate += EquipWaterDamageRate * (comp.WaterFraction - 0.6f) / 0.4f;
            float fi = fire?.GetFireIntensity(compartmentId) ?? 0f;
            if (fi > 0f) rate += EquipFireDamageRate * fi;
            return rate;
        }

        void TryIgniteFromDamage(int compartmentId, float magnitude)
        {
            if (Fire == null || compartmentId < 0) return;
            float hashBasedChance = HashToFloat(_fireIgnitionCounter++, 0, FireIgnitionSeed);
            if (hashBasedChance < magnitude * 0.3f)
                Fire.StartFire(compartmentId, magnitude * 0.15f);
        }

        static float HashToFloat(int x, int z, int seed)
        {
            uint h = Hash(x, z, seed);
            return (h & 0x00FFFFFFu) / (float)0x01000000u;
        }

        static uint Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u;
                h += (uint)z * 668265263u;
                h += (uint)seed * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
