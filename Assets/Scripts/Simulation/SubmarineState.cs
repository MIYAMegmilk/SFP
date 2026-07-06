using System;

namespace SFP.Simulation
{
    public sealed class SubmarineState
    {
        public float Depth;
        public float Velocity;

        public float Heading;
        public float HorizontalSpeed;
        public float PositionX;
        public float PositionZ;

        public float DryMass;
        public float HullVolume;
        public float CrushDepth = 500f;

        public float DragCoefficient = 102500f;
        public float HorizontalDragCoefficient = 20000f;

        const float WaterDensity = 1025f;
        const float Gravity = 9.81f;

        public float NetForce { get; private set; }
        public float TotalWaterVolume { get; private set; }
        public bool IsBelowCrushDepth => Depth > CrushDepth;

        public void Tick(float dt, CompartmentGraph graph)
        {
            Tick(dt, graph, 0f, 0f);
        }

        public void Tick(float dt, CompartmentGraph graph, float engineThrust, float rudderInput)
        {
            float totalWater = 0f;
            foreach (var c in graph.Compartments)
                totalWater += c.WaterVolume;
            TotalWaterVolume = totalWater;

            float waterMass = totalWater * WaterDensity;
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

            // Horizontal
            Heading += rudderInput * 30f * dt;
            if (Heading > 360f) Heading -= 360f;
            if (Heading < 0f) Heading += 360f;

            float horizDrag = -HorizontalDragCoefficient * HorizontalSpeed * Math.Abs(HorizontalSpeed);
            float horizAccel = (engineThrust + horizDrag) / totalMass;
            HorizontalSpeed += horizAccel * dt;

            float rad = Heading * ((float)Math.PI / 180f);
            PositionX += (float)Math.Sin(rad) * HorizontalSpeed * dt;
            PositionZ += (float)Math.Cos(rad) * HorizontalSpeed * dt;

            graph.SeaLevelY = Depth;
        }
    }
}
