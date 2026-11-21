using System;
using System.Collections.Generic;

namespace Embervale.Shared.Net
{
    /// <summary>
    /// In-process transport connecting GameClients to a ServerCore without sockets. Used for the
    /// Unity "play offline / host locally" mode and for automated multiplayer tests. Messages are
    /// queued and delivered on Pump() so ordering mirrors a real reliable-ordered channel.
    /// </summary>
    public sealed class LoopbackHub : IServerTransport
    {
        private readonly ServerCore _server;
        private readonly Dictionary<int, LoopbackClient> _clients = new Dictionary<int, LoopbackClient>();
        private readonly Queue<(int conn, byte[] data)> _toServer = new Queue<(int, byte[])>();
        private int _nextConn = 1;

        public LoopbackHub(ServerCore server)
        {
            _server = server;
            server.SetTransport(this);
        }

        public LoopbackClient Connect(GameClient client)
        {
            var c = new LoopbackClient(this, _nextConn++, client);
            _clients[c.ConnectionId] = c;
            client.SetTransport(c);
            _server.OnConnected(c.ConnectionId);
            return c;
        }

        internal void EnqueueToServer(int conn, byte[] data) => _toServer.Enqueue((conn, data));

        internal void DisconnectClient(LoopbackClient c)
        {
            if (!_clients.Remove(c.ConnectionId)) return;
            _server.OnDisconnected(c.ConnectionId);
        }

        /// <summary>Delivers all queued client->server messages, then server->client messages.</summary>
        public void Pump()
        {
            while (_toServer.Count > 0)
            {
                var (conn, data) = _toServer.Dequeue();
                if (_clients.ContainsKey(conn)) _server.OnData(conn, data, 0, data.Length);
            }
            foreach (var c in new List<LoopbackClient>(_clients.Values)) c.Deliver();
        }

        public void Send(int connectionId, byte[] data, int length, bool reliable)
        {
            if (!_clients.TryGetValue(connectionId, out var c)) return;
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            c.Inbox.Enqueue(copy);
        }

        public void Disconnect(int connectionId, string reason)
        {
            if (_clients.TryGetValue(connectionId, out var c))
            {
                c.Deliver();
                _clients.Remove(connectionId);
                c.Closed = true;
            }
        }
    }

    public sealed class LoopbackClient : IClientTransport
    {
        private readonly LoopbackHub _hub;
        public readonly int ConnectionId;
        public readonly GameClient Client;
        internal readonly Queue<byte[]> Inbox = new Queue<byte[]>();
        public bool Closed { get; internal set; }

        internal LoopbackClient(LoopbackHub hub, int id, GameClient client)
        {
            _hub = hub;
            ConnectionId = id;
            Client = client;
        }

        public void Send(byte[] data, bool reliable)
        {
            if (!Closed) _hub.EnqueueToServer(ConnectionId, data);
        }

        public void Disconnect()
        {
            if (Closed) return;
            Closed = true;
            _hub.DisconnectClient(this);
        }

        internal void Deliver()
        {
            while (Inbox.Count > 0)
            {
                var d = Inbox.Dequeue();
                Client.Receive(d, 0, d.Length);
            }
        }
    }
}
