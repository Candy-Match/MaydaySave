using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace MaydaySave
{
    [BepInPlugin("com.dsh.maydaysave", "Mayday Save", "5.0.1")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource Logs;
        internal static string Root;
        internal static string SaveDir;
        internal static ConfigEntry<bool> CfgNotify;
        internal static ConfigEntry<bool> CfgBagFallback;
        internal static ConfigEntry<bool> CfgBagFreeMode;
        internal static ConfigEntry<bool> CfgPartialBags;
        private static bool notifyBroken;

        public override void Load()
        {
            Logs = base.Log;
            Root = Path.Combine(Paths.BepInExRootPath, "MaydaySave");
            SaveDir = Path.Combine(Root, "saves");
            Directory.CreateDirectory(SaveDir);

            CfgNotify = Config.Bind("General", "Notify", true,
                "存读档时在游戏内弹出提示。");
            CfgBagFallback = Config.Bind("General", "BagFallback", true,
                "读档时若行李不足，补发可交互的行李（会占用行李总数，可关掉）");
            CfgBagFreeMode = Config.Bind("General", "BagFreeMode", true,
                "读档时清空全部行李并把乘客设为无行李，跳过行李搬运环节");
            CfgPartialBags = Config.Bind("General", "PartialBags", false,
                "BagFreeMode 开启时，随机挑几位乘客保留行李需求，并把行李发到玩家脚边");

            Logs.LogInfo($"MaydaySave v5.0.1 loaded. data={Root}");
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
}
