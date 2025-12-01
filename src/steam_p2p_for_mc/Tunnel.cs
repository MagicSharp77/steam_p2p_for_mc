using Steamworks;
using System.Net;
using System.Net.Sockets;

namespace steam_p2p_for_mc
{
    public class Tunnel
    {
        // 单例模式 (方便全局调用)
        private static Tunnel? _instance;
        public static Tunnel Instance => _instance ??= new Tunnel();

        // 状态变量
        public bool IsRunning { get; private set; } = false;
        public string StatusInfo { get; private set; } = "就绪";
        
        // 网络相关变量
        private TcpListener? _tcpListener; // 客户端用：监听本地 MC
        private TcpClient? _tcpClient;     // 通用：维持 TCP 连接
        private NetworkStream? _tcpStream; // 通用：TCP 数据流
        private CSteamID _remoteSteamID;   // 对方的 Steam ID
        
        // 缓冲区 (4KB)
        private byte[] _buffer = new byte[4096];

        // --- 房主模式 (Host) ---
        // 连接本地 Minecraft 服务器 -> 等待 Steam 上的基友发数据过来
        public void StartHost(int localMcPort)
        {
            try
            {
                // 1. 尝试连接本地的 MC 服务器
                _tcpClient = new TcpClient();
                _tcpClient.Connect("127.0.0.1", localMcPort);
                _tcpStream = _tcpClient.GetStream();

                IsRunning = true;
                StatusInfo = $"[房主] 已连接本地 MC 端口 {localMcPort}，等待基友连接...";
                Console.WriteLine(StatusInfo);
            }
            catch (Exception e)
            {
                StatusInfo = $"❌ 无法连接本地 MC: {e.Message}";
                IsRunning = false;
            }
        }

        // --- 加入者模式 (Client) ---
        // 开启本地端口 -> 等待自己打开 MC 连接 -> 把数据发给 Steam 上的房主
        public void StartClient(CSteamID hostSteamID, int localListenPort = 25565)
        {
            _remoteSteamID = hostSteamID;
            try
            {
                // 1. 在本地开启一个端口骗 MC 来连
                _tcpListener = new TcpListener(IPAddress.Loopback, localListenPort);
                _tcpListener.Start();
                
                IsRunning = true;
                StatusInfo = $"[客户端] 正在监听 127.0.0.1:{localListenPort}，请打开 MC 连接此地址！";
                Console.WriteLine(StatusInfo);

                // 注意：这里我们还没真正建立 TCP 连接，要等 Update 里 Accept
            }
            catch (Exception e)
            {
                StatusInfo = $"❌ 端口监听失败: {e.Message}";
                IsRunning = false;
            }
        }

        // --- 核心循环 (每帧调用) ---
        // 这就是“泵”，负责把水(数据)从一头搬到另一头
        public void Update()
        {
            if (!IsRunning) return;

            // =========================================================
            //  A. 处理 TCP 连接建立 (仅限客户端模式)
            // =========================================================
            if (_tcpListener != null && _tcpListener.Pending())
            {
                // MC 游戏刚刚连上来了！
                _tcpClient = _tcpListener.AcceptTcpClient();
                _tcpStream = _tcpClient.GetStream();
                StatusInfo = "✅ Minecraft 已连接！隧道打通！";
                
                // 第一次握手：主动给房主发个空包，打通 P2P 链路
                byte[] hello = new byte[1] { 0 };
                SteamNetworking.SendP2PPacket(_remoteSteamID, hello, 1, EP2PSend.k_EP2PSendReliable);
            }

            // =========================================================
            //  B. 从 Steam 收信 -> 发给 TCP (MC)
            // =========================================================
            uint msgSize;
            // 循环读取所有积压的 Steam 包
            while (SteamNetworking.IsP2PPacketAvailable(out msgSize))
            {
                byte[] p2pBuffer = new byte[msgSize];
                uint bytesRead;
                CSteamID senderId;
                
                // 读包
                if (SteamNetworking.ReadP2PPacket(p2pBuffer, msgSize, out bytesRead, out senderId))
                {
                    // 如果我是房主，我要记下来谁在连我，以后发数据就发给他
                    if (_tcpListener == null && _remoteSteamID == CSteamID.Nil)
                    {
                        _remoteSteamID = senderId;
                        StatusInfo = $"✅ 基友 ({senderId}) 已连接！隧道打通！";
                        // 自动回传一个握手包，确保双向打通
                        SteamNetworking.AcceptP2PSessionWithUser(senderId);
                    }

                    // 把数据写入 TCP (只要 TCP 连着)
                    if (_tcpStream != null && _tcpStream.CanWrite)
                    {
                        _tcpStream.Write(p2pBuffer, 0, (int)bytesRead);
                    }
                }
            }

            // =========================================================
            //  C. 从 TCP (MC) 收信 -> 发给 Steam
            // =========================================================
            // 检查 TCP 有没有新数据发过来
            if (_tcpStream != null && _tcpStream.DataAvailable)
            {
                // 从 TCP 读出来
                int len = _tcpStream.Read(_buffer, 0, _buffer.Length);
                if (len > 0)
                {
                    // 塞进 Steam P2P 发出去
                    // k_EP2PSendReliable = 像 TCP 一样可靠传输 (重要！)
                    if (_remoteSteamID != CSteamID.Nil)
                    {
                        SteamNetworking.SendP2PPacket(_remoteSteamID, _buffer, (uint)len, EP2PSend.k_EP2PSendReliable);
                    }
                }
            }
        }

        // 停止并清理
        public void Stop()
        {
            IsRunning = false;
            _tcpListener?.Stop();
            _tcpClient?.Close();
            _tcpStream?.Close();
            _tcpListener = null;
            _tcpClient = null;
            _tcpStream = null;
            _remoteSteamID = CSteamID.Nil;
            StatusInfo = "已停止";
        }
    }
}