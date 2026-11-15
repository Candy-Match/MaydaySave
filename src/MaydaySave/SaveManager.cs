using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System.Text;
using UnityEngine;

namespace MaydaySave
{
    /// <summary>存档内容：一局进度 + 物品 + 飞机/地面设备状态。</summary>
    public static class SaveManager
    {
        public const int SlotCount = 10;
        public const int Version = 3;

        public static string SlotPath(int slot) => Path.Combine(Plugin.SaveDir, $"slot{slot}.txt");

        // ------------------------------------------------------------------
        // 状态判断
        // ------------------------------------------------------------------
        public static Mayday.GameState GS => Mayday.GameState.Instance;
        public static Mayday.Airport.GameDirector GD => Mayday.Airport.GameDirector.Instance;

        public static bool InSession => GS != null && Mayday.NetFlow.InSession;
        public static bool IsHost => Mayday.NetFlow.IsHost;

        /// <summary>地面阶段（出发准备 / 地面作业）才能存读档。</summary>
        public static bool OnGround
        {
            get
            {
                var gs = GS;
                if (gs == null) return false;
                var p = (byte)gs.Phase;
                return p == 0 || p == 1;
            }
        }

        public static string PhaseText(int phase) =>
            phase == 0 ? "出发准备" : phase == 1 ? "地面作业" : phase == 2 ? "飞行中" : "本局结束";

        // ------------------------------------------------------------------
        // 槽位信息
        // ------------------------------------------------------------------
        public class SlotInfo
        {
            public int Index;
            public bool Exists;
            public string Time = "";
            public string Airport = "";
            public int Leg;
            public int Phase;
            public int Money;
            public string Raw = "";
            public string Name = "";
        }

        public static SlotInfo ReadSlot(int slot)
        {
            var info = new SlotInfo { Index = slot };
            var path = SlotPath(slot);
            if (!File.Exists(path)) return info;
            try
            {
                var d = SaveData.LoadFrom(path);
                info.Exists = d.Has("gs.seed");
                info.Leg = d.Int("gs.leg");
                info.Phase = d.Int("gs.phase");
                info.Money = d.Int("gs.money");
                info.Name = d.Str("meta.name");
                info.Airport = d.Str("gs.currentName");
                if (string.IsNullOrEmpty(info.Airport)) info.Airport = "机场 " + d.Int("gs.current");
                info.Raw = d.Str("meta.savedAt");
                if (DateTime.TryParse(info.Raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t))
                    info.Time = t.ToString("MM-dd HH:mm");
                else if (info.Raw.Length >= 16) info.Time = info.Raw.Substring(5, 11).Replace('T', ' ');
            }
            catch { info.Exists = false; }
            return info;
        }

        public static void RenameSlot(int slot, string name)
        {
            var path = SlotPath(slot);
            if (!File.Exists(path)) return;
            var d = SaveData.LoadFrom(path);
            d.Set("meta.name", name ?? "");
            d.SaveTo(path);
        }

        public static string SlotSummary(SlotInfo s)
        {
            if (!s.Exists) return "空栏位";
            string prefix = string.IsNullOrEmpty(s.Name) ? "" : $"【{s.Name}】 ";
            return $"{prefix}第 {s.Leg + 1} 段 · {s.Airport} · {PhaseText(s.Phase)} · ¥{s.Money} · {s.Time}";
        }

        // ------------------------------------------------------------------
        // 保存
        // ------------------------------------------------------------------
        public static string Save(int slot, out int itemCount, out int systemCount) => Save(slot, null, out itemCount, out systemCount);

        public static string Save(int slot, string customName, out int itemCount, out int systemCount)
        {
            itemCount = 0;
            systemCount = 0;
            if (!InSession) throw new InvalidOperationException("还没有进入房间");
            if (!IsHost) throw new InvalidOperationException("只有房主可以保存");
            if (!OnGround) throw new InvalidOperationException("只能在飞机起飞前保存");

            var d = CaptureRun();
            if (!string.IsNullOrEmpty(customName)) d.Set("meta.name", customName);
            itemCount = CaptureItems(d);
            systemCount = CaptureSystems(d);
            CaptureInventories(d);

            try
            {
                var hulls = UnityEngine.Object.FindObjectsOfType<Mayday.Systems.HullSystem>();
                if (hulls != null)
                {
                    for (int i = 0; i < hulls.Length; i++)
                    {
                        var h = hulls[i];
                        if (h == null) continue;
                        try
                        {
                            Plugin.Logs.LogInfo($"[save] hull[{i}] path={Snapshot.PathOf(h.transform)} dmg={h.damage[0]:F2},{h.damage[1]:F2} color0={h.mainColor[0]}");
                        }
                        catch (Exception e2) { Plugin.Logs.LogWarning("[save] hull peek: " + e2.Message); }
                    }
                }
            }
            catch { }

            d.SaveTo(SlotPath(slot));
            Plugin.Logs.LogInfo($"[save] slot{slot}: 物品={itemCount} 设备={systemCount}");
            return SaveGame.Describe(d);
        }

        public static SaveData CaptureRun()
        {
            var d = new SaveData();
            d.Set("meta.version", Version);
            d.Set("meta.savedAt", DateTime.Now.ToString("o"));

            var gs = GS;
            d.Set("gs.phase", (byte)gs.Phase);
            d.Set("gs.seed", gs.RunSeed);
            d.Set("gs.leg", gs.Leg);
            d.Set("gs.money", gs.Money);
            d.Set("gs.debt", gs.Debt);
            d.Set("gs.rating", gs.Rating);
            d.Set("gs.difficulty", gs.Difficulty);
            d.Set("gs.current", gs.CurrentAirport);
            d.Set("gs.destination", gs.DestinationAirport);
            d.Set("gs.currentName", gs.CurrentAirportName);
            d.Set("gs.destinationName", gs.DestinationName);
            d.Set("gs.groundLeft", gs.GroundTimeLeft);
            d.Set("gs.legTime", gs.LegTime);
            d.Set("gs.tod", gs.TimeOfDay);
            d.Set("gs.weather", gs.Weather);
            d.Set("gs.totalPax", gs.TotalPassengers);
            d.Set("gs.totalEarned", gs.TotalEarned);
            d.Set("gs.runStart", gs.RunStartTime);
            d.Set("gs.statusLine", gs.StatusLine);
            d.Set("gs.alertKey", gs.AlertKey);
            d.Set("gs.alertLevel", gs.AlertLevel);

            var up = gs.Upgrades;
            if (up != null)
            {
                d.Set("gs.upgradeCount", up.Count);
                for (int i = 0; i < up.Count; i++) d.Set("gs.upgrade." + i, up[i]);
            }

            var gd = GD;
            if (gd != null)
            {
                d.Set("gd.stage", (byte)gd.Stage);
                d.Set("gd.chosenOption", gd.ChosenOption);
                d.Set("gd.leasePaid", gd.LeasePaymentsMade);
                d.Set("gd.landingsSinceLease", gd.LandingsUntilLease);
                d.Set("gd.nextLease", gd.NextLeaseAmount);
                d.Set("gd.fuelNeeded", gd.FuelNeeded);
                d.Set("gd.lastStars", gd.LastStars);
                d.Set("gd.lastGrade", gd.LastGradeKey);
                d.Set("gd.legFrom", gd.NetworklegFrom);
                d.Set("gd.legTo", gd.NetworklegTo);
                d.Set("gd.paxTotal", gd.NetworkpaxTotal);
                d.Set("gd.paxBoarded", gd.NetworkpaxBoarded);
                d.Set("gd.paxAboard", gd.NetworkpaxAboard);
                d.Set("gd.paxLeaving", gd.NetworkpaxLeaving);
                d.Set("gd.bagsTotal", gd.NetworkbagsTotal);
                d.Set("gd.bagsLoaded", gd.NetworkbagsLoaded);
                d.Set("gd.bagsInHold", gd.NetworkbagsInHold);
                try
                {
                    double now = Mayday.World.WorldAuthority.Time;
                    d.Set("gd.slotLeft", (float)Math.Max(0.0, gd.NetworkslotEnd - now));
                    d.Set("gd.autoLeft", (float)Math.Max(0.0, gd.NetworkautoDispatchAt - now));
                }
                catch { }
            }

            var wn = Mayday.World.WorldNet.Instance;
            if (wn != null)
            {
                var s = wn.Sync;
                d.Set("wn.seed", s.seed);
                d.Set("wn.current", s.current);
                d.Set("wn.destination", s.destination);
                d.Set("wn.originA", s.originA);
                d.Set("wn.originB", s.originB);
                d.Set("wn.weatherLeg", s.weatherLeg);
                d.Set("wn.weather", s.weather);
                d.Set("wn.severity", s.severity);
                d.Set("wn.weatherFrom", s.weatherFrom);
                d.Set("wn.weatherTo", s.weatherTo);
                d.Set("wn.version", s.version);
            }
            try { d.Set("ref.stand", Vec4(Snapshot.StandRef(gs.CurrentAirport))); } catch { }
            try { Snapshot.CapturePlane(d); } catch (Exception e) { Plugin.Logs.LogWarning("[save] plane pos: " + e.Message); }
            try { Snapshot.CaptureTransforms(d); } catch (Exception e) { Plugin.Logs.LogWarning("[save] transforms: " + e.Message); }
            CaptureFuel(d);
            CaptureHull(d);
            CapturePaxTransforms(d);
            CaptureBagFallback(d);
            CaptureSeatRows(d);
            try { Snapshot.CapturePlayers(d); } catch (Exception e) { Plugin.Logs.LogWarning("[save] players: " + e.Message); }
            return d;
        }

