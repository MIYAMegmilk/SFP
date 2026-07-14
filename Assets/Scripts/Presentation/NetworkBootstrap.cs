using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace SFP.Presentation
{
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        const ushort DefaultPort = 7777;

        public static NetworkBootstrap Instance { get; private set; }

        NetworkManager _networkManager;
        UnityTransport _transport;

        void Awake()
        {
            Instance = this;

            _networkManager = GetComponent<NetworkManager>();
            if (_networkManager == null)
                _networkManager = gameObject.AddComponent<NetworkManager>();

            _transport = GetComponent<UnityTransport>();
            if (_transport == null)
                _transport = gameObject.AddComponent<UnityTransport>();

            _transport.ConnectionData.Port = DefaultPort;

            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        void OnDestroy()
        {
            if (_networkManager == null)
                return;

            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        public void StartHost()
        {
            NetworkManager.Singleton.StartHost();
        }

        public void StartClient(string ipAddress)
        {
            _transport.ConnectionData.Address = ipAddress;
            _transport.ConnectionData.Port = DefaultPort;
            NetworkManager.Singleton.StartClient();
        }

        public void Shutdown()
        {
            NetworkManager.Singleton.Shutdown();
        }

        // No NetworkManager listening = solo/standalone mode; SimulationBridge still needs to tick.
        public bool IsServer => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
        public bool IsClient => NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
        public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        public bool IsConnected => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        static void HandleClientConnected(ulong clientId)
        {
            Debug.Log($"[NetworkBootstrap] Client connected: {clientId}");
        }

        static void HandleClientDisconnected(ulong clientId)
        {
            Debug.Log($"[NetworkBootstrap] Client disconnected: {clientId}");
        }
    }
}
