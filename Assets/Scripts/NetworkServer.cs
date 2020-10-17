using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using System.Collections.Generic;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Text;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public ushort serverPort;
    
    //List of connections to clients.
    private NativeList<NetworkConnection> m_Connections;

 
    //Dictionary for heartbeat objects.
    private Dictionary<string, float> heartbeat = new Dictionary<string, float>();
    
    
    //Dictionary of connected players, Key 0,1,2, and the network object
    private Dictionary<string, NetworkObjects.NetworkPlayer> PlayersConnected = new Dictionary<string, NetworkObjects.NetworkPlayer>(); //Dictionary for all clients
   
    void Start()
    {

        m_Driver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = serverPort;
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);
        else
            m_Driver.Listen();
        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

        //Easier than Ienumerator. From Assignment 1.
        //Send updates to clients
        InvokeRepeating("UpdateAllClients", 0.1f, 0.1f);
    }
    void UpdateAllClients()
    {
        //Create a new updatemesage object
        ServerUpdateMsg m = new ServerUpdateMsg();
        //loop through the dictionary and add connected players to the message
        foreach (KeyValuePair<string, NetworkObjects.NetworkPlayer> client in PlayersConnected)
        {
            m.players.Add(client.Value);
        }

        //Now loop through connections and Send the message. Always assert of ull end up 
        //crashing the server, found out the hard way. 
        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            SendToClient(JsonUtility.ToJson(m), m_Connections[i]);
        }
    }

    void OnConnect(NetworkConnection c)
    {
        
        Debug.Log("Accepted a connection");
        //Create a new message when we connect to get our ID
        PlayerUpdateMsg MyIdMsg = new PlayerUpdateMsg();
        //Send the Get id command to this client C
        MyIdMsg.cmd = Commands.GETMYID;
        MyIdMsg.player.id = c.InternalId.ToString();

        Assert.IsTrue(c.IsCreated);
        
        SendToClient(JsonUtility.ToJson(MyIdMsg), c);

        //Create a message of the existing players.
        ServerUpdateMsg ExistingPlayersMessage = new ServerUpdateMsg();
        ExistingPlayersMessage.cmd = Commands.GETEXISITNGPLAYERS;
        
        //loop throught the existing players and add them to the message
        foreach (KeyValuePair<string, NetworkObjects.NetworkPlayer> item in PlayersConnected)
        {
            ExistingPlayersMessage.players.Add(item.Value);
        }

        Assert.IsTrue(c.IsCreated);
        SendToClient(JsonUtility.ToJson(ExistingPlayersMessage), c);


        
        //Now create a message to tell the Existing players of the new player arrival.
        PlayerUpdateMsg NEWPLAYER_Message = new PlayerUpdateMsg();
        NEWPLAYER_Message.cmd = Commands.ADDPLAYER;
        NEWPLAYER_Message.player.id = c.InternalId.ToString();
        
        //loop through our connections and send the message
        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            SendToClient(JsonUtility.ToJson(NEWPLAYER_Message), m_Connections[i]);
        }

        //Add the connection to the server connection list
        //
        m_Connections.Add(c);

        //Create a new network object for our new player and store it in our existing players list.
        PlayersConnected[c.InternalId.ToString()] = new NetworkObjects.NetworkPlayer();
    }


    void OnData(DataStreamReader stream, int i, NetworkConnection client)
    {
    
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length, Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch (header.cmd)
        {
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                Debug.Log("Handshake message received!");
                break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg updateMessage = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                //If the players connected match the key in the update from server
                if (PlayersConnected.ContainsKey(updateMessage.player.id))
                {
                    //modify its values on the server to match the update message.
                    PlayersConnected[updateMessage.player.id].id = updateMessage.player.id;
                    PlayersConnected[updateMessage.player.id].pos = updateMessage.player.pos;
                    PlayersConnected[updateMessage.player.id].isDropped = updateMessage.player.isDropped;
                }
                break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg ServerUpdateMessage = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Server Update.");
                break;
            default:
                Debug.Log("ServerError (Default Log)");
                break;
        }
    }

    void OnDisconnect(int i)
    {
        Debug.Log("Client disconnected from server");
        m_Connections[i] = default(NetworkConnection);
    }


    void SendToClient(string message, NetworkConnection c)
    {
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message), Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }
    public void OnDestroy()
    {
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    void Update()
    {
        m_Driver.ScheduleUpdate().Complete();
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {
                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }
        NetworkConnection c = m_Driver.Accept();
        while (c != default(NetworkConnection))
        {
            OnConnect(c);
            c = m_Driver.Accept();
        }

        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            NetworkEvent.Type cmd;
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            while (cmd != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    OnData(stream, i, m_Connections[i]);

                    heartbeat[m_Connections[i].InternalId.ToString()] = Time.time;
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
        AssertHeartbeatTime();
    }

    void AssertHeartbeatTime()
    {
        //Check for heartbeat time, if its > 10 seconds delete the client:

        //Create a new list for the deleted clients
        List<string> DroppedClientList = new List<string>();

        //loop through our heartbeat objects and compare the time in heartbeat
        //with the current time, if its > 10 seconds:
        foreach (KeyValuePair<string, float> item in heartbeat)
        {

            if (Time.time - item.Value >= 10f)
            {
                //Add the client to the list for deletion
                DroppedClientList.Add(item.Key);
            }
        }

        //If we have clients to be deleted:
        if (DroppedClientList.Count != 0)
        {
            //Remove the client from both our connected players and heartbeat
            //If you do not do this you will crash the server...
            for (int i = 0; i < DroppedClientList.Count; ++i)
            {
                PlayersConnected.Remove(DroppedClientList[i]);
                heartbeat.Remove(DroppedClientList[i]);
            }


            DisconnectedPlayersMsg DCMSG = new DisconnectedPlayersMsg();
            DCMSG.DROPPEDPLAYERLIST = DroppedClientList;

            // Send message to all clients
            for (int i = 0; i < m_Connections.Length; i++)
            {
                if (DroppedClientList.Contains(m_Connections[i].InternalId.ToString()) == true)
                {
                    continue;
                }
                Assert.IsTrue(m_Connections[i].IsCreated);
                SendToClient(JsonUtility.ToJson(DCMSG), m_Connections[i]);
            }
        }
    }
}