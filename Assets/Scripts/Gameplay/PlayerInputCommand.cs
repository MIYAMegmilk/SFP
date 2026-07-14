using Unity.Netcode;

namespace SFP.Gameplay
{
    public struct PlayerInputCommand : INetworkSerializable
    {
        public float MoveX;
        public float MoveZ;
        public float LookDeltaX;
        public float LookDeltaY;
        public byte Flags;

        const byte FlagJump = 1;
        const byte FlagSprint = 2;
        const byte FlagInteract = 4;
        const byte FlagClimbUp = 8;
        const byte FlagClimbDown = 16;

        public bool Jump { get => (Flags & FlagJump) != 0; set => Flags = value ? (byte)(Flags | FlagJump) : (byte)(Flags & ~FlagJump); }
        public bool Sprint { get => (Flags & FlagSprint) != 0; set => Flags = value ? (byte)(Flags | FlagSprint) : (byte)(Flags & ~FlagSprint); }
        public bool Interact { get => (Flags & FlagInteract) != 0; set => Flags = value ? (byte)(Flags | FlagInteract) : (byte)(Flags & ~FlagInteract); }
        public bool ClimbUp { get => (Flags & FlagClimbUp) != 0; set => Flags = value ? (byte)(Flags | FlagClimbUp) : (byte)(Flags & ~FlagClimbUp); }
        public bool ClimbDown { get => (Flags & FlagClimbDown) != 0; set => Flags = value ? (byte)(Flags | FlagClimbDown) : (byte)(Flags & ~FlagClimbDown); }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref MoveX);
            serializer.SerializeValue(ref MoveZ);
            serializer.SerializeValue(ref LookDeltaX);
            serializer.SerializeValue(ref LookDeltaY);
            serializer.SerializeValue(ref Flags);
        }
    }
}
