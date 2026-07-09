using System;

namespace SFP.Simulation
{
    public static class MapConstants
    {
        public const float CellSize = 8f;
        public const int ChunkCells = 32;
        public const float ChunkSize = ChunkCells * CellSize;
    }

    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public readonly int X;
        public readonly int Z;

        public ChunkCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public static ChunkCoord FromWorld(float wx, float wz)
        {
            return new ChunkCoord(
                (int)Math.Floor(wx / MapConstants.ChunkSize),
                (int)Math.Floor(wz / MapConstants.ChunkSize));
        }

        public float OriginX => X * MapConstants.ChunkSize;
        public float OriginZ => Z * MapConstants.ChunkSize;

        public long Key => ((long)X << 32) | (uint)Z;

        public bool Equals(ChunkCoord other)
        {
            return X == other.X && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is ChunkCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            return X * 397 ^ Z;
        }

        public static bool operator ==(ChunkCoord left, ChunkCoord right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ChunkCoord left, ChunkCoord right)
        {
            return !left.Equals(right);
        }
    }
}
