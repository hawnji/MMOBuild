using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Embervale.Shared;
using Embervale.Shared.Data;
using Embervale.Shared.Net;
using Embervale.Shared.Persistence;
using Embervale.Shared.Simulation;
using LiteNetLib;
using Xunit;

namespace Embervale.Tests
{
    /// <summary>Spins up a full ServerCore with real login flow and N GameClients over loopback.</summary>
    public sealed class Cluster
    {
        public readonly FileGameStore Store;
        public ServerCore Server;
        public LoopbackHub Hub;
        public readonly List<LoopbackClient> Clients = new List<LoopbackClient>();

        public Cluster(FileGameStore store = null)
        {
            Store = store ?? new FileGameStore();
            Boot();
        }

        public void Boot()
        {
            Server = new ServerCore(TestUtil.NewWorld(), Store);
            Hub = new LoopbackHub(Server);
            Clients.Clear();
        }

        public GameClient Join(string name, bool register = true)
        {
            var c = new GameClient(TestUtil.Db);
            var lc = Hub.Connect(c);
            Clients.Add(lc);
            c.Connect();
            if (register) c.Register(name.ToLowerInvariant().Replace(" ", "_"), "password1");
            else c.Login(name.ToLowerInvariant().Replace(" ", "_"), "password1");
            Pump();
            Assert.True(c.Authenticated, "auth failed for " + name);
            if (c.Characters.Count == 0)
            {
                c.CreateCharacter(name, new Appearance());
                Pump();
            }
            c.SelectCharacter(c.Characters[0].Id);
            Pump();
            Assert.True(c.InWorld);
            return c;
        }

        public void Pump() { Hub.Pump(); Hub.Pump(); }

        public void Tick(int n = 1)
        {
            for (int i = 0; i < n; i++) { Hub.Pump(); Server.Tick(); Hub.Pump(); }
        }

        public bool TickUntil(Func<bool> cond, int max = 400)
        {
            for (int i = 0; i < max; i++) { if (cond()) return true; Tick(); }
            return cond();
        }

        public Player PlayerOf(GameClient c) => Server.World.Get<Player>(c.SelfId);
    }

    public class MultiplayerTests
    {
        [Fact]
        public void LoginRegisterAndCharacterCreation()
        {
            var cl = new Cluster();
            var c = cl.Join("Rowan");
            Assert.Equal("Rowan", c.SelfName);
            Assert.NotNull(c.Self);
            Assert.True(c.Inventory.Any(s => s.ItemId == "bronze_pickaxe"));
            Assert.Equal(20, c.MaxHp);
        }

        [Fact]
        public void BadPasswordRejectedAndNamesValidated()
        {
            var cl = new Cluster();
            cl.Join("Valid");
            var c = new GameClient(TestUtil.Db);
            cl.Hub.Connect(c);
            c.Connect();
            string msg = null;
            c.OnAuthResult += (ok, m) => msg = m;
            c.Login("valid", "wrongpass");
            cl.Pump();
            Assert.False(c.Authenticated);
            Assert.Contains("Invalid", msg);
            c.Register("x", "password1");
            cl.Pump();
            Assert.False(c.Authenticated);
            c.Register("newuser", "password1");
            cl.Pump();
            Assert.True(c.Authenticated);
            bool? created = null;
            c.OnCharacterResult += (ok, _) => created = ok;
            c.CreateCharacter("<script>", new Appearance());
            cl.Pump();
            Assert.False(created);
        }

        [Fact]
        public void PlayersSeeEachOtherAndMovementSynchronises()
        {
            var cl = new Cluster();
            var a = cl.Join("Ash");
            var b = cl.Join("Birch");
            cl.Tick(2);
            Assert.True(a.Entities.ContainsKey(b.SelfId));
            Assert.True(b.Entities.ContainsKey(a.SelfId));
            var start = a.Self;
            a.MoveTo((int)start.X + 4, (int)start.Z, false);
            cl.Tick(8);
            var seenByB = b.Entities[a.SelfId];
            var server = cl.PlayerOf(a).Position;
            Assert.Equal(server.X, seenByB.X, 2);
            Assert.Equal(server.Z, seenByB.Z, 2);
            Assert.NotEqual(start.X, seenByB.X);
        }

        [Fact]
        public void InterestManagementHidesDistantPlayers()
        {
            var cl = new Cluster();
            var a = cl.Join("Near");
            var b = cl.Join("Far");
            cl.Tick();
            Assert.True(a.Entities.ContainsKey(b.SelfId));
            TestUtil.Teleport(cl.Server.World, cl.PlayerOf(b), cl.Server.World.Location("highmarch_plaza"));
            cl.Tick(2);
            Assert.False(a.Entities.ContainsKey(b.SelfId));
            Assert.True(a.Entities.Count < cl.Server.World.AllEntities.Count());
        }

