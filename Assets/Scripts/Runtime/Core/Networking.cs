using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Embervale.Shared;
using Embervale.Shared.Data;
using Embervale.Shared.Net;
using Embervale.Shared.Persistence;
using Embervale.Shared.Simulation;
using LiteNetLib;
using UnityEngine;

namespace Embervale.Client
{
    /// <summary>Loads the shared JSON game data from StreamingAssets (same files the server uses).</summary>
    public static class GameDataLoader
    {
        public static string DataPath => Path.Combine(Application.streamingAssetsPath, "GameData");

        public static GameDatabase Load()
        {
            var db = GameDatabase.LoadFromDirectory(DataPath);
            var problems = db.Validate();
            foreach (var p in problems) Debug.LogError("[GameData] " + p);
            return db;
        }
    }

    /// <summary>UDP transport to a dedicated server (LiteNetLib, same version as the server).</summary>
    public sealed class LiteNetClientTransport : IClientTransport, INetEventListener
    {
        private readonly NetManager _net;
        private readonly GameClient _client;
        private NetPeer _peer;

        public bool Connected => _peer != null && _peer.ConnectionState == ConnectionState.Connected;
        public string LastError { get; private set; }
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public int Ping => _peer?.Ping ?? 0;

        public LiteNetClientTransport(GameClient client)
        {
            _client = client;
            _net = new NetManager(this) { AutoRecycle = true, UpdateTime = 15, DisconnectTimeout = 15000 };
        }

        public void Connect(string host, int port)
        {
            if (!_net.IsRunning) _net.Start();
            _peer = _net.Connect(host, port, Protocol.ConnectionKey);
        }

        public void Poll() => _net.PollEvents();

        public void Send(byte[] data, bool reliable)
        {
            if (_peer == null) return;
            _peer.Send(data, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);
        }

        public void Disconnect()
        {
            if (_peer != null) _net.DisconnectPeer(_peer);
            _net.Stop();
            _peer = null;
        }

        public void OnPeerConnected(NetPeer peer) { _peer = peer; OnConnected?.Invoke(); }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            LastError = info.Reason.ToString();
            _peer = null;
            OnDisconnected?.Invoke(LastError);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { LastError = socketError.ToString(); }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            _client.Receive(reader.RawData, reader.UserDataOffset, reader.UserDataSize);
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { _client.LastPingMs = latency; }
        public void OnConnectionRequest(ConnectionRequest request) { request.Reject(); }
    }

    /// <summary>
    /// Runs the authoritative server in-process (offline play / "host a local world"). It is exactly the
    /// same ServerCore the dedicated server runs; the client talks to it through a loopback transport, so
    /// offline play exercises the real protocol. Saves go to Application.persistentDataPath.
    /// </summary>
    public sealed class LocalServerHost : IDisposable
    {
        public readonly ServerCore Server;
        public readonly LoopbackHub Hub;
        private readonly FileGameStore _store;
        private float _accum;

        public LocalServerHost(GameDatabase db)
        {
            var path = Path.Combine(Application.persistentDataPath, "LocalWorld", "embervale_local.json");
            _store = new FileGameStore(path);
            var world = new GameWorld(db, 1337);
            Server = new ServerCore(world, _store) { ServerName = "Local World", Motd = "Playing offline - progress is saved on this PC.", Log = m => Debug.Log("[LocalServer] " + m) };
            Hub = new LoopbackHub(Server);
        }

        public LoopbackClient Connect(GameClient client) => Hub.Connect(client);

        /// <summary>Call every frame: delivers queued packets and runs fixed 0.5 s ticks.</summary>
        public void Update(float dt)
        {
            Hub.Pump();
            _accum += dt;
            int guard = 0;
            while (_accum >= GameTime.TickSeconds && guard++ < 4)
            {
                _accum -= GameTime.TickSeconds;
                Server.Tick();
                Hub.Pump();
            }
            if (_accum > GameTime.TickSeconds * 4) _accum = 0;
        }

        public void Dispose()
        {
            try { Server.Shutdown("Local world closed."); Hub.Pump(); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}
