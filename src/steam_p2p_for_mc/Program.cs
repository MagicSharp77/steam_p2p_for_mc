using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using System.Numerics;
using Steamworks;
using System.Runtime.InteropServices;

namespace steam_p2p_for_mc
{
    class Program
    {
        private static Sdl2Window? _window;
        private static GraphicsDevice? _gd;
        private static CommandList? _cl;
        private static ImGuiRenderer? _controller;

        // 剪贴板委托
        private delegate void SetClipboardTextFn(IntPtr userData, string text);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetClipboardTextFn(IntPtr userData);

        private static SetClipboardTextFn? _setClipboardDelegate;
        private static GetClipboardTextFn? _getClipboardDelegate;

        // UI 变量
        private static string _targetSteamID = ""; 
        private static int _localPort = 25565;
        private static string _statusMessage = "";
        private static Vector4 _statusColor = new Vector4(1, 1, 1, 1);
        private static bool _isConnected = false;
        
        // 👇👇👇 1. 新增：记录 Steam 是否初始化成功 👇👇👇
        private static bool _isSteamInitialized = false; 

        static void Main(string[] args)
        {
            // --- 1. 初始化 Steam ---
            try {
                // 👇👇👇 2. 修改：获取 Init 的返回值 👇👇👇
                _isSteamInitialized = SteamAPI.Init();
                if (!_isSteamInitialized) 
                {
                    Console.WriteLine("SteamAPI.Init failed! (Is Steam running?)");
                }
            } catch (Exception ex) { 
                Console.WriteLine("Steam Init Error: " + ex.Message); 
            }

            // --- 2. 创建窗口 ---
            VeldridStartup.CreateWindowAndGraphicsDevice(
                new WindowCreateInfo(50, 50, 800, 600, WindowState.Normal, "My Steam Tunnel"),
                out _window,
                out _gd);

            // --- 3. 初始化 ImGui 渲染器 ---
            _controller = new ImGuiRenderer(_gd, _gd.MainSwapchain.Framebuffer.OutputDescription, _window.Width, _window.Height);
            
            // --- 初始化配置 (只运行一次) ---
            try 
            {
                var io = ImGui.GetIO();
                
                // 键位映射
                io.KeyMap[(int)ImGuiKey.Backspace] = (int)Key.BackSpace;
                io.KeyMap[(int)ImGuiKey.Enter] = (int)Key.Enter;
                io.KeyMap[(int)ImGuiKey.Delete] = (int)Key.Delete;
                io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Key.Left;
                io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Key.Right;
                io.KeyMap[(int)ImGuiKey.Home] = (int)Key.Home;
                io.KeyMap[(int)ImGuiKey.End] = (int)Key.End;
                io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Key.Up;
                io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Key.Down;
                io.KeyMap[(int)ImGuiKey.Tab] = (int)Key.Tab;
                io.KeyMap[(int)ImGuiKey.Escape] = (int)Key.Escape;
                io.KeyMap[(int)ImGuiKey.Space] = (int)Key.Space;
                io.KeyMap[(int)ImGuiKey.A] = (int)Key.A;
                io.KeyMap[(int)ImGuiKey.C] = (int)Key.C;
                io.KeyMap[(int)ImGuiKey.V] = (int)Key.V;
                io.KeyMap[(int)ImGuiKey.X] = (int)Key.X;
                io.KeyMap[(int)ImGuiKey.Y] = (int)Key.Y;
                io.KeyMap[(int)ImGuiKey.Z] = (int)Key.Z;

                io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

                // 暂时禁用剪贴板
                _setClipboardDelegate = (userData, text) => { };
                _getClipboardDelegate = (userData) => { return IntPtr.Zero; };
                io.SetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_setClipboardDelegate);
                io.GetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_getClipboardDelegate);

                // 字体加载
                string fontPath = "NotoSansCJK-Bold.ttc";
                if (System.IO.File.Exists(fontPath))
                {
                    io.Fonts.Clear();
                    io.Fonts.AddFontFromFileTTF(fontPath, 20.0f, null, io.Fonts.GetGlyphRangesChineseSimplifiedCommon());
                    _controller.RecreateFontDeviceTexture(_gd); 
                    Console.WriteLine("✅ 字体加载成功");
                }
            }
            catch (Exception e) 
            {
                Console.WriteLine("配置异常: " + e.Message);
            }

            _cl = _gd.ResourceFactory.CreateCommandList();

            // --- 主循环 ---
            while (_window.Exists)
            {
                InputSnapshot snapshot = _window.PumpEvents(); 
                if (!_window.Exists) break;

                // 必须每帧调用，处理 Steam 回调
                if (_isSteamInitialized) {
                    SteamAPI.RunCallbacks();
                }

                Tunnel.Instance.Update(); // 待开启

                // 处理输入
                var io = ImGui.GetIO();
                foreach (var keyEvent in snapshot.KeyEvents)
                {
                    io.KeysDown[(int)keyEvent.Key] = keyEvent.Down;
                    if (keyEvent.Key == Key.ShiftLeft || keyEvent.Key == Key.ShiftRight) io.KeyShift = keyEvent.Down;
                    if (keyEvent.Key == Key.ControlLeft || keyEvent.Key == Key.ControlRight) io.KeyCtrl = keyEvent.Down;
                    if (keyEvent.Key == Key.AltLeft || keyEvent.Key == Key.AltRight) io.KeyAlt = keyEvent.Down;
                }
                foreach (var ch in snapshot.KeyCharPresses)
                {
                    io.AddInputCharacter(ch);
                }

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

            // 清理资源
            _controller?.Dispose();
            _cl.Dispose();
            _gd.Dispose();
            
            if (_isSteamInitialized) {
                SteamAPI.Shutdown();
            }
        }

        private static void SubmitUI()
        {
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(_window!.Width, _window.Height)); // 加了感叹号消除警告
            ImGui.Begin("Main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize);

            string name = "Wait Login...";
            string steamId = "---";
            
            // 👇👇👇 3. 修改：使用我们自己的变量判断 👇👇👇
            if (_isSteamInitialized) 
            {
                try {
                    name = SteamFriends.GetPersonaName();
                    steamId = SteamUser.GetSteamID().ToString();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "Steam Status: Online");
                } catch {
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), "Steam Error");
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Steam Status: Offline");
            }

            ImGui.Text($"User: {name}");
            ImGui.Text($"ID: {steamId}");
            ImGui.Separator();

            if (ImGui.BeginTabBar("ModeTabs"))
            {
                if (ImGui.BeginTabItem("I am Host"))
                {
                    ImGui.InputInt("Local Port", ref _localPort);
                    if (ImGui.Button("Start Hosting"))
                    {
                         Tunnel.Instance.StartHost(_localPort);
                         _statusMessage = $"Starting Host on {_localPort}...";
                    }
                    ImGui.TextColored(_statusColor, _statusMessage);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("I am Client"))
                {
                    ImGui.InputText("Friend's SteamID", ref _targetSteamID, 100);
                    if (ImGui.Button("Connect"))
                    {
                        if (ulong.TryParse(_targetSteamID, out ulong id))
                        {
                            Tunnel.Instance.StartClient(new CSteamID(id), 25565);
                            _statusMessage = $"Connecting to {id}...";
                        }
                    }
                    ImGui.TextColored(_statusColor, _statusMessage);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            ImGui.End();
        }
    }
}