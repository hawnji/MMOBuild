#!/usr/bin/env python3
"""
Embervale game-data generator.

Produces the canonical JSON definitions consumed by BOTH the Unity client and the
headless .NET server (Assets/StreamingAssets/GameData/*.json).

Content is described here as compact tier tables so that adding a new metal tier,
weapon type or monster is a one-line change. Re-run after editing:

    python3 Tools/Content/generate_game_data.py

Never hand-edit the generated JSON; edit this file instead.
"""
import json
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "StreamingAssets", "GameData")

items = {}
recipes = []


def item(id, name, desc, category, value, **kw):
    if id in items:
        raise ValueError("duplicate item " + id)
    d = {
        "id": id,
        "name": name,
        "description": desc,
        "icon": kw.pop("icon", id),
        "model": kw.pop("model", None),
        "category": category,
        "stackable": kw.pop("stackable", False),
        "value": value,
        "weight": kw.pop("weight", 0.5),
        "tradeable": kw.pop("tradeable", True),
    }
    d.update(kw)
    items[id] = {k: v for k, v in d.items() if v is not None}
    return id


def recipe(id, skill, level, inputs, outputs, xp, ticks=4, station=None, tools=None, **kw):
    r = {
        "id": id,
        "skill": skill,
        "level": level,
        "station": station,
        "inputs": [{"item": i, "qty": q} for i, q in inputs],
        "tools": tools or [],
        "outputs": [{"item": i, "qty": q} for i, q in outputs],
        "xp": xp,
        "ticks": ticks,
    }
    r.update(kw)
    recipes.append({k: v for k, v in r.items() if v is not None})


# ---------------------------------------------------------------------------
# Skills
# ---------------------------------------------------------------------------
SKILLS = [
    ("Attack", "Combat", "Accuracy with melee weapons."),
    ("Strength", "Combat", "Raw melee damage."),
    ("Defense", "Combat", "Avoiding incoming blows and wearing heavier armour."),
    ("Ranged", "Combat", "Bows, crossbows and thrown weapons."),
    ("Magic", "Combat", "Channelling runes into spells."),
    ("Vitality", "Combat", "Your life force. Each level grants extra health."),
    ("Woodcutting", "Gathering", "Felling trees for logs."),
    ("Mining", "Gathering", "Extracting ore and stone."),
    ("Fishing", "Gathering", "Catching fish from rivers, lakes and the sea."),
    ("Farming", "Gathering", "Growing crops and herbs in tended patches."),
    ("Smithing", "Crafting", "Smelting ore and forging metal equipment."),
    ("Cooking", "Crafting", "Preparing food that restores health."),
    ("Crafting", "Crafting", "Leatherwork, jewellery and utility items."),
    ("Fletching", "Crafting", "Carving bows and making arrows."),
    ("Herblore", "Crafting", "Brewing potions from herbs."),
    ("Runecrafting", "Crafting", "Binding raw essence into spell runes."),
    ("Agility", "Support", "Moving across obstacles and difficult terrain."),
    ("Thieving", "Support", "Picking pockets and lifting goods from stalls."),
    ("Prayer", "Support", "Honouring the old spirits for protective blessings."),
    ("Firemaking", "Support", "Lighting fires for warmth and cooking."),
]

EXPERIENCE = {
    "maxLevel": 99,
    "formula": "geometric",
    "base": 80.0,
    "growth": 1.1,
    "linear": 20.0,
    "comment": "Total XP for level L = floor(base*(growth^(L-1)-1)/(growth-1) + linear*(L-1)).",
    "startingLevels": {"Vitality": 10},
}

# ---------------------------------------------------------------------------
# Currency, misc
# ---------------------------------------------------------------------------
item("coins", "Embers", "Small stamped copper discs. The common currency of Embervale.", "Currency", 1,
     stackable=True, weight=0.0)
item("bones", "Bones", "The bones of some creature. Burying them honours the old spirits.", "Misc", 2,
     buryXp=5)
item("big_bones", "Heavy Bones", "Thick bones from a large beast.", "Misc", 15, buryXp=15)
item("feather", "Feather", "Useful for fletching arrows and as fishing bait.", "Resource", 2, stackable=True,
     weight=0.0)
item("cowhide", "Hide", "An untanned animal hide.", "Resource", 8)
item("leather", "Leather", "Tanned leather, ready for crafting.", "Resource", 18)
item("thread", "Thread", "Strong waxed thread.", "Resource", 1, stackable=True, weight=0.0)
item("needle", "Needle", "Used with thread to stitch leather.", "Tool", 3)
item("knife", "Knife", "A small knife for carving wood.", "Tool", 5)
item("hammer", "Hammer", "A smith's hammer. Required at an anvil.", "Tool", 5)
item("tinderbox", "Tinderbox", "Strikes sparks to light fires.", "Tool", 4)
item("vial", "Empty Vial", "A small glass vial.", "Resource", 2)
item("vial_water", "Vial of Water", "A vial filled with clean water.", "Resource", 4)
item("watering_can", "Watering Can", "Keeps crops healthy.", "Tool", 8)
item("rake", "Rake", "Clears weeds from farming patches.", "Tool", 6)
item("spade", "Spade", "For digging and harvesting crops.", "Tool", 6)
item("bucket", "Bucket", "A wooden bucket.", "Misc", 2)
item("ashes", "Ashes", "The remains of a fire.", "Resource", 2)
item("blank_essence", "Raw Essence", "Unshaped magical stone, hums faintly.", "Resource", 4, stackable=True,
     weight=0.1)
item("bait_worms", "Bait Worms", "Wriggly. Fish love them.", "Resource", 1, stackable=True, weight=0.0)
item("lockpick", "Lockpick", "A thin bent wire.", "Tool", 15)
item("old_map_scrap", "Old Map Scrap", "A torn piece of a map showing the Greenleaf Forest.", "Quest", 0,
     tradeable=False)
item("wolf_pelt", "Wolf Pelt", "A thick grey pelt.", "Resource", 20)
item("goblin_charm", "Goblin Charm", "A crude carved trinket.", "Quest", 3)
item("spirit_shard", "Spirit Shard", "A glowing fragment of a forest spirit.", "Resource", 120, stackable=True)
item("ruin_relic", "Ancient Relic", "An artefact from the Ancient Ruins.", "Misc", 500)

# ---------------------------------------------------------------------------
# Metals: ore -> bar -> equipment
# ---------------------------------------------------------------------------
ORES = [
    # id, name, level, xp, respawn ticks, value
    ("copper", "Copper", 1, 17, 6, 5),
    ("tin", "Tin", 1, 17, 6, 5),
    ("iron", "Iron", 15, 35, 12, 18),
    ("coal", "Coal", 30, 50, 40, 30),
    ("silver", "Silver", 20, 40, 60, 60),
    ("gold", "Gold", 40, 65, 80, 120),
    ("mithril", "Mithril", 55, 80, 160, 200),
    ("duskite", "Duskite", 70, 95, 300, 420),
    ("starmetal", "Starmetal", 85, 125, 600, 900),
]
for oid, oname, lvl, xp, resp, val in ORES:
    item(oid + "_ore", oname + " Ore" if oid != "coal" else "Coal",
         "A lump of %s." % ("coal" if oid == "coal" else oname.lower() + " ore"),
         "Resource", val, model="ore_chunk", tint=oid)

# tier id, display, smelt level, bar inputs, equip level req, base power, colour key
TIERS = [
    ("bronze", "Bronze", 1, [("copper_ore", 1), ("tin_ore", 1)], 1, 1.0),
    ("iron", "Iron", 15, [("iron_ore", 1)], 10, 1.6),
    ("steel", "Steel", 30, [("iron_ore", 1), ("coal_ore", 2)], 20, 2.4),
    ("mithril", "Mithril", 50, [("mithril_ore", 1), ("coal_ore", 4)], 30, 3.4),
    ("duskite", "Duskite", 70, [("duskite_ore", 1), ("coal_ore", 6)], 40, 4.6),
    ("starmetal", "Starmetal", 85, [("starmetal_ore", 1), ("coal_ore", 8)], 50, 6.0),
]
# precious bars
item("silver_bar", "Silver Bar", "A bar of polished silver.", "Resource", 90, model="metal_bar", tint="silver")
item("gold_bar", "Gold Bar", "A heavy bar of gold.", "Resource", 180, model="metal_bar", tint="gold")
recipe("smelt_silver", "Smithing", 20, [("silver_ore", 1)], [("silver_bar", 1)], 14, station="Furnace")
recipe("smelt_gold", "Smithing", 40, [("gold_ore", 1)], [("gold_bar", 1)], 22, station="Furnace")

