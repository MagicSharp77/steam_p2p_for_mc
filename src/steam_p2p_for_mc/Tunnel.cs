using Steamworks;
using System.Net;
using System.Net.Sockets;

namespace steam_p2p_for_mc
{
    public class Tunnel
    {
        // 单例模式
        private static Tunnel? _instance;
        public static Tunnel Instance => _instance ??= new Tunnel();

        // 状态变量
        public bool IsRunning { get; private set; } = false;
        public string StatusInfo { get; private set; } = "Ready";
        
        // 网络相关变量
        private TcpListener? _tcpListener; // 客户端模式：监听本地端口等待 MC 连接
        private TcpClient? _tcpClient;     // 通用：维持 TCP 连接
        private NetworkStream? _tcpStream; // 通用：TCP 数据流
        private CSteamID _remoteSteamID = CSteamID.Nil;   // 对方的 Steam ID
        
        // 缓冲区
        private byte[] _buffer = new byte[4096];

        // 回调：用来自动接受连接请求
        private Callback<P2PSessionRequest_t>? _p2pSessionRequestCallback;

        private Tunnel() 
        {
            // 注册 Steam P2P 连接请求回调
            // 当有人试图连接你时，Steam 会触发这个，我们需要说 "Accept"
            _p2pSessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
        }

        // --- 房主模式 (Host) ---
        public void StartHost(int localMcPort)
        {
            Stop(); // 先重置
            try
            {
                // 1. 尝试连接本地的 MC 服务器
                // 注意：你必须先启动 Minecraft 服务器，再点这个按钮，否则会报错
                _tcpClient = new TcpClient();
                _tcpClient.Connect("127.0.0.1", localMcPort);
                _tcpStream = _tcpClient.GetStream();

                IsRunning = true;
                _remoteSteamID = CSteamID.Nil; // 等待有人连进来
                StatusInfo = $"[HOST] Connected to MC Local ({localMcPort}). Waiting for Client...";
                Console.WriteLine(StatusInfo);
            }
            catch (Exception e)
            {
                StatusInfo = $"❌ MC Connection Failed: {e.Message}";
                IsRunning = false;
            }
        }

        // --- 客户端模式 (Client) ---
        public void StartClient(CSteamID hostSteamID, int localListenPort = 25565)
        {
            Stop(); // 先重置
            _remoteSteamID = hostSteamID;
            try
            {
                // 1. 在本地开启一个端口骗 MC 来连
                _tcpListener = new TcpListener(IPAddress.Loopback, localListenPort);
                _tcpListener.Start();
                
                IsRunning = true;
                StatusInfo = $"[CLIENT] Listening on 127.0.0.1:{localListenPort}. Connect via MC!";
                Console.WriteLine(StatusInfo);
            }
            catch (Exception e)
            {
                StatusInfo = $"❌ Port Listen Failed: {e.Message}";
                IsRunning = false;
            }
        }

        // --- 核心循环 (需在 Program.cs 每帧调用) ---
        public void Update()
        {
            // 如果没运行，就不处理
            if (!IsRunning) return;

            // =========================================================
            //  A. 客户端模式：处理 MC 游戏的连接
            // =========================================================
            if (_tcpListener != null && _tcpListener.Pending())
            {
                try 
                {
                    _tcpClient = _tcpListener.AcceptTcpClient();
                    _tcpStream = _tcpClient.GetStream();
                    StatusInfo = "✅ Minecraft Connected! Tunnel Active.";
                    
                    // 第一次握手：主动给房主发个空包，打通 P2P 链路
                    byte[] hello = new byte[1] { 0 };
                    SteamNetworking.SendP2PPacket(_remoteSteamID, hello, 1, EP2PSend.k_EP2PSendReliable);
                    Console.WriteLine("Sent Handshake to Host.");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Accept Client Error: " + e.Message);
                }
            }

            // =========================================================
            //  B. 读取 Steam 发来的数据 -> 写入 TCP
            // =========================================================
            uint msgSize;
            while (SteamNetworking.IsP2PPacketAvailable(out msgSize))
            {
                byte[] p2pBuffer = new byte[msgSize];
                uint bytesRead;
                CSteamID senderId;
                
                if (SteamNetworking.ReadP2PPacket(p2pBuffer, msgSize, out bytesRead, out senderId))
                {
                    // 如果我是房主，且还不知道谁在连我，就认定第一个发包的人是基友
                    if (_tcpListener == null && _remoteSteamID == CSteamID.Nil)
                    {
                        _remoteSteamID = senderId;
                        StatusInfo = $"✅ Friend ({senderId}) Connected!";
                        // 强制接受会话
                        SteamNetworking.AcceptP2PSessionWithUser(senderId);
                    }

                    // 写入 TCP (只要 TCP 连着)
                    if (_tcpStream != null && _tcpStream.CanWrite)
                    {
                        _tcpStream.Write(p2pBuffer, 0, (int)bytesRead);
                    }
                }
            }

            // =========================================================
            //  C. 读取 TCP 数据 -> 发给 Steam
            // =========================================================
            try 
            {
                if (_tcpStream != null && _tcpStream.DataAvailable)
                {
                    int len = _tcpStream.Read(_buffer, 0, _buffer.Length);
                    if (len > 0 && _remoteSteamID != CSteamID.Nil)
                    {
                        // 发送给对方
                        SteamNetworking.SendP2PPacket(_remoteSteamID, _buffer, (uint)len, EP2PSend.k_EP2PSendReliable);
                    }
                }
            }
            catch (Exception e)
            {
                StatusInfo = "TCP Error: " + e.Message;
                Stop();
            }
        }

        // --- 自动接受连接回调 ---
        private void OnP2PSessionRequest(P2PSessionRequest_t pCallback)
        {
            CSteamID remoteID = pCallback.m_steamIDRemote;
            Console.WriteLine($"[Steam] Incoming P2P request from: {remoteID}");

            // 出于安全考虑，你也可以在这里检查 remoteID 是不是你的好友
            // 这里为了方便，直接允许所有连接
            SteamNetworking.AcceptP2PSessionWithUser(remoteID);
        }

        public void Stop()
        {
            IsRunning = false;
            
            // 关闭连接
            if (_remoteSteamID != CSteamID.Nil)
            {
                SteamNetworking.CloseP2PSessionWithUser(_remoteSteamID);
            }

            _tcpListener?.Stop();
            _tcpClient?.Close();
            _tcpStream?.Close();
            
            _tcpListener = null;
            _tcpClient = null;
            _tcpStream = null;
            _remoteSteamID = CSteamID.Nil;
            StatusInfo = "Stopped";
        }
    }
}