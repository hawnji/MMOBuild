using System;
using System.Collections.Generic;
using System.Text;
using Embervale.Shared.Data;

namespace Embervale.Shared.Simulation
{
    public sealed partial class GameWorld
    {
        // ------------------------------------------------------------------
        // Dialogue
        // ------------------------------------------------------------------
        private void StartDialogue(Player p, Npc npc)
        {
            if (npc.Def.Dialogue == null || !Db.Dialogues.TryGetValue(npc.Def.Dialogue, out var dlg)) { Message(p, npc.Def.Name + " has nothing to say."); return; }
            npc.Path.Clear();
            npc.FaceTowards(p.Position);
            npc.MarkStateChanged();
            p.DialogueNpcId = npc.Id;
            p.DialogueId = dlg.Id;
            ShowDialogueNode(p, dlg, "start");
        }

        private bool ConditionMet(Player p, DialogueCondition c)
        {
            if (c == null) return true;
            if (c.QuestNotStarted != null && p.Quests.IsStarted(c.QuestNotStarted)) return false;
            if (c.QuestComplete != null && !p.Quests.IsComplete(c.QuestComplete)) return false;
            if (c.QuestStage != null && c.QuestStage.Count == 2 && p.Quests.CurrentStageId(c.QuestStage[0]) != c.QuestStage[1]) return false;
            return true;
        }

        private void ShowDialogueNode(Player p, DialogueDef dlg, string nodeId)
        {
            var node = nodeId == null ? null : dlg.Find(nodeId);
            if (node == null) { CloseInterfaces(p); return; }
            p.DialogueNode = node.Id;
            p.DialogueOptionMap.Clear();
            for (int i = 0; i < node.Options.Count; i++)
                if (ConditionMet(p, node.Options[i].Condition)) p.DialogueOptionMap.Add(i);
            Emit(new WorldEvent { Type = WorldEventType.DialogueOpen, PlayerId = p.Id, EntityId = p.DialogueNpcId });
        }

        /// <summary>Visible options of the player's current dialogue node.</summary>
        public List<string> DialogueOptions(Player p, out string speaker, out string text)
        {
            speaker = text = null;
            var list = new List<string>();
            if (p.DialogueId == null || !Db.Dialogues.TryGetValue(p.DialogueId, out var dlg)) return list;
            var node = dlg.Find(p.DialogueNode);
            if (node == null) return list;
            speaker = node.Speaker;
            text = node.Text;
            foreach (var i in p.DialogueOptionMap) list.Add(node.Options[i].Text);
            return list;
        }

        public void RequestDialogueOption(Player p, int visibleIndex)
        {
            if (p.DialogueId == null || !Db.Dialogues.TryGetValue(p.DialogueId, out var dlg)) return;
            var node = dlg.Find(p.DialogueNode);
            if (node == null || visibleIndex < 0 || visibleIndex >= p.DialogueOptionMap.Count) return;
            var opt = node.Options[p.DialogueOptionMap[visibleIndex]];
            if (!ConditionMet(p, opt.Condition)) return;
            var npc = Get<Npc>(p.DialogueNpcId);
            if (npc == null || Vec2.TileDistance(npc.Position, p.Position) > 4) { CloseInterfaces(p); return; }
            var act = opt.Action;
            if (act != null)
            {
                if (act.StartQuest != null) StartQuest(p, act.StartQuest);
                if (act.CompleteQuestStage != null) CompleteTalkStage(p, act.CompleteQuestStage, npc.DefId);
                if (act.OpenShop != null) { CloseInterfaces(p); OpenShop(p, act.OpenShop, npc); return; }
                if (act.OpenBank) { CloseInterfaces(p); OpenBank(p, npc); return; }
            }
            ShowDialogueNode(p, dlg, opt.Next);
        }

        // ------------------------------------------------------------------
        // Quests
        // ------------------------------------------------------------------
        public void StartQuest(Player p, string questId)
        {
            if (!p.Quests.CanStart(questId, p.Skills, out var reason)) { Message(p, reason); return; }
            p.Quests.Start(questId);
            p.QuestsDirty = true;
            Message(p, "Quest started: " + Db.Quests[questId].Name);
            Emit(new WorldEvent { Type = WorldEventType.QuestUpdate, PlayerId = p.Id, Text = questId });
            EvaluateQuests(p);
        }