        [Fact]
        public void ClientCannotSpoofItemsOrMoveFarAway()
        {
            var cl = new Cluster();
            var a = cl.Join("Cheat");
            var p = cl.PlayerOf(a);
            int coins = p.Inventory.Count("coins");
            // malformed / hostile packets
            a.Send(new byte[] { (byte)C2S.ShopBuy, 0xFF });
            a.Send(ClientPackets.ShopBuy("starmetal_sword", 1));          // no shop open
            a.Send(ClientPackets.BankWithdraw(0, 1000));                    // bank not open
            a.Send(ClientPackets.TradeOffer(0, 1000));                      // no trade
            a.Send(ClientPackets.Craft("forge_starmetal_sword", 1, 0));     // no materials/level
            a.Send(ClientPackets.MoveTo(5000, 5000, true));                 // off the map
            a.Send(ClientPackets.Interact(int.MaxValue, "Attack"));
            cl.Tick(5);
            Assert.Equal(coins, p.Inventory.Count("coins"));
            Assert.Equal(0, p.Inventory.Count("starmetal_sword"));
            Assert.True(Vec2.Distance(p.Position, cl.Server.World.Location("brookhollow_square")) < 3);
        }

        [Fact]
        public void PacketFloodIsThrottled()
        {
            var cl = new Cluster();
            var a = cl.Join("Flood");
            for (int i = 0; i < 500; i++) a.Send(ClientPackets.Chat(ChatChannel.Local, "x" + i));
            cl.Tick();
            Assert.True(cl.Server.World.Players.Count == 1);   // server survived
        }

        [Fact]
        public void LocalChatReachesNearbyPlayers()
        {
            var cl = new Cluster();
            var a = cl.Join("Talker");
            var b = cl.Join("Listener");
            cl.Tick();
            string heard = null;
            b.OnChat += (ch, id, name, text) => heard = name + ": " + text;
            a.Send(ClientPackets.Chat(ChatChannel.Local, "hello there"));
            cl.Tick();
            Assert.Equal("Talker: hello there", heard);
        }

        [Fact]
        public void CombatIsSynchronisedToObservers()
        {
            var cl = new Cluster();
            var a = cl.Join("Fighter");
            var b = cl.Join("Watcher");
            var w = cl.Server.World;
            var pa = cl.PlayerOf(a);
            pa.Skills.AddXp(Skill.Attack, w.Db.XpTable.XpForLevel(30));
            pa.Skills.AddXp(Skill.Strength, w.Db.XpTable.XpForLevel(30));
            var rat = TestUtil.Nearest<Monster>(w, pa.Position, m => m.DefId == "giant_rat");
            TestUtil.Teleport(w, pa, TestUtil.StandNextTo(w, rat));
            TestUtil.Teleport(w, cl.PlayerOf(b), TestUtil.StandNextTo(w, rat));
            cl.Tick(2);
            int splatsSeen = 0;
            b.OnHitSplat += (victim, attacker, dmg, crit, kind) => { if (victim == rat.Id) splatsSeen++; };
            a.Interact(rat.Id, "Attack");
            Assert.True(cl.TickUntil(() => rat.State == MonsterState.Dead, 300));
            Assert.True(splatsSeen > 0);
            cl.Tick();
            Assert.True(b.Entities.TryGetValue(rat.Id, out var snap) && snap.Has(EntitySnapshot.FlagDead) || !b.Entities.ContainsKey(rat.Id));
        }

        [Fact]
        public void ResourceDepletionVisibleToAllClients()
        {
            var cl = new Cluster();
            var a = cl.Join("Miner");
            var b = cl.Join("Onlooker");
            var w = cl.Server.World;
            var rock = TestUtil.Nearest<ResourceNode>(w, cl.PlayerOf(a).Position, n => n.DefId == "copper_rock");
            TestUtil.Teleport(w, cl.PlayerOf(a), TestUtil.StandNextTo(w, rock));
            TestUtil.Teleport(w, cl.PlayerOf(b), TestUtil.StandNextTo(w, rock));
            cl.Tick(2);
            a.Interact(rock.Id, "Mine");
            Assert.True(cl.TickUntil(() => rock.Depleted, 300));
            cl.Tick();
            Assert.True(b.Entities[rock.Id].Has(EntitySnapshot.FlagDepleted));
            Assert.True(a.InventoryCount("copper_ore") == 1);
        }