# weapon type: id suffix, name, bars, level offset, stats multiplier, speed (ticks), extras
WEAPONS = [
    ("dagger", "Dagger", 1, 0, {"attackMelee": 4, "strengthMelee": 3}, 4, {"weaponType": "Dagger"}),
    ("sword", "Sword", 1, 2, {"attackMelee": 7, "strengthMelee": 6}, 5, {"weaponType": "Sword"}),
    ("axe", "Battleaxe", 3, 6, {"attackMelee": 6, "strengthMelee": 10}, 6, {"weaponType": "Axe"}),
    ("mace", "Mace", 2, 4, {"attackMelee": 6, "strengthMelee": 8, "defenseMelee": 1}, 5, {"weaponType": "Mace"}),
    ("greatsword", "Greatsword", 3, 8, {"attackMelee": 12, "strengthMelee": 15}, 7,
     {"weaponType": "TwoHanded", "twoHanded": True}),
]
ARMOUR = [
    ("helm", "Helm", "Head", 1, 1, {"defenseMelee": 3, "defenseRanged": 3, "defenseMagic": -1}),
    ("chest", "Chestplate", "Body", 4, 6, {"defenseMelee": 10, "defenseRanged": 9, "defenseMagic": -4}),
    ("legs", "Platelegs", "Legs", 3, 5, {"defenseMelee": 7, "defenseRanged": 6, "defenseMagic": -2}),
    ("boots", "Boots", "Feet", 1, 2, {"defenseMelee": 2, "defenseRanged": 2}),
    ("gloves", "Gauntlets", "Hands", 1, 2, {"defenseMelee": 2, "defenseRanged": 1, "attackMelee": 1}),
    ("shield", "Kite Shield", "Shield", 3, 4, {"defenseMelee": 8, "defenseRanged": 9, "defenseMagic": -1}),
]
TOOLS = [
    ("pickaxe", "Pickaxe", "Pickaxe", 2, 0),
    ("hatchet", "Hatchet", "Hatchet", 1, 0),
]