        private void CompleteTalkStage(Player p, string questId, string npcDefId)
        {
            if (!p.Quests.Quests.TryGetValue(questId, out var prog) || prog.Completed) return;
            var def = Db.Quests[questId];
            var stage = def.Stages[prog.Stage];
            foreach (var o in stage.Objectives)
            {
                if (o.Type == ObjectiveType.Talk && o.Target != npcDefId) return;
                if (o.Type == ObjectiveType.Collect && !p.Inventory.Contains(o.Target, o.Count)) { Message(p, "You still need to bring the items."); return; }
            }
            foreach (var o in stage.Objectives)
                if (o.Type == ObjectiveType.Collect && o.Consume) p.Inventory.Remove(o.Target, o.Count);
            AdvanceQuest(p, prog);
        }

        /// <summary>Automatically advances stages whose objectives are all met.</summary>
        public void EvaluateQuests(Player p)
        {
            bool any = true;
            int guard = 0;
            while (any && guard++ < 10)
            {
                any = false;
                foreach (var prog in new List<QuestProgress>(p.Quests.Quests.Values))
                {
                    if (prog.Completed || !p.Quests.StageAutoComplete(prog, p.Inventory)) continue;
                    var stage = Db.Quests[prog.QuestId].Stages[prog.Stage];
                    foreach (var o in stage.Objectives)
                        if (o.Type == ObjectiveType.Collect && o.Consume) p.Inventory.Remove(o.Target, o.Count);
                    AdvanceQuest(p, prog);
                    any = true;
                }
            }
        }

        private void AdvanceQuest(Player p, QuestProgress prog)
        {
            var def = Db.Quests[prog.QuestId];
            p.Quests.SetStage(prog, prog.Stage + 1);
            p.QuestsDirty = true;
            if (prog.Completed)
            {
                foreach (var x in def.Rewards.Xp) GiveXp(p, x.Skill, x.Amount);
                foreach (var it in def.Rewards.Items) GiveOrDrop(p, it.Item, it.Qty);
                Message(p, "Quest complete: " + def.Name + "! (+" + def.QuestPoints + " quest points)");
                Emit(new WorldEvent { Type = WorldEventType.QuestUpdate, PlayerId = p.Id, Text = def.Id, Flag = true });
            }
            else
            {
                Message(p, def.Stages[prog.Stage].Journal);
                Emit(new WorldEvent { Type = WorldEventType.QuestUpdate, PlayerId = p.Id, Text = def.Id });
            }
        }

        // ------------------------------------------------------------------
        // Bank
        // ------------------------------------------------------------------
        private void OpenBank(Player p, Entity at)
        {
            p.OpenBankEntityId = at.Id;
            p.BankDirty = true;
            Emit(new WorldEvent { Type = WorldEventType.BankOpen, PlayerId = p.Id, EntityId = at.Id });
        }

        private bool BankAccessible(Player p)
        {
            var e = Get(p.OpenBankEntityId);
            return e != null && Vec2.TileDistance(e.Position, p.Position) <= 4 && p.Trade == null && !p.IsDead;
        }

        public void RequestBankDeposit(Player p, int invSlot, int qty)
        {
            if (!BankAccessible(p)) return;
            var s = p.Inventory[invSlot];
            if (s.IsEmpty || qty <= 0) return;
            int have = p.Inventory.Count(s.ItemId);
            qty = Math.Min(qty, have);
            if (!p.Bank.CanAdd(s.ItemId, qty)) { Message(p, "Your bank is full."); return; }
            if (!p.Inventory.Remove(s.ItemId, qty)) return;
            p.Bank.Add(s.ItemId, qty);
        }

        public void RequestBankDepositAll(Player p)
        {
            if (!BankAccessible(p)) return;
            foreach (var s in new List<ItemStack>(p.Inventory.NonEmpty()))
            {
                int n = p.Inventory.Count(s.ItemId);
                if (n == 0 || !p.Bank.CanAdd(s.ItemId, n)) continue;
                p.Inventory.Remove(s.ItemId, n);
                p.Bank.Add(s.ItemId, n);
            }
        }

