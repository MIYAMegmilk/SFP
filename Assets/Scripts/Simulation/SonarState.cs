using System.Collections.Generic;

namespace SFP.Simulation
{
    public enum ContactType { Terrain, Creature, Structure }

    public struct SonarContact
    {
        public float Distance;
        public float Bearing;
        public float Depth;
        public ContactType Type;
    }

    public sealed class SonarState
    {
        public int PowerNodeId = -1;
        public bool IsActive;
        public bool IsPassive;
        public float ActiveRange = 500f;
        public float PassiveRange = 200f;
        public float PingInterval = 3f;
        public float PowerConsumption = 100f;
        public float PassivePowerConsumption = 30f;

        float _pingTimer;
        readonly List<SonarContact> _contacts = new List<SonarContact>();

        public IReadOnlyList<SonarContact> Contacts => _contacts;
        public float PingProgress => PingInterval > 0 ? _pingTimer / PingInterval : 0f;

        public float Range => IsPassive ? PassiveRange : ActiveRange;

        public void Tick(float dt, PowerGrid power, SubmarineState sub)
        {
            if (!IsActive) return;

            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node != null)
                    node.Consumption = IsPassive ? PassivePowerConsumption : PowerConsumption;
                if (node == null || !node.IsActive) return;
            }

            _pingTimer += dt;
            if (_pingTimer >= PingInterval)
            {
                _pingTimer = 0f;
                UpdateContacts(sub);
            }
        }

        void UpdateContacts(SubmarineState sub)
        {
            _contacts.Clear();

            float range = Range;

            _contacts.Add(new SonarContact
            {
                Distance = 30f + sub.PositionZ * 0.1f,
                Bearing = 0f,
                Depth = sub.Depth + 30f,
                Type = ContactType.Terrain
            });
            _contacts.Add(new SonarContact
            {
                Distance = 30f - sub.PositionZ * 0.1f,
                Bearing = 180f,
                Depth = sub.Depth + 30f,
                Type = ContactType.Terrain
            });

            if (range > 100f)
            {
                _contacts.Add(new SonarContact
                {
                    Distance = range * 0.6f,
                    Bearing = 90f,
                    Depth = sub.Depth,
                    Type = ContactType.Terrain
                });
            }
        }
    }
}
