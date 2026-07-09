using System;
using System.Collections.Generic;
using UnityEngine;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    [Serializable]
    public class StatusSnapshot
    {
        public float time;
        public SubData sub;
        public EngineData engine;
        public NavData nav;
        public PowerData power;
        public List<CompData> compartments;
        public MissionData mission;
        public SonarData sonar;

        [Serializable]
        public class SubData
        {
            public float x, z, depth, crushDepth;
            public bool belowCrush;
            public float heading, turnRate, speed, verticalVelocity;
            public float rudder, noise, waterVolume, netForce;
        }

        [Serializable]
        public class EngineData
        {
            public float throttle, thrust, condition;
        }

        [Serializable]
        public class NavData
        {
            public bool autopilot, depthHold;
            public float desiredHeading, desiredSpeed, desiredDepth;
        }

        [Serializable]
        public class PowerData
        {
            public float production, consumption, voltage;
            public List<ReactorData> reactors;
            public List<BatteryData> batteries;
            public List<JunctionData> junctions;
        }

        [Serializable]
        public class ReactorData
        {
            public float output, maxOutput, fissionRate, temp, fuel, condition;
            public float optimalTemp, meltdownTemp, meltdownProgress, meltdownFuse;
            public bool meltdown, exploded;
        }

        [Serializable]
        public class BatteryData
        {
            public float charge, maxCharge, fraction;
        }

        [Serializable]
        public class JunctionData
        {
            public float load, maxLoad, condition;
            public bool overloaded, shorted;
        }

        [Serializable]
        public class CompData
        {
            public int id;
            public string name;
            public float water, volume, fraction, pressureAtm;
            public bool sealed_;
            public float fire, oxygen, floorY, shipX;
        }

        [Serializable]
        public class MissionData
        {
            public bool available;
            public int round, completed, total;
            public string phase, label;
            public float targetX, targetZ, distance, bearing;
            public float holdProgress, holdSeconds;
        }

        [Serializable]
        public class SonarData
        {
            public bool hasPower, passive;
            public float range, pingProgress, pingInterval;
            public int res;
            public string grid;
            public List<SonarContactData> contacts;
            // Side profile (forward cross-section): base64, pairs of (floor_byte, ceil_byte) × profileSamples
            public string profile;
            public int profileSamples;
            public float profileDepthMin, profileDepthMax;
            // 3D depth grid: base64, depthGridRes×depthGridRes quantized floor depths
            public string depthGrid;
            public int depthGridRes;
            public float depthGridMin, depthGridMax;
        }

        [Serializable]
        public class SonarContactData
        {
            public float distance, bearing;
            public int type; // 0=Terrain, 1=Creature, 2=Structure
        }

        static float Safe(float v) => float.IsFinite(v) ? v : 0f;

        public static StatusSnapshot Build(SimulationBridge bridge, MissionManager mm)
        {
            var snap = new StatusSnapshot { time = Time.unscaledTime };

            var s = bridge.SubState;
            if (s != null)
            {
                snap.sub = new SubData
                {
                    x = Safe(s.PositionX), z = Safe(s.PositionZ),
                    depth = Safe(s.Depth), crushDepth = Safe(s.CrushDepth),
                    belowCrush = s.IsBelowCrushDepth,
                    heading = Safe(s.Heading), turnRate = Safe(s.TurnRate),
                    speed = Safe(s.HorizontalSpeed), verticalVelocity = Safe(s.Velocity),
                    rudder = Safe(s.RudderAngle), noise = Safe(s.NoiseLevel),
                    waterVolume = Safe(s.TotalWaterVolume), netForce = Safe(s.NetForce),
                };
            }

            var e = bridge.Engine;
            if (e != null)
            {
                snap.engine = new EngineData
                {
                    throttle = Safe(e.ThrottleSetting),
                    thrust = Safe(e.CurrentThrust),
                    condition = Safe(e.Condition),
                };
            }

            var n = bridge.Navigation;
            if (n != null)
            {
                snap.nav = new NavData
                {
                    autopilot = n.AutoPilotEnabled,
                    depthHold = n.DepthHoldEnabled,
                    desiredHeading = Safe(n.DesiredHeading),
                    desiredSpeed = Safe(n.DesiredSpeed),
                    desiredDepth = Safe(n.DesiredDepth),
                };
            }

            var pg = bridge.PowerGrid;
            if (pg != null)
            {
                snap.power = new PowerData
                {
                    production = Safe(pg.TotalProduction),
                    consumption = Safe(pg.TotalConsumption),
                    voltage = Safe(pg.GridVoltage),
                    reactors = new List<ReactorData>(),
                    batteries = new List<BatteryData>(),
                    junctions = new List<JunctionData>(),
                };
                foreach (var r in pg.Reactors)
                {
                    snap.power.reactors.Add(new ReactorData
                    {
                        output = Safe(r.CurrentPowerOutput), maxOutput = Safe(r.MaxPowerOutput),
                        fissionRate = Safe(r.FissionRate),
                        temp = Safe(r.Temperature), fuel = Safe(r.FuelRemaining),
                        condition = Safe(r.Condition),
                        optimalTemp = Safe(r.OptimalTemperature), meltdownTemp = Safe(r.MeltdownTemperature),
                        meltdownProgress = Safe(r.MeltdownProgress), meltdownFuse = Safe(r.MeltdownFuseSeconds),
                        meltdown = r.IsMeltingDown, exploded = r.HasExploded,
                    });
                }
                foreach (var b in pg.Batteries)
                {
                    snap.power.batteries.Add(new BatteryData
                    {
                        charge = Safe(b.Charge), maxCharge = Safe(b.MaxCharge), fraction = Safe(b.ChargeFraction),
                    });
                }
                foreach (var j in pg.Junctions)
                {
                    snap.power.junctions.Add(new JunctionData
                    {
                        load = Safe(j.CurrentLoad), maxLoad = Safe(j.MaxLoad),
                        condition = Safe(j.Condition), overloaded = j.IsOverloaded, shorted = j.IsShortedByWater,
                    });
                }
            }

            var graph = bridge.Graph;
            if (graph != null)
            {
                snap.compartments = new List<CompData>();
                var fire = bridge.FireSystem;
                var atmo = bridge.Atmosphere;
                foreach (var c in graph.Compartments)
                {
                    var def = bridge.GetCompartmentDef(c.Id);
                    snap.compartments.Add(new CompData
                    {
                        id = c.Id,
                        name = def != null ? def.gameObject.name : $"Comp{c.Id}",
                        water = Safe(c.WaterVolume), volume = Safe(c.Volume),
                        fraction = Safe(c.WaterFraction), pressureAtm = Safe(c.AirPressureAtm),
                        sealed_ = c.IsAirSealed,
                        fire = fire != null ? Safe(fire.GetFireIntensity(c.Id)) : 0f,
                        oxygen = atmo != null ? Safe(atmo.GetOxygenLevel(c.Id)) : 1f,
                        floorY = def != null ? def.FloorY : 0f,
                        shipX = def != null ? def.transform.localPosition.x : 0f,
                    });
                }
            }

            var missions = mm?.Missions;
            snap.mission = new MissionData();
            if (missions != null)
            {
                snap.mission.available = true;
                snap.mission.round = missions.Round;
                snap.mission.phase = missions.Phase.ToString();
                snap.mission.completed = missions.CompletedCount;
                snap.mission.total = missions.TotalCount;
                var cur = missions.Current;
                if (cur != null)
                {
                    snap.mission.label = cur.Label;
                    snap.mission.targetX = Safe(cur.TargetX);
                    snap.mission.targetZ = Safe(cur.TargetZ);
                    if (s != null)
                    {
                        snap.mission.distance = Safe(missions.DistanceToTarget(s));
                        snap.mission.bearing = Safe(missions.BearingToTarget(s));
                    }
                    snap.mission.holdProgress = Safe(cur.HoldProgress);
                    snap.mission.holdSeconds = Safe(cur.HoldSeconds);
                }
            }

            var sonarState = bridge.GetSonar(0);
            if (sonarState != null && s != null && bridge.Map != null)
            {
                const int GridRes = 96;
                snap.sonar = new SonarData
                {
                    hasPower = sonarState.HasPower,
                    passive = sonarState.IsPassive,
                    range = Safe(sonarState.Range),
                    pingProgress = Safe(sonarState.PingProgress),
                    pingInterval = Safe(sonarState.PingInterval),
                    res = GridRes,
                    contacts = new List<SonarContactData>(),
                };

                snap.sonar.grid = ClassifyAndEncode(bridge.Map, s, sonarState.Range, GridRes);

                foreach (var c in sonarState.Contacts)
                {
                    snap.sonar.contacts.Add(new SonarContactData
                    {
                        distance = Safe(c.Distance),
                        bearing = Safe(c.Bearing),
                        type = (int)c.Type,
                    });
                }

                BuildExtendedSonar(snap.sonar, bridge.Map, s, sonarState.Range);
            }

            return snap;
        }

        static void BuildExtendedSonar(SonarData sonar, ProceduralMapData map, SubmarineState sub, float range)
        {
            // --- Side Profile: 64 samples along heading direction ---
            const int PSamples = 64;
            float headRad = sub.Heading * (float)Math.PI / 180f;
            float dx = (float)Math.Sin(headRad);
            float dz = (float)Math.Cos(headRad);

            float[] pf = new float[PSamples], pc = new float[PSamples];
            float pMin = sub.Depth, pMax = sub.Depth;
            for (int i = 0; i < PSamples; i++)
            {
                float dist = (float)i / (PSamples - 1) * range;
                float wx = sub.PositionX + dx * dist;
                float wz = sub.PositionZ + dz * dist;
                pf[i] = map.GetFloorDepthAt(wx, wz);
                pc[i] = map.GetCeilingDepthAt(wx, wz);
                if (pf[i] > pMax) pMax = pf[i];
                if (pf[i] < pMin) pMin = pf[i];
                if (pc[i] > 0) { if (pc[i] < pMin) pMin = pc[i]; if (pc[i] > pMax) pMax = pc[i]; }
            }
            float pSpan = pMax - pMin;
            if (pSpan < 20f) { float mid = (pMax + pMin) * 0.5f; pMin = mid - 10f; pMax = mid + 10f; pSpan = 20f; }
            pMin -= pSpan * 0.05f; pMax += pSpan * 0.05f; pSpan = pMax - pMin;

            var pb = new byte[PSamples * 2];
            for (int i = 0; i < PSamples; i++)
            {
                pb[i * 2] = (byte)Math.Max(0, Math.Min(255, (int)((pf[i] - pMin) / pSpan * 255f)));
                // ceil: 0 = no ceiling, 1-255 = quantized depth
                pb[i * 2 + 1] = pc[i] > 0 ? (byte)Math.Max(1, Math.Min(255, (int)((pc[i] - pMin) / pSpan * 255f))) : (byte)0;
            }
            sonar.profile = Convert.ToBase64String(pb);
            sonar.profileSamples = PSamples;
            sonar.profileDepthMin = Safe(pMin);
            sonar.profileDepthMax = Safe(pMax);

            // --- 3D Depth Grid: 32x32 floor depths ---
            const int DRes = 32;
            float[] dv = new float[DRes * DRes];
            float dMin = float.MaxValue, dMax = float.MinValue;
            for (int j = 0; j < DRes; j++)
            {
                float wz = sub.PositionZ + ((float)j / DRes - 0.5f) * 2f * range;
                for (int i = 0; i < DRes; i++)
                {
                    float wx = sub.PositionX + ((float)i / DRes - 0.5f) * 2f * range;
                    float d = map.GetFloorDepthAt(wx, wz);
                    dv[j * DRes + i] = d;
                    if (d < dMin) dMin = d;
                    if (d > dMax) dMax = d;
                }
            }
            float dSpan = dMax - dMin;
            if (dSpan < 1f) dSpan = 1f;

            var db = new byte[DRes * DRes];
            for (int i = 0; i < db.Length; i++)
                db[i] = (byte)Math.Max(0, Math.Min(255, (int)((dv[i] - dMin) / dSpan * 255f)));

            sonar.depthGrid = Convert.ToBase64String(db);
            sonar.depthGridRes = DRes;
            sonar.depthGridMin = Safe(dMin);
            sonar.depthGridMax = Safe(dMax);
        }

        // Cell classes: c=clear(outside circle), o=open, w=wall, a=ascend, d=dive
        static readonly char[] CellChar = { 'c', 'o', 'w', 'a', 'd' };

        static string ClassifyAndEncode(ProceduralMapData map, SubmarineState sub, float range, int res)
        {
            const float margin = 8f;
            const float minGap = 15f;

            var cell = new byte[res * res];
            for (int j = 0; j < res; j++)
            {
                float v = (j / (float)res - 0.5f) * 2f;
                for (int i = 0; i < res; i++)
                {
                    float u = (i / (float)res - 0.5f) * 2f;
                    int idx = j * res + i;
                    if (u * u + v * v > 1f) { cell[idx] = 0; continue; }

                    float worldX = sub.PositionX + u * range;
                    float worldZ = sub.PositionZ + v * range;
                    float floor = map.GetFloorDepthAt(worldX, worldZ);
                    float ceiling = map.GetCeilingDepthAt(worldX, worldZ);
                    bool blocked = floor <= sub.Depth + margin || (ceiling > 0f && ceiling >= sub.Depth - margin);
                    if (!blocked) { cell[idx] = 1; continue; }

                    bool passable = floor > 0f && (floor - ceiling) >= minGap;
                    if (!passable) { cell[idx] = 2; continue; }
                    if (sub.Depth > floor) cell[idx] = 3;
                    else if (sub.Depth < ceiling) cell[idx] = 4;
                    else { float mid = (ceiling + floor) * 0.5f; cell[idx] = sub.Depth < mid ? (byte)3 : (byte)4; }
                }
            }

            // Surface-only pass: interior rock → open
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    int idx = j * res + i;
                    if (cell[idx] < 2) continue;
                    bool onSurface =
                        (i > 0 && cell[idx - 1] == 1) ||
                        (i < res - 1 && cell[idx + 1] == 1) ||
                        (j > 0 && cell[idx - res] == 1) ||
                        (j < res - 1 && cell[idx + res] == 1);
                    if (!onSurface) cell[idx] = 1;
                }
            }

            // RLE encode
            var sb = new System.Text.StringBuilder(512);
            byte cur = cell[0];
            int count = 1;
            for (int i = 1; i < cell.Length; i++)
            {
                if (cell[i] == cur) { count++; continue; }
                sb.Append(CellChar[cur]);
                sb.Append(count);
                cur = cell[i];
                count = 1;
            }
            sb.Append(CellChar[cur]);
            sb.Append(count);
            return sb.ToString();
        }
    }
}
