using System.Collections.Generic;

namespace SFP.Simulation
{
    public enum StructureFace : byte
    {
        WallX = 0,
        WallZ = 1,
        Floor = 2,
        DoorX = 3,
        DoorZ = 4,
        Hatch = 5
    }

    public static class StructureFaceUtil
    {
        public static bool IsOpening(StructureFace f) => f >= StructureFace.DoorX;

        public static StructureFace SupportOf(StructureFace f) => f switch
        {
            StructureFace.DoorX => StructureFace.WallX,
            StructureFace.DoorZ => StructureFace.WallZ,
            StructureFace.Hatch => StructureFace.Floor,
            _ => f
        };

        public static StructureFace OpeningOf(StructureFace f) => f switch
        {
            StructureFace.WallX => StructureFace.DoorX,
            StructureFace.WallZ => StructureFace.DoorZ,
            StructureFace.Floor => StructureFace.Hatch,
            _ => f
        };
    }

    public readonly struct GridKey : System.IEquatable<GridKey>
    {
        public readonly int X, Y, Z;
        public readonly StructureFace Face;

        public GridKey(int x, int y, int z, StructureFace face)
        {
            X = x; Y = y; Z = z; Face = face;
        }

        public bool Equals(GridKey other)
            => X == other.X && Y == other.Y && Z == other.Z && Face == other.Face;

        public override bool Equals(object obj) => obj is GridKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = X * 73856093;
                h ^= Y * 19349663;
                h ^= Z * 83492791;
                h ^= (int)Face * 393241;
                return h;
            }
        }
    }

    public sealed class BuildingGrid
    {
        readonly HashSet<GridKey> _pieces = new();

        public IReadOnlyCollection<GridKey> Pieces => _pieces;
        public int Count => _pieces.Count;

        public bool Place(GridKey key) => _pieces.Add(key);
        public bool Remove(GridKey key) => _pieces.Remove(key);
        public bool Has(GridKey key) => _pieces.Contains(key);
        public void Clear() => _pieces.Clear();
    }
}