        public void RequestBankWithdraw(Player p, int bankSlot, int qty)
        {
            if (!BankAccessible(p)) return;
            var s = p.Bank[bankSlot];
            if (s.IsEmpty || qty <= 0) return;
            qty = Math.Min(qty, s.Quantity);
            int space = p.Inventory.SpaceFor(s.ItemId);
            qty = Math.Min(qty, space);
            if (qty <= 0) { Message(p, "You don't have enough inventory space."); return; }
            p.Bank.TakeFromSlot(bankSlot, qty);
            p.Inventory.Add(s.ItemId, qty);
            if (p.Bank[bankSlot].IsEmpty) p.Bank.Compact();
        }

        public void RequestBankSwap(Player p, int a, int b)
        {
            if (!BankAccessible(p)) return;
            p.Bank.Swap(a, b);
        }

        // ------------------------------------------------------------------
        // Shops
        // ------------------------------------------------------------------
        private void OpenShopFor(Player p, Npc npc)
        {
            if (npc.Def.Dialogue != null && Db.Dialogues.TryGetValue(npc.Def.Dialogue, out var dlg))
                foreach (var n in dlg.Nodes)
                    foreach (var o in n.Options)
                        if (o.Action?.OpenShop != null) { OpenShop(p, o.Action.OpenShop, npc); return; }
            StartDialogue(p, npc);
        }

        private void OpenShop(Player p, string shopId, Npc npc)
        {
            if (!Shops.ContainsKey(shopId)) return;
            p.OpenShopId = shopId;
            p.OpenShopNpcId = npc.Id;
            Emit(new WorldEvent { Type = WorldEventType.ShopOpen, PlayerId = p.Id, Text = shopId, EntityId = npc.Id });
        }

        private ShopState ShopFor(Player p)
        {
            if (p.OpenShopId == null || p.Trade != null || p.IsDead) return null;
            var npc = Get(p.OpenShopNpcId);
            if (npc == null || Vec2.TileDistance(npc.Position, p.Position) > 4) return null;
            return Shops.TryGetValue(p.OpenShopId, out var s) ? s : null;
        }

        public void RequestShopBuy(Player p, string itemId, int qty)
        {
            var shop = ShopFor(p);
            if (shop == null || qty <= 0) return;
            var def = Db.Item(itemId);
            if (def == null) return;
            int bought = 0;
            long spent = 0;
            // price is recalculated per unit as stock drains
            while (bought < qty && shop.StockOf(itemId) > 0)
            {
                int price = shop.BuyPrice(def);
                if (p.Inventory.Count("coins") < price) { if (bought == 0) Message(p, "You don't have enough embers."); break; }
                if (!p.Inventory.CanAddAll(new[] { new ItemStack(itemId, 1) }, new[] { new ItemStack("coins", price) }))
                {
                    Message(p, "You don't have enough inventory space.");
                    break;
                }
                p.Inventory.Remove("coins", price);
                p.Inventory.Add(itemId, 1);
                shop.Stock[itemId] = shop.StockOf(itemId) - 1;
                bought++;
                spent += price;
            }
            if (bought == 0 && shop.StockOf(itemId) <= 0) Message(p, "The shop has run out of stock.");
            if (bought > 0) { shop.Version++; Emit(new WorldEvent { Type = WorldEventType.Sound, EntityId = p.Id, Text = "coins" }); }
        }

        public void RequestShopSell(Player p, int invSlot, int qty)
        {
            var shop = ShopFor(p);
            if (shop == null || qty <= 0) return;
            var s = p.Inventory[invSlot];
            if (s.IsEmpty) return;
            var def = Db.Item(s.ItemId);
            if (!shop.WillBuy(def)) { Message(p, "The shop won't buy that."); return; }
            qty = Math.Min(qty, p.Inventory.Count(def.Id));
            int sold = 0;
            for (; sold < qty; sold++)
            {
                int price = shop.SellPrice(def);
                if (!p.Inventory.CanAddAll(new[] { new ItemStack("coins", Math.Max(1, price)) }, new[] { new ItemStack(def.Id, 1) })) break;
                p.Inventory.Remove(def.Id, 1);
                if (price > 0) p.Inventory.Add("coins", price);
                if (!shop.Stock.ContainsKey(def.Id)) { shop.Stock[def.Id] = 0; shop.Order.Add(def.Id); }
                shop.Stock[def.Id]++;
            }
            if (sold > 0) { shop.Version++; Emit(new WorldEvent { Type = WorldEventType.Sound, EntityId = p.Id, Text = "coins" }); }
        }

