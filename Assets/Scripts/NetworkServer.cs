using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Collections;
using System.Text;
using System.Collections.Generic;
using System.Linq;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public ushort serverPort;
    private NativeList<NetworkConnection> m_Connections;
    [SerializeField]
    private Dictionary<string, NetworkObjects.NetworkPlayer> PlayersConnected;
    private List<float> LastHandshakeTimes;
    void Start ()
    {
        PlayersConnected = new Dictionary<string, NetworkObjects.NetworkPlayer>();

        m_Driver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = serverPort;
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);
        else
            m_Driver.Listen();

        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
        StartCoroutine(SendUpdateToAllClients());
    }

    IEnumerator SendUpdateToAllClients()
    {
        while (true)
        {
            for(int i = 0; i < m_Connections.Length; i++)
            {
                if (!m_Connections[i].IsCreated)
                    continue;

                ////example to send a handshake
                HandshakeMsg m = new HandshakeMsg();
                m.player.id = m_Connections[i].InternalId.ToString();
                Assert.IsTrue(m_Connections[i].IsCreated);
                SendToClient(JsonUtility.ToJson(m), m_Connections[i]);
            }
            yield return new WaitForSeconds(2);
        }
    }

    void SendToClient(string message, NetworkConnection c){
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }
    public void OnDestroy()
    {
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    void OnConnect(NetworkConnection c){
        //Things we gotta do:
        // - Connect the new player - done for us already.
        // A Send the player its id
        // B Send the player a list of existing clients
        // C Send all the clients the new player
        // D Add the new player to the servers list of client connections
        // D part 2 Also Add the player to the dictionary we keep. of player objects
        // - /wrist.
        m_Connections.Add(c);
        Debug.Log("Accepted a connection");
        //A
        PlayerUpdateMsg SendIDMsg = new PlayerUpdateMsg();
        SendIDMsg.cmd = Commands.SET_NEW_PLAYER_ID;
        SendIDMsg.player.id = c.InternalId.ToString();
        SendToClient(JsonUtility.ToJson(SendIDMsg), c);
        //B
        ServerUpdateMsg ExistingPlayers = new ServerUpdateMsg();
        ExistingPlayers.cmd = Commands.SET_NEW_PLAYER_LIST;
        for(int i = 0; i < PlayersConnected.Count; i++)
        {   //Fuck my life. 
            ExistingPlayers.players.Add(PlayersConnected.Values.ElementAt(i));
        }
        SendToClient(JsonUtility.ToJson(ExistingPlayers), c);
        //C
        PlayerUpdateMsg AddNewPlayerMsg = new PlayerUpdateMsg();
        AddNewPlayerMsg.cmd = Commands.ADD_NEW_PLAYER;
        //Make sure to give the ID because why the fuck is this so hard. 
        AddNewPlayerMsg.player.id = SendIDMsg.player.id;
        //Now the loop lord have mercy
        for(int i = 0; i < m_Connections.Length; i++)
        {
            SendToClient(JsonUtility.ToJson(AddNewPlayerMsg), m_Connections[i]);
        }

        //Finally D
        m_Connections.Add(c);
        //D Part 2 We are adding a new network player to the key with this objects id
        PlayersConnected[c.InternalId.ToString()] = new NetworkObjects.NetworkPlayer();

        //Add a time slot for handshake check on server
        //LastHandshakeTimes.Add(Time.time);
    }




    void OnData(DataStreamReader stream, int i){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.HANDSHAKE:
            HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
            Debug.Log("Handshake message received!");
            break;
            case Commands.PLAYER_UPDATE:
            PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
            Debug.Log("Player update message received!");
            break;
            case Commands.SERVER_UPDATE:
            ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
            Debug.Log("Server update message received!");
            break;
            default:
            Debug.Log("SERVER ERROR: Unrecognized message received!");
            break;
        }
    }

    //Can skip previous assignment Heartbeat disconnect as we automatically 
    //lose connection here.
    //Remove client from appropriate lists and notify clients of dropped player.
    void OnDisconnect(int i){
        Debug.Log("Client disconnected from server");
        
        m_Connections[i] = default(NetworkConnection);
    }

    void Update ()
    {
        m_Driver.ScheduleUpdate().Complete();

        // CleanUpConnections
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {

                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        // AcceptNewConnections
        NetworkConnection c = m_Driver.Accept();
        while (c  != default(NetworkConnection))
        {            
            OnConnect(c);

            // Check if there is another new connection
            c = m_Driver.Accept();
        }


        // Read Incoming Messages
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
                    OnData(stream, i);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
    }
}