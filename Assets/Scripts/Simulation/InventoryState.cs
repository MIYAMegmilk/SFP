using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class InventoryState
    {
        public int MaxSlots;
        readonly Dictionary<ItemId, int> _items = new Dictionary<ItemId, int>();

        public InventoryState(int maxSlots = 20)
        {
            MaxSlots = maxSlots;
        }

        public IReadOnlyDictionary<ItemId, int> Items => _items;

        public int UsedSlots
        {
            get
            {
                int count = 0;
                foreach (var kv in _items) count += kv.Value;
                return count;
            }
        }

        public bool HasSpace(int amount = 1) => UsedSlots + amount <= MaxSlots;

        public int GetCount(ItemId id) => _items.TryGetValue(id, out int v) ? v : 0;

        public bool Has(ItemId id, int amount = 1) => GetCount(id) >= amount;

        public bool HasAll(Dictionary<ItemId, int> required)
        {
            foreach (var kv in required)
                if (GetCount(kv.Key) < kv.Value) return false;
            return true;
        }

        public bool Add(ItemId id, int amount = 1)
        {
            if (!HasSpace(amount)) return false;
            _items[id] = GetCount(id) + amount;
            return true;
        }

        public bool Remove(ItemId id, int amount = 1)
        {
            int current = GetCount(id);
            if (current < amount) return false;
            int remaining = current - amount;
            if (remaining == 0) _items.Remove(id);
            else _items[id] = remaining;
            return true;
        }

        public bool RemoveAll(Dictionary<ItemId, int> items)
        {
            if (!HasAll(items)) return false;
            foreach (var kv in items)
                Remove(kv.Key, kv.Value);
            return true;
        }
    }
}
