﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace LobbyRelaySample.ngo
{
    /// <summary>
    /// Once the local localPlayer is in a localLobby and that localLobby has entered the In-Game state, this will load in whatever is necessary to actually run the game part.
    /// This will exist in the game scene so that it can hold references to scene objects that spawned prefab instances will need.
    /// </summary>
    public class SetupInGame : MonoBehaviour
    {
        [SerializeField]
        GameObject m_IngameRunnerPrefab = default;
        [SerializeField]
        private GameObject[] m_disableWhileInGame = default;

        private InGameRunner m_inGameRunner;

        private bool m_doesNeedCleanup = false;
        private bool m_hasConnectedViaNGO = false;

        private LocalLobby m_lobby;

        private void SetMenuVisibility(bool areVisible)
        {
            foreach (GameObject go in m_disableWhileInGame)
            {
                go.SetActive(areVisible);
            }
        }

        /// <summary>
        /// The prefab with the NetworkManager contains all of the assets and logic needed to set up the NGO minigame.
        /// The UnityTransport needs to also be set up with a new Allocation from Relay.
        /// </summary>
        async Task CreateNetworkManager(LocalLobby localLobby, LocalPlayer localPlayer)
        {
            Debug.Log("CreateNetworkManager(" + localLobby + ", " + localPlayer + ")");
            m_lobby = localLobby;
            m_inGameRunner = Instantiate(m_IngameRunnerPrefab).GetComponentInChildren<InGameRunner>();
            m_inGameRunner.Initialize(OnConnectionVerified, m_lobby.PlayerCount, OnGameBegin, OnGameEnd,
                localPlayer);
            if (localPlayer.IsHost.Value)
            {
                Debug.Log("  decided we are the host; awaiting SetRelayHostData()...");
                await SetRelayHostData();
                Debug.Log("NetworkManager.Singleton.StartHost()");
                NetworkManager.Singleton.StartHost();
            }
            else
            {
                Debug.Log("CreateNetworkManager() decided we are NOT the host; awaiting AwaitRelayCode(" + localLobby + ")...");
                StartCreateNonHostNetworkManager(localLobby);
            }
        }

        bool needFinishCreateNonHostNetworkManager = false;
        void StartCreateNonHostNetworkManager(LocalLobby lobby)
        {
            string relayCode = lobby.RelayCode.Value;
            lobby.RelayCode.onChanged += (code) => {
                // DJMC: Ignore it if we receive a bad relay code.
                if (!string.IsNullOrEmpty(code))
                {
                    Debug.Log("lobby.RelayCode.onChanged(" + code + ")");
                    relayCode = code;
                }
                else
                {
                    Debug.Log("lobby.RelayCode.onChanged() received a null-or-empty relay code");
                }
            };
            needFinishCreateNonHostNetworkManager = true;
        }

        bool TryFinishNonHostNetworkManager()
        {
            if (!needFinishCreateNonHostNetworkManager)
            {
                // vacuously true
                return true;
            }

            // wait for relayCode to be filled in from elsewhere
            LocalLobby lobby = m_lobby;
            string relayCode = lobby.RelayCode.Value;
            if (string.IsNullOrEmpty(relayCode))
            {
                return false;
            }

            // pre-conditions are met, we're ready to work
            // if we're here we're executing to the end
            // we can lower the flag now that says we need to do this
            needFinishCreateNonHostNetworkManager = false;

            ActuallyFinishNonHostNetworkManager();

            return true;
        }

        // the work to be done here needs to be in an async Task (since we need to await)
        async Task ActuallyFinishNonHostNetworkManager()
        {
            await SetRelayClientData();
            Debug.Log("TryFinishNonHostNetworkManager() calling NetworkManager.Singleton.StartClient()");
            NetworkManager.Singleton.StartClient();
        }

        void Update()
        {
            TryFinishNonHostNetworkManager();
        }

        async Task SetRelayHostData()
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponentInChildren<UnityTransport>();

            var allocation = await Relay.Instance.CreateAllocationAsync(m_lobby.MaxPlayerCount.Value);

            var joincode = await Relay.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log("DJMC fyi the joincode is " + joincode);
            GameManager.Instance.HostSetRelayCode(joincode);

            bool isSecure = false;
            var endpoint = GetEndpointForAllocation(allocation.ServerEndpoints,
                allocation.RelayServer.IpV4, allocation.RelayServer.Port, out isSecure);

            transport.SetHostRelayData(AddressFromEndpoint(endpoint), endpoint.Port,
                allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData, isSecure);

            // DJMC -- added to better support websockets as per https://docs.unity.com/ugs/manual/relay/manual/relay-and-ngo
            transport.SetRelayServerData(new RelayServerData(allocation, "wss"));
        }

        async Task SetRelayClientData()
        {
            Debug.Log("SetupInGame.SetRelayClientData()");
            UnityTransport transport = NetworkManager.Singleton.GetComponentInChildren<UnityTransport>();

            var joinAllocation = await Relay.Instance.JoinAllocationAsync(m_lobby.RelayCode.Value);
            bool isSecure = false;
            var endpoint = GetEndpointForAllocation(joinAllocation.ServerEndpoints,
                joinAllocation.RelayServer.IpV4, joinAllocation.RelayServer.Port, out isSecure);

            transport.SetClientRelayData(AddressFromEndpoint(endpoint), endpoint.Port,
                joinAllocation.AllocationIdBytes, joinAllocation.Key,
                joinAllocation.ConnectionData, joinAllocation.HostConnectionData, isSecure);

            // DJMC -- added to better support websockets as per https://docs.unity.com/ugs/manual/relay/manual/relay-and-ngo
            Debug.Log("SetupInGame.SetRelayClientData() calling SetRelayServerData()");
            transport.SetRelayServerData(new RelayServerData(joinAllocation, "wss"));
        }

        /// <summary>
        /// Determine the server endpoint for connecting to the Relay server, for either an Allocation or a JoinAllocation.
        /// If DTLS encryption is available, and there's a secure server endpoint available, use that as a secure connection. Otherwise, just connect to the Relay IP unsecured.
        /// </summary>
        NetworkEndpoint GetEndpointForAllocation(
            List<RelayServerEndpoint> endpoints,
            string ip,
            int port,
            out bool isSecure)
        {
#if ENABLE_MANAGED_UNITYTLS
            foreach (RelayServerEndpoint endpoint in endpoints)
            {
                if (endpoint.Secure && endpoint.Network == RelayServerEndpoint.NetworkOptions.Udp)
                {
                    isSecure = true;
                    return NetworkEndpoint.Parse(endpoint.Host, (ushort)endpoint.Port);
                }
            }
#endif
            isSecure = false;
            return NetworkEndpoint.Parse(ip, (ushort)port);
        }

        string AddressFromEndpoint(NetworkEndpoint endpoint)
        {
            return endpoint.Address.Split(':')[0];
        }

        void OnConnectionVerified()
        {
            Debug.Log("SetupInGame.OnConnectionVerified()");
            m_hasConnectedViaNGO = true;
        }

        public void StartNetworkedGame(LocalLobby localLobby, LocalPlayer localPlayer)
        {
            Debug.Log("StartNetworkedGame(" + localLobby + ", " + localPlayer + ")");
            m_doesNeedCleanup = true;
            SetMenuVisibility(false);
#pragma warning disable 4014
            CreateNetworkManager(localLobby, localPlayer);
#pragma warning restore 4014
        }

        public void OnGameBegin()
        {
            Debug.Log("SetupInGame.OnGameBegin() -- testing m_hasConnectedViaNGO (" + m_hasConnectedViaNGO + ")");
            if (!m_hasConnectedViaNGO)
            {
                // If this localPlayer hasn't successfully connected via NGO, forcibly exit the minigame.
                LogHandlerSettings.Instance.SpawnErrorPopup("Failed to join the game.");
                OnGameEnd();
            }
        }

        /// <summary>
        /// Return to the localLobby after the game, whether due to the game ending or due to a failed connection.
        /// </summary>
        public void OnGameEnd()
        {
            Debug.Log("SetupInGame.OnGameEnd()");
            if (m_doesNeedCleanup)
            {
                NetworkManager.Singleton.Shutdown(true);
                Destroy(m_inGameRunner
                    .transform.parent
                    .gameObject); // Since this destroys the NetworkManager, that will kick off cleaning up networked objects.
                SetMenuVisibility(true);
                m_lobby.RelayCode.Value = "";
                GameManager.Instance.EndGame();
                m_doesNeedCleanup = false;
            }
        }
    }
}