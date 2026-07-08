using System;

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

        // Sim-side hitscan fire resolution (used to shoot at creatures, no physics).
        public float FireCooldown = 1.2f;
        public float FireRange = 400f;
        public float BearingToleranceDeg = 6f;
        public float DepthTolerance = 60f;
        public float DamagePerHit = 30f;

        float _fireCooldownProgress;
        bool _powered;

        public bool CanFire => _fireCooldownProgress >= FireCooldown && AmmoCount > 0 && _powered;

        public bool TryFire()
        {
            if (!IsReady) return false;
            AmmoCount--;
            ReloadProgress = 0f;
            return true;
        }

        public bool TryFire(float bearingDeg, SubmarineState sub, CreatureSystem creatures,
            out int hitCreatureId, out float hitDistance)
        {
            hitCreatureId = -1;
            hitDistance = 0f;
            if (!CanFire) return false;

            AmmoCount--;
            _fireCooldownProgress = 0f;

            if (creatures == null) return true;

            float bestDist = float.MaxValue;
            int bestId = -1;
            var list = creatures.Creatures;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c.IsDead) continue;

                float dx = c.X - sub.PositionX;
                float dz = c.Z - sub.PositionZ;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dist > FireRange) continue;

                float bearingTo = (float)(Math.Atan2(dx, dz) * (180.0 / Math.PI));
                if (bearingTo < 0f) bearingTo += 360f;
                if (Math.Abs(DeltaAngle(bearingDeg, bearingTo)) > BearingToleranceDeg) continue;

                if (Math.Abs(c.Depth - sub.Depth) > DepthTolerance) continue;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestId = c.Id;
                }
            }

            if (bestId >= 0)
            {
                creatures.TakeDamage(bestId, DamagePerHit);
                hitCreatureId = bestId;
                hitDistance = bestDist;
            }
            return true;
        }

        static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d < -180f) d += 360f;
            else if (d > 180f) d -= 360f;
            return d;
        }

        public void Tick(float dt, PowerGrid power)
        {
            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                _powered = node != null && node.IsActive;
                if (node == null || !node.IsActive) return;
            }
            else
            {
                _powered = true;
            }

            if (ReloadProgress < ReloadTime)
                ReloadProgress += dt;

            if (_fireCooldownProgress < FireCooldown)
                _fireCooldownProgress += dt;
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
