namespace SFP.Simulation
{
    public enum TurretType { Coilgun, Railgun, DischargeCoil }

    public sealed class TurretState
    {
        public int PowerNodeId = -1;
        public TurretType Type = TurretType.Coilgun;
        public float Rotation;
        public float Elevation;
        public float ReloadTime = 1f;
        public float ReloadProgress;
        public float PowerConsumption = 150f;
        public int AmmoCount = 50;

        public bool IsReady => ReloadProgress >= ReloadTime && AmmoCount > 0;
        public float ReloadFraction => ReloadTime > 0f ? ReloadProgress / ReloadTime : 1f;

        public bool TryFire()
        {
            if (!IsReady) return false;
            AmmoCount--;
            ReloadProgress = 0f;
            return true;
        }

        public void Tick(float dt, PowerGrid power)
        {
            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node == null || !node.IsActive) return;
            }

            if (ReloadProgress < ReloadTime)
                ReloadProgress += dt;
        }

        public float GetDamage()
        {
            switch (Type)
            {
                case TurretType.Coilgun: return 20f;
                case TurretType.Railgun: return 80f;
                case TurretType.DischargeCoil: return 40f;
                default: return 10f;
            }
        }
    }
}