        [Fact]
        public void TradeBetweenTwoClientsOverNetwork()
        {
            var cl = new Cluster();
            var a = cl.Join("Seller");
            var b = cl.Join("Buyer");
            cl.Tick();
            a.Interact(b.SelfId, "Trade");
            cl.Tick();
            string requester = null;
            b.OnTradeRequest += (_, n) => requester = n;
            b.Interact(a.SelfId, "Trade");
            cl.Tick();
            Assert.NotNull(a.Trade);
            Assert.NotNull(b.Trade);
            int slot = Array.FindIndex(a.Inventory, s => s.ItemId == "bronze_hatchet");
            a.Send(ClientPackets.TradeOffer(slot, 1));
            cl.Tick();
            Assert.Contains(b.Trade.TheirOffer, s => s.ItemId == "bronze_hatchet");
            a.Send(ClientPackets.TradeAccept()); b.Send(ClientPackets.TradeAccept()); cl.Tick();
            a.Send(ClientPackets.TradeAccept()); b.Send(ClientPackets.TradeAccept()); cl.Tick();
            Assert.Null(a.Trade);
            Assert.Equal(2, b.InventoryCount("bronze_hatchet"));
            Assert.Equal(0, a.InventoryCount("bronze_hatchet"));
            // persisted immediately in one transaction
            var saved = cl.Store.LoadCharacter(cl.PlayerOf(b).CharacterId);
            Assert.Equal(2, saved.Inventory.Count(s => s.Item == "bronze_hatchet"));
        }

        [Fact]
        public void DisconnectMidTradeReturnsItemsAndSaves()
        {
            var cl = new Cluster();
            var a = cl.Join("Quitter");
            var b = cl.Join("Stayer");
            cl.Tick();
            a.Interact(b.SelfId, "Trade"); cl.Tick();
            b.Interact(a.SelfId, "Trade"); cl.Tick();
            int coins = a.InventoryCount("coins");
            a.Send(ClientPackets.TradeOffer(Array.FindIndex(a.Inventory, s => s.ItemId == "coins"), coins));
            cl.Tick();
            long charId = cl.PlayerOf(a).CharacterId;
            cl.Clients[0].Disconnect();
            cl.Tick();
            Assert.Null(b.Trade);
            var saved = cl.Store.LoadCharacter(charId);
            Assert.Equal(coins, saved.Inventory.Where(s => s.Item == "coins").Sum(s => s.Qty));
        }

        [Fact]
        public void ReconnectRestoresCharacterState()
        {
            var cl = new Cluster();
            var a = cl.Join("Returner");
            var p = cl.PlayerOf(a);
            p.Skills.AddXp(Skill.Woodcutting, 500);
            p.Inventory.Add("oak_logs", 4);
            a.MoveTo((int)p.Position.X + 3, (int)p.Position.Z + 3, false);
            cl.Tick(6);
            var pos = p.Position;
            cl.Clients[0].Disconnect();
            cl.Tick();
            Assert.Empty(cl.Server.World.Players);
            var again = cl.Join("Returner", register: false);
            var p2 = cl.PlayerOf(again);
            Assert.Equal(4, p2.Inventory.Count("oak_logs"));
            Assert.Equal(500, p2.Skills.Xp(Skill.Woodcutting));
            Assert.Equal(pos, p2.Position);
            Assert.Equal(4, again.InventoryCount("oak_logs"));
        }

        [Fact]
        public void DoubleLoginKicksOldSession()
        {
            var cl = new Cluster();
            var first = cl.Join("Twin");
            string kicked = null;
            first.OnKicked += r => kicked = r;
            cl.Join("Twin", register: false);
            Assert.NotNull(kicked);
            Assert.Single(cl.Server.World.Players);
        }

        [Fact]
        public void ServerRestartRecoversCharactersAndWorld()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "emb_restart_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var cl = new Cluster(new FileGameStore(path));
                var a = cl.Join("Phoenix");
                var p = cl.PlayerOf(a);
                p.Inventory.Add("gold_bar", 3);
                var node = cl.Server.World.Nodes.First(n => n.DefId == "gold_rock");
                node.DepletedUntilTick = cl.Server.World.Tick + 500;
                cl.Server.Shutdown("restart");

                var cl2 = new Cluster(new FileGameStore(path));   // fresh process, same storage
                Assert.True(cl2.Server.World.Get<ResourceNode>(node.Id).Depleted);
                var back = cl2.Join("Phoenix", register: false);
                Assert.Equal(3, cl2.PlayerOf(back).Inventory.Count("gold_bar"));
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }

