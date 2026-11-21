using System;
using System.Collections.Generic;
using Embervale.Shared.Data;

namespace Embervale.Shared
{
    [Serializable]
    public struct ItemStack : IEquatable<ItemStack>
    {
        public string ItemId;
        public int Quantity;

        public ItemStack(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public bool IsEmpty => string.IsNullOrEmpty(ItemId) || Quantity <= 0;
        public static readonly ItemStack Empty = new ItemStack(null, 0);

        public bool Equals(ItemStack o) => ItemId == o.ItemId && Quantity == o.Quantity;
        public override bool Equals(object obj) => obj is ItemStack o && Equals(o);
        public override int GetHashCode() => (ItemId?.GetHashCode() ?? 0) * 397 ^ Quantity;
        public override string ToString() => IsEmpty ? "(empty)" : ItemId + " x" + Quantity;
    }

    /// <summary>
    /// Fixed-capacity slot container used for inventories, banks and trade offers.
    /// Stackable items occupy one slot; non-stackable items occupy one slot each.
    /// All mutating operations are atomic: they either fully succeed or leave the container unchanged.
    /// </summary>
    public sealed class ItemContainer
    {
        public const int MaxStack = int.MaxValue;

        private ItemStack[] _slots;
        private readonly GameDatabase _db;

        /// <summary>When true every item stacks (banks).</summary>
        public bool AlwaysStack { get; }

        public event Action<ItemContainer> Changed;

        public ItemContainer(GameDatabase db, int capacity, bool alwaysStack = false)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _slots = new ItemStack[capacity];
            AlwaysStack = alwaysStack;
        }

        public int Capacity => _slots.Length;
        public ItemStack this[int slot] => slot >= 0 && slot < _slots.Length ? _slots[slot] : ItemStack.Empty;
        public IReadOnlyList<ItemStack> Slots => _slots;

        public int FreeSlots
        {
            get
            {
                int n = 0;
                foreach (var s in _slots) if (s.IsEmpty) n++;
                return n;
            }
        }

        public bool Stacks(string itemId) => AlwaysStack || _db.IsStackable(itemId);

        public void Resize(int capacity)
        {
            if (capacity < _slots.Length)
            {
                for (int i = capacity; i < _slots.Length; i++)
                    if (!_slots[i].IsEmpty) throw new InvalidOperationException("Cannot shrink container over occupied slots");
            }
            Array.Resize(ref _slots, capacity);
            Changed?.Invoke(this);
        }

        public int Count(string itemId)
        {
            long total = 0;
            foreach (var s in _slots) if (s.ItemId == itemId && s.Quantity > 0) total += s.Quantity;
            return (int)Math.Min(int.MaxValue, total);
        }

        public bool Contains(string itemId, int qty = 1) => qty <= 0 || Count(itemId) >= qty;

        public int IndexOf(string itemId)
        {
            for (int i = 0; i < _slots.Length; i++) if (_slots[i].ItemId == itemId && _slots[i].Quantity > 0) return i;
            return -1;
        }

        /// <summary>How many of the item could be added right now.</summary>
        public int SpaceFor(string itemId)
        {
            if (_db.Item(itemId) == null) return 0;
            if (Stacks(itemId))
            {
                int idx = IndexOf(itemId);
                if (idx >= 0) return MaxStack - _slots[idx].Quantity;
                return FreeSlots > 0 ? MaxStack : 0;
            }
            return FreeSlots;
        }

        public bool CanAdd(string itemId, int qty) => qty > 0 && SpaceFor(itemId) >= qty;

        /// <summary>Checks whether a whole set of additions fits simultaneously (used by crafting/trades).</summary>
        public bool CanAddAll(IEnumerable<ItemStack> stacks, IEnumerable<ItemStack> afterRemoving = null)
        {
            var clone = Clone();
            if (afterRemoving != null)
                foreach (var r in afterRemoving)
                    if (!clone.Remove(r.ItemId, r.Quantity, notify: false)) return false;
            foreach (var s in stacks)
                if (!clone.Add(s.ItemId, s.Quantity, notify: false)) return false;
            return true;
        }

