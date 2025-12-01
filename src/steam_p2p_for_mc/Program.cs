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

            // --- 2. 创建窗口 (Veldrid) ---
            VeldridStartup.CreateWindowAndGraphicsDevice(
                new WindowCreateInfo(50, 50, 800, 600, WindowState.Normal, "My Steam Tunnel (Linux/Win)"),
                out _window,
                out _gd);

            // --- 3. 初始化 ImGui 渲染器 ---
            // 注意：这里需要 Veldrid.ImGui 包。如果你报错找不到 ImGuiRenderer，
            // 确保执行了: dotnet add package Veldrid.ImGui
            _controller = new ImGuiRenderer(_gd, _gd.MainSwapchain.Framebuffer.OutputDescription, _window.Width, _window.Height);
            try 
            {
                var io = ImGui.GetIO();
                // 注意：有些版本的 ImGui 加载 TTC 需要指定 index (第三个参数不是 null 而是配置)
                // 但通常改个扩展名 ImGui 也能认。如果崩了，换一个纯 .ttf 的字体文件（比如 wqy-microhei）最稳。
                io.Fonts.AddFontFromFileTTF("font.ttf", 20.0f, null, io.Fonts.GetGlyphRangesChineseFull());
                // 这一步很重要：告诉控制器字体变了，需要重建字体纹理
                _controller.RecreateFontDeviceTexture(); 
            }
            catch (Exception e) 
            {
                Console.WriteLine("字体加载失败，将使用默认字体: " + e.Message);
            }
            _cl = _gd.ResourceFactory.CreateCommandList();

            // --- 4. 主循环 (Game Loop) ---
            while (_window.Exists)
            {
                InputSnapshot snapshot = _window.PumpEvents();
                if (!_window.Exists) break;

                // A. 更新 Steam 回调 (非常重要，不写这个没法联网)
                SteamSession.Instance.RunCallbacks();

                // TODO: 这里将来调用 Tunnel.Update()
                Tunnel.Instance.Update();

                // B. 更新 UI
                _controller.Update(1f / 60f, snapshot); // 假定 60fps

                // C. 绘制你的界面
                SubmitUI();

                // D. 渲染上屏
                _cl.Begin();
                _cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
                _cl.ClearColorTarget(0, RgbaFloat.Black); // 背景设为黑色
                _controller.Render(_gd, _cl);
                _cl.End();
                _gd.SubmitCommands(_cl);
                _gd.SwapBuffers();
            }

            // --- 5. 清理资源 ---
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
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Steam Status: Online");
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