        // ------------------------------------------------------------------
        // Player trading (escrow, two-step confirmation, atomic swap)
        // ------------------------------------------------------------------
        public void RequestTrade(Player p, Player other)
        {
            if (!ValidateActor(p) || other == null || other == p || other.IsDead) return;
            if (p.Trade != null || other.Trade != null) { Message(p, other.Name + " is busy."); return; }
            if (Vec2.TileDistance(p.Position, other.Position) > 12) { Message(p, "You're too far away."); return; }
            // second request from the other side opens the trade
            if (other.Settings.TryGetValue("_tradeRequest", out var reqId) && reqId == p.Id.ToString())
            {
                other.Settings.Remove("_tradeRequest");
                CloseInterfaces(p);
                CloseInterfaces(other);
                CancelActions(p);
                CancelActions(other);
                var t = new TradeSession { A = other, B = p, OfferA = new ItemContainer(Db, InventorySize), OfferB = new ItemContainer(Db, InventorySize) };
                p.Trade = other.Trade = t;
                Emit(new WorldEvent { Type = WorldEventType.TradeUpdate, PlayerId = p.Id });
                Emit(new WorldEvent { Type = WorldEventType.TradeUpdate, PlayerId = other.Id });
                return;
            }
            p.Settings["_tradeRequest"] = other.Id.ToString();
            Message(p, "Sending trade request...");
            Emit(new WorldEvent { Type = WorldEventType.TradeRequest, PlayerId = other.Id, EntityId = p.Id, Text = p.Name });
        }

        public void RequestTradeOffer(Player p, int invSlot, int qty)
        {
            var t = p.Trade;
            if (t == null || t.Stage != TradeStage.Offer || qty <= 0) return;
            var s = p.Inventory[invSlot];
            if (s.IsEmpty) return;
            var def = Db.Item(s.ItemId);
            if (def == null || !def.Tradeable) { Message(p, "You can't trade that item."); return; }
            qty = Math.Min(qty, p.Inventory.Count(s.ItemId));
            var offer = t.OfferOf(p);
            if (!offer.CanAdd(s.ItemId, qty)) return;
            if (!p.Inventory.Remove(s.ItemId, qty)) return;
            offer.Add(s.ItemId, qty);
            t.ResetAcceptance();
            TradeChanged(t);
        }

        public void RequestTradeRemove(Player p, int offerSlot, int qty)
        {
            var t = p.Trade;
            if (t == null || t.Stage != TradeStage.Offer || qty <= 0) return;
            var offer = t.OfferOf(p);
            var s = offer[offerSlot];
            if (s.IsEmpty) return;
            qty = Math.Min(qty, offer.Count(s.ItemId));
            if (!p.Inventory.CanAdd(s.ItemId, qty)) { Message(p, "You don't have enough inventory space."); return; }
            offer.Remove(s.ItemId, qty);
            p.Inventory.Add(s.ItemId, qty);
            t.ResetAcceptance();
            TradeChanged(t);
        }

        public void RequestTradeAccept(Player p)
        {
            var t = p.Trade;
            if (t == null) return;
            if (p == t.A) t.AcceptedA = true; else t.AcceptedB = true;
            t.Version++;
            if (t.AcceptedA && t.AcceptedB)
            {
                if (t.Stage == TradeStage.Offer)
                {
                    t.Stage = TradeStage.Confirm;
                    t.AcceptedA = t.AcceptedB = false;
                }
                else
                {
                    CompleteTrade(t);
                    return;
                }
            }
            TradeChanged(t);
        }

        private void CompleteTrade(TradeSession t)
        {
            // Each party must be able to hold what the other offered (their own offer is already out of their inventory).
            if (!t.A.Inventory.CanAddAll(t.OfferB.NonEmpty()))
            {
                Message(t.A, "You don't have enough inventory space for this trade.");
                Message(t.B, t.A.Name + " doesn't have enough inventory space.");
                t.ResetAcceptance(); TradeChanged(t); return;
            }
            if (!t.B.Inventory.CanAddAll(t.OfferA.NonEmpty()))
            {
                Message(t.B, "You don't have enough inventory space for this trade.");
                Message(t.A, t.B.Name + " doesn't have enough inventory space.");
                t.ResetAcceptance(); TradeChanged(t); return;
            }
            foreach (var s in t.OfferB.NonEmpty()) t.A.Inventory.Add(s.ItemId, s.Quantity);
            foreach (var s in t.OfferA.NonEmpty()) t.B.Inventory.Add(s.ItemId, s.Quantity);
            t.OfferA.Clear();
            t.OfferB.Clear();
            t.A.Trade = t.B.Trade = null;
            Message(t.A, "Trade accepted.");
            Message(t.B, "Trade accepted.");
            Emit(new WorldEvent { Type = WorldEventType.TradeClose, PlayerId = t.A.Id, Text = "complete" });
            Emit(new WorldEvent { Type = WorldEventType.TradeClose, PlayerId = t.B.Id, Text = "complete" });
            TradeCompleted?.Invoke(t);
        }

