using UnityEngine;
using Unity.Collections;
using System.Collections;
using System.Collections.Generic;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Text;
using TMPro;

public class NetworkClient : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;

    //My ID
    public string myID;


    //Add these in the inspector. 
    //Prefab of player cubes: these are different from the client cube
    [SerializeField]
    GameObject PlayerPrefab;
    
    //My cubes transform for movement and sending to the server.
    [SerializeField]
    Transform PlayerTransform;

    //Setup a Message with this players info to send to the server
    PlayerUpdateMsg MyPlayerInformation = new PlayerUpdateMsg();

    //Dictionary of ConnectedPlayers.
    private Dictionary<string, GameObject> AllConnectedPlayers = new Dictionary<string, GameObject>();

    void Start()
    {
        m_Driver = NetworkDriver.Create();
        m_Connection = default(NetworkConnection);
        serverIP = "127.0.0.1";
        var endpoint = NetworkEndPoint.Parse(serverIP, serverPort);
        m_Connection = m_Driver.Connect(endpoint);

    }

    void OnConnect()
    {
        Debug.Log("Connected to server");
        //Start regular updates to the server
        InvokeRepeating("SendServerMyInfo", 0.1f, 0.1f);
    }

    void OnDisconnect()
    {
        Debug.Log("Client got disconnected from server");
        m_Connection = default(NetworkConnection);
    }

    void SendToServer(string Message)
    {
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(Message), Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    void SendServerMyInfo()
    {
        //Send Relevant info to the server from the message we instantiated on startup
        //Reuse that message by changing its values.
        MyPlayerInformation.player.pos = PlayerTransform.position;
        MyPlayerInformation.player.color = PlayerTransform.gameObject.GetComponent<Renderer>().material.color;
        MyPlayerInformation.player.isDropped = PlayerTransform.gameObject.GetComponent<PlayerController>().isDropped;
        SendToServer(JsonUtility.ToJson(MyPlayerInformation));
    }

    void OnData(DataStreamReader stream)
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
            case Commands.GETMYID:
                PlayerUpdateMsg MyIdMessage = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Got My ID");
                MyPlayerInformation.player.id = MyIdMessage.player.id;
                myID = MyPlayerInformation.player.id;
                break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg IncomingMessage = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Client Updated By Server");
                break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg ServerUpdateMessage = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Sent Server my Update");
                //Loop through players in the server update mssg
                for (int i = 0; i < ServerUpdateMessage.players.Count; ++i)
                {
                    //if the player has an id matching i 
                    if (AllConnectedPlayers.ContainsKey(ServerUpdateMessage.players[i].id))
                    {
                        //Change MY version of that players position and color to match the server
                        AllConnectedPlayers[ServerUpdateMessage.players[i].id].transform.position = ServerUpdateMessage.players[i].pos;
                        AllConnectedPlayers[ServerUpdateMessage.players[i].id].GetComponent<Renderer>().material.color = ServerUpdateMessage.players[i].color;
                        
                        //if the player is flagged as dropped on the server, flag it as dropped for me. 
                        if (ServerUpdateMessage.players[i].isDropped)
                        {
                            AllConnectedPlayers[ServerUpdateMessage.players[i].id].GetComponent<PlayerController>().isDropped = true;
                        }
                    }        //However if the id matches my current client id
                    else if (MyPlayerInformation.player.id == ServerUpdateMessage.players[i].id)
                    {
                        //do the same stuff for just me.  ignore position as we do that on client side with input.
                        PlayerTransform.GetComponent<Renderer>().material.color = ServerUpdateMessage.players[i].color;
                        MyPlayerInformation.player.color = ServerUpdateMessage.players[i].color;
                    }
                }
                break;
            case Commands.GETEXISITNGPLAYERS:
                ServerUpdateMsg ExistingPlayersMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("existed player info received!");
                //Loop through the existing player list f rom the server
                for (int i = 0; i < ExistingPlayersMsg.players.Count; ++i)
                {
                    //instantiate a cube for each entry
                    GameObject ExistingPlayerCube = Instantiate(PlayerPrefab);
                    //Set their id's in our list
                    AllConnectedPlayers[ExistingPlayersMsg.players[i].id] = ExistingPlayerCube;
                    //Set their position in our list as well.
                    ExistingPlayerCube.transform.position = ExistingPlayersMsg.players[i].pos;
                }
                break;
            case Commands.ADDPLAYER:
                PlayerUpdateMsg newPlayerMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("new client info received!");
                //Instantiate a new cube
                GameObject NewPlayerCube = Instantiate(PlayerPrefab);
                //Add it to our list
                AllConnectedPlayers[newPlayerMsg.player.id] = NewPlayerCube;
                //Make sure to tag it as not being us. 
                NewPlayerCube.GetComponent<PlayerController>().IsClient = false;
                break;
            case Commands.PLAYERDROPPED:
                DisconnectedPlayersMsg DroppedMessage = JsonUtility.FromJson<DisconnectedPlayersMsg>(recMsg);
                Debug.Log("Dropped Client");
                //Loop through the list of dropped clients
                for (int i = 0; i < DroppedMessage.DROPPEDPLAYERLIST.Count; ++i)
                {
                    //If any of our players contains the id in the dropped list from server
                    if (AllConnectedPlayers.ContainsKey(DroppedMessage.DROPPEDPLAYERLIST[i]))
                    {
                        //Destroy that specific one on client.
                        Destroy(AllConnectedPlayers[DroppedMessage.DROPPEDPLAYERLIST[i]]);
                        //And Remove it from the our list on client. 
                        AllConnectedPlayers.Remove(DroppedMessage.DROPPEDPLAYERLIST[i]);
                    }
                }
                break;
            default:
                Debug.Log("Unrecognized message received!");
                break;
        }
    }
    void Disconnect()
    {
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    public void OnDestroy()
    {
        m_Driver.Dispose();
    }
    void Update()
    {
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated)
        {
            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type cmd;

        cmd = m_Connection.PopEvent(m_Driver, out stream);
        while (cmd != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                OnConnect();
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                OnData(stream);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                OnDisconnect();
            }

            cmd = m_Connection.PopEvent(m_Driver, out stream);
        }
    }
}