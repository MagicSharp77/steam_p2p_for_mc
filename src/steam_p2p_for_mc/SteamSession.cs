using Steamworks;
using System;

namespace steam_p2p_for_mc
{
    public class SteamSession
    {
        private static SteamSession? _instance;
        public static SteamSession Instance => _instance ??= new SteamSession();

        public CSteamID MySteamID { get; private set; }
        public string MyName { get; private set; } = "Unknown";
        public bool IsInitialized { get; private set; } = false;

        public void Init()
        {
            try
            {
                // 1. 初始化 SteamAPI
                if (!SteamAPI.Init())
                {
                    Console.WriteLine("SteamAPI.Init() failed! Is Steam running?");
                    return;
                }

                IsInitialized = true;

                // 2. 获取当前用户信息
                MySteamID = SteamUser.GetSteamID();
                MyName = SteamFriends.GetPersonaName();

                Console.WriteLine($"Steam Init Success! Logged in as: {MyName} ({MySteamID})");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error initializing Steam: {e.Message}");
            }
        }

        public void RunCallbacks()
        {
            // 必须每帧调用，处理 Steam 的网络消息和事件
            if (IsInitialized)
            {
                SteamAPI.RunCallbacks();
            }
        }

        public void Shutdown()
        {
            if (IsInitialized)
            {
                SteamAPI.Shutdown();
            }
        }
    }
}