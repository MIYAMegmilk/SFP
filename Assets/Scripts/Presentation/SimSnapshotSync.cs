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
            serializer.SerializeValue(ref Timestamp);

            SerializeArray(serializer, ref WaterVolumes);
            SerializeArray(serializer, ref OxygenLevels);
            SerializeArray(serializer, ref Pressures);
            SerializeArray(serializer, ref FireIntensities);
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
        }
    }
}