        // ------------------------------------------------------------------
        // 物品
        // ------------------------------------------------------------------
        private static int CaptureItems(SaveData d)
        {
            var items = UnityEngine.Object.FindObjectsOfType<Mayday.Item>();
            int n = 0;
            if (items == null) { d.Set("item.count", 0); return 0; }

            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];
                if (it == null) continue;
                bool held = false;
                try { held = it.IsHeld; } catch { }
                string id = SafeStr(() => it.ItemId);
                if (string.IsNullOrEmpty(id)) id = "?";
                if (id.StartsWith("bag_", StringComparison.OrdinalIgnoreCase)) continue;   // 行李由乘客系统管理
                var t = it.transform;
                string key = "item." + n;
                d.Set(key + ".id", id);
                d.Set(key + ".tag", SafeUInt(() => it.Tag));
                d.Set(key + ".state", SafeByte(() => it.State));
                d.Set(key + ".amount", SafeFloat(() => it.Amount));
                d.Set(key + ".count", SafeInt(() => it.Count));
                d.Set(key + ".pos", Vec(t.position));
                d.Set(key + ".rot", Rot(t.rotation));
                string sockPath = "";
                try
                {
                    var sock = it.Socket;
                    if (sock != null) sockPath = PathOf(sock.transform);
                }
                catch { }
                d.Set(key + ".socket", sockPath);
                try { var tc = it.Tint; d.Set(key + ".tint", tc.r + " " + tc.g + " " + tc.b + " " + tc.a); } catch { }
                if (held)
                {
                    d.Set(key + ".held", true);
                    try { d.Set(key + ".holder", ((long)it.Holder.SteamId).ToString(CultureInfo.InvariantCulture)); } catch { }
                }
                n++;
            }
            d.Set("item.count", n);
            return n;
        }

        public class SavedItem
        {
            public string Id;
            public byte State;
            public float Amount;
            public int Count;
            public Vector3 Pos;
            public Quaternion Rot;
            public string SocketPath = "";
            public bool Held;
            public string Tint = "";
        }

        private static List<SavedItem> ReadSavedItems(SaveData d)
        {
            int n = d.Int("item.count");
            var list = new List<SavedItem>(n);
            for (int i = 0; i < n; i++)
            {
                string key = "item." + i;
                if (d.Bool(key + ".held")) continue;   // 手持物品单独处理（RestoreHeld），避免刷出副本
                list.Add(new SavedItem
                {
                    Id = d.Str(key + ".id"),
                    State = (byte)d.Int(key + ".state"),
                    Amount = d.Float(key + ".amount"),
                    Count = d.Int(key + ".count"),
                    Pos = ParseVec(d.Str(key + ".pos")),
                    Rot = ParseRot(d.Str(key + ".rot")),
                    SocketPath = d.Str(key + ".socket"),
                    Held = d.Bool(key + ".held"),
                    Tint = d.Str(key + ".tint"),
                });
            }
            return list;
        }

        private static void CaptureFuel(SaveData d)
        {
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<Mayday.Systems.FuelSystem>();
                if (arr == null || arr.Length == 0) return;
                var fs = arr[0];
                d.Set("fuel.reserve", fs.Reserve);
                d.Set("fuel.t0", fs.Fuel(0));
                d.Set("fuel.t1", fs.Fuel(1));
                try { d.Set("fuel.cap", fs.Capacity); } catch { }
                try { d.Set("fuel.cross", fs.Crossfeed); } catch { }
                try { d.Set("fuel.p0", fs.PumpOn(0)); } catch { }
                try { d.Set("fuel.p1", fs.PumpOn(1)); } catch { }
                Plugin.Logs.LogInfo($"[save] fuel main={fs.Fuel(0):F0}/{fs.Fuel(1):F0} reserve={fs.Reserve:F0}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[save] fuel: " + e.Message); }
        }

        private static void RestoreFuel(SaveData d)
        {
            try
            {
                if (!d.Has("fuel.reserve")) return;
                var arr = UnityEngine.Object.FindObjectsOfType<Mayday.Systems.FuelSystem>();
                if (arr == null || arr.Length == 0) return;
                var fs = arr[0];
                float target = d.Float("fuel.reserve");
                float cur = fs.Reserve;
                float delta = target - cur;
                if (Math.Abs(delta) >= 0.05f)
                {
                    if (delta > 0f) fs.ServerAddReserve(delta);
                    else fs.ServerTakeReserve(-delta);
                }

                // 主油箱（左右机翼）
                for (int t = 0; t < 2; t++)
                {
                    string key = "fuel.t" + t;
                    if (!d.Has(key)) continue;
                    float want = d.Float(key);
                    float have = fs.Fuel(t);
                    float diff = want - have;
                    if (Math.Abs(diff) < 0.5f) continue;
                    try
                    {
                        if (diff > 0f) fs.ServerAddFuel(t, diff);
                        else fs.ServerVent(t, -diff);
                    }
                    catch (Exception e2) { Plugin.Logs.LogWarning("[load] fuel tank: " + e2.Message); }
                }

                try { if (d.Has("fuel.cross")) fs.ServerSetCrossfeed(d.Bool("fuel.cross")); } catch { }
                try { if (d.Has("fuel.p0")) fs.ServerSetPump(0, d.Bool("fuel.p0")); } catch { }
                try { if (d.Has("fuel.p1")) fs.ServerSetPump(1, d.Bool("fuel.p1")); } catch { }

                Plugin.Logs.LogInfo($"[load] fuel main={fs.Fuel(0):F0}/{fs.Fuel(1):F0} reserve={fs.Reserve:F0} (saved {d.Float("fuel.t0"):F0}/{d.Float("fuel.t1"):F0})");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] fuel: " + e.Message); }
        }

        // ---------------- 机身损伤/喷漆（HullSystem 的本地数组才是权威数据） ----------------
        private static void CaptureHull(SaveData d)
        {
            try
            {
                var hulls = UnityEngine.Object.FindObjectsOfType<Mayday.Systems.HullSystem>();
                if (hulls == null || hulls.Length == 0) return;
                var h = hulls[0];
                if (h == null) return;

                SaveFloats(d, "hull.lDamage", h.lDamage);
                SaveFloats(d, "hull.paintCover", h.paintCover);
                SaveFloats(d, "hull.coverDamage", h.coverDamage);
                SaveInts(d, "hull.holePaint", h.holePaint);
                SaveColors(d, "hull.goalMain", h.goalMain);
                SaveColors(d, "hull.goalAccent", h.goalAccent);
                SaveC32s(d, "hull.main", h.mainColor);
                SaveC32s(d, "hull.accent", h.accentColor);
                SaveShown(d, "hull.shownMain", h, false);
                SaveShown(d, "hull.shownAccent", h, true);

                int secCount = 0;
                try { secCount = Mayday.Systems.HullSystem.SectionNames.Length; } catch { }
                d.Set("hull.sections", secCount);
                for (int s2 = 0; s2 < secCount; s2++)
                {
                    try
                    {
                        d.Set("hull.s" + s2 + ".stage", h.SectionStage(s2));
                        d.Set("hull.s" + s2 + ".prog", h.SectionProgress(s2));
                    }
                    catch { }
                }
                int paintedPanels = 0;
                string firstPainted = "-";
                try
                {
                    for (int i = 0; i < h.mainColor.Count; i++)
                    {
                        var c = (Color32)h.ShownMain(i);
                        if (Math.Abs(c.r - 242) > 6 || Math.Abs(c.g - 234) > 6 || Math.Abs(c.b - 214) > 6)
                        {
                            paintedPanels++;
                            if (firstPainted == "-") firstPainted = i + ":" + c;
                        }
                    }
                }
                catch { }
                Plugin.Logs.LogInfo($"[save] hull paint panels={paintedPanels} first={firstPainted}");
                Plugin.Logs.LogInfo($"[save] hull dbg lDmg={SafeFloatOf(() => h.lDamage[0]):F2} paint={SafeFloatOf(() => h.paintCover[0]):F2} cover={SafeFloatOf(() => h.coverDamage[0]):F2} hole={SafeIntOf(() => h.holePaint[0])} goal={SafeColorOf(() => h.goalMain[0])} syncColor={SafeC32(() => h.mainColor[0])} dirt={SafeFloatOf(() => h.Dirt(0)):F2} sections={secCount}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[save] hull arrays: " + e.Message); }
        }

        private static float SafeFloatOf(Func<float> f) { try { return f(); } catch { return 0f; } }
        private static string SafeColorOf(Func<Color> f) { try { return f().ToString(); } catch { return "?"; } }
        private static int SafeIntOf(Func<int> f) { try { return f(); } catch { return -1; } }
        private static string SafeC32(Func<Color32> f) { try { return f().ToString(); } catch { return "?"; } }

        private static void SaveFloats(SaveData d, string key, Il2CppStructArray<float> arr)
        {
            if (arr == null) return;
            var sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++) { if (i > 0) sb.Append(','); sb.Append(arr[i].ToString("R", CultureInfo.InvariantCulture)); }
            d.Set(key, sb.ToString());
        }

        private static void SaveInts(SaveData d, string key, Il2CppStructArray<int> arr)
        {
            if (arr == null) return;
            var sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++) { if (i > 0) sb.Append(','); sb.Append(arr[i]); }
            d.Set(key, sb.ToString());
        }

        private static void SaveColors(SaveData d, string key, Il2CppStructArray<Color> arr)
        {
            if (arr == null) return;
            var sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var c = arr[i];
                sb.Append(c.r.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(c.g.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(c.b.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(c.a.ToString("R", CultureInfo.InvariantCulture));
            }
            d.Set(key, sb.ToString());
        }

        private static void RestoreHull(SaveData d)
        {
            try
            {
                var hulls = UnityEngine.Object.FindObjectsOfType<Mayday.Systems.HullSystem>();
                if (hulls == null || hulls.Length == 0) return;
                var h = hulls[0];
                if (h == null) return;

                LoadFloats(d, "hull.lDamage", h.lDamage);
                LoadFloats(d, "hull.paintCover", h.paintCover);
                LoadFloats(d, "hull.coverDamage", h.coverDamage);
                LoadInts(d, "hull.holePaint", h.holePaint);
                LoadColors(d, "hull.goalMain", h.goalMain);
                LoadColors(d, "hull.goalAccent", h.goalAccent);

                // 同步列表也从本地数组推一遍，保证客户端与视觉一致
                try
                {
                    int n = h.damage.Count;
                    for (int i = 0; i < n && i < h.lDamage.Length; i++) h.damage[i] = h.lDamage[i];
                    int nc = h.mainColor.Count;
                    for (int i = 0; i < nc && i < h.goalMain.Length; i++)
                    {
                        var c = h.goalMain[i];
                        h.mainColor[i] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255);
                    }
                }
                catch { }

                int secCount = d.Int("hull.sections");
                for (int s2 = 0; s2 < secCount; s2++)
                {
                    try { h.ServerSetSection(s2, d.Int("hull.s" + s2 + ".stage"), d.Float("hull.s" + s2 + ".prog")); }
                    catch { }
                }

                // 喷漆：走游戏自己的 ServerPaintTick，按"目标颜色"逐块重喷
                int painted = 0;
                try
                {
                    painted += RepaintShown(h, d, "hull.shownMain", false);
                    painted += RepaintShown(h, d, "hull.shownAccent", true);
                }
                catch (Exception e3) { Plugin.Logs.LogWarning("[load] repaint: " + e3.Message); }

                try { h.ApplyAllSectionPaint(); } catch { }
                try { h.ApplyDirtyPanels(); } catch { }
                try { h.ApplySectionVisuals(false); } catch { }
                Plugin.Logs.LogInfo($"[load] hull arrays restored dmg0={SafeFloatOf(() => h.lDamage[0]):F2} color0={SafeColorOf(() => h.goalMain[0])} sections={secCount} painted={painted} shown0={SafeColorOf(() => h.ShownMain(0))}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] hull arrays: " + e.Message); }
        }

        private static void ApplyTint(Mayday.Item it, string s)
        {
            if (it == null || string.IsNullOrEmpty(s)) return;
            var f = s.Split(' ');
            if (f.Length < 4) return;
            try
            {
                it.NetworkTint = new Color32(byte.Parse(f[0]), byte.Parse(f[1]), byte.Parse(f[2]), byte.Parse(f[3]));
            }
            catch { }
        }

        private static void SaveC32s(SaveData d, string key, Mirror.SyncList<Color32> list)
        {
            if (list == null) return;
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (i > 0) sb.Append(',');
                sb.Append(c.r).Append(' ').Append(c.g).Append(' ').Append(c.b).Append(' ').Append(c.a);
            }
            d.Set(key, sb.ToString());
        }

        private static void SaveShown(SaveData d, string key, Mayday.Systems.HullSystem h, bool accent)
        {
            int n = 0;
            try { n = accent ? h.accentColor.Count : h.mainColor.Count; } catch { }
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                Color c;
                try { c = accent ? h.ShownAccent(i) : h.ShownMain(i); } catch { c = Color.white; }
                if (i > 0) sb.Append(',');
                sb.Append((byte)(Mathf.Clamp01(c.r) * 255f)).Append(' ')
                  .Append((byte)(Mathf.Clamp01(c.g) * 255f)).Append(' ')
                  .Append((byte)(Mathf.Clamp01(c.b) * 255f)).Append(' ')
                  .Append((byte)(Mathf.Clamp01(c.a) * 255f));
            }
            d.Set(key, sb.ToString());
        }

        private static int RepaintShown(Mayday.Systems.HullSystem h, SaveData d, string key, bool accent)
        {
            if (!d.Has(key)) return 0;
            var parts = d.Str(key).Split(',');
            int count = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                var f = parts[i].Split(' ');
                if (f.Length < 4) continue;
                byte r = byte.Parse(f[0]), g = byte.Parse(f[1]), b = byte.Parse(f[2]), a = byte.Parse(f[3]);
                Color cur;
                try { cur = accent ? h.ShownAccent(i) : h.ShownMain(i); } catch { continue; }
                var cur32 = (Color32)cur;
                if (Math.Abs(cur32.r - r) <= 3 && Math.Abs(cur32.g - g) <= 3 && Math.Abs(cur32.b - b) <= 3) continue;
                try
                {
                    for (int k = 0; k < 20; k++)
                    {
                        h.ServerPaintTick(i, new Color32(r, g, b, a), accent, 1.0f);
                        var now = (Color32)(accent ? h.ShownAccent(i) : h.ShownMain(i));
                        if (Math.Abs(now.r - r) <= 3 && Math.Abs(now.g - g) <= 3 && Math.Abs(now.b - b) <= 3) break;
                    }
                    count++;
                }
                catch { }
            }
            return count;
        }

        private static int Repaint(Mayday.Systems.HullSystem h, SaveData d, string key, Mirror.SyncList<Color32> live, bool accent)
        {
            if (!d.Has(key)) return 0;
            var parts = d.Str(key).Split(',');
            int n = Math.Min(parts.Length, live.Count);
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                var f = parts[i].Split(' ');
                if (f.Length < 4) continue;
                byte r = byte.Parse(f[0]), g = byte.Parse(f[1]), b = byte.Parse(f[2]), a = byte.Parse(f[3]);
                if (live[i].r == r && live[i].g == g && live[i].b == b) continue;   // 已经是这个颜色
                try
                {
                    // 喷漆是渐进的，反复刷到颜色对上为止
                    for (int k = 0; k < 12; k++)
                    {
                        var now = live[i];
                        if (now.r == r && now.g == g && now.b == b) break;
                        h.ServerPaintTick(i, new Color32(r, g, b, a), accent, 1.0f);
                    }
                    count++;
                }
                catch { }
            }
            return count;
        }

        private static void LoadFloats(SaveData d, string key, Il2CppStructArray<float> arr)
        {
            if (arr == null || !d.Has(key)) return;
            var parts = d.Str(key).Split(',');
            int n = Math.Min(parts.Length, arr.Length);
            for (int i = 0; i < n; i++)
                if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) arr[i] = v;
        }

        private static void LoadInts(SaveData d, string key, Il2CppStructArray<int> arr)
        {
            if (arr == null || !d.Has(key)) return;
            var parts = d.Str(key).Split(',');
            int n = Math.Min(parts.Length, arr.Length);
            for (int i = 0; i < n; i++)
                if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) arr[i] = v;
        }

        private static void LoadColors(SaveData d, string key, Il2CppStructArray<Color> arr)
        {
            if (arr == null || !d.Has(key)) return;
            var parts = d.Str(key).Split(',');
            int n = Math.Min(parts.Length, arr.Length);
            for (int i = 0; i < n; i++)
            {
                var f = parts[i].Split(' ');
                if (f.Length < 4) continue;
                if (!float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) continue;
                float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g);
                float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b);
                float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var a);
                arr[i] = new Color(r, g, b, a);
            }
        }
        /// <summary>读档末尾再写一遍背包物品状态：重喷漆会动到手上的喷漆罐，最后覆盖回去。</summary>
        public static void ReapplyInventoryStates(SaveData d)
        {
            try
            {
                int n = d.Int("inv.count");
                if (n <= 0) return;
                var players = Mayday.PlayerNet.All;
                if (players == null) return;
                int fixedCount = 0;
                for (int i = 0; i < n; i++)
                {
                    string key = "inv." + i;
                    long steam = d.Long(key + ".steam");
                    Mayday.PlayerNet target = null;
                    for (int p = 0; p < players.Count; p++)
                    {
                        if (players[p] == null) continue;
                        if ((long)players[p].SteamId == steam) { target = players[p]; break; }
                    }
                    if (target == null || target.Inventory == null) continue;
                    var inv = target.Inventory;
                    int cnt = d.Int(key + ".count");
                    for (int s = 0; s < cnt; s++)
                    {
                        Mayday.Item it = null;
                        try { it = inv.Get(s); } catch { }
                        if (it == null) continue;
                        string ik = key + ".i" + s;
                        byte state = (byte)d.Int(ik + ".state");
                        float amount = d.Float(ik + ".amount");
                        int count = d.Int(ik + ".count");
                        ApplyTint(it, d.Str(ik + ".tint"));
                        if (it.NetworkState != state || Math.Abs(it.NetworkAmount - amount) > 0.01f || it.NetworkCount != count)
                        {
                            it.NetworkState = state;
                            it.NetworkAmount = amount;
                            it.NetworkCount = count;
                            fixedCount++;
                        }
                    }
                    try { inv.Networkactive = d.Int(key + ".active", inv.Networkactive); } catch { }
                }
                Plugin.Logs.LogInfo($"[load] inventory states reapplied {fixedCount}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] reapply inv: " + e.Message); }
        }
        // ---------------- 行李快照（按乘客归属 + 世界位置） ----------------
        private static void CaptureBags(SaveData d)
        {
            try
            {
                var items = UnityEngine.Object.FindObjectsOfType<Mayday.Item>();
                if (items == null) { d.Set("bag.count", 0); return; }

                // 乘客按 netId 排序，序号就是归属索引
                var pax = UnityEngine.Object.FindObjectsOfType<Mayday.Passengers.Passenger>();
                var paxList = new List<Mayday.Passengers.Passenger>();
                if (pax != null)
                {
                    for (int i = 0; i < pax.Length; i++) if (pax[i] != null) paxList.Add(pax[i]);
                }
                paxList.Sort((a, b) => a.netId.CompareTo(b.netId));

                var bagOfPax = new Dictionary<uint, int>();
                for (int i = 0; i < paxList.Count; i++)
                {
                    uint bn = 0;
                    try { bn = paxList[i].BagNetId; } catch { }
                    if (bn != 0) bagOfPax[bn] = i;
                }

                int n = 0;
                for (int i = 0; i < items.Length; i++)
                {
                    var it = items[i];
                    if (it == null) continue;
                    string id = SafeStr(() => it.ItemId);
                    if (string.IsNullOrEmpty(id) || !id.StartsWith("bag_", StringComparison.OrdinalIgnoreCase)) continue;

                    string key = "bag." + n;
                    var t = it.transform;
                    d.Set(key + ".id", id);
                    d.Set(key + ".state", SafeByte(() => it.State));
                    d.Set(key + ".amount", SafeFloat(() => it.Amount));
                    d.Set(key + ".count", SafeInt(() => it.Count));
                    d.Set(key + ".pos", Vec(t.position));
                    d.Set(key + ".rot", Rot(t.rotation));
                    try { var tc = it.Tint; d.Set(key + ".tint", tc.r + " " + tc.g + " " + tc.b + " " + tc.a); } catch { }
                    string sockPath = "";
                    try { var sock = it.Socket; if (sock != null) sockPath = PathOf(sock.transform); } catch { }
                    d.Set(key + ".socket", sockPath);
                    try { d.Set(key + ".parent", it.transform.parent == null ? "" : PathOf(it.transform.parent)); } catch { }
                    int pi = -1;
                    try { if (bagOfPax.TryGetValue(it.netId, out var idx)) pi = idx; } catch { }
                    d.Set(key + ".pax", pi);
                    if (pi < 0)
                    {
                        // 在自己手上的：记一下是谁
                        try
                        {
                            var holder = it.Holder;
                            if (holder != null) d.Set(key + ".holder", ((long)holder.SteamId).ToString(CultureInfo.InvariantCulture));
                        }
                        catch { }
                    }
                    n++;
                }
                for (int k2 = 0; k2 < n; k2++) Plugin.Logs.LogInfo($"[save] bag{k2} {d.Str("bag." + k2 + ".id")} parent={d.Str("bag." + k2 + ".parent")} socket={d.Str("bag." + k2 + ".socket")}");
                d.Set("bag.count", n);
                Plugin.Logs.LogInfo($"[save] bags captured {n} (pax {paxList.Count})");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[save] bags: " + e.Message); }
        }

        private static List<Transform> beltAnchors;

        private static void RefreshBeltAnchors()
        {
            beltAnchors = new List<Transform>();
            void Add<T>() where T : UnityEngine.Object
            {
                try
                {
                    var arr = UnityEngine.Object.FindObjectsOfType<T>();
                    if (arr == null) return;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var o = arr[i] as UnityEngine.Object;
                        if (o == null) continue;
                        beltAnchors.Add(o.Cast<Component>().transform);
                    }
                }
                catch { }
            }
            Add<Mayday.Airport.BagDrop>();
            Add<Mayday.Airport.CargoLoaders>();
        }

        private static bool IsNearBelt(Vector3 pos)
        {
            try
            {
                if (beltAnchors == null) return false;
                for (int i = 0; i < beltAnchors.Count; i++)
                {
                    if (beltAnchors[i] == null) continue;
                    if ((beltAnchors[i].position - pos).sqrMagnitude < 64f) return true;   // 8m 内算传送带
                }
            }
            catch { }
            return false;
        }

        private static bool IsBeltBag(string socketPath)
        {
            if (string.IsNullOrEmpty(socketPath)) return false;
            string p = socketPath.ToLowerInvariant();
            return p.Contains("belt") || p.Contains("conveyor") || p.Contains("carousel")
                || p.Contains("bagdrop") || p.Contains("bagdrop") || p.Contains("checkin") || p.Contains("drop");
        }

        private static Mayday.ItemSocket FindSocketByName(string sockPath)
        {
            try
            {
                var go = UnityEngine.GameObject.Find(sockPath);
                if (go != null)
                {
                    var comp = go.GetComponent(Il2CppType.Of<Mayday.ItemSocket>());
                    if (comp != null) return comp.Cast<Mayday.ItemSocket>();
                }
            }
            catch { }

            // 兜底：按最后一段名字（Slot_N）在传送带下找
            try
            {
                int slash = sockPath.LastIndexOf('/');
                string leaf = slash >= 0 ? sockPath.Substring(slash + 1) : sockPath;
                var sockets = UnityEngine.Object.FindObjectsOfType<Mayday.ItemSocket>();
                if (sockets != null)
                {
                    for (int i = 0; i < sockets.Length; i++)
                    {
                        var s = sockets[i];
                        if (s == null) continue;
                        if (!string.Equals(s.gameObject.name, leaf, StringComparison.Ordinal)) continue;
                        var tr = s.transform;
                        bool underBelt = false;
                        while (tr != null)
                        {
                            string n = tr.name ?? "";
                            if (n.IndexOf("BagDrop", StringComparison.OrdinalIgnoreCase) >= 0) { underBelt = true; break; }
                            tr = tr.parent;
                        }
                        if (underBelt) return s;
                    }
                }
            }
            catch { }
            return null;
        }
        public static void RestoreBags(SaveData d)
        {
            int n = d.Int("bag.count");
            int spawned = 0, moved = 0, given = 0, attached = 0, failed = 0;
            try
            {
                if (n <= 0) { Plugin.Logs.LogInfo("[load] bags: saved=0"); return; }

                var items = UnityEngine.Object.FindObjectsOfType<Mayday.Item>();
                var live = new List<Mayday.Item>();
                if (items != null)
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        var it = items[i];
                        if (it == null) continue;
                        string id = SafeStr(() => it.ItemId);
                        if (string.IsNullOrEmpty(id) || !id.StartsWith("bag_", StringComparison.OrdinalIgnoreCase)) continue;
                        live.Add(it);
                    }
                }
                live.Sort((a, b) => a.netId.CompareTo(b.netId));

                var pax = UnityEngine.Object.FindObjectsOfType<Mayday.Passengers.Passenger>();
                var paxList = new List<Mayday.Passengers.Passenger>();
                if (pax != null) for (int i = 0; i < pax.Length; i++) if (pax[i] != null) paxList.Add(pax[i]);
                paxList.Sort((a, b) => a.netId.CompareTo(b.netId));

                var players = Mayday.PlayerNet.All;
                for (int i = 0; i < n; i++)
                {
                    string key = "bag." + i;
                    string id = d.Str(key + ".id");
                    if (string.IsNullOrEmpty(id)) continue;
                    var pos = ParseVec(d.Str(key + ".pos"));
                    var rot = ParseRot(d.Str(key + ".rot"));
                    byte state = (byte)d.Int(key + ".state");
                    float amount = d.Float(key + ".amount");
                    int count = d.Int(key + ".count");
                    string tint = d.Str(key + ".tint");
                    string sockPath = d.Str(key + ".socket");
                    long holderSteam = d.Long(key + ".holder");

                    // 存档时在玩家手上的：还给玩家
                    if (holderSteam != 0 && players != null)
                    {
                        for (int p = 0; p < players.Count; p++)
                        {
                            if (players[p] == null) continue;
                            if ((long)players[p].SteamId != holderSteam) continue;
                            try
                            {
                                var got = players[p].Inventory.ServerGive(id, null);
                                if (got != null)
                                {
                                    try { got.NetworkState = state; got.NetworkAmount = amount; got.NetworkCount = count; } catch { }
                                    ApplyTint(got, tint);
                                    given++;
                                }
                            }
                            catch { }
                            break;
                        }
                        if (given > 0) continue;
                    }

                    // 优先复用世界里已有的同类行李，没有再生成
                    Mayday.Item target = null;
                    for (int j = 0; j < live.Count; j++)
                    {
                        if (live[j] == null) continue;
                        if (SafeStr(() => live[j].ItemId) != id) continue;
                        target = live[j];
                        live.RemoveAt(j);
                        break;
                    }
                    bool created = false;
                    if (target == null)
                    {
                        try { target = Mayday.Item.ServerSpawn(id, pos, rot, null, true); created = true; } catch { }
                    }
                    if (target == null) { failed++; continue; }
                    try
                    {
                        target.NetworkState = state;
                        target.NetworkAmount = amount;
                        target.NetworkCount = count;
                        ApplyTint(target, tint);
                        if (!created) target.ServerTeleport(pos, rot);
                        // 让实体"落地"成可交互状态（生成/传送后的行李默认不可交互）
                        try
                        {
                            bool act2 = true;
                            try { act2 = target.InteractEnabled; } catch { }
                            if (!act2) target.ServerRelease(pos, rot, Vector3.zero);
                        }
                        catch { }
                        if (created) spawned++; else moved++;
                    }
                    catch { failed++; continue; }

                    // 存档时挂在传送带槽位上的：挂回新世界的同名槽位，让传送带继续驮着它
                    if (!string.IsNullOrEmpty(sockPath))
                    {
                        var sock = FindSocketByName(sockPath);
                        if (sock != null)
                        {
                            try { target.ServerAttachToSocket(sock); attached++; }
                            catch { }
                        }
                    }

                    // 归属回乘客
                    int pi = d.Int(key + ".pax", -1);
                    if (pi >= 0 && pi < paxList.Count)
                    {
                        try { paxList[pi].BagNetId = target.netId; } catch { }
                    }
                }

                // 让传送带重新扫描/接管刚生成的行李
                try
                {
                    var bd = Mayday.Airport.BagDrop.Instance;
                    if (bd != null)
                    {
                        try { bd.ServerOutScan(); } catch { }
                        try { bd.ServerOutReanchor(); } catch { }
                    }
                }
                catch { }

                Plugin.Logs.LogInfo($"[load] bags: spawned={spawned} moved={moved} given={given} attached={attached} failed={failed} saved={n}");

                // 汇总一下恢复后的行李状态，便于排查
                try
                {
                    var all = UnityEngine.Object.FindObjectsOfType<Mayday.Item>();
                    var sb = new StringBuilder();
                    int cnt = 0;
                    for (int i = 0; i < all.Length; i++)
                    {
                        var it = all[i];
                        if (it == null) continue;
                        string id2 = SafeStr(() => it.ItemId);
                        if (string.IsNullOrEmpty(id2) || !id2.StartsWith("bag_", StringComparison.OrdinalIgnoreCase)) continue;
                        cnt++;
                        bool act = true, sock = false;
                        try { act = it.InteractEnabled; } catch { }
                        try { sock = it.InSocket; } catch { }
                        if (sb.Length < 400) sb.Append(id2).Append(" act=").Append(act ? 1 : 0).Append(" sock=").Append(sock ? 1 : 0).Append(';');
                    }
                    int onBelt = -1, remain = -1;
                    try { var bd2 = Mayday.Airport.BagDrop.Instance; if (bd2 != null) { onBelt = bd2.OutOnBelt; remain = bd2.Remaining; } } catch { }
                    Plugin.Logs.LogInfo($"[load] bags after: count={cnt} belt={onBelt} remaining={remain} {sb}");
                }
                catch { }
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] bags: " + e.Message); }
        }
        /// <summary>读档后清理上一局遗留在世界里的行李孤儿（新世界传送带不认识它们）。</summary>
        /// <summary>只清理"僵死"行李：不可交互且没被玩家拿着的——它们才是幽灵。</summary>
        public static void FixGhostBags()
        {
            int total = 0, killed = 0, kept = 0, held = 0;
            try
            {
                var items = UnityEngine.Object.FindObjectsOfType<Mayday.Item>();
                if (items == null) return;
                for (int i = 0; i < items.Length; i++)
                {
                    var it = items[i];
                    if (it == null) continue;
                    string id = SafeStr(() => it.ItemId);
                    if (string.IsNullOrEmpty(id) || !id.StartsWith("bag_", StringComparison.OrdinalIgnoreCase)) continue;
                    total++;

                    bool isHeld = false, act = true;
                    try { isHeld = it.IsHeld; } catch { }
                    try { act = it.InteractEnabled; } catch { }
                    if (isHeld) { held++; continue; }
                    if (act) { kept++; continue; }

                    // 僵死：不可交互 → 释放槽位、忘掉注册、销毁
                    try
                    {
                        bool inSock = false;
                        try { inSock = it.InSocket; } catch { }
                        if (inSock) { try { it.ServerRelease(it.transform.position, it.transform.rotation, Vector3.zero); } catch { } }
                        try
                        {
                            var ident = it.GetType().GetProperty("netIdentity")?.GetValue(it);
                            if (ident != null) Mayday.InteractionRegistry.Forget((Mirror.NetworkIdentity)ident);
                        }
                        catch { }
                        try { Mayday.VoiceVolumes.Forget(it.netId); } catch { }
                        it.ServerDestroy();
                        killed++;
                    }
                    catch { }
                }
                Plugin.Logs.LogInfo($"[load] bags prune total={total} killed={killed} kept={kept} held={held}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] fix bags: " + e.Message); }
        }
        // ---------------- 乘客位置（座位/排队） ----------------
        private static List<Mayday.Passengers.Passenger> SortedPax()
        {
            var list = new List<Mayday.Passengers.Passenger>();
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<Mayday.Passengers.Passenger>();
                if (arr != null) for (int i = 0; i < arr.Length; i++) if (arr[i] != null) list.Add(arr[i]);
                list.Sort((a, b) => a.netId.CompareTo(b.netId));
            }
            catch { }
            return list;
        }

        private static void CapturePaxTransforms(SaveData d)
        {
            try
            {
                var list = SortedPax();
                d.Set("pax.count", list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    string k = "pax." + i;
                    d.Set(k + ".pos", Vec(p.transform.position));
                    d.Set(k + ".rot", Rot(p.transform.rotation));
                    try { d.Set(k + ".seat", p.SeatIndex); } catch { }
                }
                Plugin.Logs.LogInfo("[save] pax transforms " + list.Count);
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[save] pax tf: " + e.Message); }
        }

        private static Vector3 PlanePosForPax = Vector3.zero;

        public static void RestorePaxTransforms(SaveData d)
        {
            try
            {
                var pf0 = Mayday.Flight.PlaneFlight.Instance;
                if (pf0 != null) PlanePosForPax = pf0.sim.Pos;
            }
            catch { }
            try
            {
                int n = d.Int("pax.count");
                if (n <= 0) return;
                int stg = d.Int("gd.stage", 0);
                if (stg == 3) { Plugin.Logs.LogInfo("[load] pax restore skipped (turnaround, saved 0 expected)"); return; }
                var list = SortedPax();
                int placed = 0, skipped = 0;
                for (int i = 0; i < n && i < list.Count; i++)
                {
                    string k = "pax." + i;
                    var p = list[i];
                    if (p == null) continue;
                    var pos = ParseVec(d.Str(k + ".pos"));
                    var rot = ParseRot(d.Str(k + ".rot"));
                    try
                    {
                        p.transform.position = pos;
                        p.transform.rotation = rot;
                        placed++;

                        // 座位号照抄
                        int seat = d.Int(k + ".seat", -1);
                        if (seat >= 0) { try { p.SeatIndex = (sbyte)seat; } catch { } }

                        // 用游戏自己的接口把乘客驱动到"该在的状态"：
                        // 存档位置在飞机附近（客舱里）→ 直接落座；否则重新开始登机
                        float dist = (pos - PlanePosForPax).magnitude;
                        if (dist < 28f)
                        {
                            try { p.BeltServerSitDown(Mayday.World.WorldAuthority.Time); } catch { }
                        }
                        else
                        {
                            try { p.ServerStartBoarding(); } catch { }
                        }
                    }
                    catch { skipped++; }
                }
                // 兜底：如果某位乘客没被放回去（离飞机很远），重新让他开始登机，避免卡关
                int reboard = 0;
                Vector3 planePos = Vector3.zero;
                bool hasPlane = false;
                try
                {
                    var pf = Mayday.Flight.PlaneFlight.Instance;
                    if (pf != null) { planePos = pf.sim.Pos; hasPlane = true; }
                }
                catch { }
                if (hasPlane)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var p = list[i];
                        if (p == null) continue;
                        float dist = (p.transform.position - planePos).magnitude;
                        // 只有当乘客"存档时就在飞机旁"却没能摆回去时，才重启登机（避免误伤候机楼的乘客）
                        bool wasNearPlane = false;
                        try
                        {
                            var savedPos = ParseVec(d.Str("pax." + i + ".pos"));
                            wasNearPlane = d.Has("plane.pos") && (savedPos - ParseVec(d.Str("plane.pos"))).magnitude < 80f;
                        }
                        catch { }
                        if (dist > 60f && wasNearPlane)
                        {
                            try { p.ServerStartBoarding(); reboard++; } catch { }
                        }
                    }
                }
                Plugin.Logs.LogInfo($"[load] pax placed {placed}/{n} skipped={skipped} reboard={reboard}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] pax tf: " + e.Message); }
        }
        // ---------------- 行李兜底：按存档数量补发"可交互"的行李 ----------------
        private static void CaptureBagFallback(SaveData d)
        {
            try
            {
                var items = UnityEngine.Object.FindObjectsOfType<Mayday.Item>();
                int n = 0;
                if (items != null)
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        var it = items[i];
                        if (it == null) continue;
                        string id = SafeStr(() => it.ItemId);
                        if (string.IsNullOrEmpty(id) || !id.StartsWith("bag_", StringComparison.OrdinalIgnoreCase)) continue;
                        d.Set("bagfb." + n + ".id", id);
                        n++;
                    }
                }
                d.Set("bagfb.count", n);
                Plugin.Logs.LogInfo("[save] bag fallback record " + n);
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[save] bagfb: " + e.Message); }
        }

        public static void RestoreBagFallback(SaveData d)
        {
            try
            {
                if (Plugin.CfgBagFallback != null && !Plugin.CfgBagFallback.Value)
                {
                    Plugin.Logs.LogInfo("[load] bag fallback disabled by config");
                    return;
                }
                int saved = d.Int("bagfb.count");
                int paxCount = 0;
                try
                {
                    var pax = UnityEngine.Object.FindObjectsOfType<Mayday.Passengers.Passenger>();
                    if (pax != null) paxCount = pax.Length;
                }
                catch { }
                // 目标：至少满足"每位乘客一件行李"，同时不少于存档里记录的件数
                int target = Math.Max(saved, paxCount);
                if (target <= 0) return;

                // 只统计"可交互"的行李：被传送带收养停靠的幽灵不算数
                int interactable = 0;
                var items = UnityEngine.Object.FindObjectsOfType<Mayday.Item>();
                if (items != null)
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        var it = items[i];
                        if (it == null) continue;
                        string id = SafeStr(() => it.ItemId);
                        if (string.IsNullOrEmpty(id) || !id.StartsWith("bag_", StringComparison.OrdinalIgnoreCase)) continue;
                        bool act = true, held = false;
                        try { act = it.InteractEnabled; } catch { }
                        try { held = it.IsHeld; } catch { }
                        if (act || held) interactable++;
                    }
                }
                int missing = target - interactable;
                if (missing <= 0)
                {
                    Plugin.Logs.LogInfo($"[load] bag fallback: target={target} interactable={interactable} (ok)");
                    return;
                }

                int given = 0, spawned = 0;
                var local = Mayday.PlayerNet.Local;
                var inv = local != null ? local.Inventory : null;

                for (int k = 0; k < missing; k++)
                {
                    string id = d.Str("bagfb." + (k % Math.Max(1, saved)) + ".id");
                    if (string.IsNullOrEmpty(id)) id = k % 2 == 0 ? "bag_suitcase_s" : "bag_duffel";

                    // 优先塞进玩家背包：绝对不会被传送带收养，玩家可以随手拿出来交给乘客
                    if (inv != null)
                    {
                        try
                        {
                            var got = inv.ServerGive(id, null);
                            if (got != null) { given++; continue; }
                        }
                        catch { }
                    }

                    // 背包满了：生成在玩家脚边（离传送带很远，避免被收养）
                    Vector3 pos = Vector3.zero;
                    try
                    {
                        if (local != null) pos = local.transform.position; 
                        if (pos == Vector3.zero)
                        {
                            var pf = Mayday.Flight.PlaneFlight.Instance;
                            if (pf != null) pos = Mayday.Airport.AirportSites.WorldToScene(pf.sim.Pos) + new Vector3(3f, 1f, 3f);
                        }
                    }
                    catch { }
                    try
                    {
                        var made = Mayday.Item.ServerSpawn(id, pos + new Vector3(k * 0.6f, 0f, 0f), Quaternion.identity, null, true);
                        if (made != null)
                        {
                            bool act = true;
                            try { act = made.InteractEnabled; } catch { }
                            if (!act) { try { made.ServerRelease(pos, Quaternion.identity, Vector3.zero); } catch { } }
                            spawned++;
                        }
                    }
                    catch (Exception e2) { Plugin.Logs.LogWarning("[load] bag spawn: " + e2.Message); }
                }
                Plugin.Logs.LogInfo($"[load] bag fallback: target={target} interactable={interactable} given={given} spawned={spawned} saved={saved} pax={paxCount}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] bagfb: " + e.Message); }
        }
        /// <summary>清空所有行李，并把乘客设为"没有行李"，从而完全跳过行李环节。</summary>
        public static void ClearBagsAndDetachPax()
        {
            int killed = 0, detached = 0;
            try
            {
                var items = UnityEngine.Object.FindObjectsOfType<Mayday.Item>();
                if (items != null)
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        var it = items[i];
                        if (it == null) continue;
                        string id = SafeStr(() => it.ItemId);
                        if (string.IsNullOrEmpty(id) || !id.StartsWith("bag_", StringComparison.OrdinalIgnoreCase)) continue;
                        try
                        {
                            bool inSock = false;
                            try { inSock = it.InSocket; } catch { }
                            if (inSock) { try { it.ServerRelease(it.transform.position, it.transform.rotation, Vector3.zero); } catch { } }
                            try
                            {
                                var ident = it.GetType().GetProperty("netIdentity")?.GetValue(it);
                                if (ident != null) Mayday.InteractionRegistry.Forget((Mirror.NetworkIdentity)ident);
                            }
                            catch { }
                            try { Mayday.VoiceVolumes.Forget(it.netId); } catch { }
                            it.ServerDestroy();
                            killed++;
                        }
                        catch (Exception e2) { Plugin.Logs.LogWarning("[load] bag free destroy: " + e2.Message); }
                    }
                }
                try { Mayday.Passengers.PassengerDirector.Instance?.ClearAllBags(); } catch { }

                var pax = UnityEngine.Object.FindObjectsOfType<Mayday.Passengers.Passenger>();
                if (pax != null)
                {
                    for (int i = 0; i < pax.Length; i++)
                    {
                        var p = pax[i];
                        if (p == null) continue;
                        try { p.BagNetId = 0; detached++; } catch { }
                    }
                }
                Plugin.Logs.LogInfo($"[load] bag-free mode: destroyed={killed} paxDetached={detached}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] bag free: " + e.Message); }
        }
        /// <summary>在"无行李模式"下，随机挑几位乘客恢复行李需求，并把行李发到玩家身边。</summary>
        public static void SpawnPartialBags()
        {
            try
            {
                var pax = UnityEngine.Object.FindObjectsOfType<Mayday.Passengers.Passenger>();
                if (pax == null || pax.Length == 0) return;

                var list = new List<Mayday.Passengers.Passenger>();
                for (int i = 0; i < pax.Length; i++) if (pax[i] != null) list.Add(pax[i]);
                if (list.Count == 0) return;

                // 随机 1 .. 乘客总数
                int want = UnityEngine.Random.Range(1, list.Count + 1);

                // 打乱
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
                }
                want = Mathf.Clamp(want, 1, list.Count);

                var local = Mayday.PlayerNet.Local;
                Vector3 basePos = Vector3.zero;
                try
                {
                    if (local != null) basePos = local.transform.position;
                    else
                    {
                        var pf = Mayday.Flight.PlaneFlight.Instance;
                        if (pf != null) basePos = Mayday.Airport.AirportSites.WorldToScene(pf.sim.Pos);
                    }
                }
                catch { }
                var inv = local != null ? local.Inventory : null;

                string[] kinds = { "bag_suitcase_s", "bag_suitcase_l", "bag_duffel", "bag_backpack", "bag_box" };
                int bound = 0, toInv = 0;
                for (int k = 0; k < want; k++)
                {
                    var p = list[k];
                    if (p == null) continue;
                    string id = kinds[UnityEngine.Random.Range(0, kinds.Length)];

                    Mayday.Item made = null;
                    var pos = basePos + new Vector3(0.9f + k * 0.5f, 0.2f, 0.9f);
                    try { made = Mayday.Item.ServerSpawn(id, pos, Quaternion.identity, null, true); } catch { }

                    // 生成失败或被传送带收养（不可交互）→ 塞进玩家背包，保证能拿
                    bool ok = made != null;
                    if (ok)
                    {
                        bool act = true;
                        try { act = made.InteractEnabled; } catch { }
                        if (!act)
                        {
                            try { made.ServerRelease(pos, Quaternion.identity, Vector3.zero); } catch { }
                            bool act2 = true;
                            try { act2 = made.InteractEnabled; } catch { }
                            if (!act2)
                            {
                                if (inv != null)
                                {
                                    try
                                    {
                                        var ident = made.GetType().GetProperty("netIdentity")?.GetValue(made);
                                        if (ident != null) Mayday.InteractionRegistry.Forget((Mirror.NetworkIdentity)ident);
                                    }
                                    catch { }
                                    try { made.ServerDestroy(); } catch { }
                                    made = null;
                                }
                            }
                        }
                    }
                    if (made == null && inv != null)
                    {
                        try { made = inv.ServerGive(id, null); if (made != null) toInv++; } catch { }
                    }
                    if (made == null) continue;

                    try { p.BagNetId = made.netId; bound++; } catch { }
                }
                Plugin.Logs.LogInfo($"[load] partial bags: pax={list.Count} chosen={bound} toInventory={toInv}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] partial bags: " + e.Message); }
        }
        // ---------------- 客舱座椅排（是否安装 / 是否头等 / 安装时间） ----------------
        private static List<Mayday.Passengers.SeatRow> SortedSeatRows()
        {
            var list = new List<Mayday.Passengers.SeatRow>();
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<Mayday.Passengers.SeatRow>();
                if (arr != null) for (int i = 0; i < arr.Length; i++) if (arr[i] != null) list.Add(arr[i]);
                list.Sort((a, b) => string.CompareOrdinal(a.gameObject.name, b.gameObject.name));
            }
            catch { }
            return list;
        }

        private static void CaptureSeatRows(SaveData d)
        {
            try
            {
                var pd = Mayday.Passengers.PassengerDirector.Instance;
                if (pd == null) return;
                int rows = pd.RowCount;
                d.Set("seat.rows", rows);
                int installed = 0;
                for (int s2 = 0; s2 < rows; s2++)
                {
                    bool inst = false, prem = false;
                    try { inst = pd.IsRowInstalled(s2); } catch { }
                    if (inst)
                    {
                        installed++;
                        try { var r = Mayday.Passengers.SeatRow.Get(s2); if (r != null) prem = r.premium; } catch { }
                    }
                    d.Set("seat.s" + s2 + ".inst", inst);
                    d.Set("seat.s" + s2 + ".prem", prem);
                }
                Plugin.Logs.LogInfo($"[save] seat rows total={rows} installed={installed}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[save] seats: " + e.Message); }
        }

        /// <summary>场景里所有座椅排对象（含未激活的）。</summary>
        private static List<Mayday.Passengers.SeatRow> AllSeatRows()
        {
            var list = new List<Mayday.Passengers.SeatRow>();
            try
            {
                var arr = UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppType.Of<Mayday.Passengers.SeatRow>());
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var o = arr[i];
                        if (o == null) continue;
                        var row = o.Cast<Mayday.Passengers.SeatRow>();
                        if (row == null) continue;
                        try { if (!row.gameObject.scene.IsValid()) continue; } catch { }
                        list.Add(row);
                    }
                }
            }
            catch { }
            return list;
        }

        public static void RestoreSeatRows(SaveData d)
        {
            try
            {
                var pd = Mayday.Passengers.PassengerDirector.Instance;
                if (pd == null) return;
                int savedRows = d.Int("seat.rows", -1);
                if (savedRows < 0) return;

                // 存档里"已安装"的排位集合
                var want = new HashSet<int>();
                for (int s2 = 0; s2 < savedRows; s2++)
                    if (d.Bool("seat.s" + s2 + ".inst")) want.Add(s2);

                var rows = AllSeatRows();
                int existing = 0, removed = 0, added = 0, premSet = 0;

                // 1) 场景里多出来的排位：卸下并隐藏（世界重建不会销毁上一局的座椅排）
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null) continue;
                    int slot = -1;
                    try { slot = row.slot; } catch { }
                    if (slot < 0) continue;
                    existing++;
                    if (want.Contains(slot)) continue;
                    try
                    {
                        try { Mirror.NetworkServer.Destroy(row.gameObject); } catch { }
                        try { row.Networkslot = -1; } catch { }
                        try { row.gameObject.SetActive(false); } catch { }
                        removed++;
                    }
                    catch { }
                }

                // 2) 存档里有、场景里没有的：让游戏自己装（会 Instantiate + Spawn 模型）
                for (int s2 = 0; s2 < savedRows; s2++)
                {
                    if (!want.Contains(s2)) continue;
                    bool exists = false;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        if (row == null) continue;
                        int slot = -1;
                        try { slot = row.slot; } catch { }
                        if (slot == s2) { exists = true; break; }
                    }
                    if (!exists) added++;   // 世界刚重建时装不上，交给 2 秒后的 EnsureSeatRows

                    bool prem = d.Bool("seat.s" + s2 + ".prem");
                    try
                    {
                        var r = Mayday.Passengers.SeatRow.Get(s2);
                        if (r != null && r.premium != prem) { r.ServerSetPremium(prem); premSet++; }
                        else if (r != null) { try { r.gameObject.SetActive(true); } catch { } }
                    }
                    catch { }
                }

                try { pd.RefreshMounts(); } catch { }

                int finalRows = 0;
                try
                {
                    var after = AllSeatRows();
                    for (int i = 0; i < after.Count; i++) { if (after[i] != null && after[i].slot >= 0) finalRows++; }
                }
                catch { }
                Plugin.Logs.LogInfo($"[load] seat rows: saved={savedRows} want={want.Count} existing={existing} added={added} removed={removed} premium={premSet} nowInScene={finalRows}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] seats: " + e.Message); }
        }

        /// <summary>延时二次校验：如果某排仍然没有实际对象，就让游戏再装一次。</summary>
        public static void EnsureSeatRows(SaveData d)
        {
            try
            {
                var pd = Mayday.Passengers.PassengerDirector.Instance;
                if (pd == null) return;
                int savedRows = d.Int("seat.rows", -1);
                if (savedRows < 0) return;

                int ok = 0, retried = 0, stillMissing = 0;
                for (int s2 = 0; s2 < savedRows; s2++)
                {
                    if (!d.Bool("seat.s" + s2 + ".inst")) continue;
                    Mayday.Passengers.SeatRow row = null;
                    try { row = Mayday.Passengers.SeatRow.Get(s2); } catch { }
                    if (row != null) { ok++; continue; }
                    retried++;
                    // 关键：先清掉 rowMask 里这一位的"已安装"标记，否则 ServerInstallRow 会直接返回、不生成模型
                    try
                    {
                        byte mask = pd.NetworkrowMask;
                        byte cleared2 = (byte)(mask & ~(1 << s2));
                        if (cleared2 != mask)
                        {
                            pd.NetworkrowMask = cleared2;
                            System.Threading.Thread.Sleep(60);
                        }
                    }
                    catch (Exception e4) { Plugin.Logs.LogWarning("[load] clear mask " + s2 + ": " + e4.Message); }

                    for (int attempt = 0; attempt < 3 && row == null; attempt++)
                    {
                        try { pd.ServerInstallRow(s2, false); } catch (Exception e2) { Plugin.Logs.LogWarning("[load] seat retry " + s2 + ": " + e2.Message); }
                        System.Threading.Thread.Sleep(180);
                        try { row = Mayday.Passengers.SeatRow.Get(s2); } catch { }
                    }
                    if (row == null) stillMissing++;
                }
                try { pd.RefreshMounts(); } catch { }
                Plugin.Logs.LogInfo($"[load] seat rows verify: ok={ok} retried={retried} stillMissing={stillMissing} (saved {savedRows})");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] seats verify: " + e.Message); }
        }

        public static void ClearPassengers()
        {
            try
            {
                var pd = Mayday.Passengers.PassengerDirector.Instance;
                if (pd == null) { Plugin.Logs.LogWarning("[load] no PassengerDirector"); return; }
                // 只清乘客；行李交给游戏自己（之前清行李会留下看不见的幽灵实体）
                pd.ServerClearAll();
                // 行李孤儿在重建后统一处理（FixGhostBags / RestoreBags）
                Plugin.Logs.LogInfo("[load] passengers cleared");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] clear pax: " + e.Message); }
        }

        /// <summary>把当前世界里的物品对账成存档里的样子：多余的销毁、缺的补生成、位置状态覆盖。</summary>
        public static void RestoreItemsPublic(SaveData d) => RestoreItems(d);

        private static void RestoreItems(SaveData d)
        {
            var saved = ReadSavedItems(d);
            var live = UnityEngine.Object.FindObjectsOfType<Mayday.Item>();
            int liveCount = live == null ? 0 : live.Length;

            var liveList = new List<Mayday.Item>(liveCount);
            for (int i = 0; i < liveCount; i++)
            {
                var it = live[i];
                if (it == null) continue;
                bool held = false;
                try { held = it.IsHeld; } catch { }
                if (held) continue;                  // 手上物品不参与对账
                liveList.Add(it);
            }

            var claimed = new bool[liveList.Count];
            var match = new Mayday.Item[saved.Count];

            // 0) 槽位匹配（挂在同一个挂架/容器里的物品最可靠）
            int socketHits = 0;
            for (int i = 0; i < saved.Count; i++)
            {
                var sv = saved[i];
                if (string.IsNullOrEmpty(sv.SocketPath)) continue;
                for (int j = 0; j < liveList.Count; j++)
                {
                    if (claimed[j]) continue;
                    var it = liveList[j];
                    string lp = "";
                    try { var sk = it.Socket; if (sk != null) lp = PathOf(sk.transform); } catch { }
                    if (!string.Equals(lp, sv.SocketPath, StringComparison.Ordinal)) continue;
                    if (!string.Equals(SafeStr(() => it.ItemId), sv.Id, StringComparison.OrdinalIgnoreCase)) continue;
                    match[i] = it; claimed[j] = true; socketHits++;
                    break;
                }
            }

            // 1) 位置精确匹配（没动过的物品）
            for (int i = 0; i < saved.Count; i++)
            {
                var sv = saved[i];
                int best = -1; float bestD = 0.36f;   // 0.6m
                for (int j = 0; j < liveList.Count; j++)
                {
                    if (claimed[j]) continue;
                    var it = liveList[j];
                    if (!string.Equals(SafeStr(() => it.ItemId), sv.Id, StringComparison.OrdinalIgnoreCase)) continue;
                    float dist = (it.transform.position - sv.Pos).sqrMagnitude;
                    if (dist < bestD) { bestD = dist; best = j; }
                }
                if (best >= 0) { match[i] = liveList[best]; claimed[best] = true; }
            }

            // 2) 同类型配对（被移动过的物品）
            for (int i = 0; i < saved.Count; i++)
            {
                if (match[i] != null) continue;
                for (int j = 0; j < liveList.Count; j++)
                {
                    if (claimed[j]) continue;
                    var it = liveList[j];
                    if (!string.Equals(SafeStr(() => it.ItemId), saved[i].Id, StringComparison.OrdinalIgnoreCase)) continue;
                    match[i] = it; claimed[j] = true; break;
                }
            }

            // 3) 覆盖状态与位置
            int moved = 0, applied = 0, socketRestored = 0;
            for (int i = 0; i < saved.Count; i++)
            {
                var it = match[i];
                if (it == null) continue;
                var sv = saved[i];
                try
                {
                    it.NetworkState = sv.State;
                    it.NetworkAmount = sv.Amount;
                    it.NetworkCount = sv.Count;
                    ApplyTint(it, sv.Tint);
                    if ((it.transform.position - sv.Pos).sqrMagnitude > 0.04f)
                    {
                        it.transform.position = sv.Pos;
                        it.transform.rotation = sv.Rot;
                        moved++;
                    }

                    // 存档时拿在手里的：读档后放回当时的位置（不然会凭空消失）
                    if (sv.Held)
                    {
                        try { it.ServerRelease(sv.Pos, sv.Rot, Vector3.zero); } catch { }
                    }

                    // 挂在槽位里的物品：真正挂回去（否则会浮在原地）
                    if (!string.IsNullOrEmpty(sv.SocketPath))
                    {
                        string liveSock = "";
                        try { var cur = it.Socket; if (cur != null) liveSock = PathOf(cur.transform); } catch { }
                        if (!string.Equals(liveSock, sv.SocketPath, StringComparison.Ordinal))
                        {
                            var sock = FindSocket(sv.SocketPath);
                            if (sock != null)
                            {
                                try { it.ServerAttachToSocket(sock); socketRestored++; }
                                catch (Exception e2) { Plugin.Logs.LogWarning("[load] attach: " + e2.Message); }
                            }
                        }
                    }
                    applied++;
                }
                catch (Exception e) { Plugin.Logs.LogWarning("[load] item apply: " + e.Message); }
            }

            // 4) 存档里没有的多余物品：销毁（玩家买/拿出来的）
            int destroyed = 0;
            for (int j = 0; j < liveList.Count; j++)
            {
                if (claimed[j]) continue;
                string victimId = SafeStr(() => liveList[j].ItemId);
                if (victimId.StartsWith("bag_", StringComparison.OrdinalIgnoreCase)) continue;  // 行李归乘客系统管，不动它
                try
                {
                    var victim = liveList[j];
                    Plugin.Logs.LogInfo($"[load] destroy extra {SafeStr(() => victim.ItemId)} at {victim.transform.position}");
                    // 先从交互注册表里忘掉，否则会留下"看得见、点不动"的幽灵
                    try
                    {
                        var ident = victim.GetType().GetProperty("netIdentity")?.GetValue(victim);
                        if (ident != null) Mayday.InteractionRegistry.Forget((Mirror.NetworkIdentity)ident);
                    }
                    catch { }
                    try { Mayday.VoiceVolumes.Forget(victim.netId); } catch { }
                    victim.ServerDestroy();
                    destroyed++;
                }
                catch (Exception e) { Plugin.Logs.LogWarning("[load] destroy: " + e.Message); }
            }

            // 5) 存档里有、重开后不存在的：补生成（乘客行李 bag_* 由乘客系统自己刷，跳过避免重复）
            int spawned = 0, skippedBag = 0;
            for (int i = 0; i < saved.Count; i++)
            {
                if (match[i] != null) continue;
                var sv = saved[i];

                try
                {
                    var it = Mayday.Item.ServerSpawn(sv.Id, sv.Pos, sv.Rot, null, true);
                    if (it != null)
                    {
                        it.NetworkState = sv.State;
                        it.NetworkAmount = sv.Amount;
                        it.NetworkCount = sv.Count;
                        spawned++;
                    }
                    else Plugin.Logs.LogWarning("[load] spawn failed: " + sv.Id);
                }
                catch (Exception e) { Plugin.Logs.LogWarning("[load] spawn " + sv.Id + ": " + e.Message); }
            }

            Plugin.Logs.LogInfo($"[load] items: live={liveList.Count} saved={saved.Count} applied={applied} moved={moved} socket={socketRestored} destroyed={destroyed} spawned={spawned} bagSkipped={skippedBag}");
        }
        private static Mayday.ItemSocket FindSocket(string path)
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Mayday.ItemSocket>();
                if (all == null) return null;
                for (int i = 0; i < all.Length; i++)
                {
                    var sk = all[i];
                    if (sk == null) continue;
                    if (string.Equals(PathOf(sk.transform), path, StringComparison.Ordinal)) return sk;
                }
            }
            catch { }
            return null;
        }

        private static Mayday.Item NearestItem(Mayday.Item[] all, string id, Vector3 pos)
        {
            if (all == null) return null;
            Mayday.Item best = null;
            float bestD = 2.5f;
            for (int i = 0; i < all.Length; i++)
            {
                var it = all[i];
                if (it == null) continue;
                string iid = SafeStr(() => it.ItemId);
                if (!string.Equals(iid, id, StringComparison.OrdinalIgnoreCase)) continue;
                float dist = (it.transform.position - pos).sqrMagnitude;
                if (dist < bestD) { bestD = dist; best = it; }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // 玩家背包
        // ------------------------------------------------------------------
        private static void CaptureInventories(SaveData d)
        {
            var players = Mayday.PlayerNet.All;
            if (players == null) { d.Set("inv.count", 0); return; }
            int n = 0;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;
                var inv = p.Inventory;
                if (inv == null) continue;
                bool host = i == 0 || (Mayday.PlayerNet.Local != null && p.SteamId == Mayday.PlayerNet.Local.SteamId);
                string key = "inv." + n;
                d.Set(key + ".name", p.DisplayName);
                d.Set(key + ".steam", ((long)p.SteamId).ToString(CultureInfo.InvariantCulture));
                d.Set(key + ".host", host);
                try { d.Set(key + ".active", inv.Active); } catch { }
                int got = 0;
                for (int s = 0; s < Mayday.PlayerInventory.SlotCount; s++)
                {
                    Mayday.Item it = null;
                    try { it = inv.Get(s); } catch { }
                    if (it == null) continue;
                    string ik = key + ".i" + got;
                    d.Set(ik + ".slot", s);
                    d.Set(ik + ".id", SafeStr(() => it.ItemId));
                    d.Set(ik + ".state", SafeByte(() => it.State));
                    d.Set(ik + ".amount", SafeFloat(() => it.Amount));
                    d.Set(ik + ".count", SafeInt(() => it.Count));
                    try { var t = it.Tint; d.Set(ik + ".tint", t.r + " " + t.g + " " + t.b + " " + t.a); Plugin.Logs.LogInfo($"[save] inv {key} i{got} {it.ItemId} tint={t}"); } catch { }
                    got++;
                }
                d.Set(key + ".count", got);
                n++;
            }
            d.Set("inv.count", n);
        }

        private static void RestoreInventories(SaveData d)
        {
            int n = d.Int("inv.count");
            if (n <= 0) return;
            var players = Mayday.PlayerNet.All;
            if (players == null) return;

            for (int i = 0; i < n; i++)
            {
                string key = "inv." + i;
                long steam = d.Long(key + ".steam");
                Mayday.PlayerNet target = null;
                for (int p = 0; p < players.Count; p++)
                {
                    if (players[p] == null) continue;
                    if ((long)players[p].SteamId == steam) { target = players[p]; break; }
                }
                if (target == null) { Plugin.Logs.LogWarning($"[load] inventory: player {steam} not found"); continue; }
                var inv = target.Inventory;
                if (inv == null) continue;

                try
                {
                    inv.ServerDropAll();
                    inv.Networkactive = 0;              // 从 1 号位开始发放，保证顺序和存档一致
                    int cnt = d.Int(key + ".count");
                    int given = 0;
                    for (int s = 0; s < cnt; s++)
                    {
                        string ik = key + ".i" + s;
                        string id = d.Str(ik + ".id");
                        if (string.IsNullOrEmpty(id)) continue;
                        byte state = (byte)d.Int(ik + ".state");
                        float amount = d.Float(ik + ".amount");
                        int count = d.Int(ik + ".count");
                        var it = inv.ServerGive(id, null);
                        if (it == null) { Plugin.Logs.LogWarning("[load] give failed: " + id); continue; }
                        it.NetworkState = state;
                        it.NetworkAmount = amount;
                        it.NetworkCount = count;
                        ApplyTint(it, d.Str(ik + ".tint"));
                        given++;
                    }
                    int active = d.Int(key + ".active", 0);
                    if (active < 0 || active >= Mayday.PlayerInventory.SlotCount) active = 0;
                    inv.Networkactive = active;         // 选回存档时选中的那一格
                    Plugin.Logs.LogInfo($"[load] inventory of {target.DisplayName} restored ({given}/{cnt}) active={active}");
                }
                catch (Exception e) { Plugin.Logs.LogWarning("[load] inventory: " + e.Message); }
            }
        }

        // ------------------------------------------------------------------
        // 飞机/地面设备的通用 SyncVar 快照
        // ------------------------------------------------------------------
        private static readonly HashSet<string> SkipTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Mayday.Item", "Mayday.PlayerNet", "Mayday.PlayerInventory",
            "Mayday.GameState", "Mayday.Airport.GameDirector", "Mayday.World.WorldNet",
            "Mayday.Airport.GroundCrew",
            "Mayday.Passengers.Passenger",
        };

        private static void AddAll<T>(List<Il2CppObjectBase> into) where T : UnityEngine.Object
        {
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<T>();
                if (arr == null) return;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == null) continue;
                    into.Add(arr[i]);
                }
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[save] gather " + typeof(T).Name + ": " + e.Message); }
        }

        private static Il2CppObjectBase[] GatherNetworkBehaviours()
        {
            var list = new List<Il2CppObjectBase>(64);
            AddAll<Mayday.Systems.EngineUnit>(list);
            AddAll<Mayday.Systems.ElectricalSystem>(list);
            AddAll<Mayday.Systems.FuelSystem>(list);
            AddAll<Mayday.Systems.HullSystem>(list);
            AddAll<Mayday.Systems.DoorSystem>(list);
            AddAll<Mayday.Systems.IgnitionSystem>(list);
            AddAll<Mayday.Systems.FireNode>(list);
            AddAll<Mayday.Systems.FailureDirector>(list);
            AddAll<Mayday.Systems.CapedBoss>(list);
            AddAll<Mayday.Service.GalleySystem>(list);
            AddAll<Mayday.Service.LavSystem>(list);
            AddAll<Mayday.Service.TrashBin>(list);
            AddAll<Mayday.Service.DrinkTrolley>(list);
            AddAll<Mayday.Service.Mess>(list);
            AddAll<Mayday.Service.ServiceDirector>(list);
            AddAll<Mayday.Service.ConsumeNet>(list);
            AddAll<Mayday.Flight.PlaneFlight>(list);
            AddAll<Mayday.Flight.PASystem>(list);
            AddAll<Mayday.Passengers.SeatRow>(list);
            Plugin.Logs.LogInfo("[save] systems gathered: " + list.Count);
            return list.ToArray();
        }
        private static int CaptureSystems(SaveData d) => Snapshot.Capture(d); 
        private static int CaptureSystemsOld(SaveData d)
        {
            var all = GatherNetworkBehaviours();
            int n = 0;
            if (all == null) { d.Set("nb.count", 0); return 0; }

            for (int i = 0; i < all.Length; i++)
            {
                var nb = all[i];
                if (nb == null) continue;
                var t = nb.GetType();
                if (SkipTypes.Contains(t.FullName)) continue;

                var props = SyncProps(t);
                if (props.Count == 0) continue;
                string key = "nb." + n;
                string path;
                try { path = PathOf(nb.Cast<Component>().transform); } catch { continue; }
                d.Set(key + ".key", t.Name + "|" + path);
                int pi = 0;
                foreach (var p in props)
                {
                    if (!p.CanRead || !p.CanWrite) continue;
                    object v;
                    try { v = p.GetValue(nb); } catch { continue; }
                    string enc = Encode(v, p.PropertyType);
                    if (enc == null) continue;
                    d.Set(key + ".p" + pi, p.Name + "=" + enc);
                    pi++;
                }
                if (pi == 0) continue;
                d.Set(key + ".n", pi);
                n++;
                if (n >= 600) break;
            }
            d.Set("nb.count", n);
            return n;
        }

        public static void RestoreSystems(SaveData d) => Snapshot.Restore(d);
        public static void RestoreSystemsOld(SaveData d)
        {
            int n = d.Int("nb.count");
            if (n <= 0) return;
            var all = GatherNetworkBehaviours();
            if (all == null) return;
            var map = new Dictionary<string, Il2CppObjectBase>(StringComparer.Ordinal);
            for (int i = 0; i < all.Length; i++)
            {
                var nb = all[i];
                if (nb == null) continue;
                try
                {
                    string k = nb.GetType().Name + "|" + PathOf(nb.Cast<Component>().transform);
                    if (!map.ContainsKey(k)) map[k] = nb;
                }
                catch { }
            }

            int applied = 0, skipped = 0;
            for (int i = 0; i < n; i++)
            {
                string key = "nb." + i;
                string id = d.Str(key + ".key");
                if (!map.TryGetValue(id, out var nb)) { skipped++; continue; }
                int cnt = d.Int(key + ".n");
                for (int p = 0; p < cnt; p++)
                {
                    string line = d.Str(key + ".p" + p);
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string propName = line.Substring(0, eq);
                    string enc = line.Substring(eq + 1);
                    var prop = nb.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop == null || !prop.CanWrite) continue;
                    try
                    {
                        object v = Decode(enc, prop.PropertyType);
                        if (v != null) prop.SetValue(nb, v);
                    }
                    catch { }
                }
                applied++;
            }
            Plugin.Logs.LogInfo($"[load] systems restored {applied}, unmatched {skipped}");
        }

        private static List<PropertyInfo> SyncProps(Type t)
        {
            var list = new List<PropertyInfo>();
            if (t == null) return list;
            var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                if (p.Name.Length > 7 && p.Name.StartsWith("Network", StringComparison.Ordinal) && p.CanWrite) list.Add(p);
            }
            return list;
        }

        private static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var cur = t.parent;
            int guard = 0;
            while (cur != null && guard++ < 24)
            {
                sb.Insert(0, cur.name + "/");
                cur = cur.parent;
            }
            return sb.ToString();
        }

        // -------------------------- 值编码 --------------------------
        private static string Encode(object v, Type t)
        {
            if (v == null) return "n:";
            if (t.IsEnum) return "e:" + Convert.ToInt64(v, CultureInfo.InvariantCulture);
            if (v is bool b) return "b:" + (b ? 1 : 0);
            if (v is int i2) return "i:" + i2;
            if (v is uint u2) return "u:" + u2;
            if (v is byte by) return "y:" + by;
            if (v is sbyte sb2) return "sb:" + sb2;
            if (v is short sh) return "h:" + sh;
            if (v is ushort us) return "hu:" + us;
            if (v is long l) return "l:" + l;
            if (v is ulong ul) return "ul:" + ul;
            if (v is float f) return "f:" + f.ToString("R", CultureInfo.InvariantCulture);
            if (v is double dd) return "d:" + dd.ToString("R", CultureInfo.InvariantCulture);
            if (v is string s) return "s:" + s;
            if (v is Vector3 v3) return "v3:" + Vec(v3);
            if (v is Vector2 v2) return "v2:" + Num(v2.x) + "," + Num(v2.y);
            if (v is Quaternion q) return "q:" + Rot(q);
            if (v is Color32 c32) return "c32:" + c32.r + "," + c32.g + "," + c32.b + "," + c32.a;
            if (v is Color c) return "c:" + Num(c.r) + "," + Num(c.g) + "," + Num(c.b) + "," + Num(c.a);

            // 可序列化结构体：逐字段
            if (t.IsValueType && !t.IsPrimitive)
            {
                var sb = new StringBuilder("st:");
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public);
                bool any = false;
                for (int i = 0; i < fields.Length; i++)
                {
                    string fe = Encode(fields[i].GetValue(v), fields[i].FieldType);
                    if (fe == null) return null;
                    sb.Append(fields[i].Name).Append('#').Append(fe).Append(';');
                    any = true;
                }
                return any ? sb.ToString() : null;
            }
            return null;
        }

        private static object Decode(string enc, Type t)
        {
            if (enc == null) return null;
            int c = enc.IndexOf(':');
            if (c < 0) return null;
            string kind = enc.Substring(0, c);
            string body = enc.Substring(c + 1);

            if (kind == "n") return null;
            if (kind == "e") return Enum.ToObject(t, long.Parse(body, CultureInfo.InvariantCulture));
            if (kind == "b") return body == "1";
            if (kind == "i") return int.Parse(body, CultureInfo.InvariantCulture);
            if (kind == "u") return uint.Parse(body, CultureInfo.InvariantCulture);
            if (kind == "y") return byte.Parse(body, CultureInfo.InvariantCulture);
            if (kind == "sb") return sbyte.Parse(body, CultureInfo.InvariantCulture);
            if (kind == "h") return short.Parse(body, CultureInfo.InvariantCulture);
            if (kind == "hu") return ushort.Parse(body, CultureInfo.InvariantCulture);
            if (kind == "l") return long.Parse(body, CultureInfo.InvariantCulture);
            if (kind == "ul") return ulong.Parse(body, CultureInfo.InvariantCulture);
            if (kind == "f") return float.Parse(body, CultureInfo.InvariantCulture);
            if (kind == "d") return double.Parse(body, CultureInfo.InvariantCulture);
            if (kind == "s") return body;
            if (kind == "v3") return ParseVec(body);
            if (kind == "v2") { var p = ParseNums(body); return new Vector2(p[0], p[1]); }
            if (kind == "q") return ParseRot(body);
            if (kind == "c32") { var p = ParseNums(body); return new Color32((byte)p[0], (byte)p[1], (byte)p[2], (byte)p[3]); }
            if (kind == "c") { var p = ParseNums(body); return new Color(p[0], p[1], p[2], p[3]); }
            if (kind == "st")
            {
                object boxed = Activator.CreateInstance(t);
                foreach (var part in body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int h = part.IndexOf('#');
                    if (h <= 0) continue;
                    var f = t.GetField(part.Substring(0, h), BindingFlags.Instance | BindingFlags.Public);
                    if (f == null) continue;
                    var fv = Decode(part.Substring(h + 1), f.FieldType);
                    if (fv != null) f.SetValue(boxed, fv);
                }
                return boxed;
            }
            return null;
        }

        private static string Num(float f) => f.ToString("R", CultureInfo.InvariantCulture);
        private static string Vec(Vector3 v) => Num(v.x) + "," + Num(v.y) + "," + Num(v.z);
        private static string Rot(Quaternion q) => Num(q.x) + "," + Num(q.y) + "," + Num(q.z) + "," + Num(q.w);

        private static float[] ParseNums(string s)
        {
            var parts = s.Split(',');
            var r = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]);
            return r;
        }

        private static Vector3 ParseVec(string s)
        {
            var p = ParseNums(s);
            return p.Length >= 3 ? new Vector3(p[0], p[1], p[2]) : Vector3.zero;
        }

        private static Quaternion ParseRot(string s)
        {
            var p = ParseNums(s);
            return p.Length >= 4 ? new Quaternion(p[0], p[1], p[2], p[3]) : Quaternion.identity;
        }

        private static string SafeStr(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }
        private static int SafeInt(Func<int> f) { try { return f(); } catch { return 0; } }
        private static byte SafeByte(Func<byte> f) { try { return f(); } catch { return 0; } }
        private static float SafeFloat(Func<float> f) { try { return f(); } catch { return 0f; } }
        private static uint SafeUInt(Func<uint> f) { try { return f(); } catch { return 0u; } }

        // ------------------------------------------------------------------
        // 应用（读档）
        // ------------------------------------------------------------------
        public static void ApplyRun(SaveData d)
        {
            var gs = GS;
            var gd = GD;
            var wn = Mayday.World.WorldNet.Instance;
            if (gs == null || gd == null || wn == null) return;

            try { gs.ServerResetRun(d.Int("gs.seed"), Math.Max(0, d.Int("gs.money", 600))); }
            catch (Exception e) { Plugin.Logs.LogError("[load] reset: " + e.Message); }

            ApplyGameState(d, gs);
            ApplyWorld(d, wn);
            ApplyDirector(d, gd);
        }

        public static void ApplyGameState(SaveData d, Mayday.GameState gs)
        {
            gs.NetworkLeg = d.Int("gs.leg");
            gs.NetworkMoney = d.Int("gs.money");
            gs.NetworkDebt = d.Int("gs.debt");
            gs.NetworkRating = d.Float("gs.rating", 3f);
            gs.NetworkDifficulty = d.Float("gs.difficulty");
            gs.NetworkCurrentAirport = d.Int("gs.current");
            gs.NetworkDestinationAirport = d.Int("gs.destination", -1);
            gs.NetworkCurrentAirportName = d.Str("gs.currentName");
            gs.NetworkDestinationName = d.Str("gs.destinationName");
            gs.NetworkGroundTimeLeft = d.Float("gs.groundLeft");
            gs.NetworkLegTime = d.Float("gs.legTime");
            gs.NetworkTimeOfDay = d.Float("gs.tod");
            gs.NetworkWeather = d.Float("gs.weather");
            gs.NetworkTotalPassengers = d.Int("gs.totalPax");
            gs.NetworkTotalEarned = d.Int("gs.totalEarned");
            gs.NetworkRunStartTime = d.Float("gs.runStart");
            gs.NetworkStatusLine = d.Str("gs.statusLine");
            gs.NetworkAlertKey = d.Str("gs.alertKey");
            gs.NetworkAlertLevel = (byte)d.Int("gs.alertLevel");

            try
            {
                var up = gs.Upgrades;
                up.Clear();
                int n = d.Int("gs.upgradeCount");
                for (int i = 0; i < n; i++) up.Add(d.Str("gs.upgrade." + i));
            }
            catch (Exception e) { Plugin.Logs.LogError("[load] upgrades: " + e.Message); }
        }

        public static void ApplyWorld(SaveData d, Mayday.World.WorldNet wn)
        {
            var s = default(Mayday.World.WorldSync);
            s.seed = d.Int("wn.seed");
            s.current = d.Int("wn.current");
            s.destination = d.Int("wn.destination", -1);
            s.originA = d.Int("wn.originA");
            s.originB = d.Int("wn.originB");
            s.weatherLeg = d.Int("wn.weatherLeg");
            s.weather = (byte)d.Int("wn.weather");
            s.severity = d.Float("wn.severity");
            s.weatherFrom = d.Int("wn.weatherFrom");
            s.weatherTo = d.Int("wn.weatherTo");
            s.version = d.Int("wn.version", 1) + 1;
            wn.ServerSetSync(s);
        }

        public static void ApplyDirector(SaveData d, Mayday.Airport.GameDirector gd)
        {
            gd.NetworkleasePaid = d.Int("gd.leasePaid");
            gd.NetworklandingsSinceLease = d.Int("gd.landingsSinceLease");
            gd.NetworknextLease = d.Int("gd.nextLease");
            gd.NetworkfuelNeeded = d.Int("gd.fuelNeeded");
            gd.NetworklastStars = d.Float("gd.lastStars", -1f);
            gd.NetworklastGradeKey = d.Str("gd.lastGrade");
            gd.NetworklegFrom = d.Str("gd.legFrom");
            gd.NetworklegTo = d.Str("gd.legTo");
            gd.NetworkpaxTotal = d.Int("gd.paxTotal");
            gd.NetworkpaxBoarded = d.Int("gd.paxBoarded");
            gd.NetworkpaxAboard = d.Int("gd.paxAboard");
            gd.NetworkpaxLeaving = d.Int("gd.paxLeaving");
            gd.NetworkbagsTotal = d.Int("gd.bagsTotal");
            gd.NetworkbagsLoaded = d.Int("gd.bagsLoaded");
            gd.NetworkbagsInHold = d.Int("gd.bagsInHold");
            try
            {
                double now = Mayday.World.WorldAuthority.Time;
                float slotLeft = d.Float("gd.slotLeft", -1f);
                gd.NetworkslotEnd = slotLeft > 0f ? now + slotLeft : 0.0;
                float autoLeft = d.Float("gd.autoLeft", -1f);
                gd.NetworkautoDispatchAt = autoLeft > 0f ? now + autoLeft : 0.0;
                Plugin.Logs.LogInfo($"[load] timers slotLeft={slotLeft:F0}s autoLeft={autoLeft:F0}s");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] timers: " + e.Message); }
        }

        public static void DropAllInventories()
        {
            try
            {
                var players = Mayday.PlayerNet.All;
                if (players == null) return;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null) continue;
                    var inv = p.Inventory;
                    if (inv == null) continue;
                    inv.ServerDropAll();
                }
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] drop inv: " + e.Message); }
        }

        /// <summary>物品对账 + 背包恢复 + 设备状态（读档时调用一次）。</summary>
        /// <summary>飞机位置、玩家位置、各网络对象状态（在物品之后调用）。</summary>
        private static string Vec4(Vector3 v) =>
            v.x.ToString("R", CultureInfo.InvariantCulture) + "," + v.y.ToString("R", CultureInfo.InvariantCulture) + "," + v.z.ToString("R", CultureInfo.InvariantCulture);

        public static void ApplyWorldState(SaveData d)
        {
            try { Snapshot.PrepareRestore(d); } catch { }
            try { Snapshot.Restore(d); } catch (Exception e) { Plugin.Logs.LogWarning("[load] snapshot: " + e.Message); }
            Snapshot.RestorePlane(d);
            Snapshot.RestoreTransforms(d);
            Snapshot.RestorePlayers(d);
            RestoreFuel(d);
            RestoreHull(d);
        }

        public static void ApplyExtras(SaveData d)
        {
            DropAllInventories();
            RestoreItems(d);
            RestoreInventories(d);
            RestoreSystems(d);
        }

        /// <summary>把存档时拿在手里的东西还给对应玩家。</summary>
        private static void RestoreHeldItems(SaveData d)
        {
            int n = d.Int("item.count");
            if (n <= 0) return;
            var players = Mayday.PlayerNet.All;
            if (players == null) return;
            int given = 0, dropped = 0;
            for (int i = 0; i < n; i++)
            {
                string key = "item." + i;
                if (!d.Bool(key + ".held")) continue;
                string id = d.Str(key + ".id");
                if (string.IsNullOrEmpty(id)) continue;
                long steam = d.Long(key + ".holder");
                Mayday.PlayerNet target = null;
                for (int p = 0; p < players.Count; p++)
                {
                    if (players[p] == null) continue;
                    if ((long)players[p].SteamId == steam) { target = players[p]; break; }
                }
                var pos = Snapshot.ParseVec(d.Str(key + ".pos")) + Snapshot.RefFor(d.Int("gs.current"));
                bool done = false;
                if (target != null && target.Inventory != null)
                {
                    try
                    {
                        var it = target.Inventory.ServerGive(id, null);
                        if (it != null)
                        {
                            it.NetworkState = (byte)d.Int(key + ".state");
                            it.NetworkAmount = d.Float(key + ".amount");
                            it.NetworkCount = d.Int(key + ".count");
                            given++; done = true;
                        }
                    }
                    catch { }
                }
                if (!done)
                {
                    try
                    {
                        var it = Mayday.Item.ServerSpawn(id, pos, Quaternion.identity, null, true);
                        if (it != null) dropped++;
                    }
                    catch { }
                }
            }
            Plugin.Logs.LogInfo($"[load] held items given={given} dropped={dropped}");
        }

        public static void BuildWorld(SaveData d)
        {
            var wm = Mayday.World.WorldManager.Instance;
            if (wm == null) return;
            int seed = d.Int("gs.seed"), cur = d.Int("gs.current"), dest = d.Int("gs.destination", -1);
            wm.Reconcile(seed, cur, dest, true);
            var airports = wm.airports;
            if (airports == null) return;
            for (int k = Math.Max(0, cur - 1); k <= cur + 1; k++)
            {
                try
                {
                    var rt = airports[k];
                    if (rt != null && !rt.contentBuilt) wm.CompleteNow(rt);
                }
                catch { }
            }
        }

        public static void PlaceAtAirport(SaveData d)
        {
            int idx = d.Int("gs.current");
            var site = Mayday.Airport.AirportSites.Find(idx);
            if (site == null) return;

            string nm = Mayday.Airport.AirportSites.NameOf(site, idx);
            if (!string.IsNullOrEmpty(nm)) GS.NetworkCurrentAirportName = nm;

            Mayday.Flight.PlaneFlight.StandWorldPose(site, out var planePos, out var planeRot);
            var plane = Mayday.Flight.PlaneFlight.Instance;
            if (plane != null)
            {
                plane.transform.position = planePos;
                plane.transform.rotation = planeRot;
            }

            Mayday.Airport.AirportSites.WorldPose(site, Mayday.Airport.AnchorKind.Gate, out var gate, out var gateRot);
            Vector3 away = gate - planePos; away.y = 0f;
            if (away.sqrMagnitude < 400f) away = new Vector3(-1f, 0f, 0f);
            away.Normalize();
            var spawnPos = planePos + away * 24f;
            spawnPos.y = planePos.y - 2.2f;
            float yaw = Mathf.Atan2(-away.x, -away.z) * Mathf.Rad2Deg;

            var players = Mayday.PlayerNet.All;
            if (players != null)
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null) continue;
                    try { p.ServerTeleport(spawnPos, yaw); }
                    catch { p.transform.position = spawnPos; }
                }

            var gc = Mayday.Airport.GroundCrew.Instance;
            if (gc != null) gc.ServerReanchor((int)Mayday.Airport.ReanchorKind.Snap);
        }
    }
}