        [Fact]
        public void PartyFormationAndPartyChat()
        {
            var cl = new Cluster();
            var a = cl.Join("Leader");
            var b = cl.Join("Member");
            cl.Tick();
            a.Send(ClientPackets.PartyInvite("Member"));
            cl.Tick();
            b.Send(ClientPackets.PartyAccept());
            cl.Tick();
            Assert.Equal(2, a.Party.Count);
            Assert.Equal(2, b.Party.Count);
            string got = null;
            b.OnChat += (ch, id, n, t) => { if (ch == ChatChannel.Party) got = t; };
            a.Send(ClientPackets.Chat(ChatChannel.Party, "follow me"));
            cl.Tick();
            Assert.Equal("follow me", got);
        }

        [Fact]
        public void ManySimultaneousPlayersTickWithinBudget()
        {
            var cl = new Cluster();
            var clients = new List<GameClient>();
            for (int i = 0; i < 40; i++) clients.Add(cl.Join("Bot" + i));
            var rng = new XorShiftRandom(1);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int t = 0; t < 60; t++)
            {
                foreach (var c in clients)
                    if (rng.Chance(0.2)) { var s = c.Self; if (s != null) c.MoveTo((int)s.X + rng.Next(-6, 6), (int)s.Z + rng.Next(-6, 6), rng.Chance(0.5)); }
                cl.Tick();
            }
            double msPerTick = sw.Elapsed.TotalMilliseconds / 60;
            Assert.Equal(40, cl.Server.World.Players.Count);
            Assert.True(msPerTick < 250, "tick took " + msPerTick + " ms");
            // every client's view of every other visible player matches the server
            foreach (var c in clients)
                foreach (var e in c.Entities.Values.Where(e => e.Kind == EntityKind.Player))
                {
                    var server = cl.Server.World.Get(e.Id);
                    Assert.Equal(server.Position.X, e.X, 2);
                }
        }
    }

    /// <summary>End-to-end over real UDP sockets using LiteNetLib on both ends.</summary>
    public class UdpNetworkTests
    {
        private sealed class UdpClientTransport : IClientTransport, INetEventListener
        {
            public NetManager Net;
            public NetPeer Peer;
            public GameClient Client;
            public void Send(byte[] data, bool reliable) => Peer?.Send(data, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);
            public void Disconnect() => Peer?.Disconnect();
            public void OnPeerConnected(NetPeer peer) { Peer = peer; }
            public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) { }
            public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) { }
            public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method) => Client.Receive(reader.RawData, reader.UserDataOffset, reader.UserDataSize);
            public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
            public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
            public void OnConnectionRequest(ConnectionRequest request) => request.Reject();
        }

        [Fact]
        public void ClientLogsInAndMovesOverUdp()
        {
            var core = new ServerCore(TestUtil.NewWorld(), new FileGameStore());
            var server = new Embervale.Server.LiteNetServerTransport(core, 10);
            core.SetTransport(server);
            int port = 20000 + new Random().Next(0, 20000);
            Assert.True(server.Start(port));
            var t = new UdpClientTransport();
            t.Client = new GameClient(TestUtil.Db, t);
            t.Net = new NetManager(t);
            t.Net.Start();
            t.Net.Connect("127.0.0.1", port, Protocol.ConnectionKey);
            try
            {
                bool Wait(Func<bool> cond, int ms = 5000)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < ms)
                    {
                        server.Poll(); t.Net.PollEvents();
                        if (cond()) return true;
                        Thread.Sleep(5);
                    }
                    return cond();
                }
                Assert.True(Wait(() => t.Peer != null && t.Peer.ConnectionState == ConnectionState.Connected));
                t.Client.Connect();
                Assert.True(Wait(() => t.Client.HandshakeDone));
                t.Client.Register("udpuser", "password1");
                Assert.True(Wait(() => t.Client.Authenticated));
                t.Client.CreateCharacter("Udp Hero", new Appearance { Gender = 1 });
                Assert.True(Wait(() => t.Client.Characters.Count == 1));
                t.Client.SelectCharacter(t.Client.Characters[0].Id);
                Assert.True(Wait(() => t.Client.InWorld && t.Client.Self != null));
                var start = t.Client.Self.X;
                t.Client.MoveTo((int)start + 3, (int)t.Client.Self.Z, false);
                for (int i = 0; i < 6; i++) { Wait(() => false, 20); core.Tick(); }
                Assert.True(Wait(() => Math.Abs(t.Client.Self.X - start) >= 2.9f));
            }
            finally
            {
                t.Net.Stop();
                server.Stop();
            }
        }
    }
}