for tid, tname, smelt, bar_in, req, power in TIERS:
    bar = item(tid + "_bar", tname + " Bar", "A bar of %s." % tname.lower(), "Resource",
               int(12 * power * power), model="metal_bar", tint=tid)
    recipe("smelt_" + tid, "Smithing", smelt, bar_in, [(bar, 1)], int(6 + smelt * 0.6), station="Furnace",
           ticks=4)
    for wid, wname, bars, loff, stats, speed, extra in WEAPONS:
        iid = "%s_%s" % (tid, wid)
        st = {k: int(round(v * power)) for k, v in stats.items()}
        st["attackSpeed"] = speed
        item(iid, "%s %s" % (tname, wname), "A %s %s." % (tname.lower(), wname.lower()), "Weapon",
             int(20 * bars * power * power), slot="Weapon", weight=1.0 + bars, model="%s_%s" % ("w", wid),
             tint=tid, requirements=[{"skill": "Attack", "level": req}], stats=st, **extra)
        recipe("forge_" + iid, "Smithing", min(99, smelt + loff), [(bar, bars)], [(iid, 1)],
               int((6 + smelt * 0.6) * bars * 1.5), station="Anvil", tools=["hammer"], ticks=5)
    for aid, aname, slot, bars, loff, stats in ARMOUR:
        iid = "%s_%s" % (tid, aid)
        st = {k: int(round(v * power)) for k, v in stats.items()}
        item(iid, "%s %s" % (tname, aname), "%s %s." % (tname, aname.lower()), "Armour",
             int(20 * bars * power * power), slot=slot, weight=1.0 + bars * 1.5, model="a_%s" % aid, tint=tid,
             requirements=[{"skill": "Defense", "level": req}], stats=st)
        recipe("forge_" + iid, "Smithing", min(99, smelt + loff), [(bar, bars)], [(iid, 1)],
               int((6 + smelt * 0.6) * bars * 1.5), station="Anvil", tools=["hammer"], ticks=5)
    for toid, toname, ttype, bars, loff in TOOLS:
        iid = "%s_%s" % (tid, toid)
        item(iid, "%s %s" % (tname, toname), "A %s %s." % (tname.lower(), toname.lower()), "Tool",
             int(15 * bars * power * power), slot="Weapon", weight=2.0, model="t_%s" % toid, tint=tid,
             toolType=ttype, toolPower=power, toolLevel=req,
             requirements=[{"skill": "Mining" if ttype == "Pickaxe" else "Woodcutting", "level": req}],
             stats={"attackMelee": int(3 * power), "strengthMelee": int(3 * power), "attackSpeed": 5},
             weaponType="Axe" if ttype == "Hatchet" else "Mace")
        recipe("forge_" + iid, "Smithing", min(99, smelt + loff), [(bar, bars)], [(iid, 1)],
               int((6 + smelt * 0.6) * bars * 1.5), station="Anvil", tools=["hammer"], ticks=5)
    # arrowheads
    ah = item(tid + "_arrowheads", "%s Arrowheads" % tname, "Unattached arrowheads.", "Resource",
              int(2 * power * power), stackable=True, weight=0.0, model="arrowheads", tint=tid)
    recipe("forge_" + ah, "Smithing", min(99, smelt + 3), [(bar, 1)], [(ah, 15)], int(6 + smelt * 0.6),
           station="Anvil", tools=["hammer"], ticks=4)
    arrow = item(tid + "_arrow", "%s Arrow" % tname, "Arrows with %s heads." % tname.lower(), "Ammo",
                 int(3 * power * power), stackable=True, weight=0.0, slot="Ammo", ammoType="Arrow",
                 model="arrow", tint=tid, stats={"strengthRanged": int(round(5 * power))},
                 requirements=[{"skill": "Ranged", "level": req}])
    recipe("fletch_" + arrow, "Fletching", min(99, smelt + 4), [("headless_arrow", 15), (ah, 15)],
           [(arrow, 15)], int(2 + smelt * 0.5) * 15 // 4, ticks=3)

# ---------------------------------------------------------------------------
# Woodcutting / Fletching / Firemaking
# ---------------------------------------------------------------------------
TREES = [
    # id, name, level, xp, logs name, respawn ticks, deplete chance, value
    ("oak", "Oak Tree", 1, 25, "Oak Logs", 30, 1.0 / 8, 4),
    ("pine", "Pine Tree", 15, 38, "Pine Logs", 40, 1.0 / 8, 10),
    ("willow", "Willow Tree", 30, 68, "Willow Logs", 50, 1.0 / 8, 20),
    ("maple", "Maple Tree", 45, 100, "Maple Logs", 70, 1.0 / 9, 40),
    ("yew", "Elderyew Tree", 60, 175, "Elderyew Logs", 120, 1.0 / 10, 90),
    ("ghostwood", "Ghostwood Tree", 75, 250, "Ghostwood Logs", 200, 1.0 / 12, 200),
]
BOWS = {"oak": (1, 5), "pine": (20, 15), "willow": (35, 25), "maple": (50, 35), "yew": (65, 48),
        "ghostwood": (80, 60)}
for tid, tname, lvl, xp, logname, resp, dep, val in TREES:
    logs = item(tid + "_logs", logname, "Freshly cut %s." % logname.lower(), "Resource", val, model="logs",
                tint=tid)
    recipe("burn_" + logs, "Firemaking", lvl, [(logs, 1)], [("ashes", 1)], int(xp * 1.2), tools=["tinderbox"],
           ticks=3, spawnsFire=True)
    blvl, bpow = BOWS[tid]
    sb = item(tid + "_shortbow", tname.replace(" Tree", "") + " Shortbow", "A short bow.", "Weapon",
              int(bpow * 6), slot="Weapon", weight=1.0, model="w_shortbow", tint=tid, weaponType="Bow",
              twoHanded=True, requirements=[{"skill": "Ranged", "level": max(1, blvl - 1)}],
              stats={"attackRanged": bpow, "attackSpeed": 4, "attackRange": 7})
    lb = item(tid + "_longbow", tname.replace(" Tree", "") + " Longbow", "A tall bow with long reach.",
              "Weapon", int(bpow * 9), slot="Weapon", weight=1.5, model="w_longbow", tint=tid, weaponType="Bow",
              twoHanded=True, requirements=[{"skill": "Ranged", "level": max(1, blvl - 1)}],
              stats={"attackRanged": int(bpow * 1.3), "attackSpeed": 6, "attackRange": 9})
    recipe("fletch_" + sb, "Fletching", blvl, [(logs, 1)], [(sb, 1)], int(xp * 0.4) + 5, tools=["knife"],
           ticks=3)
    recipe("fletch_" + lb, "Fletching", min(99, blvl + 5), [(logs, 2)], [(lb, 1)], int(xp * 0.8) + 5,
           tools=["knife"], ticks=3)
item("arrow_shaft", "Arrow Shaft", "A plain wooden shaft.", "Resource", 1, stackable=True, weight=0.0)
item("headless_arrow", "Headless Arrow", "A shaft with feathers attached.", "Resource", 2, stackable=True,
     weight=0.0)
recipe("fletch_shafts", "Fletching", 1, [("oak_logs", 1)], [("arrow_shaft", 15)], 5, tools=["knife"], ticks=3)
recipe("fletch_headless", "Fletching", 1, [("arrow_shaft", 15), ("feather", 15)], [("headless_arrow", 15)],
       15, ticks=3)
# crossbow
item("oak_crossbow", "Oak Crossbow", "A sturdy crossbow.", "Weapon", 80, slot="Weapon", model="w_crossbow",
     tint="oak", weaponType="Crossbow", twoHanded=True, requirements=[{"skill": "Ranged", "level": 10}],
     stats={"attackRanged": 14, "attackSpeed": 6, "attackRange": 7})
item("iron_bolt", "Iron Bolt", "Short iron-tipped bolts.", "Ammo", 4, stackable=True, slot="Ammo",
     ammoType="Bolt", model="bolt", tint="iron", stats={"strengthRanged": 10},
     requirements=[{"skill": "Ranged", "level": 10}])
recipe("fletch_oak_crossbow", "Fletching", 18, [("oak_logs", 2), ("iron_bar", 1)], [("oak_crossbow", 1)], 40,
       tools=["knife", "hammer"], ticks=4)
recipe("fletch_iron_bolts", "Fletching", 18, [("iron_arrowheads", 10), ("arrow_shaft", 10)], [("iron_bolt", 10)],
       25, ticks=3)

# ---------------------------------------------------------------------------
# Fishing / Cooking
# ---------------------------------------------------------------------------
FISH = [
    # id, name, fish level, xp, cook level, cook xp, heal, value, tool
    ("minnow", "Minnow", 1, 10, 1, 30, 3, 3, "FishingNet"),
    ("perch", "Perch", 5, 20, 5, 40, 5, 6, "FishingRod"),
    ("trout", "Trout", 20, 50, 15, 70, 7, 15, "FishingRod"),
    ("pike", "Pike", 30, 70, 25, 90, 9, 25, "FishingRod"),
    ("salmon", "Salmon", 35, 80, 30, 100, 10, 35, "FishingRod"),
    ("eel", "Marsh Eel", 50, 105, 45, 130, 14, 70, "FishingRod"),
    ("swordfish", "Bladefin", 60, 120, 55, 150, 16, 120, "Harpoon"),
    ("deepcod", "Deepcod", 76, 150, 72, 200, 20, 250, "Harpoon"),
]
item("small_net", "Small Fishing Net", "For catching small fish in shallow water.", "Tool", 5,
     toolType="FishingNet", toolPower=1.0, model="t_net")
item("fishing_rod", "Fishing Rod", "A rod and line. Needs bait.", "Tool", 5, toolType="FishingRod",
     toolPower=1.0, model="t_rod")
item("harpoon", "Harpoon", "For spearing large fish.", "Tool", 45, toolType="Harpoon", toolPower=1.0,
     model="t_harpoon", requirements=[{"skill": "Fishing", "level": 50}])
for fid, fname, flvl, fxp, clvl, cxp, heal, val, tool in FISH:
    raw = item("raw_" + fid, "Raw " + fname, "A raw %s. Needs cooking." % fname.lower(), "Resource", val,
               model="fish", tint=fid)
    cooked = item(fid, fname, "A cooked %s. Restores %d health." % (fname.lower(), heal), "Food",
                  int(val * 1.4), heal=heal, model="fish_cooked", tint=fid)
    burnt = item("burnt_" + fid, "Burnt " + fname, "Oops.", "Misc", 1, model="fish_burnt")
    recipe("cook_" + fid, "Cooking", clvl, [(raw, 1)], [(cooked, 1)], cxp, station="Range", ticks=4,
           failOutput=burnt, failChanceAtLevel=0.45, noFailLevel=min(99, clvl + 33), altStations=["Fire"])
item("raw_meat", "Raw Meat", "A cut of raw meat.", "Resource", 3, model="meat")
item("cooked_meat", "Cooked Meat", "Roasted meat. Restores 4 health.", "Food", 5, heal=4, model="meat_cooked")
item("burnt_meat", "Burnt Meat", "Charred beyond recognition.", "Misc", 1, model="meat_burnt")
recipe("cook_meat", "Cooking", 1, [("raw_meat", 1)], [("cooked_meat", 1)], 30, station="Range", ticks=4,
       failOutput="burnt_meat", failChanceAtLevel=0.4, noFailLevel=30, altStations=["Fire"])
item("bread_dough", "Bread Dough", "Unbaked dough.", "Resource", 4)
item("bread", "Bread", "Fresh bread. Restores 5 health.", "Food", 12, heal=5, model="bread")
item("flour", "Flour", "Finely milled grain.", "Resource", 3)
recipe("make_dough", "Cooking", 1, [("flour", 1), ("bucket_water", 1)], [("bread_dough", 1), ("bucket", 1)], 5,
       ticks=2)
item("bucket_water", "Bucket of Water", "A bucket full of water.", "Misc", 4)
recipe("bake_bread", "Cooking", 1, [("bread_dough", 1)], [("bread", 1)], 40, station="Range", ticks=4)
item("hearty_stew", "Hearty Stew", "Potato and meat stew. Restores 12 health.", "Food", 60, heal=12, model="stew")
recipe("cook_stew", "Cooking", 25, [("raw_meat", 1), ("potato", 2), ("bucket_water", 1)],
       [("hearty_stew", 1), ("bucket", 1)], 115, station="Range", ticks=5)

FISHING_SPOTS = [
    {"id": "net_spot", "name": "Shallow Water", "skill": "Fishing", "toolType": "FishingNet",
     "model": "fishing_spot", "yields": [{"item": "raw_minnow", "level": 1, "xp": 10, "weight": 1}],
     "baseChance": 0.35, "respawnTicks": 0, "depleteChance": 0.0},
    {"id": "rod_spot", "name": "Rippling Water", "skill": "Fishing", "toolType": "FishingRod",
     "bait": "bait_worms", "model": "fishing_spot",
     "yields": [{"item": "raw_perch", "level": 5, "xp": 20, "weight": 4},
                {"item": "raw_trout", "level": 20, "xp": 50, "weight": 3},
                {"item": "raw_pike", "level": 30, "xp": 70, "weight": 2},
                {"item": "raw_salmon", "level": 35, "xp": 80, "weight": 2}],
     "baseChance": 0.3, "respawnTicks": 0, "depleteChance": 0.0},
    {"id": "marsh_spot", "name": "Murky Water", "skill": "Fishing", "toolType": "FishingRod",
     "bait": "bait_worms", "model": "fishing_spot",
     "yields": [{"item": "raw_eel", "level": 50, "xp": 105, "weight": 1}], "baseChance": 0.25,
     "respawnTicks": 0, "depleteChance": 0.0},
    {"id": "deep_spot", "name": "Deep Water", "skill": "Fishing", "toolType": "Harpoon", "model": "fishing_spot",
     "yields": [{"item": "raw_swordfish", "level": 60, "xp": 120, "weight": 3},
                {"item": "raw_deepcod", "level": 76, "xp": 150, "weight": 1}], "baseChance": 0.22,
     "respawnTicks": 0, "depleteChance": 0.0},
]

# ---------------------------------------------------------------------------
# Farming / Herblore
# ---------------------------------------------------------------------------
CROPS = [
    # id, name, level, plant xp, harvest xp, growth ticks (per stage x 4 stages), yield min, max, value, kind
    ("potato", "Potato", 1, 8, 9, 60, 3, 6, 3, "Food"),
    ("onion", "Onion", 5, 10, 11, 60, 3, 6, 3, "Resource"),
    ("cabbage", "Cabbage", 7, 11, 12, 70, 3, 6, 4, "Food"),
    ("wheat", "Wheat", 10, 12, 14, 80, 3, 7, 3, "Resource"),
    ("sunroot", "Sunroot", 20, 20, 25, 120, 2, 5, 25, "Herb"),
    ("mirewort", "Mirewort", 35, 32, 40, 160, 2, 5, 45, "Herb"),
    ("ashbloom", "Ashbloom", 50, 50, 65, 200, 2, 5, 80, "Herb"),
]
crops = []
for cid, cname, lvl, pxp, hxp, gt, ymin, ymax, val, kind in CROPS:
    seed = item(cid + "_seed", cname + " Seed", "Plant in a farming patch.", "Seed", max(1, val // 2),
                stackable=True, weight=0.0, model="seed_pouch")
    extra = {"heal": 2} if kind == "Food" else {}
    produce = item(cid, cname, "A freshly harvested %s." % cname.lower(), "Food" if kind == "Food" else "Resource",
                   val, model="crop_" + cid, **extra)
    crops.append({"id": cid, "name": cname, "seed": seed, "produce": produce, "level": lvl, "plantXp": pxp,
                  "harvestXp": hxp, "ticksPerStage": gt, "stages": 4, "yieldMin": ymin, "yieldMax": ymax,
                  "failChanceUnwatered": 0.25, "failChanceWatered": 0.03})
recipe("mill_flour", "Cooking", 1, [("wheat", 1)], [("flour", 1)], 2, station="Mill", ticks=2)

POTIONS = [
    # id, name, herb, secondary, level, xp, effect
    ("vigor_potion", "Vigor Draught", "sunroot", "wolf_pelt", 5, 25, {"buff": "Strength", "amount": 3,
                                                                       "durationTicks": 300}),
    ("guard_potion", "Guard Draught", "sunroot", "bones", 12, 38, {"buff": "Defense", "amount": 3,
                                                                    "durationTicks": 300}),
    ("healing_potion", "Healing Draught", "mirewort", "raw_eel", 30, 60, {"heal": 20}),
    ("focus_potion", "Focus Draught", "mirewort", "spirit_shard", 38, 75, {"buff": "Magic", "amount": 4,
                                                                            "durationTicks": 300}),
    ("hunter_potion", "Hunter's Draught", "ashbloom", "feather", 50, 105, {"buff": "Ranged", "amount": 5,
                                                                          "durationTicks": 300}),
]
for pid, pname, herb, sec, lvl, xp, eff in POTIONS:
    item(pid, pname, "A potion. %s" % ("Restores health." if "heal" in eff else "Temporarily boosts %s." %
                                         eff["buff"]), "Potion", 20 + lvl * 3, model="potion", tint=pid,
         consumeEffect=eff)
    recipe("brew_" + pid, "Herblore", lvl, [("vial_water", 1), (herb, 1), (sec, 1)], [(pid, 1)], xp, ticks=3)

# ---------------------------------------------------------------------------
# Crafting: leather, jewellery, utility
# ---------------------------------------------------------------------------
LEATHER = [
    ("leather_gloves", "Leather Gloves", "Hands", 1, 1, 14, {"defenseMelee": 1, "defenseRanged": 2}),
    ("leather_boots", "Leather Boots", "Feet", 7, 1, 16, {"defenseMelee": 1, "defenseRanged": 2}),
    ("leather_cowl", "Leather Cowl", "Head", 9, 1, 18, {"defenseMelee": 2, "defenseRanged": 3}),
    ("leather_chaps", "Leather Chaps", "Legs", 11, 2, 22, {"defenseMelee": 3, "defenseRanged": 5,
                                                            "attackRanged": 2}),
    ("leather_body", "Leather Body", "Body", 14, 3, 25, {"defenseMelee": 5, "defenseRanged": 8,
                                                          "attackRanged": 3}),
    ("wolfhide_cape", "Wolfhide Cape", "Cape", 20, 0, 40, {"defenseMelee": 2, "defenseRanged": 2,
                                                            "defenseMagic": 1}),
]
for lid, lname, slot, lvl, n, xp, stats in LEATHER:
    item(lid, lname, "Made from tanned leather.", "Armour", 15 + lvl * 4, slot=slot, model="a_" + lid,
         requirements=[{"skill": "Defense", "level": 1}], stats=stats)
    ins = [("leather", n), ("thread", 1)] if n else [("wolf_pelt", 2), ("thread", 1)]
    recipe("craft_" + lid, "Crafting", lvl, ins, [(lid, 1)], xp, tools=["needle"], ticks=3)
recipe("tan_hide", "Crafting", 1, [("cowhide", 1), ("coins", 2)], [("leather", 1)], 1, station="Tannery",
       ticks=1)
GEMS = [("amber", "Amber", 1, 40), ("jade", "Jade", 20, 90), ("garnet", "Garnet", 35, 180),
        ("sapphire", "Sapphire", 50, 350), ("starstone", "Starstone", 70, 800)]
for gid, gname, lvl, val in GEMS:
    item("uncut_" + gid, "Uncut " + gname, "A rough gemstone.", "Resource", val // 2, model="gem_uncut", tint=gid)
    item(gid, gname, "A cut and polished gemstone.", "Resource", val, model="gem", tint=gid)
    recipe("cut_" + gid, "Crafting", lvl, [("uncut_" + gid, 1)], [(gid, 1)], 20 + lvl, tools=["chisel"], ticks=2)
    bonus = 1 + lvl // 12
    item(gid + "_ring", gname + " Ring", "A gold ring set with %s." % gname.lower(), "Jewellery", val + 200,
         slot="Ring", model="ring", tint=gid,
         stats={"attackMelee": bonus, "attackRanged": bonus, "attackMagic": bonus, "defenseMagic": bonus})
    item(gid + "_amulet", gname + " Amulet", "A gold amulet set with %s." % gname.lower(), "Jewellery",
         val + 300, slot="Amulet", model="amulet", tint=gid,
         stats={"strengthMelee": bonus, "strengthRanged": bonus, "magicPower": bonus,
                "defenseMelee": bonus})
    recipe("craft_%s_ring" % gid, "Crafting", min(99, lvl + 5), [("gold_bar", 1), (gid, 1)],
           [(gid + "_ring", 1)], 40 + lvl, station="Furnace", tools=["ring_mould"], ticks=3)
    recipe("craft_%s_amulet" % gid, "Crafting", min(99, lvl + 10), [("gold_bar", 1), (gid, 1)],
           [(gid + "_amulet", 1)], 50 + lvl, station="Furnace", tools=["amulet_mould"], ticks=3)
item("silver_ring", "Silver Ring", "A plain silver band.", "Jewellery", 120, slot="Ring", model="ring",
     tint="silver", stats={"defenseMagic": 1})
recipe("craft_silver_ring", "Crafting", 16, [("silver_bar", 1)], [("silver_ring", 1)], 30, station="Furnace",
       tools=["ring_mould"], ticks=3)
item("chisel", "Chisel", "For cutting gems.", "Tool", 4)
item("ring_mould", "Ring Mould", "Shapes molten metal into a ring.", "Tool", 5)
item("amulet_mould", "Amulet Mould", "Shapes molten metal into an amulet.", "Tool", 5)
recipe("craft_vial", "Crafting", 4, [("sand", 1), ("soda_ash", 1)], [("vial", 1)], 18, station="Furnace",
       ticks=3)
item("sand", "Bucket of Sand", "Fine sand.", "Resource", 2)
item("soda_ash", "Soda Ash", "Burnt seaweed, used in glassmaking.", "Resource", 3)
recipe("fill_vial", "Crafting", 1, [("vial", 1)], [("vial_water", 1)], 0, station="Water", ticks=1)
recipe("fill_bucket", "Crafting", 1, [("bucket", 1)], [("bucket_water", 1)], 0, station="Water", ticks=1)

# ---------------------------------------------------------------------------
# Runecrafting + Magic
# ---------------------------------------------------------------------------
RUNES = [
    ("air", "Gale", 1, 5), ("water", "Tide", 5, 6), ("earth", "Stone", 9, 7), ("fire", "Ember", 14, 8),
    ("mind", "Mind", 2, 5.5), ("life", "Life", 27, 9), ("chaos", "Chaos", 35, 10), ("void", "Void", 44, 11),
    ("ward", "Ward", 20, 8.5), ("path", "Path", 25, 9),
]
for rid, rname, lvl, xp in RUNES:
    item(rid + "_rune", rname + " Rune", "A rune charged with %s energy." % rname.lower(), "Rune",
         int(4 + lvl), stackable=True, weight=0.0, model="rune_stone", tint=rid)
    recipe("bind_" + rid, "Runecrafting", lvl, [("blank_essence", 1)], [(rid + "_rune", 1)], xp,
           station="Altar_" + rid, ticks=1, bulk=True)
item("apprentice_staff", "Apprentice Staff", "A plain ash staff.", "Weapon", 30, slot="Weapon",
     model="w_staff", tint="oak", weaponType="Staff", twoHanded=True,
     requirements=[{"skill": "Magic", "level": 1}], stats={"attackMagic": 5, "attackMelee": 2, "attackSpeed": 5,
                                                          "attackRange": 8})
item("emberwood_staff", "Emberwood Staff", "Warm to the touch. Boosts fire spells.", "Weapon", 600,
     slot="Weapon", model="w_staff", tint="fire", weaponType="Staff", twoHanded=True,
     requirements=[{"skill": "Magic", "level": 30}],
     stats={"attackMagic": 14, "magicPower": 4, "attackSpeed": 5, "attackRange": 8})
item("acolyte_robe", "Acolyte Robe", "Simple robes favoured by mages.", "Armour", 60, slot="Body",
     model="a_robe", tint="blue", stats={"attackMagic": 4, "defenseMagic": 4})
item("acolyte_hat", "Acolyte Hat", "A pointed hat.", "Armour", 30, slot="Head", model="a_hat", tint="blue",
     stats={"attackMagic": 2, "defenseMagic": 2})

SPELLS = [
    {"id": "gale_bolt", "name": "Gale Bolt", "type": "Attack", "element": "Air", "level": 1, "maxHit": 4,
     "runes": [{"item": "air_rune", "qty": 1}, {"item": "mind_rune", "qty": 1}], "xp": 6, "range": 8},
    {"id": "tide_bolt", "name": "Tide Bolt", "type": "Attack", "element": "Water", "level": 5, "maxHit": 6,
     "runes": [{"item": "water_rune", "qty": 1}, {"item": "air_rune", "qty": 1}, {"item": "mind_rune", "qty": 1}],
     "xp": 8, "range": 8},
    {"id": "stone_bolt", "name": "Stone Bolt", "type": "Attack", "element": "Earth", "level": 9, "maxHit": 8,
     "runes": [{"item": "earth_rune", "qty": 2}, {"item": "air_rune", "qty": 1}, {"item": "mind_rune", "qty": 1}],
     "xp": 10, "range": 8},
    {"id": "ember_bolt", "name": "Ember Bolt", "type": "Attack", "element": "Fire", "level": 14, "maxHit": 10,
     "runes": [{"item": "fire_rune", "qty": 2}, {"item": "air_rune", "qty": 2}, {"item": "mind_rune", "qty": 1}],
     "xp": 12, "range": 8},
    {"id": "chaos_lance", "name": "Chaos Lance", "type": "Attack", "element": "Fire", "level": 35, "maxHit": 16,
     "runes": [{"item": "chaos_rune", "qty": 1}, {"item": "fire_rune", "qty": 3}, {"item": "air_rune", "qty": 2}],
     "xp": 25, "range": 8},
    {"id": "void_storm", "name": "Void Storm", "type": "Attack", "element": "Void", "level": 50, "maxHit": 22,
     "runes": [{"item": "void_rune", "qty": 1}, {"item": "chaos_rune", "qty": 2}, {"item": "air_rune", "qty": 4}],
     "xp": 36, "range": 8},
    {"id": "mend", "name": "Mend", "type": "Heal", "element": "Life", "level": 10, "heal": 8,
     "runes": [{"item": "life_rune", "qty": 1}, {"item": "water_rune", "qty": 2}], "xp": 15, "range": 6},
    {"id": "wardskin", "name": "Wardskin", "type": "Buff", "element": "Earth", "level": 20, "buff": "Defense",
     "amount": 5, "durationTicks": 200, "runes": [{"item": "ward_rune", "qty": 1}, {"item": "earth_rune", "qty": 2}],
     "xp": 20, "range": 0},
    {"id": "home_path", "name": "Path to Brookhollow", "type": "Teleport", "element": "Path", "level": 15,
     "destination": "brookhollow_square",
     "runes": [{"item": "path_rune", "qty": 1}, {"item": "air_rune", "qty": 3}], "xp": 30, "range": 0},
    {"id": "capital_path", "name": "Path to Highmarch", "type": "Teleport", "element": "Path", "level": 30,
     "destination": "highmarch_plaza",
     "runes": [{"item": "path_rune", "qty": 2}, {"item": "air_rune", "qty": 4}], "xp": 45, "range": 0},
]

# ---------------------------------------------------------------------------
# Gathering nodes (mining, woodcutting, misc)
# ---------------------------------------------------------------------------
nodes = []
for oid, oname, lvl, xp, resp, val in ORES:
    nodes.append({"id": oid + "_rock", "name": "%s Rock" % oname if oid != "coal" else "Coal Rock",
                  "skill": "Mining", "toolType": "Pickaxe", "level": lvl,
                  "yields": [{"item": oid + "_ore", "level": lvl, "xp": xp, "weight": 1}],
                  "baseChance": max(0.12, 0.55 - lvl * 0.005), "depleteChance": 1.0, "respawnTicks": resp,
                  "model": "rock_" + oid, "depletedModel": "rock_depleted",
                  "gemChance": 0.004})
for tid, tname, lvl, xp, logname, resp, dep, val in TREES:
    nodes.append({"id": tid + "_tree", "name": tname, "skill": "Woodcutting", "toolType": "Hatchet",
                  "level": lvl, "yields": [{"item": tid + "_logs", "level": lvl, "xp": xp, "weight": 1}],
                  "baseChance": max(0.12, 0.5 - lvl * 0.005), "depleteChance": dep if tid != "oak" else 1.0,
                  "respawnTicks": resp, "model": "tree_" + tid, "depletedModel": "tree_stump"})
nodes += FISHING_SPOTS
nodes.append({"id": "essence_rock", "name": "Essence Vein", "skill": "Mining", "toolType": "Pickaxe",
              "level": 1, "yields": [{"item": "blank_essence", "level": 1, "xp": 5, "weight": 1}],
              "baseChance": 0.8, "depleteChance": 0.0, "respawnTicks": 0, "model": "rock_essence"})
nodes.append({"id": "sand_pit", "name": "Sand Pit", "skill": "Mining", "toolType": "Spade", "level": 1,
              "yields": [{"item": "sand", "level": 1, "xp": 2, "weight": 1}], "baseChance": 0.9,
              "depleteChance": 0.0, "respawnTicks": 0, "model": "sand_pit", "requiresItem": "bucket"})

# Thieving targets
THIEVING = [
    {"id": "pickpocket_villager", "name": "Pickpocket", "level": 1, "xp": 8, "stunTicks": 6, "damage": 1,
     "loot": [{"item": "coins", "min": 3, "max": 3, "weight": 1}], "targets": ["villager_m", "villager_f"]},
    {"id": "pickpocket_merchant", "name": "Pickpocket", "level": 25, "xp": 26, "stunTicks": 8, "damage": 3,
     "loot": [{"item": "coins", "min": 15, "max": 30, "weight": 4}, {"item": "uncut_amber", "min": 1, "max": 1,
                                                                      "weight": 1}],
     "targets": ["merchant"]},
    {"id": "steal_bakery", "name": "Steal from stall", "level": 5, "xp": 16, "stunTicks": 0, "damage": 0,
     "loot": [{"item": "bread", "min": 1, "max": 1, "weight": 1}], "stall": "bakery_stall", "respawnTicks": 8},
    {"id": "steal_gem", "name": "Steal from stall", "level": 45, "xp": 60, "stunTicks": 0, "damage": 0,
     "loot": [{"item": "uncut_jade", "min": 1, "max": 1, "weight": 3},
              {"item": "uncut_garnet", "min": 1, "max": 1, "weight": 1}], "stall": "gem_stall",
     "respawnTicks": 30},
]

# Agility obstacles
AGILITY = [
    {"id": "log_balance", "name": "Balance across", "level": 1, "xp": 8, "ticks": 4, "failChance": 0.1,
     "failDamage": 2, "model": "obstacle_log"},
    {"id": "rope_swing", "name": "Swing across", "level": 10, "xp": 14, "ticks": 4, "failChance": 0.15,
     "failDamage": 3, "model": "obstacle_rope"},
    {"id": "wall_climb", "name": "Climb", "level": 20, "xp": 22, "ticks": 5, "failChance": 0.15,
     "failDamage": 4, "model": "obstacle_wall"},
    {"id": "ledge_walk", "name": "Shimmy along", "level": 35, "xp": 35, "ticks": 5, "failChance": 0.2,
     "failDamage": 6, "model": "obstacle_ledge"},
    {"id": "course_lap", "name": "Lap bonus", "level": 1, "xp": 40, "ticks": 0, "failChance": 0,
     "failDamage": 0, "lapBonus": True},
]

# Prayer blessings
PRAYERS = [
    {"id": "stone_heart", "name": "Stone Heart", "level": 1, "drainPerMinute": 5, "effect": {"Defense": 0.05}},
    {"id": "iron_will", "name": "Iron Will", "level": 4, "drainPerMinute": 5, "effect": {"Strength": 0.05}},
    {"id": "keen_eye", "name": "Keen Eye", "level": 7, "drainPerMinute": 5, "effect": {"Attack": 0.05}},
    {"id": "hawk_sight", "name": "Hawk Sight", "level": 12, "drainPerMinute": 6, "effect": {"Ranged": 0.05}},
    {"id": "inner_flame", "name": "Inner Flame", "level": 15, "drainPerMinute": 6, "effect": {"Magic": 0.05}},
    {"id": "spirit_ward", "name": "Spirit Ward", "level": 40, "drainPerMinute": 20,
     "effect": {"DamageReduction": 0.25}},
]

# ---------------------------------------------------------------------------
# Monsters + loot
# ---------------------------------------------------------------------------
loot_tables = {}


def loot(id, always=None, rolls=1, entries=None, nothing=0, rare=None, rareChance=0.0):
    loot_tables[id] = {
        "id": id,
        "always": [{"item": i, "min": a, "max": b} for i, a, b in (always or [])],
        "rolls": rolls,
        "entries": [{"item": i, "min": a, "max": b, "weight": w} for i, a, b, w in (entries or [])],
        "nothingWeight": nothing,
        "rare": [{"item": i, "min": a, "max": b, "weight": w} for i, a, b, w in (rare or [])],
        "rareChance": rareChance,
    }


loot("rat", always=[("bones", 1, 1)], entries=[("coins", 1, 5, 3), ("raw_meat", 1, 1, 2)], nothing=5)
loot("goblin", always=[("bones", 1, 1)],
     entries=[("coins", 3, 15, 5), ("bronze_dagger", 1, 1, 1), ("goblin_charm", 1, 1, 3), ("air_rune", 4, 8, 2),
              ("mind_rune", 2, 5, 2), ("bait_worms", 5, 10, 2), ("potato_seed", 1, 3, 2)], nothing=4,
     rare=[("old_map_scrap", 1, 1, 1), ("uncut_amber", 1, 1, 2)], rareChance=0.02)
loot("wolf", always=[("big_bones", 1, 1), ("wolf_pelt", 1, 1)], entries=[("raw_meat", 1, 2, 3)], nothing=3)
loot("skeleton", always=[("bones", 1, 1)],
     entries=[("coins", 10, 40, 5), ("iron_sword", 1, 1, 1), ("iron_helm", 1, 1, 1), ("chaos_rune", 2, 5, 2),
              ("iron_arrow", 5, 15, 2), ("sunroot_seed", 1, 2, 2)], nothing=4,
     rare=[("uncut_jade", 1, 1, 2), ("ruin_relic", 1, 1, 1)], rareChance=0.03)
loot("spider", entries=[("coins", 5, 20, 3), ("thread", 3, 10, 4), ("mirewort_seed", 1, 1, 1)], nothing=3)
loot("zombie", always=[("bones", 1, 1)],
     entries=[("coins", 8, 30, 4), ("raw_meat", 1, 1, 2), ("steel_mace", 1, 1, 1), ("mirewort_seed", 1, 2, 2),
              ("earth_rune", 5, 12, 2)], nothing=4)
loot("orc", always=[("big_bones", 1, 1)],
     entries=[("coins", 20, 80, 5), ("steel_axe", 1, 1, 1), ("steel_chest", 1, 1, 1), ("coal_ore", 2, 5, 2),
              ("fire_rune", 5, 15, 2)], nothing=3, rare=[("uncut_garnet", 1, 1, 1)], rareChance=0.03)
loot("troll", always=[("big_bones", 1, 2)],
     entries=[("coins", 50, 200, 5), ("mithril_ore", 1, 3, 2), ("mithril_greatsword", 1, 1, 1),
              ("hearty_stew", 1, 2, 2)], nothing=2, rare=[("uncut_sapphire", 1, 1, 1)], rareChance=0.04)
loot("bandit", always=[("bones", 1, 1)],
     entries=[("coins", 15, 60, 6), ("leather_body", 1, 1, 1), ("oak_shortbow", 1, 1, 1), ("bronze_arrow", 10, 25, 2),
              ("lockpick", 1, 1, 1)], nothing=3)
loot("forest_spirit", entries=[("spirit_shard", 1, 2, 4), ("life_rune", 3, 8, 3), ("ashbloom_seed", 1, 1, 1)],
     nothing=2, rare=[("emberwood_staff", 1, 1, 1)], rareChance=0.01)
loot("boss_warden", always=[("big_bones", 3, 3), ("coins", 500, 1500), ("ruin_relic", 1, 2)],
     rolls=3, entries=[("duskite_ore", 3, 8, 3), ("duskite_greatsword", 1, 1, 1), ("duskite_chest", 1, 1, 1),
                       ("void_rune", 20, 50, 3), ("uncut_starstone", 1, 1, 1)],
     rare=[("starmetal_sword", 1, 1, 1)], rareChance=0.05)
loot("boss_mirequeen", always=[("big_bones", 2, 2), ("coins", 400, 1000)],
     rolls=2, entries=[("mirewort_seed", 3, 6, 3), ("healing_potion", 2, 4, 3), ("mithril_chest", 1, 1, 1),
                       ("sapphire_amulet", 1, 1, 1)], rare=[("uncut_starstone", 1, 1, 1)], rareChance=0.05)

MONSTERS = [
    # id, name, level, hp, attack, strength, defense, speed, aggressive, aggro range, wander, respawn, loot, model,
    ("giant_rat", "Giant Rat", 2, 8, 1, 2, 1, 4, False, 0, 5, 20, "rat", "enemy_rat", {}),
    ("goblin", "Goblin Scrapper", 5, 14, 4, 4, 3, 5, False, 0, 6, 25, "goblin", "enemy_goblin", {}),
    ("wolf", "Grey Wolf", 14, 30, 12, 12, 9, 4, True, 5, 8, 35, "wolf", "enemy_wolf", {}),
    ("skeleton", "Restless Skeleton", 22, 40, 20, 18, 18, 5, True, 4, 4, 40, "skeleton", "enemy_skeleton", {}),
    ("giant_spider", "Webspinner", 18, 32, 18, 14, 12, 4, True, 4, 4, 30, "spider", "enemy_spider",
     {"poison": 2}),
    ("zombie", "Marsh Shambler", 26, 55, 22, 24, 16, 6, True, 4, 3, 45, "zombie", "enemy_zombie", {}),
    ("orc", "Ironridge Orc", 38, 80, 36, 38, 32, 5, True, 5, 6, 60, "orc", "enemy_orc", {}),
    ("troll", "Cave Troll", 55, 140, 50, 58, 48, 6, True, 5, 4, 90, "troll", "enemy_troll", {}),
    ("bandit", "Roadside Bandit", 20, 38, 18, 18, 14, 5, True, 5, 6, 40, "bandit", "enemy_bandit", {}),
    ("forest_spirit", "Forest Spirit", 44, 70, 38, 30, 40, 5, False, 0, 6, 70, "forest_spirit",
     "enemy_forest_spirit", {"style": "Magic", "range": 6}),
]
monsters = []
for (mid, mname, lvl, hp, att, st, df, spd, aggr, arange, wander, resp, lt, model, extra) in MONSTERS:
    m = {"id": mid, "name": mname, "level": lvl, "hp": hp, "attack": att, "strength": st, "defense": df,
         "attackSpeed": spd, "aggressive": aggr, "aggroRange": arange, "wanderRange": wander, "respawnTicks": resp,
         "lootTable": lt, "model": model, "style": extra.get("style", "Melee"), "attackRange": extra.get("range", 1),
         "chaseRange": 12, "boss": False, "abilities": []}
    if "poison" in extra:
        m["poison"] = extra["poison"]
    monsters.append(m)
monsters.append({"id": "ruin_warden", "name": "The Hollow Warden", "level": 90, "hp": 600, "attack": 80,
                 "strength": 85, "defense": 75, "attackSpeed": 6, "aggressive": True, "aggroRange": 7,
                 "wanderRange": 2, "respawnTicks": 300, "lootTable": "boss_warden", "model": "boss_warden",
                 "style": "Melee", "attackRange": 2, "chaseRange": 14, "boss": True,
                 "abilities": [{"id": "ground_slam", "everyAttacks": 4, "radius": 3, "maxHit": 30,
                                "telegraphTicks": 2},
                               {"id": "summon_skeletons", "atHpFraction": 0.5, "monster": "skeleton",
                                "count": 3}]})
monsters.append({"id": "mire_queen", "name": "Mire Queen", "level": 70, "hp": 400, "attack": 60, "strength": 55,
                 "defense": 50, "attackSpeed": 5, "aggressive": True, "aggroRange": 7, "wanderRange": 2,
                 "respawnTicks": 240, "lootTable": "boss_mirequeen", "model": "boss_mire_queen", "style": "Magic",
                 "attackRange": 6, "chaseRange": 12, "boss": True,
                 "abilities": [{"id": "poison_cloud", "everyAttacks": 3, "radius": 2, "maxHit": 12,
                                "telegraphTicks": 2, "poison": 4}]})

# ---------------------------------------------------------------------------
# NPCs, dialogue, shops, quests
# ---------------------------------------------------------------------------
SHOPS = [
    {"id": "brookhollow_general", "name": "Brookhollow General Goods", "generalStore": True,
     "buyMultiplier": 1.0, "sellMultiplier": 0.4,
     "stock": [{"item": i, "qty": q, "restockTicks": 50} for i, q in [
         ("bronze_pickaxe", 5), ("bronze_hatchet", 5), ("small_net", 5), ("fishing_rod", 5), ("bait_worms", 500),
         ("tinderbox", 5), ("knife", 5), ("hammer", 5), ("needle", 5), ("thread", 200), ("chisel", 5),
         ("bucket", 10), ("vial", 20), ("watering_can", 5), ("rake", 5), ("spade", 5), ("potato_seed", 50),
         ("onion_seed", 30), ("bread", 10), ("ring_mould", 3), ("amulet_mould", 3)]]},
    {"id": "brookhollow_smith", "name": "Hollin's Forge", "generalStore": False, "buyMultiplier": 1.2,
     "sellMultiplier": 0.5,
     "stock": [{"item": i, "qty": q, "restockTicks": 100} for i, q in [
         ("bronze_sword", 3), ("bronze_dagger", 3), ("bronze_helm", 3), ("bronze_chest", 2), ("bronze_legs", 2),
         ("bronze_shield", 2), ("iron_sword", 2), ("iron_pickaxe", 2), ("iron_hatchet", 2), ("bronze_arrow", 300),
         ("oak_shortbow", 2), ("hammer", 3)]]},
    {"id": "highmarch_magic", "name": "The Gilded Sigil", "generalStore": False, "buyMultiplier": 1.1,
     "sellMultiplier": 0.5,
     "stock": [{"item": i, "qty": q, "restockTicks": 60} for i, q in [
         ("air_rune", 1000), ("water_rune", 500), ("earth_rune", 500), ("fire_rune", 500), ("mind_rune", 800),
         ("path_rune", 50), ("apprentice_staff", 3), ("acolyte_robe", 3), ("acolyte_hat", 3)]]},
    {"id": "highmarch_arms", "name": "Highmarch Armoury", "generalStore": False, "buyMultiplier": 1.15,
     "sellMultiplier": 0.5,
     "stock": [{"item": i, "qty": q, "restockTicks": 120} for i, q in [
         ("steel_sword", 3), ("steel_greatsword", 2), ("steel_chest", 2), ("steel_legs", 2), ("steel_helm", 3),
         ("steel_shield", 2), ("willow_longbow", 2), ("iron_arrow", 500), ("oak_crossbow", 2), ("iron_bolt", 300),
         ("leather_body", 3), ("leather_chaps", 3), ("harpoon", 2)]]},
    {"id": "highmarch_apothecary", "name": "Mossbottle Apothecary", "generalStore": False, "buyMultiplier": 1.1,
     "sellMultiplier": 0.5,
     "stock": [{"item": i, "qty": q, "restockTicks": 80} for i, q in [
         ("vial_water", 50), ("sunroot_seed", 20), ("mirewort_seed", 10), ("healing_potion", 5),
         ("cabbage_seed", 30), ("wheat_seed", 30)]]},
]

DIALOGUES = {
    "elder_maren": [
        {"id": "start", "speaker": "Elder Maren", "text": "Welcome to Brookhollow, traveller. These are troubled times. Goblins raid our fields, and something stirs in the old ruins.",
         "options": [
             {"text": "Can I help with the goblins?", "next": "goblins", "condition": {"questNotStarted": "goblin_trouble"}},
             {"text": "I've dealt with the goblins.", "next": "goblins_done", "condition": {"questStage": ["goblin_trouble", "return"]}},
             {"text": "Tell me about Embervale.", "next": "lore"},
             {"text": "Farewell.", "next": None}]},
        {"id": "goblins", "speaker": "Elder Maren", "text": "Bless you. Drive off five goblins at the edge of Greenleaf Forest and bring me three of their crude charms as proof.",
         "options": [{"text": "I'll do it.", "next": None, "action": {"startQuest": "goblin_trouble"}},
                     {"text": "Maybe later.", "next": None}]},
        {"id": "goblins_done", "speaker": "Elder Maren", "text": "Three charms! The fields will be safe for a while. Take this for your trouble.",
         "options": [{"text": "Thank you.", "next": None, "action": {"completeQuestStage": "goblin_trouble"}}]},
        {"id": "lore", "speaker": "Elder Maren", "text": "Embervale was once lit by the great Hearthstones. When they dimmed, the old kingdom fell to ruin. Highmarch to the north is all that remains of its glory.",
         "options": [{"text": "Back.", "next": "start"}]},
    ],
    "hollin_smith": [
        {"id": "start", "speaker": "Hollin the Smith", "text": "Need steel? Or need to learn to make your own?",
         "options": [{"text": "Show me your wares.", "next": None, "action": {"openShop": "brookhollow_smith"}},
                     {"text": "Teach me to smith.", "next": "teach", "condition": {"questNotStarted": "first_forge"}},
                     {"text": "I've made the dagger.", "next": "done", "condition": {"questStage": ["first_forge", "return"]}},
                     {"text": "Goodbye.", "next": None}]},
        {"id": "teach", "speaker": "Hollin the Smith", "text": "Mine copper and tin in the quarry east of the village, smelt a bronze bar at my furnace, then hammer a dagger on the anvil. Bring it here.",
         "options": [{"text": "Sounds simple enough.", "next": None, "action": {"startQuest": "first_forge"}}]},
        {"id": "done", "speaker": "Hollin the Smith", "text": "Not bad for a first try! Keep it, and take a few bars to practise.",
         "options": [{"text": "Thanks!", "next": None, "action": {"completeQuestStage": "first_forge"}}]},
    ],
    "tilda_merchant": [
        {"id": "start", "speaker": "Tilda", "text": "Supplies for every adventurer! Take a look.",
         "options": [{"text": "Let's trade.", "next": None, "action": {"openShop": "brookhollow_general"}},
                     {"text": "No thanks.", "next": None}]}],
    "banker": [
        {"id": "start", "speaker": "Banker", "text": "Good day. How may the Hearthvault serve you?",
         "options": [{"text": "I'd like to access my vault.", "next": None, "action": {"openBank": True}},
                     {"text": "Nothing, thanks.", "next": None}]}],
    "exchange_clerk": [
        {"id": "start", "speaker": "Exchange Clerk", "text": "Welcome to the Embervale Exchange. Post an offer and we'll match it with other adventurers, even while you're away.",
         "options": [{"text": "I'd like to trade on the Exchange.", "next": None, "action": {"openMarket": True}},
                     {"text": "How does it work?", "next": "help"},
                     {"text": "Nothing, thanks.", "next": None}]},
        {"id": "help", "speaker": "Exchange Clerk", "text": "Buy offers hold your coins, sell offers hold your goods. Trades happen at the older offer's price, any overpayment is refunded, and the Exchange keeps one coin in every hundred from sales.",
         "options": [{"text": "Let me trade.", "next": None, "action": {"openMarket": True}},
                     {"text": "Thanks.", "next": None}]}],
    "guard": [
        {"id": "start", "speaker": "Guard", "text": "Keep out of trouble. The roads past the forest aren't safe after dark.",
         "options": [{"text": "I'll be careful.", "next": None}]}],
    "villager": [
        {"id": "start", "speaker": "Villager", "text": "Lovely weather today, isn't it?",
         "options": [{"text": "It is.", "next": None}]}],
    "innkeeper_bram": [
        {"id": "start", "speaker": "Bram the Innkeeper", "text": "Warm stew and a soft bed. Folk say a lost map of the forest is held by the goblins.",
         "options": [{"text": "A map?", "next": "map", "condition": {"questNotStarted": "lost_map"}},
                     {"text": "I found the map scrap.", "next": "map_done", "condition": {"questStage": ["lost_map", "return"]}},
                     {"text": "Goodbye.", "next": None}]},
        {"id": "map", "speaker": "Bram the Innkeeper", "text": "My grandmother's map shows a hidden grove deep in Greenleaf. Find the scrap and visit the grove for me.",
         "options": [{"text": "I'll look for it.", "next": None, "action": {"startQuest": "lost_map"}}]},
        {"id": "map_done", "speaker": "Bram the Innkeeper", "text": "So the grove is real! Here, take these for your trouble.",
         "options": [{"text": "Happy to help.", "next": None, "action": {"completeQuestStage": "lost_map"}}]}],
    "warden_seeker": [
        {"id": "start", "speaker": "Seeker Ilsa", "text": "The Hollow Warden guards the deepest vault of the Ancient Ruins. Defeat it and the Hearthstone shard is yours.",
         "options": [{"text": "I accept the challenge.", "next": None, "action": {"startQuest": "hollow_warden"}, "condition": {"questNotStarted": "hollow_warden"}},
                     {"text": "The Warden has fallen.", "next": "done", "condition": {"questStage": ["hollow_warden", "return"]}},
                     {"text": "Not yet.", "next": None}]},
        {"id": "done", "speaker": "Seeker Ilsa", "text": "Incredible. Embervale owes you a debt.",
         "options": [{"text": "It was nothing.", "next": None, "action": {"completeQuestStage": "hollow_warden"}}]}],
    "fisher_odo": [
        {"id": "start", "speaker": "Odo the Fisher", "text": "Fish are biting by the lake. Catch me five perch and cook them, would you?",
         "options": [{"text": "Sure.", "next": None, "action": {"startQuest": "fishy_favour"}, "condition": {"questNotStarted": "fishy_favour"}},
                     {"text": "Here are your perch.", "next": "done", "condition": {"questStage": ["fishy_favour", "return"]}},
                     {"text": "Bye.", "next": None}]},
        {"id": "done", "speaker": "Odo the Fisher", "text": "Delicious! Here's a harpoon voucher... well, some coins anyway.",
         "options": [{"text": "Thanks.", "next": None, "action": {"completeQuestStage": "fishy_favour"}}]}],
    "magic_vendor": [
        {"id": "start", "speaker": "Archivist Sel", "text": "Runes, staves and robes. Mind the sparks.",
         "options": [{"text": "Show me.", "next": None, "action": {"openShop": "highmarch_magic"}},
                     {"text": "Goodbye.", "next": None}]}],
    "arms_vendor": [
        {"id": "start", "speaker": "Quartermaster Dane", "text": "Highmarch steel. The best in the realm.",
         "options": [{"text": "Show me.", "next": None, "action": {"openShop": "highmarch_arms"}},
                     {"text": "Goodbye.", "next": None}]}],
    "apothecary": [
        {"id": "start", "speaker": "Wenna Mossbottle", "text": "Herbs, seeds and remedies!",
         "options": [{"text": "Let me browse.", "next": None, "action": {"openShop": "highmarch_apothecary"}},
                     {"text": "Goodbye.", "next": None}]}],
}

NPCS = [
    {"id": "elder_maren", "name": "Elder Maren", "model": "npc_elder", "role": "Quest", "dialogue": "elder_maren"},
    {"id": "hollin_smith", "name": "Hollin the Smith", "model": "npc_blacksmith", "role": "Blacksmith",
     "dialogue": "hollin_smith"},
    {"id": "tilda_merchant", "name": "Tilda", "model": "npc_merchant", "role": "Merchant",
     "dialogue": "tilda_merchant"},
    {"id": "banker", "name": "Banker", "model": "npc_banker", "role": "Banker", "dialogue": "banker"},
    {"id": "exchange_clerk", "name": "Exchange Clerk", "model": "npc_merchant", "role": "Exchange", "dialogue": "exchange_clerk"},
    {"id": "guard", "name": "Guard", "model": "npc_guard", "role": "Guard", "dialogue": "guard", "wander": 3},
    {"id": "villager_m", "name": "Villager", "model": "npc_villager_m", "role": "Villager", "dialogue": "villager",
     "wander": 6},
    {"id": "villager_f", "name": "Villager", "model": "npc_villager_f", "role": "Villager", "dialogue": "villager",
     "wander": 6},
    {"id": "merchant", "name": "Travelling Merchant", "model": "npc_merchant", "role": "Villager",
     "dialogue": "villager", "wander": 4},
    {"id": "innkeeper_bram", "name": "Bram the Innkeeper", "model": "npc_innkeeper", "role": "Quest",
     "dialogue": "innkeeper_bram"},
    {"id": "warden_seeker", "name": "Seeker Ilsa", "model": "npc_seeker", "role": "Quest",
     "dialogue": "warden_seeker"},
    {"id": "fisher_odo", "name": "Odo the Fisher", "model": "npc_fisher", "role": "Quest", "dialogue": "fisher_odo"},
    {"id": "magic_vendor", "name": "Archivist Sel", "model": "npc_mage", "role": "Merchant",
     "dialogue": "magic_vendor"},
    {"id": "arms_vendor", "name": "Quartermaster Dane", "model": "npc_guard", "role": "Merchant",
     "dialogue": "arms_vendor"},
    {"id": "apothecary", "name": "Wenna Mossbottle", "model": "npc_villager_f", "role": "Merchant",
     "dialogue": "apothecary"},
]

QUESTS = [
    {"id": "first_forge", "name": "The First Forge", "category": "Crafting", "questPoints": 1,
     "description": "Hollin the Smith will teach you the basics of smithing.", "requirements": {},
     "stages": [
         {"id": "make", "journal": "Mine copper and tin, smelt a bronze bar and forge a bronze dagger.",
          "objectives": [{"type": "Craft", "target": "bronze_dagger", "count": 1}]},
         {"id": "return", "journal": "Bring the dagger to Hollin.", "objectives": [{"type": "Talk", "target": "hollin_smith", "count": 1}]}],
     "rewards": {"xp": [{"skill": "Smithing", "amount": 250}, {"skill": "Mining", "amount": 150}],
                 "items": [{"item": "bronze_bar", "qty": 5}, {"item": "coins", "qty": 100}]}},
    {"id": "goblin_trouble", "name": "Goblin Trouble", "category": "Combat", "questPoints": 1,
     "description": "Elder Maren needs the goblin raiders driven off.", "requirements": {},
     "stages": [
         {"id": "hunt", "journal": "Defeat five goblins and collect three goblin charms.",
          "objectives": [{"type": "Kill", "target": "goblin", "count": 5},
                         {"type": "Collect", "target": "goblin_charm", "count": 3, "consume": True}]},
         {"id": "return", "journal": "Return to Elder Maren.", "objectives": [{"type": "Talk", "target": "elder_maren", "count": 1}]}],
     "rewards": {"xp": [{"skill": "Attack", "amount": 300}, {"skill": "Strength", "amount": 300}],
                 "items": [{"item": "iron_sword", "qty": 1}, {"item": "coins", "qty": 250}]}},
    {"id": "fishy_favour", "name": "A Fishy Favour", "category": "Gathering", "questPoints": 1,
     "description": "Odo the Fisher is hungry for cooked perch.", "requirements": {"skills": [{"skill": "Fishing", "level": 5}]},
     "stages": [
         {"id": "catch", "journal": "Catch and cook five perch.",
          "objectives": [{"type": "Gather", "target": "raw_perch", "count": 5},
                         {"type": "Collect", "target": "perch", "count": 5, "consume": True}]},
         {"id": "return", "journal": "Bring the perch to Odo.", "objectives": [{"type": "Talk", "target": "fisher_odo", "count": 1}]}],
     "rewards": {"xp": [{"skill": "Fishing", "amount": 400}, {"skill": "Cooking", "amount": 400}],
                 "items": [{"item": "coins", "qty": 300}]}},
    {"id": "lost_map", "name": "The Lost Grove", "category": "Exploration", "questPoints": 2,
     "description": "Bram's grandmother hid a map to a secret grove.", "requirements": {},
     "stages": [
         {"id": "scrap", "journal": "Find the old map scrap. The goblins may have it.",
          "objectives": [{"type": "Collect", "target": "old_map_scrap", "count": 1}]},
         {"id": "visit", "journal": "Find the hidden grove deep in Greenleaf Forest.",
          "objectives": [{"type": "Visit", "target": "hidden_grove", "count": 1}]},
         {"id": "return", "journal": "Tell Bram what you found.", "objectives": [{"type": "Talk", "target": "innkeeper_bram", "count": 1}]}],
     "rewards": {"xp": [{"skill": "Woodcutting", "amount": 600}, {"skill": "Agility", "amount": 400}],
                 "items": [{"item": "coins", "qty": 500}, {"item": "uncut_jade", "qty": 1}]}},
    {"id": "hollow_warden", "name": "The Hollow Warden", "category": "Story", "questPoints": 5,
     "description": "Defeat the guardian of the Ancient Ruins.",
     "requirements": {"quests": ["goblin_trouble"], "skills": [{"skill": "Attack", "level": 40}]},
     "stages": [
         {"id": "defeat", "journal": "Defeat the Hollow Warden in the depths of the Ancient Ruins.",
          "objectives": [{"type": "Kill", "target": "ruin_warden", "count": 1}]},
         {"id": "return", "journal": "Return to Seeker Ilsa in Highmarch.", "objectives": [{"type": "Talk", "target": "warden_seeker", "count": 1}]}],
     "rewards": {"xp": [{"skill": "Attack", "amount": 20000}, {"skill": "Defense", "amount": 20000},
                        {"skill": "Vitality", "amount": 10000}],
                 "items": [{"item": "coins", "qty": 10000}, {"item": "starmetal_bar", "qty": 2}]}},
]

STARTER_KIT = [
    {"item": "bronze_dagger", "qty": 1}, {"item": "bronze_pickaxe", "qty": 1}, {"item": "bronze_hatchet", "qty": 1},
    {"item": "small_net", "qty": 1}, {"item": "tinderbox", "qty": 1}, {"item": "bread", "qty": 3},
    {"item": "coins", "qty": 25},
]

# ---------------------------------------------------------------------------
# Validation + output
# ---------------------------------------------------------------------------

def validate():
    errors = []
    item_ids = set(items)

    def chk(i, where):
        if i not in item_ids:
            errors.append("%s references unknown item '%s'" % (where, i))

    for r in recipes:
        for io in r["inputs"] + r["outputs"]:
            chk(io["item"], "recipe " + r["id"])
        for t in r["tools"]:
            chk(t, "recipe %s tool" % r["id"])
        if "failOutput" in r:
            chk(r["failOutput"], "recipe " + r["id"])
    for t in loot_tables.values():
        for e in t["always"] + t["entries"] + t["rare"]:
            chk(e["item"], "loot " + t["id"])
    for m in monsters:
        if m["lootTable"] not in loot_tables:
            errors.append("monster %s unknown loot table" % m["id"])
    for n in nodes:
        for y in n["yields"]:
            chk(y["item"], "node " + n["id"])
    for s in SHOPS:
        for st in s["stock"]:
            chk(st["item"], "shop " + s["id"])
    for q in QUESTS:
        for it in q["rewards"].get("items", []):
            chk(it["item"], "quest " + q["id"])
    for sp in SPELLS:
        for r in sp["runes"]:
            chk(r["item"], "spell " + sp["id"])
    for k in STARTER_KIT:
        chk(k["item"], "starter kit")
    for t in THIEVING:
        for l in t["loot"]:
            chk(l["item"], "thieving " + t["id"])
    for p in POTIONS:
        chk(p[2], "potion " + p[0])
        chk(p[3], "potion " + p[0])
    if errors:
        for e in errors:
            print("ERROR:", e, file=sys.stderr)
        sys.exit(1)


def write(name, data):
    path = os.path.join(OUT, name)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write("\n")


def main():
    validate()
    os.makedirs(OUT, exist_ok=True)
    write("experience.json", EXPERIENCE)
    write("skills.json", [{"id": s, "category": c, "description": d, "icon": "skill_" + s.lower()}
                          for s, c, d in SKILLS])
    write("items.json", sorted(items.values(), key=lambda x: x["id"]))
    write("recipes.json", recipes)
    write("nodes.json", nodes)
    write("crops.json", crops)
    write("monsters.json", monsters)
    write("loot.json", list(loot_tables.values()))
    write("npcs.json", NPCS)
    write("dialogues.json", [{"id": k, "nodes": v} for k, v in DIALOGUES.items()])
    write("shops.json", SHOPS)
    write("quests.json", QUESTS)
    write("spells.json", SPELLS)
    write("prayers.json", PRAYERS)
    write("thieving.json", THIEVING)
    write("agility.json", AGILITY)
    write("starter.json", {"items": STARTER_KIT, "spawnPoint": "brookhollow_square"})
    print("Wrote %d items, %d recipes, %d nodes, %d monsters, %d quests to %s" %
          (len(items), len(recipes), len(nodes), len(monsters), len(QUESTS), OUT))


if __name__ == "__main__":
    main()
