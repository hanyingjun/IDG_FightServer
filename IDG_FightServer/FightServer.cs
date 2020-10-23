using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Timers;

namespace IDG.FightServer
{
    public class PlayerInfo
    {
        public string username { get; set; }
        public string name { get; set; }
        public bool isReady { get; set; }
        public Character character { get; set; }
    }

    public class Character
    {
        public int Id { get; set; }
        public string info { get; set; }
    }

    public class FightRoom
    {
        public int Id { get; set; }
        public string url { get; set; }
        public string ip { get; set; }
        public string port { get; set; }
        public string isFull { get; set; }

        public List<PlayerInfo> playerInfos { get; set; }

        public bool InRoom(string userName)
        {
            foreach (var pi in playerInfos)
            {
                if (pi.username == userName)
                {
                    return true;
                }
            }
            return false;
        }

        public bool CheckAllReady()
        {
            foreach (var player in playerInfos)
            {
                if (!player.isReady)
                {
                    return false;
                }
            }
            return true;
        }

        public void AddPlayer(PlayerInfo player)
        {
            if (playerInfos == null)
            {
                playerInfos = new List<PlayerInfo>();
            }
            playerInfos.Add(player);
        }
    }

    public class FightServer
    {
        private FightRoom _fightRoom = null;
        public FightRoom fightRoom
        {
            get
            {
                return _fightRoom;
            }
            set
            {
                _fightRoom = value;
            }
        }

        public IndexObjectPool<Connection> ClientPool
        {
            get
            {
                lock (_clientPool)
                {
                    return _clientPool;
                }
            }
        }

        public Socket Listener
        {
            get
            {
                lock (_serverListener)
                {
                    return _serverListener;
                }
            }
        }

        public List<byte[]> FrameList
        {
            get
            {
                lock (_frameList)
                {
                    return _frameList;
                }
            }
            set
            {
                lock(_frameList)
                {
                    _frameList = value;
                }
            }
        }

        protected List<byte[]> _frameList;

        private IndexObjectPool<Connection> _clientPool;
        private int m_nPort = 0;
        public int Port
        {
            get { return m_nPort; }
            private set { m_nPort = value; }
        }
        private string m_sIP = null;
        public string IP
        {
            get { return m_sIP; }
            private set { m_sIP = value; }
        }
        /// <summary>
        /// 最大连接数
        /// </summary>
        private int _maxConnectNum = 0;
        public Socket _serverListener;
        public Timer timer;
        private Byte[][] _stepMessage = null;
        /// <summary>
        /// 所有玩家的帧数据
        /// </summary>
        public Byte[][] StepMessage
        {
            get
            {
                lock (_stepMessage)
                {
                    return _stepMessage;
                }
            }
            set
            {
                lock (_stepMessage)
                {
                    _stepMessage = value;
                }
            }
        }
        /// <summary>
        /// 持续时间
        /// </summary>
        private int _durationTime = 0;
        private bool _battleEnd = false;

        public FightServer(string host, int port, int maxServerCount)
        {
            this.IP = host;
            this.Port = port;
            this._maxConnectNum = maxServerCount;
            _clientPool = new IndexObjectPool<Connection>(this._maxConnectNum);
        }

        public void StartServer()
        {
            _serverListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Listener.NoDelay = true;
            timer = new Timer(100);
            timer.AutoReset = true;
            timer.Elapsed += SendStepAll;
            timer.Enabled = true;
            Listener.Bind(new IPEndPoint(IPAddress.Parse(this.IP), this.Port));
            Listener.Listen(this._maxConnectNum);
            Listener.BeginAccept(AcceptCallBack, Listener);
            _frameList = new List<byte[]>();
            _stepMessage = new byte[this._maxConnectNum][];

            ServerLog.LogServer("服务器启动成功", 0);
        }

        /// <summary>
        /// 当所有客户端都推出的时候，关闭服务器监听
        /// </summary>
        public void StopServer()
        {
            try
            {
                Listener.Shutdown(SocketShutdown.Both);
            }
            catch (Exception) { }
            try
            {
                Listener.Disconnect(false);
            }
            catch (Exception) { }
            try
            {
                Listener.Close();
            }
            catch (Exception) { }

            _serverListener = null;
            timer.Stop();
            timer = null;
            FrameList = null;
            StepMessage = null;
        }

