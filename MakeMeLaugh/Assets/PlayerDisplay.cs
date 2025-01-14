using LobbyRelaySample;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Lobbies;
using UnityEngine;

public class PlayerDisplay : NetworkBehaviour
{
    [SerializeField]
    private GameObject body;
    public NetworkVariable<int> emote = new NetworkVariable<int>();

    int emoteAsApplied;

    // Start is called before the first frame update
    void Start()
    {
        if (IsLocalPlayer)
        {
            int emoteValue = (int)GameManager.Instance.m_LocalUser.Emote.Value;
            SetEmoteServerRpc(emoteValue);
        }
    }

    [ServerRpc]
    void SetEmoteServerRpc(int emoteValue)
    {
        emote.Value = emoteValue;
    }

    private void ApplyEmoteColor(int previousValue, int newValue)
    {
        Material[] materials = body.GetComponent<Renderer>().materials;
        Color color = Color.white;
        EmoteType _emote = (EmoteType) newValue;
        switch (_emote)
        {
            case EmoteType.Tongue:
                color = Color.blue;
                break;
            case EmoteType.Unamused:
                color = Color.red;
                break;
            case EmoteType.Frown:
                color = Color.yellow;
                break;
            case EmoteType.Smile:
            case EmoteType.None:
                color = Color.green;
                break;
        }
        foreach (Material m in materials)
        {
            m.color = color;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (emote.Value != emoteAsApplied)
        {
            ApplyEmoteColor(emoteAsApplied, emote.Value);
            emoteAsApplied = emote.Value;
        }
    }
}
