using System;
using Embervale.Shared.Data;
using Embervale.Shared.Net;
using UnityEngine;

namespace Embervale.Client
{
    public enum ClientState { Boot, Login, Connecting, CharacterSelect, CharacterCreate, EnteringWorld, InWorld }

    /// <summary>
    /// Root of the client. Loads the game data, owns the GameClient and its transport (UDP to a dedicated
    /// server, or an in-process ServerCore for offline play), drives the login -> character -> world flow and
    /// builds the world scene once the server says we are in. Created automatically when any scene starts.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }

        public GameDatabase Db { get; private set; }
        public GameClient Client { get; private set; }
        public ClientState State { get; private set; } = ClientState.Boot;
        public bool Offline { get; private set; }
        public string StatusText { get; private set; } = "";

        public WorldBuilder World { get; private set; }
        public EntityManager Entities { get; private set; }
        public CameraController Camera { get; private set; }
        public PlayerInteraction Interaction { get; private set; }
        public UIRoot UI { get; private set; }
        public AudioManager Audio { get; private set; }
        public EnvironmentController Environment { get; private set; }

        public event Action<ClientState> OnStateChanged;

        private LiteNetClientTransport _net;
        private LocalServerHost _local;
        private string _pendingUser, _pendingPass;
        private bool _pendingRegister;
        private float _pingTimer, _connectTimeout;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null || FindAnyObjectByType<GameSession>() != null) return;
            new GameObject("GameSession").AddComponent<GameSession>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = Settings.TargetFps;
            QualitySettings.vSyncCount = Settings.VSync ? 1 : 0;

            try { Db = GameDataLoader.Load(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                StatusText = "Game data failed to load: " + e.Message;
            }

            Audio = gameObject.AddComponent<AudioManager>();
            UI = UIRoot.Create(this);
            SetState(ClientState.Login);
            Audio.PlayMusic("music_title");
        }

        private void SetState(ClientState s)
        {
            State = s;
            OnStateChanged?.Invoke(s);
        }

        // ------------------------------------------------------------------ connection flow
        public void PlayOffline(string user, string pass, bool register)
        {
            if (Db == null) return;
            Disconnect();
            Offline = true;
            _local = new LocalServerHost(Db);
            NewClient();
            _local.Connect(Client);
            _pendingUser = user; _pendingPass = pass; _pendingRegister = register;
            Client.Connect();
            SetStatus("Starting local world...");
            SetState(ClientState.Connecting);
            _connectTimeout = 10f;
        }

        public void PlayOnline(string host, int port, string user, string pass, bool register)
        {
            if (Db == null) return;
            Disconnect();
            Offline = false;
            NewClient();
            _net = new LiteNetClientTransport(Client);
            Client.SetTransport(_net);
            _net.OnConnected += () => { Client.Connect(); SetStatus("Connected. Logging in..."); };
            _net.OnDisconnected += reason =>
            {
                SetStatus("Disconnected: " + reason);
                ReturnToLogin();
            };
            _pendingUser = user; _pendingPass = pass; _pendingRegister = register;
            _net.Connect(host, port);
            SetStatus("Connecting to " + host + ":" + port + "...");
            SetState(ClientState.Connecting);
            _connectTimeout = 10f;
        }

        private void NewClient()
        {
            Client = new GameClient(Db);
            Client.OnAuthResult += (ok, msg) =>
            {
                SetStatus(msg);
                if (!ok) { Disconnect(); SetState(ClientState.Login); }
            };
            Client.OnCharacterList += () =>
            {
                if (State == ClientState.InWorld) TearDownWorld();
                SetState(Client.Characters.Count == 0 ? ClientState.CharacterCreate : ClientState.CharacterSelect);
            };
            Client.OnCharacterResult += (ok, msg) => SetStatus(msg);
            Client.OnEnterWorld += EnterWorld;
            Client.OnKicked += reason => { SetStatus(reason); ReturnToLogin(); };
        }

        public void CreateCharacter(string name, Shared.Simulation.Appearance a) => Client?.CreateCharacter(name, a);
        public void SelectCharacter(long id) { Client?.SelectCharacter(id); SetStatus("Entering world..."); SetState(ClientState.EnteringWorld); }
        public void ShowCreate() => SetState(ClientState.CharacterCreate);
        public void ShowSelect() => SetState(Client != null && Client.Characters.Count > 0 ? ClientState.CharacterSelect : ClientState.CharacterCreate);

        public void Logout()
        {
            if (Client == null) return;
            Client.Send(ClientPackets.Logout());
        }

        public void ReturnToLogin()
        {
            TearDownWorld();
            Disconnect();
            SetState(ClientState.Login);
            Audio.PlayMusic("music_title");
        }

        private void Disconnect()
        {
            if (_net != null) { _net.Disconnect(); _net = null; }
            if (_local != null) { _local.Dispose(); _local = null; }
            Client = null;
        }

        private void SetStatus(string s)
        {
            StatusText = s ?? "";
            UI?.SetStatus(StatusText);
        }

        // ------------------------------------------------------------------ world
        private void EnterWorld()
        {
            TearDownWorld();
            var root = new GameObject("WorldRoot").transform;
            root.SetParent(transform, false);

            World = new WorldBuilder(Db.World, AssetRegistry.Instance, root);
            World.BuildAll();
            NavMeshBaker.Bake(World.Root.gameObject);

            Environment = root.gameObject.AddComponent<EnvironmentController>();
            Environment.Init(this);

            var em = new GameObject("Entities");
            em.transform.SetParent(root, false);
            Entities = em.AddComponent<EntityManager>();
            Entities.Init(this, World.Terrain);

            var camGo = UnityEngine.Camera.main != null ? UnityEngine.Camera.main.gameObject : new GameObject("Main Camera", typeof(UnityEngine.Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            Camera = camGo.GetComponent<CameraController>();
            if (Camera == null) Camera = camGo.AddComponent<CameraController>();
            Camera.Init(this);

            Interaction = root.gameObject.AddComponent<PlayerInteraction>();
            Interaction.Init(this);

            Effects.Init(this, root);
            UI.EnterWorld();
            SetState(ClientState.InWorld);
            SetStatus("");
        }

        private void TearDownWorld()
        {
            if (World == null) return;
            UI.LeaveWorld();
            Effects.Shutdown();
            if (Interaction != null) Destroy(Interaction);
            if (World.Root != null) Destroy(World.Root.parent.gameObject);
            World = null;
            Entities = null;
            Interaction = null;
            Environment = null;
        }

        public EntityView SelfView => Client != null && Entities != null ? Entities.Get(Client.SelfId) : null;

        // ------------------------------------------------------------------ loop
        private void Update()
        {
            _net?.Poll();
            _local?.Update(Time.unscaledDeltaTime);

            if (State == ClientState.Connecting && Client != null)
            {
                if (Client.HandshakeDone && _pendingUser != null)
                {
                    if (_pendingRegister) Client.Register(_pendingUser, _pendingPass);
                    else Client.Login(_pendingUser, _pendingPass);
                    _pendingUser = null; _pendingPass = null;
                }
                _connectTimeout -= Time.unscaledDeltaTime;
                if (_connectTimeout <= 0 && !Client.Authenticated)
                {
                    SetStatus("Could not reach the server" + (_net?.LastError != null ? " (" + _net.LastError + ")" : "") + ".");
                    ReturnToLogin();
                }
            }

            if (State == ClientState.InWorld && Client != null)
            {
                var self = SelfView;
                if (self != null) World.UpdateRoofs(self.transform.position);
                _pingTimer -= Time.unscaledDeltaTime;
                if (_pingTimer <= 0 && !Offline) { _pingTimer = 5f; Client.Send(ClientPackets.Ping(System.Environment.TickCount), false); }
            }
        }

        private void OnApplicationQuit()
        {
            if (Client != null && Client.InWorld) Client.Send(ClientPackets.Logout());
            _local?.Update(0);
            Disconnect();
        }
    }
}
