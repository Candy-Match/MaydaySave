using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

[assembly: MelonInfo(typeof(MaydaySave.MaydaySaveMod), "Mayday Save", "5.2.1", "MaydaySave")]
[assembly: MelonGame(null, null)]

namespace MaydaySave
{
    /// <summary>配置项薄封装（MelonLoader 版），与 BepInEx 版同签名。</summary>
    internal class ConfigEntry<T>
    {
        private readonly MelonPreferences_Entry<T> inner;

        internal ConfigEntry(MelonPreferences_Entry<T> inner) { this.inner = inner; }

        public T Value
        {
            get { return inner.Value; }
            set { inner.Value = value; }
        }

        public string Key { get { return inner.Identifier; } }

        public void Save()
        {
            try { MelonPreferences.Save(); } catch { }
        }
    }

    /// <summary>日志薄封装，统一两套加载器的输出。</summary>
    internal class Logger
    {
        public void LogInfo(object m) { try { MelonLogger.Msg(m); } catch { } }
        public void LogWarning(object m) { try { MelonLogger.Warning(m); } catch { } }
        public void LogError(object m) { try { MelonLogger.Error(m); } catch { } }
        public void LogDebug(object m) { try { MelonLogger.Msg(m); } catch { } }
    }

    public static class Plugin
    {
        internal static Logger Logs;
        internal static string Root;
        internal static string SaveDir;
        internal static ConfigEntry<bool> CfgNotify;
        internal static ConfigEntry<bool> CfgBagFallback;
        internal static ConfigEntry<bool> CfgBagFreeMode;
        internal static ConfigEntry<bool> CfgPartialBags;
        internal static ConfigEntry<int> CfgKeyCode;
        internal const string VersionText = "5.2.1";
        internal const string ConfigPathText = @"UserData\MelonPreferences.cfg";
        private static bool notifyBroken;

        internal static void Init()
        {
            Logs = new Logger();

            string userData = null;
            try { userData = MelonEnvironment.UserDataDirectory; } catch { }
            if (string.IsNullOrEmpty(userData))
            {
                string gameDir = null;
                try { gameDir = MelonEnvironment.GameRootDirectory; } catch { }
                userData = Path.Combine(gameDir ?? ".", "UserData");
            }

            Root = Path.Combine(userData, "MaydaySave");
            SaveDir = Path.Combine(Root, "saves");
            Directory.CreateDirectory(SaveDir);
            MigrateLegacySaves();

            var cat = MelonPreferences.CreateCategory("MaydaySave");
            CfgNotify = new ConfigEntry<bool>(cat.CreateEntry("Notify", true,
                "存读档时在游戏内弹出提示。"));
            CfgBagFallback = new ConfigEntry<bool>(cat.CreateEntry("BagFallback", true,
                "读档时若行李不足，补发可交互的行李（会占用行李总数，可关掉）"));
            CfgBagFreeMode = new ConfigEntry<bool>(cat.CreateEntry("BagFreeMode", true,
                "读档时清空全部行李并把乘客设为无行李，跳过行李搬运环节"));
            CfgPartialBags = new ConfigEntry<bool>(cat.CreateEntry("PartialBags", false,
                "BagFreeMode 开启时，随机挑几位乘客保留行李需求，并把行李发到玩家脚边"));
            CfgKeyCode = new ConfigEntry<int>(cat.CreateEntry("KeyCode", -1,
                "-1 = F8；其它值为 UnityEngine.InputSystem.Key 的整数（可在设置界面里改，不用手填）"));

            try { MelonPreferences.Save(); } catch { }
            Logs.LogInfo($"MaydaySave v{VersionText} (MelonLoader) loaded. data={Root}");
        }

        /// <summary>把旧 BepInEx 版的存档复制过来，避免换加载器后存档"消失"。</summary>
        private static void MigrateLegacySaves()
        {
            try
            {
                if (Directory.GetFiles(SaveDir, "slot*.txt").Length > 0) return;
                string gameDir = null;
                try { gameDir = MelonEnvironment.GameRootDirectory; } catch { }
                if (string.IsNullOrEmpty(gameDir)) return;
                string old = Path.Combine(gameDir, "BepInEx", "MaydaySave", "saves");
                if (!Directory.Exists(old)) return;
                int n = 0;
                foreach (var f in Directory.GetFiles(old, "slot*.txt"))
                {
                    try { File.Copy(f, Path.Combine(SaveDir, Path.GetFileName(f)), false); n++; } catch { }
                }
                if (n > 0) MelonLogger.Msg($"[MaydaySave] migrated {n} save(s) from BepInEx folder");
            }
            catch { }
        }

        internal static void Notify(string text, bool bad = false)
        {
            if (CfgNotify == null || !CfgNotify.Value) return;
            try
            {
                Mayday.Notify.Raise(text, bad ? Mayday.NotifyKind.Warning : Mayday.NotifyKind.Good);
            }
            catch (Exception e)
            {
                if (!notifyBroken)
                {
                    notifyBroken = true;
                    Logs.LogWarning("notify failed: " + e.Message);
                }
            }
        }
    }

    /// <summary>MelonLoader 入口。</summary>
    public class MaydaySaveMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            try
            {
                Plugin.Init();
            }
            catch (Exception e)
            {
                MelonLogger.Error("[MaydaySave] init failed: " + e);
                return;
            }

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<GameHost>();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[MaydaySave] inject GameHost: " + e.Message);
            }

            try
            {
                var go = new GameObject("MaydaySaveHost");
                UnityEngine.Object.DontDestroyOnLoad(go);
                var host = go.AddComponent(Il2CppType.Of<GameHost>()).Cast<GameHost>();
                GameHost.Instance = host;
                MelonLogger.Msg("[MaydaySave] GameHost created");
            }
            catch (Exception e)
            {
                MelonLogger.Error("[MaydaySave] create GameHost failed: " + e);
            }
        }
    }
}