        protected void AcceptCallBack(IAsyncResult ar)
        {
            Socket client = Listener.EndAccept(ar);
            int index = ClientPool.Get();
            if (index >= 0)
            {
                ClientPool[index].SetActive();
                Connection con = ClientPool[index];
                con.clientId = index;
                StepMessage = new byte[ClientPool.Count][];
                con.socket = client;
                SendIninInfo((byte)con.clientId);
                if (FrameList.Count > 0)
                    SendToClientAllFrame(index);
                con.socket.BeginReceive(con.readBuff, 0, Connection.buffer_size, SocketFlags.None, ReceiveCallBack, con);
            }
            else
            {
                ServerLog.LogServer("服务器人数达到上限", 0);
            }
            Listener.BeginAccept(AcceptCallBack, Listener);
        }

        protected void ReceiveCallBack(IAsyncResult ar)
        {
            Connection con = (Connection)ar.AsyncState;
            if (!con.ActiveCheck())
                return;

            try
            {
                lock (con)
                {
                    int length = con.socket.EndReceive(ar);
                    ServerLog.LogClient("receive:" + length, 1, con.clientId);
                    if (length <= 0)
                    {
                        ServerLog.LogClient("客户端断开连接：" + ClientPool[con.clientId].socket.LocalEndPoint + "ClientID:" + con.clientId, 0, con.clientId);
                        con.socket.Close();
                        ClientPool.Recover(con.clientId);
                        return;
                    }

                    con.length += length;

                    ProcessData(con);

                    con.socket.BeginReceive(con.readBuff, con.length, con.BuffRemain, SocketFlags.None, ReceiveCallBack, con);
                }
            }
            catch (Exception)
            {
                ServerLog.LogClient("客户端异常终止连接：" + ClientPool[con.clientId].socket.LocalEndPoint + "ClientID:" + con.clientId, 0, con.clientId);

                con.socket.Close();
                ClientPool.Recover(con.clientId);
            }
        }

        private void ProcessData(Connection connection)
        {
            if (connection.length < sizeof(Int32))
            {
                // Debug.Log("获取不到信息大小重新接包解析：" + connection.length.ToString());
                return;
            }
            Array.Copy(connection.readBuff, connection.lenBytes, sizeof(Int32));
            connection.msgLength = BitConverter.ToInt32(connection.lenBytes, 0);

            if (connection.length < connection.msgLength + sizeof(Int32))
            {
                //Debug.Log("信息大小不匹配重新接包解析：" + connection.msgLength.ToString());
                return;
            }
            ProtocolBase message = new ByteProtocol();
            message.InitMessage(connection.ReceiveBytes);
            ParseMessage(connection, message);

            int count = connection.length - connection.msgLength - sizeof(Int32);
            Array.Copy(connection.readBuff, sizeof(Int32) + connection.msgLength, connection.readBuff, 0, count);
            connection.length = count;
            if (connection.length > 0)
            {
                ProcessData(connection);
            }
        }

        protected void ParseMessage(Connection con, ProtocolBase protocol)
        {
            if (_battleEnd == true)
            {
                return;
            }

            MessageType messageType = (MessageType)protocol.getByte();
            switch (messageType)
            {
                case MessageType.Frame:
                    byte clientId = protocol.getByte();
                    byte[] t2 = protocol.getLastBytes();
                    StepMessage[con.clientId] = t2;
                    ClientPool[clientId].SetActive();
                    ServerLog.LogClient("Key:[" + t2.Length + "]", 3, clientId);
                    break;
                case MessageType.ClientReady:
                    break;
                case MessageType.Ping:
                    byte id = protocol.getByte();
                    SendPingToClient(id);
                    break;
                default:
                    Console.WriteLine("not handle messagetype " + messageType);
                    return;
            }

            if (protocol.Length > 0)
            {
                ServerLog.LogServer("剩余未解析" + protocol.Length, 1);
            }
        }

