using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace IDG.FightServer
{
    public class FightServerManager : DataHttpServer
    {
        private Dictionary<string, string> users = new Dictionary<string, string>();
        private Dictionary<string, Character> clothes = new Dictionary<string, Character>();

        public string ip;
        public int port;
        public List<FightServer> runingServers;
        public List<FightServer> waitServers;

        public override void Start(string ipPort)
        {
            base.Start(ipPort);
            var strs = ipPort.Split(':');
            ip = strs[0];
            port = int.Parse(strs[1]) + 100;
            Console.WriteLine(ip + ":" + port);
            waitServers = new List<FightServer>();
            runingServers = new List<FightServer>();
            users["123456"] = "e10adc3949ba59abbe56e057f20f883e";
            users["1234567"] = "fcea920f7412b5da7be0cf42b8c93759";
        }

        public FightServer GetWaitServer()
        {
            FightServer server = null;
            if (waitServers.Count > 0)
            {
                server = waitServers[0];
            }
            else
            {
                server = new FightServer();
                server.StartServer(ip, port++, 10);
                waitServers.Add(server);
            }
            return server;
        }

        public FightServer GetServerByUserName(string username)
        {
            foreach (var item in waitServers)
            {
                if (item.fightRoom.InRoom(username))
                {
                    return item;
                }
            }
            return null;
        }

        public override KeyValueProtocol ParseReceive(KeyValueProtocol receive)
        {
            var send = new KeyValueProtocol();
            Console.WriteLine("接收 " + receive.GetString());
            string cmd = receive["cmd"];
            switch (cmd)
            {
                //case "startGame":
                //    var server = GetServer();
                //    server.fightRoom = JsonConvert.DeserializeObject<FightRoom>(receive["fightRoom"]);
                //    send["ip"] = server.ip;
                //    send["port"] = server.port;

                //    send["status"] = "成功";
                //    break;
                case "register":
                    string username = receive["username"];
                    string pwd = receive["password"];
                    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(pwd))
                    {
                        send["status"] = "用户名或者密码为空";
                    }
                    else
                    {
                        if (users.ContainsKey(username))
                        {
                            send["status"] = "用户名已被注册";
                        }
                        else
                        {
                            users[username] = pwd;
                            send["status"] = "成功";
                        }
                    }
                    break;
                case "login":
                    HandleLogin(receive, send);
                    break;
                case "loginByToken":
                    HandleLoginByTokin(receive, send);
                    break;
                case "createCharacter":
                    HandleCreateCharacter(receive, send);
                    break;
                case "getCharacter":
                    HandleGetCharacter(receive, send);
                    break;
                case "matching":
                    HandleMatchFight(receive, send);
                    break;
                case "getFightRoom":
                    HandleGetFightRoom(receive, send);
                    break;
                case "switchReady":
                    HandleReadFight(receive, send);
                    break;
                default:
                    Console.WriteLine("not handle command : " + cmd);
                    break;
            }
            return send;
        }

        private bool CheckToken(KeyValueProtocol receive)
        {
            string username = receive["username"];
            string loginToken = receive["loginToken"];
            string outToken;
            if (users.TryGetValue(username, out outToken))
            {
                if (outToken.Equals(loginToken))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        private void HandleLogin(KeyValueProtocol receive, KeyValueProtocol send)
        {
            string username = receive["username"];
            string pwd = receive["password"];
            if (users.ContainsKey(username) && users[username] == pwd)
            {
                send["username"] = username;
                send["loginToken"] = pwd;
                send["status"] = "成功";
            }
            else
            {
                send["status"] = "用户不存在";
            }
        }

        private void HandleLoginByTokin(KeyValueProtocol receive, KeyValueProtocol send)
        {
            string username = receive["username"];
            string loginToken = receive["loginToken"];
            if (users.ContainsKey(username) && users[username] == loginToken)
            {
                send["username"] = username;
                send["loginToken"] = loginToken;
                send["status"] = "成功";
            }
            else
            {
                send["status"] = "用户不存在";
            }
        }

        private void HandleCreateCharacter(KeyValueProtocol receive, KeyValueProtocol send)
        {
            if (CheckToken(receive))
            {
                string username = receive["username"];
                if (clothes.ContainsKey(username))
                {
                    send["status"] = "创建失败，角色已存在";
                }
                else
                {
                    Character character = new Character();
                    character.Id = users.Count;
                    KeyValueProtocol cloth = new KeyValueProtocol();
                    cloth["Top"] = "1";
                    cloth["Bottom"] = "1";
                    cloth["Shoes"] = "1";
                    character.info = cloth.GetString();

                    clothes[username] = character;

                    send["status"] = "成功";
                }
            }
            else
            {
                send["status"] = "验证失败";
            }
        }

        private void HandleGetCharacter(KeyValueProtocol receive, KeyValueProtocol send)
        {
            if (CheckToken(receive))
            {
                string usename = receive["username"];

                if (clothes.ContainsKey(usename))
                {
                    send["characters_count"] = "1";
                    send["character_info0"] = clothes[usename].info;
                    send["character_id0"] = clothes[usename].Id.ToString();
                    send["status"] = "成功";
                }
                else
                {
                    send["status"] = "验证失败";
                }
            }
            else
            {
                send["status"] = "验证失败";
            }
        }

        private void HandleMatchFight(KeyValueProtocol receive, KeyValueProtocol send)
        {
            if (CheckToken(receive))
            {
                string username = receive["username"];
                if (!clothes.ContainsKey(username))
                {
                    send["status"] = "未创建角色";
                }
                else
                {
                    FightServer server = GetWaitServer();
                    if (server.fightRoom == null)
                    {
                        server.fightRoom = new FightRoom();
                        server.fightRoom.ip = server.ip;
                        server.fightRoom.port = server.port;
                    }
                    PlayerInfo pi = new PlayerInfo();
                    pi.isReady = false;
                    pi.username = username;
                    pi.character = clothes[username];
                    server.fightRoom.AddPlayer(pi);
                    if (server.fightRoom.playerInfos.Count == 2)
                    {
                        server.fightRoom.isFull = "true";
                    }
                    send["status"] = "成功";
                }
            }
            else
            {
                send["status"] = "匹配失败";
            }
        }

        private void HandleReadFight(KeyValueProtocol receive, KeyValueProtocol send)
        {
            if (CheckToken(receive))
            {
                string username = receive["username"];
                FightServer server = GetServerByUserName(username);
                for (int i = 0; i < server.fightRoom.playerInfos.Count; i++)
                {
                    server.fightRoom.playerInfos[i].isReady = true;
                }
                send["status"] = "成功";
            }
            else
            {
                send["status"] = "准备失败";
            }
        }

        private void HandleGetFightRoom(KeyValueProtocol receive, KeyValueProtocol send)
        {
            if (CheckToken(receive))
            {
                string username = receive["username"];
                FightServer server = GetServerByUserName(username);
                if (server != null)
                {
                    send["fightRoom"] = JsonConvert.SerializeObject(server.fightRoom);
                    send["status"] = "成功";
                }
                else
                {
                    send["status"] = "准备失败";
                }
            }
            else
            {
                send["status"] = "准备失败";
            }
        }
    }
}