        /// <summary>Raised after a successful trade so the server can save both characters immediately.</summary>
        public event Action<TradeSession> TradeCompleted;

        public void CancelTrade(Player p, string reason)
        {
            var t = p?.Trade;
            if (t == null) return;
            ReturnEscrow(t.A, t.OfferA);
            ReturnEscrow(t.B, t.OfferB);
            t.A.Trade = t.B.Trade = null;
            Emit(new WorldEvent { Type = WorldEventType.TradeClose, PlayerId = t.A.Id, Text = reason });
            Emit(new WorldEvent { Type = WorldEventType.TradeClose, PlayerId = t.B.Id, Text = reason });
        }

        private void ReturnEscrow(Player p, ItemContainer offer)
        {
            foreach (var s in new List<ItemStack>(offer.NonEmpty()))
            {
                offer.Remove(s.ItemId, s.Quantity, false);
                GiveOrDrop(p, s.ItemId, s.Quantity);
            }
        }

        private void TradeChanged(TradeSession t)
        {
            Emit(new WorldEvent { Type = WorldEventType.TradeUpdate, PlayerId = t.A.Id });
            Emit(new WorldEvent { Type = WorldEventType.TradeUpdate, PlayerId = t.B.Id });
        }

        // ------------------------------------------------------------------
        // Chat
        // ------------------------------------------------------------------
        public const int MaxChatLength = 120;
        public const float LocalChatRadius = 32f;

        public static string SanitizeChat(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var sb = new StringBuilder(Math.Min(text.Length, MaxChatLength));
            foreach (var c in text)
            {
                if (sb.Length >= MaxChatLength) break;
                if (char.IsControl(c) || c == '<' || c == '>') continue;   // no rich-text injection
                sb.Append(c);
            }
            var s = sb.ToString().Trim();
            return s.Length == 0 ? null : s;
        }

        public void RequestChat(Player p, ChatChannel channel, string text, string targetName = null)
        {
            if (p == null) return;
            text = SanitizeChat(text);
            if (text == null) return;
            // simple rate limit: 6 messages per 10 seconds
            if (Tick - p.ChatWindowStart > 20) { p.ChatWindowStart = Tick; p.ChatCountInWindow = 0; }
            if (++p.ChatCountInWindow > 6) { Message(p, "You're sending messages too quickly."); return; }

            if (text.StartsWith("/") && HandleCommand(p, text)) return;

            switch (channel)
            {
                case ChatChannel.Local:
                    Emit(new WorldEvent { Type = WorldEventType.Chat, EntityId = p.Id, Text = text, Text2 = p.Name, Channel = ChatChannel.Local });
                    break;
                case ChatChannel.Party:
                    if (p.Party == null) { Message(p, "You're not in a party."); return; }
                    foreach (var m in p.Party.Members)
                        Emit(new WorldEvent { Type = WorldEventType.Chat, PlayerId = m.Id, EntityId = p.Id, Text = text, Text2 = p.Name, Channel = ChatChannel.Party });
                    break;
                case ChatChannel.Private:
                    var to = FindPlayerByName(targetName ?? "");
                    if (to == null) { Message(p, "That player is not online."); return; }
                    Emit(new WorldEvent { Type = WorldEventType.Chat, PlayerId = to.Id, EntityId = p.Id, Text = text, Text2 = p.Name, Channel = ChatChannel.Private });
                    Emit(new WorldEvent { Type = WorldEventType.Chat, PlayerId = p.Id, EntityId = p.Id, Text = text, Text2 = "To " + to.Name, Channel = ChatChannel.Private });
                    break;
                case ChatChannel.Trade:
                    foreach (var pl in _players)
                        Emit(new WorldEvent { Type = WorldEventType.Chat, PlayerId = pl.Id, EntityId = p.Id, Text = text, Text2 = p.Name, Channel = ChatChannel.Trade });
                    break;
            }
        }

