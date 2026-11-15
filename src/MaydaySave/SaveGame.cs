using System;

namespace MaydaySave
{
    /// <summary>存档摘要的小工具。</summary>
    public static class SaveGame
    {
        public static SaveData Capture() => SaveManager.CaptureRun();

        public static string Describe(SaveData d)
        {
            string name = d.Str("gs.currentName");
            if (string.IsNullOrEmpty(name)) name = "机场 " + d.Int("gs.current");
            int leg = d.Int("gs.leg");
            int phase = d.Int("gs.phase");
            return $"第 {leg + 1} 段 · {name} · {SaveManager.PhaseText(phase)} · ¥{d.Int("gs.money")}";
        }
    }
}