        protected void SendToClient(int clientId, byte[] bytes)
        {
            byte[] length = BitConverter.GetBytes(bytes.Length);
            byte[] send = new byte[4 + bytes.Length];
            Array.Copy(length, send, 4);
            Array.Copy(bytes, 0, send, 4, bytes.Length);
            ServerLog.LogClient("send:" + send.Length, 2, clientId);
            try
            {
                ClientPool[clientId].socket.BeginSend(send, 0, send.Length, SocketFlags.None, null, null);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        protected void SendIninInfo(byte clientId)
        {
            ProtocolBase protocol = new ByteProtocol();
            protocol.push((byte)MessageType.Init);
            protocol.push(clientId);
            protocol.push((byte)MessageType.end);
            this.SendToClient(clientId, protocol.GetByteStream());
            ServerLog.LogClient("客户端连接成功：" + ClientPool[clientId].socket.LocalEndPoint + "ClientID:" + clientId, 0, clientId);
        }

        protected void SendPingToClient(byte clientId)
        {
            ProtocolBase protocol = new ByteProtocol();
            protocol.push((byte)MessageType.Ping);
            protocol.push((byte)MessageType.end);
            this.SendToClient(clientId, protocol.GetByteStream());
        }

        protected void SendToClientAllFrame(int clientId)
        {
            byte[][] list = FrameList.ToArray();
            ServerLog.LogClient("中途加入 发送历史帧：" + list.Length, 3, clientId);
            foreach (var item in list)
            {
                SendToClient(clientId, item);
            }
        }

        protected void SendStepAll(object sender, ElapsedEventArgs e)
        {
            _durationTime += 100;
            if (_durationTime >= 20000)
            {
                //_battleEnd = true;
            }

            if (ClientPool.ActiveCount <= 0)
            {
                if (FrameList.Count > 0)
                {
                    ServerLog.LogServer("所有客户端退出游戏 战斗结束！！！", 1);
                    FrameList.Clear();
                }
                return;
            }

            if (FrameList.Count == 0)
            {
                ServerLog.LogServer("玩家进入服务器 战斗开始！！！", 1);
            }

            ServerLog.LogServer("0[" + FrameList.Count + "]", 1);

            byte[][] temp = StepMessage;
            int length = temp.Length;
            ProtocolBase protocol = new ByteProtocol();
            protocol.push((byte)MessageType.Frame);
            protocol.push((byte)length);
            //ServerLog.LogServer("获取[" + FrameList.Count + "]", 1);
            for (int i = 0; i < length; i++)
            {
                protocol.push(temp[i] != null);
                protocol.push(temp[i]);
            }

            if (FrameList.Count == 0)
            {
                protocol.push((byte)MessageType.RandomSeed);
                Random rand = new Random();
                protocol.push(rand.Next(10000));
            }

            if (_battleEnd == true)
            {
                protocol.push((byte)MessageType.BattleEnd);
            }

            protocol.push((byte)MessageType.end);
            ServerLog.LogServer("生成帧信息[" + length + "]", 1);
            byte[] temp2 = protocol.GetByteStream();

            FrameList.Add(temp2);

            ClientPool.Foreach((con) =>
            {
                SendToClient(con.clientId, temp2);
                if (!con.ActiveCheck())
                {
                    ServerLog.LogClient("客户端断线 中止连接：" + ClientPool[con.clientId].socket.LocalEndPoint + "ClientID:" + con.clientId, 0, con.clientId);
                    con.socket.Close();
                    ClientPool.Recover(con.clientId);
                }
            });

            if (_battleEnd == true)
            {
                ClientPool.Foreach((con) =>
                {
                    con.socket.Close();
                    ClientPool.Recover(con.clientId);
                });
            }

            ServerLog.LogServer("帧同步[" + FrameList.Count + "]", 2);
        }
    }

    public enum MessageType : byte
    {
        Init = 11,
        Frame = 12,
        ClientReady = 13,
        RandomSeed = 14,
        BattleEnd = 15,
        Ping = 16,
        end = 200,
    }
}
