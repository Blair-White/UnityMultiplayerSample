using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class NetworkClient : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;
    public string myID;
    //since we can't serialize dictionaries easy and clients are in order 0,1,2,3 im just going to use arrays.
    public List<string> ClientIDs;
    public List<GameObject> ClientObjects;
    public List<Vector3> ClientPositions;
    
    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        m_Connection = default(NetworkConnection);
        var endpoint = NetworkEndPoint.Parse(serverIP,serverPort);
        m_Connection = m_Driver.Connect(endpoint);
    }
    
    void SendToServer(string message){
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    IEnumerator StartUpdateCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(2);
            Debug.Log("Sending Client Update to server");
            HandshakeMsg m = new HandshakeMsg();
            m.player.id = m_Connection.InternalId.ToString();
            SendToServer(JsonUtility.ToJson(m));
        }
    }

    void OnConnect(){
        Debug.Log("We are now connected to the server");
        StartCoroutine(StartUpdateCoroutine());
    }

    void OnData(DataStreamReader stream){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                Debug.Log("Handshake message received!");
                break;
            case Commands.SET_NEW_PLAYER_ID:
                PlayerUpdateMsg incIdMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("RECEIVED MY ID");
                myID = incIdMsg.player.id;
                break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Player update message received!");
                break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Server update message received!");
                break;
            case Commands.ADD_NEW_PLAYER:
                Debug.Log("NEW PLAYER CONNECTED");
                break;
            case Commands.SET_NEW_PLAYER_LIST:
                ServerUpdateMsg newListMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("NEW PLAYER LIST RECEIVED");
                break;
            case Commands.REMOVE_PLAYER:
                Debug.Log("PLAYER REMOVED FROM GAME");
                break;
            default:
                Debug.Log("Unrecognized message received!");
            break;
        }
    }

    void Disconnect(){
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void OnDisconnect(){
        Debug.Log("Client got disconnected from server");
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