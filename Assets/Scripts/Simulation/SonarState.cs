using System;
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
        public bool HasPower { get; private set; }
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

        public void Tick(float dt, PowerGrid power, SubmarineState sub, TerrainModel terrain = null, MineSystem mines = null, CreatureSystem creatures = null)
        {
            if (!IsActive)
            {
                HasPower = false;
                return;
            }

            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node != null)
                    node.Consumption = IsPassive ? PassivePowerConsumption : PowerConsumption;
                HasPower = node != null && node.IsActive;
                if (node == null || !node.IsActive) return;
            }
            else
            {
                HasPower = true;
            }

            _pingTimer += dt;
            if (_pingTimer >= PingInterval)
            {
                _pingTimer = 0f;
                UpdateContacts(sub, terrain, mines, creatures);
            }
        }

        void UpdateContacts(SubmarineState sub, TerrainModel terrain, MineSystem mines, CreatureSystem creatures)
        {
            _contacts.Clear();

            float range = Range;

            if (terrain != null && terrain.Map != null)
            {
                for (int b = 0; b < 72; b++)
                {
                    float bearing = b * 5f;
                    float rad = bearing * ((float)Math.PI / 180f);
                    float dirX = (float)Math.Sin(rad);
                    float dirZ = (float)Math.Cos(rad);

                    for (float dist = 20f; dist <= range; dist += 20f)
                    {
                        float sx = sub.PositionX + dirX * dist;
                        float sz = sub.PositionZ + dirZ * dist;
                        float floorDepth = terrain.GetFloorDepthAt(sx, sz);
                        if (floorDepth <= sub.Depth + 5f)
                        {
                            _contacts.Add(new SonarContact
                            {
                                Distance = dist,
                                Bearing = bearing,
                                Depth = floorDepth,
                                Type = ContactType.Terrain
                            });
                            break;
                        }
                    }
                }
            }
            else
            {
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

            if (mines != null)
            {
                for (int i = 0; i < mines.Mines.Count; i++)
                {
                    var m = mines.Mines[i];
                    if (m.Exploded) continue;

                    float dx = m.X - sub.PositionX;
                    float dz = m.Z - sub.PositionZ;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist > range) continue;

                    float bearing = (float)(Math.Atan2(dx, dz) * (180.0 / Math.PI));
                    if (bearing < 0f) bearing += 360f;

                    _contacts.Add(new SonarContact
                    {
                        Distance = dist,
                        Bearing = bearing,
                        Depth = m.Depth,
                        Type = ContactType.Structure
                    });
                }
            }

            // Creatures are noisy: they show up at the full sonar Range in both active and
            // passive mode (unlike terrain, which needs an active ping to resolve at range).
            if (creatures != null)
            {
                for (int i = 0; i < creatures.Creatures.Count; i++)
                {
                    var cr = creatures.Creatures[i];
                    if (cr.IsDead) continue;

                    float dx = cr.X - sub.PositionX;
                    float dz = cr.Z - sub.PositionZ;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist > range) continue;

                    float bearing = (float)(Math.Atan2(dx, dz) * (180.0 / Math.PI));
                    if (bearing < 0f) bearing += 360f;

                    _contacts.Add(new SonarContact
                    {
                        Distance = dist,
                        Bearing = bearing,
                        Depth = cr.Depth,
                        Type = ContactType.Creature
                    });
                }
            }
        }
    }
}
