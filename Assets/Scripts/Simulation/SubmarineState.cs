using System;

namespace SFP.Simulation
{
    public sealed class SubmarineState
    {
        public float Depth;
        public float Velocity;

        public float Heading;
        // World-space horizontal velocity (m/s). Momentum lives in the world frame — turning
        // the hull does NOT redirect it; only thrust and hull drag gradually swing it around.
        public float VelocityX;
        public float VelocityZ;
        // Signed speed along the hull axis (surge). >0 = making way ahead.
        public float HorizontalSpeed { get; private set; }
        public float PositionX;
        public float PositionZ;

        // -1 (full port) to +1 (full starboard)
        public float RudderAngle;
        // deg/s at full rudder and sufficient speed — big boats turn slowly.
        public float MaxTurnRate = 8f;
        // First-order yaw lag (s): the hull needs seconds to build up or shed turn rate
        // (rotational inertia), so the boat eases into and out of turns.
        public float TurnResponseTime = 3f;
        public float TurnRate { get; private set; }

        public float PrevPositionX { get; private set; }
        public float PrevPositionZ { get; private set; }

        public float DryMass;
        public float HullVolume;
        public float CrushDepth = 500f;

        // m³ of water in the external main ballast tanks; assigned by the bridge before each
        // tick. Adds mass (Archimedes) but is not part of the interior flooding volume.
        public float ExternalBallastVolume;

        public float DragCoefficient = 102500f;
        // Surge (bow-on): 0.5 * ρ * Cd * A ≈ 0.5 * 1025 * 0.04 * 30 ≈ 615 — streamlined.
        public float HorizontalDragCoefficient = 600f;
        // Sway (broadside): bluff body, 0.5 * ρ * Cd * A ≈ 0.5 * 1025 * 1.0 * (24m × 18m) ≈ 220000.
        // Kills lateral slip within seconds, so momentum carves onto the new course.
        public float LateralDragCoefficient = 220000f;

        const float WaterDensity = 1025f;
        const float Gravity = 9.81f;

        public float NetForce { get; private set; }
        public float TotalWaterVolume { get; private set; }
        public bool IsBelowCrushDepth => Depth > CrushDepth;

        public float NoiseLevel { get; private set; }
        const float NoiseThrustNormalization = 50000f; // matches EngineState.MaxThrust

        public void Tick(float dt, CompartmentGraph graph)
        {
            Tick(dt, graph, 0f);
        }

        public void Tick(float dt, CompartmentGraph graph, float engineThrust,
            float oceanCurrentX = 0f, float oceanCurrentZ = 0f)
        {
            PrevPositionX = PositionX;
            PrevPositionZ = PositionZ;

            float totalWater = 0f;
            foreach (var c in graph.Compartments)
                totalWater += c.WaterVolume;
            TotalWaterVolume = totalWater;

            float waterMass = (totalWater + ExternalBallastVolume) * WaterDensity;
            float totalMass = DryMass + waterMass;
            if (totalMass < 1f) totalMass = 1f;

            // Vertical
            float buoyancy = WaterDensity * Gravity * HullVolume;
            float weight = totalMass * Gravity;
            NetForce = weight - buoyancy;
            float vertDrag = -DragCoefficient * Velocity * Math.Abs(Velocity);
            float vertAccel = (NetForce + vertDrag) / totalMass;
            Velocity += vertAccel * dt;
            Depth += Velocity * dt;

            if (Depth < 0f)
            {
                Depth = 0f;
                if (Velocity < 0f) Velocity = 0f;
            }

            // Heading: rudder commands a target yaw rate (needs way on to bite); the hull
            // approaches it through a first-order lag — rotational inertia.
            float speedFactor = Math.Clamp(Math.Abs(HorizontalSpeed) / 2f, 0f, 1f);
            float rudder = Math.Clamp(RudderAngle, -1f, 1f);
            float targetTurnRate = rudder * MaxTurnRate * speedFactor;
            float yawLag = TurnResponseTime > 0f ? Math.Clamp(dt / TurnResponseTime, 0f, 1f) : 1f;
            TurnRate += (targetTurnRate - TurnRate) * yawLag;
            Heading += TurnRate * dt;
            if (Heading > 360f) Heading -= 360f;
            if (Heading < 0f) Heading += 360f;

            // Horizontal: decompose world-frame momentum into surge (along hull) and sway
            // (broadside). Thrust acts on surge only; each axis gets its own quadratic drag,
            // so after a turn the boat keeps sliding until sway drag bleeds the old momentum.
            float rad = Heading * ((float)Math.PI / 180f);
            float fwdX = (float)Math.Sin(rad), fwdZ = (float)Math.Cos(rad);
            float latX = fwdZ, latZ = -fwdX;
            float vSurge = VelocityX * fwdX + VelocityZ * fwdZ;
            float vSway = VelocityX * latX + VelocityZ * latZ;

            float surgeAccel = (engineThrust - HorizontalDragCoefficient * vSurge * Math.Abs(vSurge)) / totalMass;
            float swayAccel = -LateralDragCoefficient * vSway * Math.Abs(vSway) / totalMass;

            VelocityX += (surgeAccel * fwdX + swayAccel * latX) * dt;
            VelocityZ += (surgeAccel * fwdZ + swayAccel * latZ) * dt;
            HorizontalSpeed = VelocityX * fwdX + VelocityZ * fwdZ;

            PositionX += (VelocityX + oceanCurrentX) * dt;
            PositionZ += (VelocityZ + oceanCurrentZ) * dt;

            NoiseLevel = Math.Clamp(
                Math.Abs(engineThrust) / NoiseThrustNormalization * 0.7f
                + HorizontalSpeedMagnitude / 8f * 0.3f, 0f, 1f);

            graph.SeaLevelY = Depth;
        }

        public float HorizontalSpeedMagnitude =>
            (float)Math.Sqrt(VelocityX * VelocityX + VelocityZ * VelocityZ);

        public void RevertHorizontal()
        {
            PositionX = PrevPositionX;
            PositionZ = PrevPositionZ;
        }

        public void ScaleHorizontalVelocity(float factor)
        {
            VelocityX *= factor;
            VelocityZ *= factor;
        }
    }
}