        /// <summary>Adds items atomically. Returns false (and changes nothing) if they don't all fit.</summary>
        public bool Add(string itemId, int qty, bool notify = true)
        {
            if (qty <= 0 || !CanAdd(itemId, qty)) return false;
            if (Stacks(itemId))
            {
                int idx = IndexOf(itemId);
                if (idx < 0) idx = FirstFree();
                _slots[idx] = new ItemStack(itemId, (_slots[idx].ItemId == itemId ? _slots[idx].Quantity : 0) + qty);
            }
            else
            {
                for (int n = 0; n < qty; n++) _slots[FirstFree()] = new ItemStack(itemId, 1);
            }
            if (notify) Changed?.Invoke(this);
            return true;
        }

        /// <summary>Removes items atomically. Returns false (and changes nothing) if not enough are present.</summary>
        public bool Remove(string itemId, int qty, bool notify = true)
        {
            if (qty <= 0) return qty == 0;
            if (Count(itemId) < qty) return false;
            int left = qty;
            for (int i = _slots.Length - 1; i >= 0 && left > 0; i--)
            {
                if (_slots[i].ItemId != itemId) continue;
                int take = Math.Min(left, _slots[i].Quantity);
                left -= take;
                int remain = _slots[i].Quantity - take;
                _slots[i] = remain > 0 ? new ItemStack(itemId, remain) : ItemStack.Empty;
            }
            if (notify) Changed?.Invoke(this);
            return true;
        }

        /// <summary>Removes up to <paramref name="qty"/> from a specific slot. Returns the removed stack.</summary>
        public ItemStack TakeFromSlot(int slot, int qty)
        {
            if (slot < 0 || slot >= _slots.Length || _slots[slot].IsEmpty || qty <= 0) return ItemStack.Empty;
            var s = _slots[slot];
            int take = Math.Min(qty, s.Quantity);
            int remain = s.Quantity - take;
            _slots[slot] = remain > 0 ? new ItemStack(s.ItemId, remain) : ItemStack.Empty;
            Changed?.Invoke(this);
            return new ItemStack(s.ItemId, take);
        }

        public void SetSlot(int slot, ItemStack stack, bool notify = true)
        {
            if (slot < 0 || slot >= _slots.Length) throw new ArgumentOutOfRangeException(nameof(slot));
            _slots[slot] = stack.IsEmpty ? ItemStack.Empty : stack;
            if (notify) Changed?.Invoke(this);
        }

        /// <summary>Swaps two slots (drag and drop within the same container).</summary>
        public bool Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= _slots.Length || b >= _slots.Length) return false;
            if (a == b) return true;
            var t = _slots[a];
            _slots[a] = _slots[b];
            _slots[b] = t;
            Changed?.Invoke(this);
            return true;
        }

        public int FirstFree()
        {
            for (int i = 0; i < _slots.Length; i++) if (_slots[i].IsEmpty) return i;
            return -1;
        }

        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++) _slots[i] = ItemStack.Empty;
            Changed?.Invoke(this);
        }

        public ItemContainer Clone()
        {
            var c = new ItemContainer(_db, _slots.Length, AlwaysStack);
            Array.Copy(_slots, c._slots, _slots.Length);
            return c;
        }

        public void CopyFrom(ItemContainer other)
        {
            if (other._slots.Length != _slots.Length) _slots = new ItemStack[other._slots.Length];
            Array.Copy(other._slots, _slots, _slots.Length);
            Changed?.Invoke(this);
        }

        /// <summary>Total value of contents (for trade warnings / death rules).</summary>
        public long TotalValue()
        {
            long v = 0;
            foreach (var s in _slots)
                if (!s.IsEmpty) v += (long)(_db.Item(s.ItemId)?.Value ?? 0) * s.Quantity;
            return v;
        }

        public IEnumerable<ItemStack> NonEmpty()
        {
            foreach (var s in _slots) if (!s.IsEmpty) yield return s;
        }

        /// <summary>Moves empty slots to the end (bank tidy).</summary>
        public void Compact()
        {
            var list = new List<ItemStack>();
            foreach (var s in _slots) if (!s.IsEmpty) list.Add(s);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = i < list.Count ? list[i] : ItemStack.Empty;
            Changed?.Invoke(this);
        }
    }
}
