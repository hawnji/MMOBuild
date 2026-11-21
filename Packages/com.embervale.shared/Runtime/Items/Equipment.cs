using System;
using Embervale.Shared.Data;

namespace Embervale.Shared
{
    public enum EquipResult
    {
        Ok, NotEquippable, RequirementNotMet, InventoryFull, NotInInventory, InvalidSlot
    }

    /// <summary>Worn equipment. One stack per slot (ammo slot may hold a stack).</summary>
    public sealed class Equipment
    {
        private readonly ItemStack[] _slots = new ItemStack[EquipSlots.Count];
        private readonly GameDatabase _db;

        public event Action<Equipment> Changed;

        public Equipment(GameDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public ItemStack this[EquipSlot slot] => slot == EquipSlot.None ? ItemStack.Empty : _slots[(int)slot];
        public ItemDef DefIn(EquipSlot slot) => _db.Item(this[slot].ItemId);

        public ItemDef Weapon => DefIn(EquipSlot.Weapon);

        public void SetSlot(EquipSlot slot, ItemStack stack, bool notify = true)
        {
            _slots[(int)slot] = stack.IsEmpty ? ItemStack.Empty : stack;
            if (notify) Changed?.Invoke(this);
        }

        /// <summary>
        /// Equips the item in inventory slot <paramref name="invSlot"/>. Handles two-handed weapons
        /// (removes shield) and returns previously worn items to the inventory. Atomic.
        /// </summary>
        public EquipResult Equip(ItemContainer inventory, int invSlot, SkillSet skills, out Requirement failed)
        {
            failed = null;
            var stack = inventory[invSlot];
            if (stack.IsEmpty) return EquipResult.NotInInventory;
            var def = _db.Item(stack.ItemId);
            if (def == null || !def.IsEquippable) return EquipResult.NotEquippable;
            if (!skills.MeetsAll(def.Requirements, out failed)) return EquipResult.RequirementNotMet;

            var slot = def.Slot;
            var toReturn = new System.Collections.Generic.List<ItemStack>();
            var clearSlots = new System.Collections.Generic.List<EquipSlot>();
            var current = this[slot];
            bool stackAmmo = slot == EquipSlot.Ammo && current.ItemId == def.Id;
            if (!current.IsEmpty && !stackAmmo) { toReturn.Add(current); clearSlots.Add(slot); }
            if (def.TwoHanded && !this[EquipSlot.Shield].IsEmpty)
            {
                toReturn.Add(this[EquipSlot.Shield]); clearSlots.Add(EquipSlot.Shield);
            }
            if (slot == EquipSlot.Shield)
            {
                var w = Weapon;
                if (w != null && w.TwoHanded) { toReturn.Add(this[EquipSlot.Weapon]); clearSlots.Add(EquipSlot.Weapon); }
            }

            int qty = def.Stackable ? stack.Quantity : 1;
            var removed = new[] { new ItemStack(def.Id, qty) };
            if (!inventory.CanAddAll(toReturn, removed)) return EquipResult.InventoryFull;

            inventory.TakeFromSlot(invSlot, qty);
            foreach (var r in toReturn) inventory.Add(r.ItemId, r.Quantity);
            foreach (var c in clearSlots) _slots[(int)c] = ItemStack.Empty;
            _slots[(int)slot] = stackAmmo ? new ItemStack(def.Id, current.Quantity + qty) : new ItemStack(def.Id, qty);
            Changed?.Invoke(this);
            return EquipResult.Ok;
        }

        public EquipResult Unequip(EquipSlot slot, ItemContainer inventory)
        {
            if (slot == EquipSlot.None) return EquipResult.InvalidSlot;
            var cur = this[slot];
            if (cur.IsEmpty) return EquipResult.NotInInventory;
            if (!inventory.Add(cur.ItemId, cur.Quantity)) return EquipResult.InventoryFull;
            _slots[(int)slot] = ItemStack.Empty;
            Changed?.Invoke(this);
            return EquipResult.Ok;
        }

        /// <summary>Consumes one piece of ammunition. Returns false if none is equipped.</summary>
        public bool ConsumeAmmo(int qty = 1)
        {
            var a = this[EquipSlot.Ammo];
            if (a.Quantity < qty) return false;
            _slots[(int)EquipSlot.Ammo] = a.Quantity - qty > 0 ? new ItemStack(a.ItemId, a.Quantity - qty) : ItemStack.Empty;
            Changed?.Invoke(this);
            return true;
        }

        public ItemStats TotalStats()
        {
            var total = new ItemStats();
            foreach (var s in _slots)
            {
                if (s.IsEmpty) continue;
                total.Add(_db.Item(s.ItemId)?.Stats);
            }
            var w = Weapon;
            total.AttackSpeed = w?.Stats != null && w.Stats.AttackSpeed > 0 ? w.Stats.AttackSpeed : 4;
            total.AttackRange = w?.Stats != null && w.Stats.AttackRange > 0 ? w.Stats.AttackRange : 1;
            return total;
        }

        /// <summary>Best equipped tool of the given type (inventory tools are checked separately).</summary>
        public ItemDef Tool(string toolType)
        {
            var w = Weapon;
            return w != null && w.ToolType == toolType ? w : null;
        }

        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++) _slots[i] = ItemStack.Empty;
            Changed?.Invoke(this);
        }

        public ItemStack[] Snapshot() => (ItemStack[])_slots.Clone();
    }
}
