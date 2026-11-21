using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace MaydaySave
{
    /// <summary>配置项薄封装：让共享代码不直接依赖 BepInEx 类型（MelonLoader 版有同样签名的实现）。</summary>
    internal class ConfigEntry<T>
    {
        private readonly BepInEx.Configuration.ConfigEntry<T> inner;

        internal ConfigEntry(BepInEx.Configuration.ConfigEntry<T> inner) { this.inner = inner; }

        public T Value
        {
            get { return inner.Value; }
            set { inner.Value = value; }
        }

        public string Key { get { return inner.Definition.Key; } }

        public void Save()
        {
            try { if (inner.ConfigFile != null) inner.ConfigFile.Save(); } catch { }
        }
    }

    [BepInPlugin("com.dsh.maydaysave", "Mayday Save", "5.2.1")]
    public class Plugin : BasePlugin
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
        internal const string ConfigPathText = @"BepInEx\config\com.dsh.maydaysave.cfg";
        private static bool notifyBroken;

        public override void Load()
        {
            Logs = new Logger(base.Log);
            Root = Path.Combine(Paths.BepInExRootPath, "MaydaySave");
            SaveDir = Path.Combine(Root, "saves");
            Directory.CreateDirectory(SaveDir);

            CfgNotify = new ConfigEntry<bool>(Config.Bind("General", "Notify", true,
                "存读档时在游戏内弹出提示。"));
            CfgBagFallback = new ConfigEntry<bool>(Config.Bind("General", "BagFallback", true,
                "读档时若行李不足，补发可交互的行李（会占用行李总数，可关掉）"));
            CfgBagFreeMode = new ConfigEntry<bool>(Config.Bind("General", "BagFreeMode", true,
                "读档时清空全部行李并把乘客设为无行李，跳过行李搬运环节"));
            CfgPartialBags = new ConfigEntry<bool>(Config.Bind("General", "PartialBags", false,
                "BagFreeMode 开启时，随机挑几位乘客保留行李需求，并把行李发到玩家脚边"));
            CfgKeyCode = new ConfigEntry<int>(Config.Bind("General", "KeyCode", -1,
                "-1 = F8；其它值为 UnityEngine.InputSystem.Key 的整数（可在设置界面里改，不用手填）"));

            Logs.LogInfo($"MaydaySave v5.2.1 loaded. data={Root}");
            Logs.LogInfo("热键 F8 打开存档界面；右下角也有“存档”按钮。");
            var host = AddComponent<GameHost>();
            GameHost.Instance = host;
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

    /// <summary>日志薄封装，统一 BepInEx / MelonLoader 两套。</summary>
    internal class Logger
    {
        private readonly BepInEx.Logging.ManualLogSource inner;

        internal Logger(BepInEx.Logging.ManualLogSource inner) { this.inner = inner; }

        public void LogInfo(object m) { try { inner.LogInfo(m); } catch { } }
        public void LogWarning(object m) { try { inner.LogWarning(m); } catch { } }
        public void LogError(object m) { try { inner.LogError(m); } catch { } }
        public void LogDebug(object m) { try { inner.LogDebug(m); } catch { } }
    }
}
