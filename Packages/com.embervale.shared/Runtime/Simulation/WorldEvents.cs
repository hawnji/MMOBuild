using Embervale.Shared.Data;

namespace Embervale.Shared.Simulation
{
    public enum WorldEventType : byte
    {
        GameMessage,      // PlayerId, Text
        Chat,             // EntityId (speaker), Text, Channel, Text2 (speaker name)
        HitSplat,         // EntityId (victim), TargetId (attacker), Value (damage), Flag (critical/miss)
        Projectile,       // EntityId (from), TargetId (to), Text (projectile kind)
        Animation,        // EntityId, Value (ActionAnim)
        LevelUp,          // PlayerId, Skill, Value (new level)
        XpDrop,           // PlayerId, Skill, Value (xp tenths)
        Death,            // EntityId
        DialogueOpen,     // PlayerId - read dialogue fields from player
        DialogueClose,    // PlayerId
        ShopOpen,         // PlayerId, Text (shop id)
        ShopClose,        // PlayerId
        BankOpen,         // PlayerId
        BankClose,        // PlayerId
        TradeRequest,     // PlayerId (target), EntityId (requester), Text (name)
        TradeUpdate,      // PlayerId
        TradeClose,       // PlayerId, Text (reason)
        PartyUpdate,      // PlayerId
        QuestUpdate,      // PlayerId, Text (quest id), Flag (completed)
        Telegraph,        // EntityId (source), Value (radius), Text (ability id), X, Z
        Teleport,         // EntityId
        Sound,            // EntityId, Text (sound id)
        CraftMenu,        // PlayerId, EntityId (station), Text (station type)
        MarketOpen,       // PlayerId, EntityId (clerk)
        MarketUpdate,     // PlayerId - resend the player's orders
        MarketQuote,      // PlayerId, Text (item id)
        MarketClose,      // PlayerId
    }

    public enum ChatChannel : byte { Local, Party, Private, Trade, System }

    /// <summary>
    /// Something that happened during a tick that clients may need to know about. Kept as a flat
    /// record so the server can map it to network messages without per-type classes.
    /// </summary>
    public sealed class WorldEvent
    {
        public WorldEventType Type;
        public int PlayerId;     // recipient player entity (0 = area broadcast around EntityId)
        public int EntityId;
        public int TargetId;
        public int Value;
        public bool Flag;
        public Skill Skill;
        public string Text;
        public string Text2;
        public ChatChannel Channel;
        public float X, Z;
    }
}
