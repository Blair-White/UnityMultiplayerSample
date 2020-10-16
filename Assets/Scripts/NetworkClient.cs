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
    //My cubes transform for sending to server.
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
                for (int i = 0; i < ServerUpdateMessage.players.Count; ++i)
                {
                    if (AllConnectedPlayers.ContainsKey(ServerUpdateMessage.players[i].id))
                    {
                        AllConnectedPlayers[ServerUpdateMessage.players[i].id].transform.position = ServerUpdateMessage.players[i].pos;
                        AllConnectedPlayers[ServerUpdateMessage.players[i].id].GetComponent<Renderer>().material.color = ServerUpdateMessage.players[i].color;
                        if (ServerUpdateMessage.players[i].isDropped)
                        {
                            AllConnectedPlayers[ServerUpdateMessage.players[i].id].GetComponent<PlayerController>().isDropped = true;
                            AllConnectedPlayers[ServerUpdateMessage.players[i].id].GetComponentInChildren<TextMeshProUGUI>().SetText(":(");
                        }
                    }

                    else if (MyPlayerInformation.player.id == ServerUpdateMessage.players[i].id)
                    {
                        PlayerPrefab.gameObject.GetComponent<Renderer>().material.color = ServerUpdateMessage.players[i].color;
                        MyPlayerInformation.player.color = ServerUpdateMessage.players[i].color;
                        if (ServerUpdateMessage.players[i].isDropped)
                        {
                            AllConnectedPlayers[ServerUpdateMessage.players[i].id].GetComponent<PlayerController>().isDropped = true;

                        }
                    }
                }
                break;
            case Commands.GETEXISITNGPLAYERS:
                ServerUpdateMsg ExistingPlayersMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("existed player info received!");
                AddExistingPlayers(ExistingPlayersMsg);
                break;
            case Commands.ADDPLAYER:
                PlayerUpdateMsg newPlayerMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("new client info received!");
                SpawnNewPlayer(newPlayerMsg);
                break;
            case Commands.PLAYERDROPPED:
                DisconnectedPlayersMsg DroppedMessage = JsonUtility.FromJson<DisconnectedPlayersMsg>(recMsg);
                Debug.Log("Dropped Client");
                DeleteDisconnectPlayer(DroppedMessage);
                break;

            default:
                Debug.Log("Unrecognized message received!");
                break;
        }
    }

    void AddExistingPlayers(ServerUpdateMsg Message)
    {
        for (int i = 0; i < Message.players.Count; ++i)
        {
            GameObject cube = Instantiate(PlayerPrefab);
            AllConnectedPlayers[Message.players[i].id] = cube;
            cube.transform.position = Message.players[i].pos;
        }
    }
    void OnDisconnect()
    {
        Debug.Log("Client got disconnected from server");
        m_Connection = default(NetworkConnection);
    }

    void SendToServer(string message)
    {
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message), Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    void Disconnect()
    {
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void SpawnNewPlayer(PlayerUpdateMsg puMsg)
    {
        GameObject cube = Instantiate(PlayerPrefab);

        AllConnectedPlayers[puMsg.player.id] = cube;

        cube.GetComponent<PlayerController>().IsClient = false; 
    }

    void DeleteDisconnectPlayer(DisconnectedPlayersMsg dcMsg)
    {
        for (int i = 0; i < dcMsg.DROPPEDPLAYERLIST.Count; ++i)
        {
            if (AllConnectedPlayers.ContainsKey(dcMsg.DROPPEDPLAYERLIST[i]))
            {
                Destroy(AllConnectedPlayers[dcMsg.DROPPEDPLAYERLIST[i]]);
                AllConnectedPlayers.Remove(dcMsg.DROPPEDPLAYERLIST[i]);
            }
        }
    }

    public void OnDestroy()
    {
        //Disconnect();
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