﻿using ServerHub.Data;
using ServerHub.Hub;
using ServerHub.Misc;
using ServerHub.Rooms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Timers;

namespace ServerHub.Data
{
    public class StateObject
    {
        public Client client;
        public int offset;
        public byte[] buffer;

        public StateObject(int BufferSize)
        {
            buffer = new byte[BufferSize];
        }
    }

    public class Client
    {
        Stopwatch timeoutTimer;

        int inactivityKickTime = 45000;

        public event Action<Client> ClientDisconnected;
        public event Action<Client, uint, string> ClientJoinedRoom;
        public event Action<Client> ClientLeftRoom;

        public Socket socket;
        public PlayerInfo playerInfo;

        public uint joinedRoomID;

        public bool active;

        public Client(Socket tcp)
        {
            socket = tcp;
            socket.NoDelay = true;
        }

        public bool InitializeClient()
        {
            try
            {
                BasePacket packet = this.ReceiveData(true);

                if (packet.additionalData == null)
                    return false;

                if (packet.commandType == CommandType.Connect && packet.additionalData.Length > 0)
                {
                    uint version = BitConverter.ToUInt32(packet.additionalData, 0);
                    uint serverVersion = ((uint)Assembly.GetEntryAssembly().GetName().Version.Major).ConcatUInts((uint)Assembly.GetEntryAssembly().GetName().Version.Minor).ConcatUInts((uint)Assembly.GetEntryAssembly().GetName().Version.Build).ConcatUInts((uint)Assembly.GetEntryAssembly().GetName().Version.Revision);

                    if (CompareVersions(version, serverVersion))
                    {
                        KickClient("Plugin version mismatch:\nServer: " + serverVersion + "\nClient: " + version);
                        Logger.Instance.Log($"Client version v{version} tried to connect");
                        return false;
                    }

                    playerInfo = new PlayerInfo(packet.additionalData.Skip(4).ToArray());

                    if (Settings.Instance.Access.WhitelistEnabled)
                    {
                        if (!IsWhitelisted())
                        {
                            List<byte> buffer = new List<byte>();

                            byte[] reasonText = Encoding.UTF8.GetBytes("You are not whitelisted on this ServerHub!");
                            buffer.AddRange(BitConverter.GetBytes(reasonText.Length));
                            buffer.AddRange(reasonText);

                            this.SendData(new BasePacket(CommandType.Disconnect, buffer.ToArray()));
                            Logger.Instance.Warning($"Client {playerInfo.playerName}({playerInfo.playerId})@{((IPEndPoint)socket.RemoteEndPoint).Address} is not whitelisted!");
                            return false;
                        }
                    }

                    if (IsBlacklisted())
                    {
                        List<byte> buffer = new List<byte>();

                        byte[] reasonText = Encoding.UTF8.GetBytes("You are banned on this ServerHub!");
                        buffer.AddRange(BitConverter.GetBytes(reasonText.Length));
                        buffer.AddRange(reasonText);

                        this.SendData(new BasePacket(CommandType.Disconnect, buffer.ToArray()));
                        Logger.Instance.Warning($"Client {playerInfo.playerName}({playerInfo.playerId})@{((IPEndPoint)socket.RemoteEndPoint).Address} is blacklisted!");
                        return false;
                    }

                    return true;
                }
                else
                {
                    throw new Exception($"Wrong command received!");
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Warning($"Unable to initialize client! Exception: {e}");
                return false;
            }
        }

        public void ClientAccepted()
        {
            try
            {
                playerInfo.playerState = PlayerState.Lobby;
                this.SendData(new BasePacket(CommandType.Connect, new byte[0]));

                Logger.Instance.Log($"{playerInfo.playerName} connected!");

                timeoutTimer = new Stopwatch();
                HighResolutionTimer.LoopTimer.Elapsed += ClientLoop;

                active = true;

                StateObject state = new StateObject(4) { client = this };
                socket.BeginReceive(state.buffer, 0, 4, SocketFlags.None, ReceiveHeader, state);
            }catch(Exception e)
            {
                Logger.Instance.Warning($"Unable to initialize client! Exception: {e}");
                ClientDisconnected?.Invoke(this);
            }
        }

        private bool CompareVersions(uint clientVersion, uint serverVersion)
        {
            string client = clientVersion.ToString();
            string server = serverVersion.ToString();

            return (client.Substring(0, client.Length - 1)) != (server.Substring(0, server.Length - 1));
        }

        public bool IsBlacklisted()
        {
            return  Program.blacklistedIPs.Any(x => x.Contains(((IPEndPoint)socket.RemoteEndPoint).Address)) ||
                    Program.blacklistedIDs.Contains(playerInfo.playerId) ||
                    Program.blacklistedNames.Contains(playerInfo.playerName);
        }

        public bool IsWhitelisted()
        {
            return  Program.whitelistedIPs.Any(x => x.Contains(((IPEndPoint)socket.RemoteEndPoint).Address)) ||
                    Program.whitelistedIDs.Contains(playerInfo.playerId) ||
                    Program.whitelistedNames.Contains(playerInfo.playerName);
        }

        void ClientLoop(object sender, HighResolutionTimerElapsedEventArgs e)
        {

            if(socket == null || !socket.Connected)
            {
                DestroyClient();
            }

            if ((playerInfo.playerState == PlayerState.Room || playerInfo.playerState == PlayerState.Game))
            {
                if(!timeoutTimer.IsRunning)
                    timeoutTimer.Start();
            }
            else
            {
                timeoutTimer.Reset();
            }

            if (timeoutTimer.ElapsedMilliseconds > inactivityKickTime)
            {
                Logger.Instance.Log($"Kicked {playerInfo.playerName} for inactivity");
                KickClient();
            }
        }

        public void ReceiveHeader(IAsyncResult ar)
        {

            StateObject recState = (StateObject)ar.AsyncState;
            Socket client = recState.client.socket;

            try
            {
                int bytesRead = client.EndReceive(ar, out SocketError error);

                if (error != SocketError.Success)
                {
                    Logger.Instance.Warning("Socket error occurred! " + error);
                }

                if (bytesRead == recState.buffer.Length)
                {
                    Program.networkBytesInNow += recState.buffer.Length;

                    int bytesToReceive = BitConverter.ToInt32(recState.buffer, 0);

                    StateObject state = new StateObject(bytesToReceive) { client = this };
                    client.BeginReceive(state.buffer, 0, bytesToReceive, SocketFlags.None, new AsyncCallback(ReceiveCallback), state);
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Warning("Socket exception occurred! " + e);
                DestroyClient();
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            StateObject recState = (StateObject)ar.AsyncState;
            Socket client = recState.client.socket;

            try {
                int bytesRead = client.EndReceive(ar, out SocketError error);

                if (error != SocketError.Success)
                {
                    Logger.Instance.Warning("Socket error occurred! " + error);
                    DestroyClient();
                }

                if (bytesRead == recState.buffer.Length - recState.offset)
                {
                    Program.networkBytesInNow += recState.buffer.Length;

                    BasePacket packet = new BasePacket(recState.buffer);

#if DEBUG
                    if (packet.commandType != CommandType.UpdatePlayerInfo)
                        Logger.Instance.Log($"Received packet from client: type={packet.commandType}");
#endif

                    ProcessPacket(packet);

                    if (client.Connected && packet.commandType != CommandType.Disconnect)
                    {
                        StateObject state = new StateObject(4) { client = this };
                        client.BeginReceive(state.buffer, 0, 4, SocketFlags.None, new AsyncCallback(ReceiveHeader), state);
                    }
                }
                else if (bytesRead < recState.buffer.Length - recState.offset)
                {
                    recState.offset += bytesRead;
                    client.BeginReceive(recState.buffer, recState.offset, recState.buffer.Length - recState.offset, SocketFlags.None, new AsyncCallback(ReceiveCallback), recState);
                }
            }
            catch (ObjectDisposedException)
            {
                DestroyClient();
            }
            catch (Exception e)
            {
                Logger.Instance.Warning("Socket exception occurred! " + e);
                DestroyClient();
            }            
        }

        public void ProcessPacket(BasePacket packet)
        {
            switch (packet.commandType)
            {
                case CommandType.Connect:
                    {
                        if (packet.additionalData == null || packet.additionalData.Length == 0 || !packet.additionalData.Any(x => x != 0))
                        {
                            DestroyClient();
                        }
                    }
                    break;
                case CommandType.Disconnect:
                    {
                        DestroyClient();
                    }
                    break;
                case CommandType.UpdatePlayerInfo:
                    {
                        playerInfo = new PlayerInfo(packet.additionalData);
                        timeoutTimer.Restart();
                    }
                    break;
                case CommandType.JoinRoom:
                    {
                        uint roomId = BitConverter.ToUInt32(packet.additionalData, 0);
                        if (RoomsController.GetRoomInfosList().Any(x => x.roomId == roomId && x.usePassword))
                        {
                            ClientJoinedRoom?.Invoke(this, roomId, Encoding.UTF8.GetString(packet.additionalData, 8, BitConverter.ToInt32(packet.additionalData, 4)));
                        }
                        else
                        {
                            ClientJoinedRoom?.Invoke(this, roomId, "");
                        }
                    }
                    break;
                case CommandType.LeaveRoom:
                    {
                        ClientLeftRoom?.Invoke(this);
                        playerInfo.playerState = PlayerState.Lobby;
                    }
                    break;
                case CommandType.GetRooms:
                    {
                        this.SendData(new BasePacket(CommandType.GetRooms, RoomsController.GetRoomsListInBytes()));
                    }
                    break;
                case CommandType.CreateRoom:
                    {
                        uint roomId = RoomsController.CreateRoom(new RoomSettings(packet.additionalData), playerInfo);
                        this.SendData(new BasePacket(CommandType.CreateRoom, BitConverter.GetBytes(roomId)));
                    }
                    break;
                case CommandType.GetRoomInfo:
                    {
#if DEBUG
                        Logger.Instance.Log("GetRoomInfo: Client room=" + joinedRoomID+", IncludeSongList="+(packet.additionalData.Length == 1 && packet.additionalData[0] == 1));
#endif
                        if (joinedRoomID != 0)
                        {
                            Room joinedRoom = RoomsController.GetRoomsList().FirstOrDefault(x => x.roomId == joinedRoomID);
                            if (joinedRoom != null)
                            {
                                List<byte> buffer = new List<byte>();
                                if ((packet.additionalData.Length == 1 && packet.additionalData[0] == 1) || packet.additionalData.Length == 0)
                                {
                                    buffer.Add(1);
                                    buffer.AddRange(joinedRoom.GetSongInfos());
                                }
                                else
                                {
                                    buffer.Add(0);
                                }
                                buffer.AddRange(joinedRoom.GetRoomInfo().ToBytes());
                                this.SendData(new BasePacket(CommandType.GetRoomInfo, buffer.ToArray()));
                            }
                        }

                    }
                    break;
                case CommandType.SetSelectedSong:
                    {
                        if (joinedRoomID != 0)
                        {
                            Room joinedRoom = RoomsController.GetRoomsList().FirstOrDefault(x => x.roomId == joinedRoomID);
                            if (joinedRoom != null)
                            {
                                if (packet.additionalData == null || packet.additionalData.Length == 0)
                                {
                                    joinedRoom.SetSelectedSong(playerInfo, null);
                                }
                                else
                                {
                                    joinedRoom.SetSelectedSong(playerInfo, new SongInfo(packet.additionalData));
                                }
                            }
                        }
                    }
                    break;
                case CommandType.StartLevel:
                    {
#if DEBUG
                        Logger.Instance.Log("Received command StartLevel");
#endif

                        if (joinedRoomID != 0)
                        {
                            Room joinedRoom = RoomsController.GetRoomsList().FirstOrDefault(x => x.roomId == joinedRoomID);
                            if (joinedRoom != null)
                            {
                                byte difficulty = packet.additionalData[0];
                                SongInfo song = new SongInfo(packet.additionalData.Skip(1).ToArray());
                                song.songDuration += 2.5f;
                                joinedRoom.StartLevel(playerInfo, difficulty, song);
                            }
                        }
                    }
                    break;
                case CommandType.DestroyRoom:
                    {
                        if (joinedRoomID != 0)
                        {
                            Room joinedRoom = RoomsController.GetRoomsList().FirstOrDefault(x => x.roomId == joinedRoomID);
                            if (joinedRoom != null)
                            {
                                joinedRoom.DestroyRoom(playerInfo);
                            }
                        }
                    }
                    break;
                case CommandType.TransferHost:
                    {
                        if (joinedRoomID != 0)
                        {
                            Room joinedRoom = RoomsController.GetRoomsList().FirstOrDefault(x => x.roomId == joinedRoomID);
                            if (joinedRoom != null)
                            {
                                joinedRoom.TransferHost(playerInfo, new PlayerInfo(packet.additionalData));
                            }
                        }
                    }
                    break;
                case CommandType.PlayerReady:
                    {
                        if (joinedRoomID != 0)
                        {
                            Room joinedRoom = RoomsController.GetRoomsList().FirstOrDefault(x => x.roomId == joinedRoomID);
                            if (joinedRoom != null)
                            {
                                joinedRoom.ReadyStateChanged(playerInfo, (packet.additionalData[0] == 0) ? false : true);
                            }
                        }
                    }
                    break;
            }
        }

        public void DestroyClient()
        {
            if (active)
            {
                if (playerInfo != null)
                {
                    Logger.Instance.Log($"{playerInfo.playerName} disconnected!");
                }
                else
                {
                    Logger.Instance.Log($"Client disconnected!");
                }
                if (socket != null)
                {
                    socket.Close(1);
#if DEBUG
                    Logger.Instance.Warning("Closed client socket");
#endif
                }
                HighResolutionTimer.LoopTimer.Elapsed -= ClientLoop;
                active = false;
                ClientDisconnected?.Invoke(this);
            }
        }

        public void KickClient()
        {
            if (socket != null)
            {
                try
                {
                    this.SendData(new BasePacket(CommandType.Disconnect, new byte[0]));
                }
                catch (Exception)
                {
                }
            }
            DestroyClient();
        }

        public void KickClient(string reason)
        {
            if (socket != null)
            {
                try
                {
                    List<byte> buffer = new List<byte>();
                    byte[] stringBuf = Encoding.UTF8.GetBytes(reason);
                    buffer.AddRange(BitConverter.GetBytes(stringBuf.Length));
                    buffer.AddRange(stringBuf);
                    this.SendData(new BasePacket(CommandType.Disconnect, buffer.ToArray()));
                }
                catch (Exception)
                {
                }
            }
            DestroyClient();
        }
    }
}