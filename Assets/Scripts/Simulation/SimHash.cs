namespace SFP.Simulation
{
    public static class SimHash
    {
        public static uint Hash(int a, int b, int seed)
        {
            unchecked
            {
                uint h = (uint)a * 374761393u;
                h += (uint)b * 668265263u;
                h += (uint)seed * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }

        public static float HashToFloat(int a, int b, int seed)
        {
            uint h = Hash(a, b, seed);
            return (h & 0x00FFFFFFu) / (float)0x01000000u;
        }

        public static uint Hash(int a, int seed)
        {
            return Hash(a, 0, seed);
        }

        public static float HashToFloat(int a, int seed)
        {
            return HashToFloat(a, 0, seed);
        }
    }
}
