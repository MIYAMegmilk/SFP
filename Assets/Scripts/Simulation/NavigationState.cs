namespace SFP.Simulation
{
    public sealed class NavigationState
    {
        public float DesiredDepth = 200f;
        public float DesiredSpeed;
        public bool AutoPilotEnabled;

        public void Tick(float dt, SubmarineState sub, EngineState engine, BallastTankState[] ballasts)
        {
            if (!AutoPilotEnabled) return;

            float depthError = sub.Depth - DesiredDepth;
            float targetBallast;
            if (depthError > 5f)
                targetBallast = 0f;
            else if (depthError < -5f)
                targetBallast = 0.5f;
            else
                targetBallast = 0.25f - depthError * 0.025f;

            if (targetBallast < 0f) targetBallast = 0f;
            if (targetBallast > 1f) targetBallast = 1f;

            for (int i = 0; i < ballasts.Length; i++)
                ballasts[i].TargetFillLevel = targetBallast;

            if (engine != null)
            {
                float speedError = DesiredSpeed - sub.HorizontalSpeed;
                float throttle = engine.ThrottleSetting + speedError * 0.1f * dt;
                if (throttle > 1f) throttle = 1f;
                if (throttle < -1f) throttle = -1f;
                engine.ThrottleSetting = throttle;
            }
        }
    }
}
