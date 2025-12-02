using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using System.Numerics; // 用于 Vector3/Vector2
using Steamworks;

namespace steam_p2p_for_mc
{
    class Program
    {
        private static Sdl2Window? _window;
        private static GraphicsDevice? _gd;
        private static CommandList? _cl;
        private static ImGuiRenderer? _controller; // Veldrid.ImGui 提供的渲染器

        // UI 状态变量 (Vue 里的 data)
        private static string _targetSteamID = ""; 
        private static int _localPort = 25565;
        private static string _statusMessage = ""; // 用来存提示文字
        private static Vector4 _statusColor = new Vector4(1, 1, 1, 1); // 文字颜色 (默认白)
        private static bool _isConnected = false;  // 是否已连接

        static void Main(string[] args)
        {
            // --- 1. 初始化 Steam ---
            SteamSession.Instance.Init();

            // --- 2. 创建窗口 ---
            VeldridStartup.CreateWindowAndGraphicsDevice(
                new WindowCreateInfo(50, 50, 800, 600, WindowState.Normal, "My Steam Tunnel (Linux/Win)"),
                out _window,
                out _gd);

            // --- 3. 初始化 ImGui ---
            _controller = new ImGuiRenderer(_gd, _gd.MainSwapchain.Framebuffer.OutputDescription, _window.Width, _window.Height);
            
            // 👇👇👇 修正后的字体加载逻辑 👇👇👇
            try 
            {
                var io = ImGui.GetIO(); // 先获取 io 对象

                // 1. 定义字体路径 (优先用 Noto，如果没有则降级)
                string fontPath = "NotoSansCJK-Bold.ttc";
                Console.WriteLine($"正在尝试加载字体: {fontPath}"); // 打印出来看看路径对不对
                if (System.IO.File.Exists(fontPath))
                {
                    io.Fonts.Clear();
                    io.Fonts.AddFontFromFileTTF(fontPath, 20.0f, null, io.Fonts.GetGlyphRangesChineseSimplifiedCommon());
                    _controller.RecreateFontDeviceTexture(_gd); 
                    Console.WriteLine("✅ 中文字体加载成功！");
                }
                else
                {
                    Console.WriteLine("❌ 找不到任何中文字体文件，将显示乱码。");
                }
            }
            catch (Exception e) 
            {
                Console.WriteLine("💥 字体加载炸了: " + e.Message);
            }
            // 👆👆👆 修正结束 👆👆👆

            _cl = _gd.ResourceFactory.CreateCommandList();

            while (_window.Exists)
            {
                InputSnapshot snapshot = _window.PumpEvents();
                if (!_window.Exists) break;

                SteamSession.Instance.RunCallbacks();
                
                // 暂时注释掉 Tunnel 直到你创建了那个类
                // Tunnel.Instance.Update(); 

                _controller.Update(1f / 60f, snapshot); 
                SubmitUI();

                _cl.Begin();
                _cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
                _cl.ClearColorTarget(0, RgbaFloat.Black);
                _controller.Render(_gd, _cl);
                _cl.End();
                _gd.SubmitCommands(_cl);
                _gd.SwapBuffers();
            }

            _controller.Dispose();
            _cl.Dispose();
            _gd.Dispose();
            SteamSession.Instance.Shutdown();
        }

        // --- 你的前端代码主要写在这里 ---
        private static void SubmitUI()
        {
            // 设置整个窗口填满
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(_window.Width, _window.Height));
            
            // 开始绘制 ImGui 窗口
            ImGui.Begin("Main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize);

            // 显示用户信息
            if (SteamSession.Instance.IsInitialized)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Steam Status: Online");
                ImGui.Text($"User: {SteamSession.Instance.MyName}");
                ImGui.Text($"ID: {SteamSession.Instance.MySteamID}");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Steam Status: Offline (Check steam_appid.txt)");
            }

            ImGui.Separator();

            // 两个 Tab：主机模式 / 客户模式
            if (ImGui.BeginTabBar("ModeTabs"))
            {
                if (ImGui.BeginTabItem("I am Host"))
                {
                    ImGui.Text("Host a local Minecraft server to Steam friends.");
                    ImGui.InputInt("Local Port (MC Port)", ref _localPort);
                    
                    if (ImGui.Button("Start Hosting"))
                    {
                        Console.WriteLine($"Starting Host on port {_localPort}...");
                        // TODO: 这里将来调用 Tunnel.StartHost(_localPort)
                        // 停止旧的（如果有）
                        Tunnel.Instance.Stop();
                        // 启动新的
                        Tunnel.Instance.StartHost(_localPort);
                        // 更新状态信息
                        _statusMessage = Tunnel.Instance.StatusInfo;
                    }
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), Tunnel.Instance.StatusInfo);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("I am Client"))
                {
                    ImGui.Text("Connect to a friend's Steam Tunnel.");
                    ImGui.InputText("Friend's SteamID", ref _targetSteamID, 100);
                    if (ImGui.Button("Connect"))
                    {
                        if (string.IsNullOrEmpty(_targetSteamID))
                        {
                            _statusMessage = "❌SteamID!";
                            _statusColor = new Vector4(1, 0, 0, 1); // 红色
                        }
                        else
                        {
                            _statusMessage = "⏳ try to Connect...";
                            _statusColor = new Vector4(1, 1, 0, 1); // 黄色

                            ulong id;
                            // 尝试把字符串解析成 ulong，再转成 CSteamID
                            if (ulong.TryParse(_targetSteamID, out id))
                            {
                                Tunnel.Instance.Stop();
                                // 这里我们固定让客户端监听本地 25565，方便玩家直连
                                Tunnel.Instance.StartClient(new CSteamID(id), 25565);
                                _statusMessage = "⏳ starting listener...";
                                _isConnected = true;
                                _statusMessage = $"✅ connected!\nplease connect MC : 127.0.0.1:25565";
                                _statusColor = new Vector4(0, 1, 0, 1); // 绿色
                            }
                            else
                            {
                                _statusMessage = "❌ SteamID 格式不对！应该是纯数字";
                            }
                        }
                    }
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), Tunnel.Instance.StatusInfo);
                    if (Tunnel.Instance.IsRunning)
                    {
                        ImGui.Text("please connect MC : 127.0.0.1:yourOpenPort");
                        if (ImGui.Button("copy address"))
                        {
                            ImGui.SetClipboardText("127.0.0.1:yourOpenPort");
                        }
                    }
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.End();
        }
    }
}