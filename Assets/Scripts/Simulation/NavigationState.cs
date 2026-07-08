namespace SFP.Simulation
{
    public sealed class NavigationState
    {
        public float DesiredDepth = 200f;
        public float DesiredHeading;
        public float DesiredSpeed;
        public bool AutoPilotEnabled;
        // Depth hold runs independently of the speed/heading autopilot: manual ballast input
        // at a pump console disarms it, setting a new depth target at the helm re-arms it.
        public bool DepthHoldEnabled = true;

        // DryMass is trimmed so this fill level is neutrally buoyant (Archimedes).
        const float NeutralFill = 0.5f;
        // P gain: full fill authority (±0.5) at 25 m of depth error.
        const float FillPerMeterError = 0.02f;
        // D gain: damps the approach — cancels the P term of a 25 m error at ~2 m/s
        // vertical speed, so the boat coasts into the target instead of overshooting.
        const float FillPerVerticalSpeed = 0.25f;

        public void Tick(float dt, SubmarineState sub, EngineState engine, BallastTankState[] ballasts)
        {
            if (DepthHoldEnabled)
            {
                float depthError = DesiredDepth - sub.Depth; // >0 → need to sink
                float fill = NeutralFill
                    + FillPerMeterError * depthError
                    - FillPerVerticalSpeed * sub.Velocity;
                if (fill < 0f) fill = 0f;
                if (fill > 1f) fill = 1f;

                for (int i = 0; i < ballasts.Length; i++)
                    ballasts[i].TargetFillLevel = fill;
            }

            if (!AutoPilotEnabled) return;

            // Speed → throttle
            if (engine != null)
            {
                float speedError = DesiredSpeed - sub.HorizontalSpeed;
                float throttle = engine.ThrottleSetting + speedError * 0.1f * dt;
                if (throttle > 1f) throttle = 1f;
                if (throttle < -1f) throttle = -1f;
                engine.ThrottleSetting = throttle;
            }

            // Heading → rudder
            float headingError = DesiredHeading - sub.Heading;
            if (headingError > 180f) headingError -= 360f;
            if (headingError < -180f) headingError += 360f;
            // P-controller: full rudder at ≥15° error, proportional below
            float rudder = headingError / 15f;
            if (rudder > 1f) rudder = 1f;
            if (rudder < -1f) rudder = -1f;
            sub.RudderAngle = rudder;
        }
    }
}