        private bool HandleCommand(Player p, string text)
        {
            var parts = text.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            switch (parts[0].ToLowerInvariant())
            {
                case "players":
                    Message(p, "Players online: " + _players.Count);
                    return true;
                case "pos":
                    Message(p, "Position: " + p.Position + " in " + (Db.World?.RegionAt(p.Position.X, p.Position.Z)?.Name ?? "?"));
                    return true;
                case "party":
                    if (parts.Length >= 3 && parts[1] == "invite") { RequestPartyInvite(p, FindPlayerByName(parts[2])); return true; }
                    if (parts.Length >= 2 && parts[1] == "leave") { LeaveParty(p); return true; }
                    if (parts.Length >= 2 && parts[1] == "accept") { RequestPartyAccept(p); return true; }
                    Message(p, "Usage: /party invite <name> | accept | leave");
                    return true;
                case "w":
                case "whisper":
                    if (parts.Length >= 3) { RequestChat(p, ChatChannel.Private, text.Substring(text.IndexOf(parts[2], StringComparison.Ordinal)), parts[1]); return true; }
                    return true;
                case "t":
                case "trade":
                    if (parts.Length >= 2) RequestChat(p, ChatChannel.Trade, text.Substring(text.IndexOf(parts[1], StringComparison.Ordinal)));
                    return true;
                case "p":
                    if (parts.Length >= 2) RequestChat(p, ChatChannel.Party, text.Substring(text.IndexOf(parts[1], StringComparison.Ordinal)));
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Parties
        // ------------------------------------------------------------------
        public void RequestPartyInvite(Player p, Player other)
        {
            if (other == null || other == p) { Message(p, "That player is not online."); return; }
            if (other.Party != null) { Message(p, other.Name + " is already in a party."); return; }
            if (p.Party == null)
            {
                p.Party = new Party { Id = _nextPartyId++, Leader = p };
                p.Party.Members.Add(p);
                PartyChanged(p.Party);
            }
            if (p.Party.Leader != p) { Message(p, "Only the party leader can invite."); return; }
            if (p.Party.Members.Count >= Party.MaxSize) { Message(p, "Your party is full."); return; }
            p.Party.Invites.Add(other.CharacterId == 0 ? other.Id : other.CharacterId);
            other.Settings["_partyInvite"] = p.Party.Id.ToString();
            Message(p, "You invite " + other.Name + " to your party.");
            Message(other, p.Name + " has invited you to a party. Type /party accept to join.");
        }

        public void RequestPartyAccept(Player p)
        {
            if (p.Party != null || !p.Settings.TryGetValue("_partyInvite", out var pid)) return;
            p.Settings.Remove("_partyInvite");
            Party party = null;
            foreach (var pl in _players) if (pl.Party != null && pl.Party.Id.ToString() == pid) party = pl.Party;
            long key = p.CharacterId == 0 ? p.Id : p.CharacterId;
            if (party == null || !party.Invites.Remove(key)) { Message(p, "That invitation has expired."); return; }
            if (party.Members.Count >= Party.MaxSize) { Message(p, "That party is full."); return; }
            party.Members.Add(p);
            p.Party = party;
            foreach (var m in party.Members) Message(m, p.Name + " has joined the party.");
            PartyChanged(party);
        }

        public void LeaveParty(Player p)
        {
            var party = p?.Party;
            if (party == null) return;
            party.Members.Remove(p);
            p.Party = null;
            Emit(new WorldEvent { Type = WorldEventType.PartyUpdate, PlayerId = p.Id });
            foreach (var m in party.Members) Message(m, p.Name + " has left the party.");
            if (party.Members.Count <= 1)
            {
                foreach (var m in party.Members) { m.Party = null; Emit(new WorldEvent { Type = WorldEventType.PartyUpdate, PlayerId = m.Id }); }
                return;
            }
            if (party.Leader == p) party.Leader = party.Members[0];
            PartyChanged(party);
        }

        private void PartyChanged(Party party)
        {
            foreach (var m in party.Members) Emit(new WorldEvent { Type = WorldEventType.PartyUpdate, PlayerId = m.Id });
        }
    }
}
