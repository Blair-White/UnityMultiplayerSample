using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NetworkMessages
{
    public enum Commands
    {
        PLAYER_UPDATE,
        SERVER_UPDATE,
        HANDSHAKE,
        GETEXISITNGPLAYERS,
        ADDPLAYER,
        GETMYID,
        PLAYERDROPPED
    }

    [System.Serializable]
    public class NetworkHeader
    {
        public Commands cmd;
    }

    [System.Serializable]
    public class HandshakeMsg : NetworkHeader
    {
        public NetworkObjects.NetworkPlayer player;

        public HandshakeMsg()
        {    
            cmd = Commands.HANDSHAKE;
            player = new NetworkObjects.NetworkPlayer();
        }
    }



    [System.Serializable]
    public class PlayerUpdateMsg : NetworkHeader
    {
        public NetworkObjects.NetworkPlayer player;
        public PlayerUpdateMsg()
        {     
            cmd = Commands.PLAYER_UPDATE;
            player = new NetworkObjects.NetworkPlayer();
        }
    };

    [System.Serializable]
    public class ServerUpdateMsg : NetworkHeader
    {
        public List<NetworkObjects.NetworkPlayer> players;
        public ServerUpdateMsg()
        {      
            cmd = Commands.SERVER_UPDATE;
            players = new List<NetworkObjects.NetworkPlayer>();
        }
    }

    [System.Serializable]
    public class DisconnectedPlayersMsg : NetworkHeader
    {
        public List<string> DROPPEDPLAYERLIST;
        public DisconnectedPlayersMsg()
        {     
            cmd = Commands.PLAYERDROPPED;
            DROPPEDPLAYERLIST = new List<string>();
        }
    }
}

namespace NetworkObjects
{
    [System.Serializable]
    public class NetworkObject
    {
        public string id;
    }

    [System.Serializable]
    public class NetworkPlayer : NetworkObject
    {
        public Color color;
        public Vector3 pos;
        public bool isDropped;

        public NetworkPlayer()
        {
            color = new Color();
            pos = Vector3.zero;
            isDropped = false;
        }
    }
}