using System.Collections.Generic;

namespace SFP.Simulation
{
    public enum CrewTaskKind { None, Idle, FightFire, RepairBreach, OperatePump, OperateReactor, MoveTo, Flee }

    public enum CrewOrderKind { None, MoveTo, RepairBreach, FightFire, OperatePump, OperateReactor }

    public enum CrewMoveMode { Walking, Swimming, Climbing }

    public enum CrewEventKind { BreachSealed, CrewDied }

    public struct CrewEvent
    {
        public CrewEventKind Kind;
        public int CrewId;
        public int TargetId;
    }

    public sealed class CrewMemberState
    {
        public int Id;
        public CrewJobKind Job;

        public float X, Y, Z;
        public float VelX, VelY, VelZ;
        public int CompartmentId = -1;
        public CrewMoveMode MoveMode;

        public float Health = 100f;
        public float Oxygen = 30f;
        public bool IsDead;
        public float ExtinguisherCharge = 100f;

        public CrewTaskKind Task;
        public int TaskTargetId = -1;
        public float TaskScore;

        public CrewOrderKind Order;
        public int OrderTargetId = -1;
        public float OrderX, OrderY, OrderZ;

        public readonly List<int> PathCompartments = new();
        public readonly List<int> PathOpenings = new();
        public int PathIndex;
        public float WaypointX, WaypointY, WaypointZ;
        public bool HasWaypoint;

        internal float RescoreTimer;
        internal float IdleTimer;
        internal float DoorTimer;
        internal float StableTimer;
        internal int RepairingOpeningId = -1;
    }
}
