using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Embervale.Shared;
using Embervale.Shared.Data;
using Embervale.Shared.Net;
using Embervale.Shared.Simulation;
using LiteNetLib;
using Newtonsoft.Json;

namespace Embervale.Server
{
    public sealed class ServerConfig
    {
        public string ServerName = "Embervale";
        public string Motd = "Welcome to Embervale!";
        public int Port = Protocol.DefaultPort;
        public int MaxPlayers = 200;
        public string DatabasePath = "Data/embervale.db";
        public string GameDataPath = "../../Assets/StreamingAssets/GameData";
        public int AutosaveSeconds = 60;
        public ulong WorldSeed = 1337;
    }

    /// <summary>LiteNetLib adapter for ServerCore.</summary>
    public sealed class LiteNetServerTransport : IServerTransport, INetEventListener
    {
        private readonly NetManager _net;
        private readonly ServerCore _core;
        private readonly int _maxPlayers;

        public LiteNetServerTransport(ServerCore core, int maxPlayers)
        {
            _core = core;
            _maxPlayers = maxPlayers;
            _net = new NetManager(this) { AutoRecycle = true, UpdateTime = 15, DisconnectTimeout = 15000, UnconnectedMessagesEnabled = false };
        }

        public bool Start(int port) => _net.Start(port);
        public void Poll() => _net.PollEvents();
        public void Stop() => _net.Stop();
        public int Connected => _net.ConnectedPeersCount;

        public void Send(int connectionId, byte[] data, int length, bool reliable)
        {
            var peer = _net.GetPeerById(connectionId);
            peer?.Send(data, 0, length, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);
        }

        public void Disconnect(int connectionId, string reason)
        {
            var peer = _net.GetPeerById(connectionId);
            if (peer != null) _net.DisconnectPeer(peer);
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (_net.ConnectedPeersCount >= _maxPlayers + 20) { request.Reject(); return; }
            request.AcceptIfKey(Protocol.ConnectionKey);
        }

        public void OnPeerConnected(NetPeer peer) => _core.OnConnected(peer.Id);
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) => _core.OnDisconnected(peer.Id);
        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) => Console.WriteLine("[net] error " + socketError);
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            _core.OnData(peer.Id, reader.RawData, reader.UserDataOffset, reader.UserDataSize);
        }
    }

    public static class Program
    {
        private static volatile bool _running = true;

        public static int Main(string[] args)
        {
            var cfg = LoadConfig(args);
            var dataDir = FindGameData(cfg.GameDataPath);
            if (dataDir == null)
            {
                Console.Error.WriteLine("Could not find game data (looked for " + cfg.GameDataPath + "). Use --data <path>.");
                return 1;
            }
            Console.WriteLine("Embervale dedicated server");
            Console.WriteLine("  game data : " + dataDir);
            Console.WriteLine("  database  : " + Path.GetFullPath(cfg.DatabasePath));

            var sw = Stopwatch.StartNew();
            var db = GameDatabase.LoadFromDirectory(dataDir);
            var problems = db.Validate();
            foreach (var p in problems) Console.Error.WriteLine("  data error: " + p);
            if (problems.Count > 0) return 2;
            var world = new GameWorld(db, cfg.WorldSeed);
            using var store = new SqliteGameStore(cfg.DatabasePath);
            var core = new ServerCore(world, store)
            {
                ServerName = cfg.ServerName,
                Motd = cfg.Motd,
                MaxPlayers = cfg.MaxPlayers,
                AutosaveIntervalTicks = Math.Max(10, (int)(cfg.AutosaveSeconds / GameTime.TickSeconds)),
                Log = msg => Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg),
            };
            var transport = new LiteNetServerTransport(core, cfg.MaxPlayers);
            core.SetTransport(transport);
            Console.WriteLine("  world     : " + world.Monsters.Count + " monsters, " + world.Nodes.Count + " resource nodes, loaded in " + sw.ElapsedMilliseconds + " ms");
            if (!transport.Start(cfg.Port))
            {
                Console.Error.WriteLine("Failed to bind UDP port " + cfg.Port);
                return 3;
            }
            Console.WriteLine("Listening on UDP " + cfg.Port + ". Press Ctrl+C to stop.");

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; _running = false; };
            AppDomain.CurrentDomain.ProcessExit += (_, __) => _running = false;

            long tickMs = (long)(GameTime.TickSeconds * 1000);
            var clock = Stopwatch.StartNew();
            long nextTick = tickMs;
            long ticks = 0, slowTicks = 0;
            int maxTicks = ArgInt(args, "--ticks", -1);   // for automated smoke tests
            while (_running)
            {
                transport.Poll();
                long now = clock.ElapsedMilliseconds;
                if (now >= nextTick)
                {
                    var t0 = clock.ElapsedMilliseconds;
                    core.Tick();
                    var dt = clock.ElapsedMilliseconds - t0;
                    if (dt > tickMs / 2) slowTicks++;
                    nextTick += tickMs;
                    if (now - nextTick > tickMs * 10) nextTick = now + tickMs; // don't spiral after a stall
                    ticks++;
                    if (ticks % 600 == 0) Console.WriteLine("[status] tick " + world.Tick + ", " + world.Players.Count + " players, " + transport.Connected + " connections, slow ticks " + slowTicks);
                    if (maxTicks > 0 && ticks >= maxTicks) break;
                }
                Thread.Sleep(5);
            }
            Console.WriteLine("Shutting down, saving world...");
            core.Shutdown("The server is shutting down.");
            transport.Poll();
            transport.Stop();
            Console.WriteLine("Goodbye.");
            return 0;
        }

        private static int ArgInt(string[] args, string name, int def)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name && int.TryParse(args[i + 1], out var v)) return v;
            return def;
        }

        private static string ArgStr(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private static ServerConfig LoadConfig(string[] args)
        {
            var path = ArgStr(args, "--config") ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var cfg = File.Exists(path) ? JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(path)) : new ServerConfig();
            var local = Path.Combine(Path.GetDirectoryName(path) ?? ".", "appsettings.Local.json");
            if (File.Exists(local)) JsonConvert.PopulateObject(File.ReadAllText(local), cfg);
            cfg.Port = ArgInt(args, "--port", cfg.Port);
            cfg.DatabasePath = ArgStr(args, "--db") ?? cfg.DatabasePath;
            cfg.GameDataPath = ArgStr(args, "--data") ?? cfg.GameDataPath;
            return cfg;
        }

        /// <summary>Resolves the GameData folder relative to cwd, the executable, or any parent of either.</summary>
        private static string FindGameData(string configured)
        {
            if (Path.IsPathRooted(configured)) return Directory.Exists(configured) ? configured : null;
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    var c1 = Path.GetFullPath(Path.Combine(dir.FullName, configured));
                    if (Directory.Exists(c1) && File.Exists(Path.Combine(c1, "items.json"))) return c1;
                    var c2 = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "GameData");
                    if (Directory.Exists(c2) && File.Exists(Path.Combine(c2, "items.json"))) return c2;
                    dir = dir.Parent;
                }
            }
            return null;
        }
    }
}
