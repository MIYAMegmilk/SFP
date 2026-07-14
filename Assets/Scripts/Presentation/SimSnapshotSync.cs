using Unity.Netcode;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public struct SimSnapshot : INetworkSerializable
    {
        public float Depth;
        public float Heading;
        public float Speed;
        public float PositionX;
        public float PositionZ;

        public float Throttle;
        public float Rudder;

        public float PowerVoltage;

        public float[] WaterVolumes;
        public float[] OxygenLevels;
        public float[] Pressures;
        public float[] FireIntensities;

        // Bit i = Openings[i].IsOpen. 32 bits covers every authored opening with room to spare.
        public uint OpeningBitfield;

        // Navigation
        public float NavDesiredDepth;
        public float NavDesiredHeading;
        public float NavDesiredSpeed;
        // bit0=AutoPilotEnabled, bit1=DepthHoldEnabled
        public byte NavFlags;

        // External main ballast tanks
        public float[] BallastFillLevels;

        // Power grid — reactors and batteries
        public float[] ReactorFission;
        public float[] ReactorTurbine;
        public float[] ReactorTemp;
        public float[] BatteryCharge;

        // Device on/off state, bit-packed:
        // bit 0-13: BilgePumps[0..13].IsActive
        // bit 14-16: Vents[0..2].IsEnabled
        // bit 17-18: O2Generators[0..1].IsEnabled
        // bit 19-20: CO2Scrubbers[0..1].IsEnabled
        // bit 21-22: SuppressionSystems[0..1].IsActive
        public uint DeviceEnabledBits;

        // Airlock (cast from AirlockPhase enum)
        public byte AirlockPhase;

        // Sonar: bit0=IsActive, bit1=IsPassive
        public byte SonarFlags;

        // Turret
        public float TurretRotation;
        public float TurretElevation;
        public int TurretAmmo;

        // Hull integrity, one entry per registered HullSection
        public float[] HullIntegrities;

        // Creatures — fixed-size slots, up to CreatureSystem.MaxActiveCreatures
        public float[] CreatureX;
        public float[] CreatureZ;
        public float[] CreatureDepth;
        public float[] CreatureHealth;
        // bit i = creature slot i is alive
        public byte CreatureAliveBits;

        public float Timestamp;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Depth);
            serializer.SerializeValue(ref Heading);
            serializer.SerializeValue(ref Speed);
            serializer.SerializeValue(ref PositionX);
            serializer.SerializeValue(ref PositionZ);
            serializer.SerializeValue(ref Throttle);
            serializer.SerializeValue(ref Rudder);
            serializer.SerializeValue(ref PowerVoltage);
            serializer.SerializeValue(ref OpeningBitfield);

            serializer.SerializeValue(ref NavDesiredDepth);
            serializer.SerializeValue(ref NavDesiredHeading);
            serializer.SerializeValue(ref NavDesiredSpeed);
            serializer.SerializeValue(ref NavFlags);

            serializer.SerializeValue(ref DeviceEnabledBits);
            serializer.SerializeValue(ref AirlockPhase);
            serializer.SerializeValue(ref SonarFlags);

            serializer.SerializeValue(ref TurretRotation);
            serializer.SerializeValue(ref TurretElevation);
            serializer.SerializeValue(ref TurretAmmo);

            serializer.SerializeValue(ref CreatureAliveBits);

            serializer.SerializeValue(ref Timestamp);

            SerializeArray(serializer, ref WaterVolumes);
            SerializeArray(serializer, ref OxygenLevels);
            SerializeArray(serializer, ref Pressures);
            SerializeArray(serializer, ref FireIntensities);

            SerializeArray(serializer, ref BallastFillLevels);
            SerializeArray(serializer, ref ReactorFission);
            SerializeArray(serializer, ref ReactorTurbine);
            SerializeArray(serializer, ref ReactorTemp);
            SerializeArray(serializer, ref BatteryCharge);
            SerializeArray(serializer, ref HullIntegrities);

            SerializeArray(serializer, ref CreatureX);
            SerializeArray(serializer, ref CreatureZ);
            SerializeArray(serializer, ref CreatureDepth);
            SerializeArray(serializer, ref CreatureHealth);
        }

        static void SerializeArray<T>(BufferSerializer<T> serializer, ref float[] array) where T : IReaderWriter
        {
            int length = array?.Length ?? 0;
            serializer.SerializeValue(ref length);
            if (serializer.IsReader)
                array = new float[length];
            for (int i = 0; i < length; i++)
                serializer.SerializeValue(ref array[i]);
        }
    }

    public class SimSnapshotSync : NetworkBehaviour
    {
        public static SimSnapshotSync Instance { get; private set; }

        // Interpolated PowerGrid.GridVoltage for remote clients: the grid's value has no public
        // setter (server-computed each tick), so it cannot be written back into PowerGrid. UI
        // that needs the client-side smoothed voltage should read this instead.
        public float LatestPowerVoltage { get; private set; }

        const float SendInterval = 0.1f; // 10Hz
        const int BufferSize = 3;

        // Device bit-packing layout (see SimSnapshot.DeviceEnabledBits).
        const int MaxBilgePumpBits = 14;
        const int MaxVents = 3;
        const int MaxOxygenGenerators = 2;
        const int MaxCo2Scrubbers = 2;
        const int MaxSuppressionSystems = 2;
        const int VentBitOffset = MaxBilgePumpBits;
        const int OxygenBitOffset = VentBitOffset + MaxVents;
        const int ScrubberBitOffset = OxygenBitOffset + MaxOxygenGenerators;
        const int SuppressionBitOffset = ScrubberBitOffset + MaxCo2Scrubbers;

        // Creature slots mirror CreatureSystem.MaxActiveCreatures.
        const int MaxCreatures = 6;

        float _sendTimer;

        readonly SimSnapshot[] _buffer = new SimSnapshot[BufferSize];
        int _bufferCount;
        int _writeIndex;

        public override void OnNetworkSpawn()
        {
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (IsServer)
            {
                _sendTimer += Time.deltaTime;
                if (_sendTimer >= SendInterval)
                {
                    _sendTimer -= SendInterval;
                    if (SimulationBridge.Instance != null)
                        SendSnapshotClientRpc(BuildSnapshot());
                }
                return;
            }

            // Host runs the authoritative simulation locally, so only remote (non-host) clients
            // interpolate and apply received snapshots.
            if (_bufferCount < 2)
                return;

            int latestIdx = (_writeIndex - 1 + BufferSize) % BufferSize;
            int prevIdx = (_writeIndex - 2 + BufferSize) % BufferSize;
            SimSnapshot prevSnap = _buffer[prevIdx];
            SimSnapshot currSnap = _buffer[latestIdx];

            float span = currSnap.Timestamp - prevSnap.Timestamp;
            float t = span > 0f ? (Time.time - prevSnap.Timestamp) / span : 1f;
            t = Mathf.Clamp01(t);

            ApplySnapshot(Interpolate(prevSnap, currSnap, t));
        }

        [ClientRpc]
        void SendSnapshotClientRpc(SimSnapshot snapshot)
        {
            if (IsServer)
                return; // host already has authoritative state; no need to buffer its own broadcast

            _buffer[_writeIndex] = snapshot;
            _writeIndex = (_writeIndex + 1) % BufferSize;
            if (_bufferCount < BufferSize)
                _bufferCount++;
        }

        SimSnapshot BuildSnapshot()
        {
            var bridge = SimulationBridge.Instance;
            var graph = bridge.Graph;
            var compartments = graph.Compartments;
            int compCount = compartments.Count;

            var waterVolumes = new float[compCount];
            var oxygenLevels = new float[compCount];
            var pressures = new float[compCount];
            var fireIntensities = new float[compCount];

            for (int i = 0; i < compCount; i++)
            {
                var c = compartments[i];
                waterVolumes[i] = c.WaterVolume;
                oxygenLevels[i] = bridge.Atmosphere != null ? bridge.Atmosphere.GetOxygenLevel(c.Id) : 0f;
                pressures[i] = c.AirPressureAtm;
                fireIntensities[i] = bridge.FireSystem != null ? bridge.FireSystem.GetFireIntensity(c.Id) : 0f;
            }

            var openings = graph.Openings;
            int openingCount = Mathf.Min(openings.Count, 32);
            uint openingBits = 0;
            for (int i = 0; i < openingCount; i++)
            {
                if (openings[i].IsOpen)
                    openingBits |= 1u << i;
            }

            // Navigation
            var nav = bridge.Navigation;
            float navDesiredDepth = nav != null ? nav.DesiredDepth : 0f;
            float navDesiredHeading = nav != null ? nav.DesiredHeading : 0f;
            float navDesiredSpeed = nav != null ? nav.DesiredSpeed : 0f;
            byte navFlags = 0;
            if (nav != null)
            {
                if (nav.AutoPilotEnabled) navFlags |= 1;
                if (nav.DepthHoldEnabled) navFlags |= 2;
            }

            // Ballast tanks
            var ballasts = bridge.Ballasts;
            int ballastCount = ballasts?.Length ?? 0;
            var ballastFillLevels = new float[ballastCount];
            for (int i = 0; i < ballastCount; i++)
                ballastFillLevels[i] = ballasts[i].CurrentFillLevel;

            // Power grid — reactors & batteries
            var powerGrid = bridge.PowerGrid;
            int reactorCount = powerGrid != null ? powerGrid.Reactors.Count : 0;
            var reactorFission = new float[reactorCount];
            var reactorTurbine = new float[reactorCount];
            var reactorTemp = new float[reactorCount];
            for (int i = 0; i < reactorCount; i++)
            {
                var r = powerGrid.Reactors[i];
                reactorFission[i] = r.FissionRate;
                reactorTurbine[i] = r.TurbineOutput;
                reactorTemp[i] = r.Temperature;
            }

            int batteryCount = powerGrid != null ? powerGrid.Batteries.Count : 0;
            var batteryCharge = new float[batteryCount];
            for (int i = 0; i < batteryCount; i++)
                batteryCharge[i] = powerGrid.Batteries[i].Charge;

            // Device enabled bits
            uint deviceBits = 0;
            var bilgePumps = bridge.BilgePumps;
            int bilgePumpBitCount = Mathf.Min(bilgePumps.Count, MaxBilgePumpBits);
            for (int i = 0; i < bilgePumpBitCount; i++)
            {
                if (bilgePumps[i].IsActive)
                    deviceBits |= 1u << i;
            }
            for (int i = 0; i < MaxVents; i++)
            {
                var vent = bridge.GetVent(i);
                if (vent != null && vent.IsEnabled)
                    deviceBits |= 1u << (VentBitOffset + i);
            }
            for (int i = 0; i < MaxOxygenGenerators; i++)
            {
                var gen = bridge.GetOxygenGenerator(i);
                if (gen != null && gen.IsEnabled)
                    deviceBits |= 1u << (OxygenBitOffset + i);
            }
            for (int i = 0; i < MaxCo2Scrubbers; i++)
            {
                var scrubber = bridge.GetScrubber(i);
                if (scrubber != null && scrubber.IsEnabled)
                    deviceBits |= 1u << (ScrubberBitOffset + i);
            }
            for (int i = 0; i < MaxSuppressionSystems; i++)
            {
                var supp = bridge.GetSuppression(i);
                if (supp != null && supp.IsActive)
                    deviceBits |= 1u << (SuppressionBitOffset + i);
            }

            // Airlock
            var airlocks = bridge.Airlocks;
            byte airlockPhase = airlocks.Count > 0 ? (byte)airlocks[0].Phase : (byte)0;

            // Sonar
            var sonar = bridge.GetSonar(0);
            byte sonarFlags = 0;
            if (sonar != null)
            {
                if (sonar.IsActive) sonarFlags |= 1;
                if (sonar.IsPassive) sonarFlags |= 2;
            }

            // Turret
            var turret = bridge.GetTurret(0);
            float turretRotation = turret != null ? turret.Rotation : 0f;
            float turretElevation = turret != null ? turret.Elevation : 0f;
            int turretAmmo = turret != null ? turret.AmmoCount : 0;

            // Hull integrity
            var sections = bridge.DamageSystem != null ? bridge.DamageSystem.Sections : null;
            int sectionCount = sections?.Count ?? 0;
            var hullIntegrities = new float[sectionCount];
            for (int i = 0; i < sectionCount; i++)
                hullIntegrities[i] = sections[i].Integrity;

            // Creatures
            var creatureList = bridge.Creatures != null ? bridge.Creatures.Creatures : null;
            var creatureX = new float[MaxCreatures];
            var creatureZ = new float[MaxCreatures];
            var creatureDepth = new float[MaxCreatures];
            var creatureHealth = new float[MaxCreatures];
            byte creatureAliveBits = 0;
            if (creatureList != null)
            {
                int n = Mathf.Min(creatureList.Count, MaxCreatures);
                for (int i = 0; i < n; i++)
                {
                    var c = creatureList[i];
                    creatureX[i] = c.X;
                    creatureZ[i] = c.Z;
                    creatureDepth[i] = c.Depth;
                    creatureHealth[i] = c.Health;
                    if (!c.IsDead)
                        creatureAliveBits |= (byte)(1 << i);
                }
            }

            var sub = bridge.SubState;
            return new SimSnapshot
            {
                Depth = sub.Depth,
                Heading = sub.Heading,
                Speed = sub.HorizontalSpeed,
                PositionX = sub.PositionX,
                PositionZ = sub.PositionZ,
                Throttle = bridge.Engine != null ? bridge.Engine.ThrottleSetting : 0f,
                Rudder = sub.RudderAngle,
                PowerVoltage = bridge.PowerGrid != null ? Mathf.Clamp01(bridge.PowerGrid.GridVoltage) : 0f,
                WaterVolumes = waterVolumes,
                OxygenLevels = oxygenLevels,
                Pressures = pressures,
                FireIntensities = fireIntensities,
                OpeningBitfield = openingBits,

                NavDesiredDepth = navDesiredDepth,
                NavDesiredHeading = navDesiredHeading,
                NavDesiredSpeed = navDesiredSpeed,
                NavFlags = navFlags,

                BallastFillLevels = ballastFillLevels,

                ReactorFission = reactorFission,
                ReactorTurbine = reactorTurbine,
                ReactorTemp = reactorTemp,
                BatteryCharge = batteryCharge,

                DeviceEnabledBits = deviceBits,
                AirlockPhase = airlockPhase,
                SonarFlags = sonarFlags,

                TurretRotation = turretRotation,
                TurretElevation = turretElevation,
                TurretAmmo = turretAmmo,

                HullIntegrities = hullIntegrities,

                CreatureX = creatureX,
                CreatureZ = creatureZ,
                CreatureDepth = creatureDepth,
                CreatureHealth = creatureHealth,
                CreatureAliveBits = creatureAliveBits,

                Timestamp = Time.time,
            };
        }

        static SimSnapshot Interpolate(SimSnapshot prev, SimSnapshot curr, float t)
        {
            var result = curr;
            result.Depth = Mathf.Lerp(prev.Depth, curr.Depth, t);
            result.Heading = Mathf.LerpAngle(prev.Heading, curr.Heading, t);
            result.Speed = Mathf.Lerp(prev.Speed, curr.Speed, t);
            result.PositionX = Mathf.Lerp(prev.PositionX, curr.PositionX, t);
            result.PositionZ = Mathf.Lerp(prev.PositionZ, curr.PositionZ, t);
            result.Throttle = Mathf.Lerp(prev.Throttle, curr.Throttle, t);
            result.Rudder = Mathf.Lerp(prev.Rudder, curr.Rudder, t);
            result.PowerVoltage = Mathf.Lerp(prev.PowerVoltage, curr.PowerVoltage, t);
            // Discrete state (door/hatch open-closed): snap to the newest snapshot rather than blend.
            result.OpeningBitfield = curr.OpeningBitfield;

            int compCount = curr.WaterVolumes?.Length ?? 0;
            result.WaterVolumes = new float[compCount];
            result.OxygenLevels = new float[compCount];
            result.Pressures = new float[compCount];
            result.FireIntensities = new float[compCount];
            for (int i = 0; i < compCount; i++)
            {
                result.WaterVolumes[i] = Mathf.Lerp(LerpSource(prev.WaterVolumes, curr.WaterVolumes, i), curr.WaterVolumes[i], t);
                result.OxygenLevels[i] = Mathf.Lerp(LerpSource(prev.OxygenLevels, curr.OxygenLevels, i), curr.OxygenLevels[i], t);
                result.Pressures[i] = Mathf.Lerp(LerpSource(prev.Pressures, curr.Pressures, i), curr.Pressures[i], t);
                result.FireIntensities[i] = Mathf.Lerp(LerpSource(prev.FireIntensities, curr.FireIntensities, i), curr.FireIntensities[i], t);
            }

            // Navigation — heading wraps, everything else blends linearly.
            result.NavDesiredDepth = Mathf.Lerp(prev.NavDesiredDepth, curr.NavDesiredDepth, t);
            result.NavDesiredHeading = Mathf.LerpAngle(prev.NavDesiredHeading, curr.NavDesiredHeading, t);
            result.NavDesiredSpeed = Mathf.Lerp(prev.NavDesiredSpeed, curr.NavDesiredSpeed, t);
            result.NavFlags = curr.NavFlags;

            int ballastCount = curr.BallastFillLevels?.Length ?? 0;
            result.BallastFillLevels = new float[ballastCount];
            for (int i = 0; i < ballastCount; i++)
                result.BallastFillLevels[i] = Mathf.Lerp(LerpSource(prev.BallastFillLevels, curr.BallastFillLevels, i), curr.BallastFillLevels[i], t);

            int reactorCount = curr.ReactorFission?.Length ?? 0;
            result.ReactorFission = new float[reactorCount];
            result.ReactorTurbine = new float[reactorCount];
            result.ReactorTemp = new float[reactorCount];
            for (int i = 0; i < reactorCount; i++)
            {
                result.ReactorFission[i] = Mathf.Lerp(LerpSource(prev.ReactorFission, curr.ReactorFission, i), curr.ReactorFission[i], t);
                result.ReactorTurbine[i] = Mathf.Lerp(LerpSource(prev.ReactorTurbine, curr.ReactorTurbine, i), curr.ReactorTurbine[i], t);
                result.ReactorTemp[i] = Mathf.Lerp(LerpSource(prev.ReactorTemp, curr.ReactorTemp, i), curr.ReactorTemp[i], t);
            }

            int batteryCount = curr.BatteryCharge?.Length ?? 0;
            result.BatteryCharge = new float[batteryCount];
            for (int i = 0; i < batteryCount; i++)
                result.BatteryCharge[i] = Mathf.Lerp(LerpSource(prev.BatteryCharge, curr.BatteryCharge, i), curr.BatteryCharge[i], t);

            // Discrete device/system state: snap to newest snapshot rather than blend.
            result.DeviceEnabledBits = curr.DeviceEnabledBits;
            result.AirlockPhase = curr.AirlockPhase;
            result.SonarFlags = curr.SonarFlags;
            result.TurretAmmo = curr.TurretAmmo;
            result.CreatureAliveBits = curr.CreatureAliveBits;

            result.TurretRotation = Mathf.LerpAngle(prev.TurretRotation, curr.TurretRotation, t);
            result.TurretElevation = Mathf.Lerp(prev.TurretElevation, curr.TurretElevation, t);

            int sectionCount = curr.HullIntegrities?.Length ?? 0;
            result.HullIntegrities = new float[sectionCount];
            for (int i = 0; i < sectionCount; i++)
                result.HullIntegrities[i] = Mathf.Lerp(LerpSource(prev.HullIntegrities, curr.HullIntegrities, i), curr.HullIntegrities[i], t);

            int creatureCount = curr.CreatureX?.Length ?? 0;
            result.CreatureX = new float[creatureCount];
            result.CreatureZ = new float[creatureCount];
            result.CreatureDepth = new float[creatureCount];
            result.CreatureHealth = new float[creatureCount];
            for (int i = 0; i < creatureCount; i++)
            {
                result.CreatureX[i] = Mathf.Lerp(LerpSource(prev.CreatureX, curr.CreatureX, i), curr.CreatureX[i], t);
                result.CreatureZ[i] = Mathf.Lerp(LerpSource(prev.CreatureZ, curr.CreatureZ, i), curr.CreatureZ[i], t);
                result.CreatureDepth[i] = Mathf.Lerp(LerpSource(prev.CreatureDepth, curr.CreatureDepth, i), curr.CreatureDepth[i], t);
                result.CreatureHealth[i] = Mathf.Lerp(LerpSource(prev.CreatureHealth, curr.CreatureHealth, i), curr.CreatureHealth[i], t);
            }

            return result;
        }

        // Falls back to the current-snapshot value when the previous snapshot's array is shorter
        // (e.g. the very first buffered snapshot), so interpolation never indexes out of range.
        static float LerpSource(float[] prevArr, float[] currArr, int i)
        {
            return prevArr != null && i < prevArr.Length ? prevArr[i] : currArr[i];
        }

        void ApplySnapshot(SimSnapshot snapshot)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null)
                return;

            var sub = bridge.SubState;
            if (sub != null)
            {
                sub.Depth = snapshot.Depth;
                sub.Heading = snapshot.Heading;
                sub.PositionX = snapshot.PositionX;
                sub.PositionZ = snapshot.PositionZ;
                sub.RudderAngle = snapshot.Rudder;
                // HorizontalSpeed/Velocity are computed by SubmarineState.Tick each frame and have
                // no public setter; Snapshot.Speed is display-only on remote clients.
            }

            if (bridge.Engine != null)
                bridge.Engine.ThrottleSetting = snapshot.Throttle;

            LatestPowerVoltage = snapshot.PowerVoltage;

            var graph = bridge.Graph;
            if (graph != null)
            {
                var compartments = graph.Compartments;
                int compCount = Mathf.Min(compartments.Count, snapshot.WaterVolumes?.Length ?? 0);
                for (int i = 0; i < compCount; i++)
                {
                    compartments[i].WaterVolume = snapshot.WaterVolumes[i];
                    compartments[i].AirPressureAtm = snapshot.Pressures[i];
                }

                var openings = graph.Openings;
                int openingCount = Mathf.Min(openings.Count, 32);
                for (int i = 0; i < openingCount; i++)
                    openings[i].IsOpen = (snapshot.OpeningBitfield & (1u << i)) != 0;
            }

            if (bridge.Atmosphere != null && snapshot.OxygenLevels != null)
            {
                for (int i = 0; i < snapshot.OxygenLevels.Length; i++)
                    bridge.Atmosphere.SetOxygenLevel(i, snapshot.OxygenLevels[i]);
            }

            // FireSystem exposes only relative StartFire/Extinguish, not an absolute setter, so
            // apply the interpolated target as a delta against the client's current value.
            if (bridge.FireSystem != null && snapshot.FireIntensities != null)
            {
                for (int i = 0; i < snapshot.FireIntensities.Length; i++)
                {
                    float current = bridge.FireSystem.GetFireIntensity(i);
                    float delta = snapshot.FireIntensities[i] - current;
                    if (delta > 0f)
                        bridge.FireSystem.StartFire(i, delta);
                    else if (delta < 0f)
                        bridge.FireSystem.Extinguish(i, -delta);
                }
            }

            var nav = bridge.Navigation;
            if (nav != null)
            {
                nav.DesiredDepth = snapshot.NavDesiredDepth;
                nav.DesiredHeading = snapshot.NavDesiredHeading;
                nav.DesiredSpeed = snapshot.NavDesiredSpeed;
                nav.AutoPilotEnabled = (snapshot.NavFlags & 1) != 0;
                nav.DepthHoldEnabled = (snapshot.NavFlags & 2) != 0;
            }

            var ballasts = bridge.Ballasts;
            if (ballasts != null && snapshot.BallastFillLevels != null)
            {
                int ballastCount = Mathf.Min(ballasts.Length, snapshot.BallastFillLevels.Length);
                for (int i = 0; i < ballastCount; i++)
                    ballasts[i].CurrentFillLevel = snapshot.BallastFillLevels[i];
            }

            var powerGrid = bridge.PowerGrid;
            if (powerGrid != null)
            {
                if (snapshot.ReactorFission != null)
                {
                    int reactorCount = Mathf.Min(powerGrid.Reactors.Count, snapshot.ReactorFission.Length);
                    for (int i = 0; i < reactorCount; i++)
                    {
                        var r = powerGrid.Reactors[i];
                        r.FissionRate = snapshot.ReactorFission[i];
                        r.TurbineOutput = snapshot.ReactorTurbine[i];
                        r.Temperature = snapshot.ReactorTemp[i];
                    }
                }
                if (snapshot.BatteryCharge != null)
                {
                    int batteryCount = Mathf.Min(powerGrid.Batteries.Count, snapshot.BatteryCharge.Length);
                    for (int i = 0; i < batteryCount; i++)
                        powerGrid.Batteries[i].Charge = snapshot.BatteryCharge[i];
                }
            }

            var bilgePumps = bridge.BilgePumps;
            int bilgePumpBitCount = Mathf.Min(bilgePumps.Count, MaxBilgePumpBits);
            for (int i = 0; i < bilgePumpBitCount; i++)
                bilgePumps[i].IsActive = (snapshot.DeviceEnabledBits & (1u << i)) != 0;

            for (int i = 0; i < MaxVents; i++)
            {
                var vent = bridge.GetVent(i);
                if (vent != null)
                    vent.IsEnabled = (snapshot.DeviceEnabledBits & (1u << (VentBitOffset + i))) != 0;
            }
            for (int i = 0; i < MaxOxygenGenerators; i++)
            {
                var gen = bridge.GetOxygenGenerator(i);
                if (gen != null)
                    gen.IsEnabled = (snapshot.DeviceEnabledBits & (1u << (OxygenBitOffset + i))) != 0;
            }
            for (int i = 0; i < MaxCo2Scrubbers; i++)
            {
                var scrubber = bridge.GetScrubber(i);
                if (scrubber != null)
                    scrubber.IsEnabled = (snapshot.DeviceEnabledBits & (1u << (ScrubberBitOffset + i))) != 0;
            }
            for (int i = 0; i < MaxSuppressionSystems; i++)
            {
                var supp = bridge.GetSuppression(i);
                if (supp != null)
                    supp.IsActive = (snapshot.DeviceEnabledBits & (1u << (SuppressionBitOffset + i))) != 0;
            }

            var airlocks = bridge.Airlocks;
            if (airlocks.Count > 0)
                airlocks[0].Phase = (AirlockPhase)snapshot.AirlockPhase;

            var sonar = bridge.GetSonar(0);
            if (sonar != null)
            {
                sonar.IsActive = (snapshot.SonarFlags & 1) != 0;
                sonar.IsPassive = (snapshot.SonarFlags & 2) != 0;
            }

            var turret = bridge.GetTurret(0);
            if (turret != null)
            {
                turret.Rotation = snapshot.TurretRotation;
                turret.Elevation = snapshot.TurretElevation;
                // AmmoCount is informational on the client; the server is authoritative on fires.
            }

            var sections = bridge.DamageSystem != null ? bridge.DamageSystem.Sections : null;
            if (sections != null && snapshot.HullIntegrities != null)
            {
                int sectionCount = Mathf.Min(sections.Count, snapshot.HullIntegrities.Length);
                for (int i = 0; i < sectionCount; i++)
                    sections[i].Integrity = snapshot.HullIntegrities[i];
            }

            // Remote clients never run CreatureSystem.Tick (SimulationBridge.Update only ticks on
            // the server), so creatures only exist locally once spawned; this writes into whatever
            // slots are already present rather than spawning new entries.
            var creatureList = bridge.Creatures != null ? bridge.Creatures.Creatures : null;
            if (creatureList != null && snapshot.CreatureX != null)
            {
                int creatureCount = Mathf.Min(creatureList.Count, snapshot.CreatureX.Length);
                for (int i = 0; i < creatureCount; i++)
                {
                    var c = creatureList[i];
                    c.X = snapshot.CreatureX[i];
                    c.Z = snapshot.CreatureZ[i];
                    c.Depth = snapshot.CreatureDepth[i];
                    c.Health = snapshot.CreatureHealth[i];
                    c.IsDead = (snapshot.CreatureAliveBits & (1 << i)) == 0;
                }
            }
        }
    }
}
