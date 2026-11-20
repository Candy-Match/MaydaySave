using System;
using System.IO;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace MaydaySave
{
    /// <summary>插件在游戏里的入口：热键、读档流程。</summary>
    public class GameHost : MonoBehaviour
    {
        public static GameHost Instance;
        internal static UnityEngine.InputSystem.Key HotkeyKey = UnityEngine.InputSystem.Key.F8;
        private bool hotkeyLoaded;

        private int alarmForceStall;
        private int alarmProbeFrames;
        private int alarmProbeTotal;

        internal static float seatVerifyAt = -1f;
        internal static SaveData seatVerifyData;

        private float t;
        private LoadJob job;
        private bool keyboardSeen;
        private bool loggedReady;
        private bool uiReady;

        public GameHost(IntPtr ptr) : base(ptr) { }

        public void Update()
        {
            if (seatVerifyAt > 0f && UnityEngine.Time.time >= seatVerifyAt)
            {
                seatVerifyAt = -1f;
                var svd = seatVerifyData;
                seatVerifyData = null;
                if (svd != null)
                {
                    try { SaveManager.EnsureSeatRows(svd); } catch (Exception ex) { Plugin.Logs.LogWarning("[load] seats verify: " + ex.Message); }
                }
            }

            if (alarmForceStall > 0)
            {
                alarmForceStall--;
                try
                {
                    var pf3 = Mayday.Flight.PlaneFlight.Instance;
                    if (pf3 != null)
                    {
                        var sim3 = pf3.sim;
                        sim3.OnGround = false;
                        sim3.WeightOnWheels = 0f;
                        sim3.Speed = 20f;
                        sim3.Alpha = 0.55f;
                        sim3.Agl = 400f;
                    }
                    if (alarmForceStall == 100 || alarmForceStall == 40 || alarmForceStall == 5)
                        SaveManager.CaptureAlarmProbe("STALL-" + alarmForceStall);
                }
                catch (Exception e) { Plugin.Logs.LogWarning("[dev] force stall: " + e.Message); }
            }

            if (alarmProbeFrames > 0)
            {
                alarmProbeFrames--;
                try { AlarmProbeStep(alarmProbeFrames); } catch (Exception e) { Plugin.Logs.LogWarning("[probe] " + e.Message); }
            }

            t += Time.deltaTime;
            try { Tick(); }
            catch (Exception e) { Plugin.Logs.LogError("tick: " + e); }
        }

        private bool autoHosted;
        private float nextFileCheck;

        private void DevTriggers()
        {
            if (t < nextFileCheck) return;
            nextFileCheck = t + 0.5f;
            try
            {
                // 测试用：存在 autohost.txt 时自动开单人房
                if (!autoHosted && t > 6f && File.Exists(Path.Combine(Plugin.Root, "autohost.txt")))
                {
                    autoHosted = true;
                    if (!Mayday.NetFlow.InSession) { Plugin.Logs.LogInfo("[dev] host solo"); Mayday.NetFlow.HostSolo(); }
                }

                var choose = Path.Combine(Plugin.Root, "choose_request.txt");
                if (File.Exists(choose))
                {
                    string w = File.ReadAllText(choose).Trim();
                    File.Delete(choose);
                    if (w.Length == 0) w = "0";
                    Plugin.Logs.LogInfo("[dev] choose " + w);
                    Mayday.Airport.GameDirector.DevChoose(w);
                }

                var activeReq = Path.Combine(Plugin.Root, "set_active.txt");
                if (File.Exists(activeReq))
                {
                    int idx = 0;
                    int.TryParse(File.ReadAllText(activeReq).Trim(), out idx);
                    File.Delete(activeReq);
                    try
                    {
                        var p = Mayday.PlayerNet.Local;
                        if (p != null && p.Inventory != null) { p.Inventory.Networkactive = idx; Plugin.Logs.LogInfo("[dev] active=" + idx); }
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] active: " + e.Message); }
                }

                var mvPlane = Path.Combine(Plugin.Root, "move_plane.txt");
                if (File.Exists(mvPlane))
                {
                    File.Delete(mvPlane);
                    try
                    {
                        var pf = Mayday.Flight.PlaneFlight.Instance;
                        var scene = new Vector3(180f, 4f, 60f);
                        var world = Mayday.Airport.AirportSites.SceneToWorld(scene);
                        pf.ServerTeleport(world, 45f);
                        Plugin.Logs.LogInfo($"[dev] plane moved to scene={scene} world={world} wposNow={Mayday.Airport.AirportSites.PlaneWorldPos}");
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] move plane: " + e.Message); }
                }

                var tintReq = Path.Combine(Plugin.Root, "tint_test.txt");
                if (File.Exists(tintReq))
                {
                    File.Delete(tintReq);
                    try
                    {
                        var pl = Mayday.PlayerNet.Local;
                        var inv = pl != null ? pl.Inventory : null;
                        if (inv != null)
                        {
                            var it = inv.Get(0);
                            if (it != null)
                            {
                                it.NetworkTint = new Color32(204, 31, 31, 255);
                                Plugin.Logs.LogInfo($"[dev] tint set id={it.ItemId} tint={it.Tint}");
                            }
                            else Plugin.Logs.LogWarning("[dev] tint: slot0 empty");
                        }
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] tint: " + e.Message); }
                }

                var rowReq2 = Path.Combine(Plugin.Root, "install_row2.txt");
                if (File.Exists(rowReq2))
                {
                    File.Delete(rowReq2);
                    try
                    {
                        var pd = Mayday.Passengers.PassengerDirector.Instance;
                        int before = 0;
                        var arr0 = UnityEngine.Object.FindObjectsOfType<Mayday.Passengers.SeatRow>();
                        if (arr0 != null) before = arr0.Length;
                        pd.ServerInstallRow(3, false);
                        System.Threading.Thread.Sleep(260);
                        var arr1 = UnityEngine.Object.FindObjectsOfType<Mayday.Passengers.SeatRow>();
                        int after = arr1 == null ? 0 : arr1.Length;
                        var r3 = Mayday.Passengers.SeatRow.Get(3);
                        Plugin.Logs.LogInfo($"[dev] install(false) row3: installed={pd.IsRowInstalled(3)} scene {before}->{after} rowCount={pd.RowCount} get3={(r3 != null)}");
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] install row2: " + e.Message); }
                }

                var confirmReq = Path.Combine(Plugin.Root, "settings_confirm.txt");
                if (File.Exists(confirmReq))
                {
                    File.Delete(confirmReq);
                    try
                    {
                        if (SettingsUI.Instance == null) SettingsUI.Ensure(this, transform);
                        if (!SettingsUI.Instance.IsOpen) SettingsUI.Instance.Open();
                        System.Threading.Thread.Sleep(120);
                        SettingsUI.DebugConfirm();
                        Plugin.Logs.LogInfo("[dev] confirm shown");
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] confirm: " + e.Message); }
                }

                var galleyReq = Path.Combine(Plugin.Root, "galley_fire.txt");
                if (File.Exists(galleyReq))
                {
                    File.Delete(galleyReq);
                    try
                    {
                        var g = Mayday.Service.GalleySystem.Instance;
                        if (g != null)
                        {
                            var ov = g.NetworkOven;
                            ov.on = true;
                            ov.fire = true;
                            ov.doorPulseUntil = Mayday.World.WorldAuthority.Time + 9999.0;
                            g.NetworkOven = ov;
                            Plugin.Logs.LogInfo("[dev] oven forced on+fire, OverloadUntil=" + g.NetworkOverloadUntil.ToString("F0"));
                        }
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] galley: " + e.Message); }
                }

                var futureReq = Path.Combine(Plugin.Root, "alarm_future.txt");
                if (File.Exists(futureReq))
                {
                    File.Delete(futureReq);
                    try
                    {
                        double now = Mayday.World.WorldAuthority.Time;
                        var pfF = Mayday.Flight.PlaneFlight.Instance;
                        if (pfF != null) pfF.NetworkpaReadyAt = now + 5000.0;
                        var pa = Mayday.Flight.PASystem.Instance;
                        if (pa != null) { pa.NetworklockoutUntil = now + 5000.0; pa.NetworkliveAt = now + 5000.0; }
                        var paxAll = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Mayday.Passengers.Passenger>());
                        if (paxAll != null && paxAll.Length > 0) { var p0 = paxAll[0].Cast<Mayday.Passengers.Passenger>(); p0.NetworkRequestDeadline = now + 5000.0; }
                        Plugin.Logs.LogInfo($"[dev] injected future timestamps (now={now:F0})");
                        SaveManager.CaptureAlarmProbe("FUTURE");
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] future: " + e.Message); }
                }

                var stallReq = Path.Combine(Plugin.Root, "alarm_stall.txt");
                if (File.Exists(stallReq))
                {
                    File.Delete(stallReq);
                    try
                    {
                        alarmForceStall = 150;
                        SaveManager.CaptureAlarmProbe("STALL-BEFORE");
                        Plugin.Logs.LogInfo("[dev] forced stall test armed (150 frames)");
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] stall: " + e.Message); }
                }

                var airReq = Path.Combine(Plugin.Root, "alarm_air.txt");
                if (File.Exists(airReq))
                {
                    File.Delete(airReq);
                    try
                    {
                        var pf2 = Mayday.Flight.PlaneFlight.Instance;
                        if (pf2 != null)
                        {
                            var sim2 = pf2.sim;
                            var air = sim2.Pos + new Vector3(0f, 320f, 0f);
                            pf2.ServerTeleport(air, sim2.Heading);
                            Plugin.Logs.LogInfo("[dev] plane teleported to " + air + " for stall test");
                            SaveManager.CaptureAlarmProbe("AIR-BEFORE");
                        }
                        alarmProbeFrames = 90;
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] air: " + e.Message); }
                }

                var probeReq = Path.Combine(Plugin.Root, "alarm_probe.txt");
                if (File.Exists(probeReq))
                {
                    File.Delete(probeReq);
                    try
                    {
                        SaveManager.CaptureAlarmProbe("BEFORE");
                        var pf2 = Mayday.Flight.PlaneFlight.Instance;
                        if (pf2 != null)
                        {
                            var sim2 = pf2.sim;
                            sim2.Stalling = true;
                            sim2.StallWarning = true;
                        }
                        alarmProbeFrames = 40;
                        Plugin.Logs.LogInfo("[probe] armed (40 frames)");
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[probe] arm: " + e.Message); }
                }

                var keyReq = Path.Combine(Plugin.Root, "settings_key.txt");
                if (File.Exists(keyReq))
                {
                    File.Delete(keyReq);
                    try
                    {
                        SettingsUI.ApplyKey(UnityEngine.InputSystem.Key.F9);
                        Plugin.Logs.LogInfo("[dev] key set to F9");
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] key: " + e.Message); }
                }

                var resetReq = Path.Combine(Plugin.Root, "settings_reset.txt");
                if (File.Exists(resetReq))
                {
                    File.Delete(resetReq);
                    try { SettingsUI.ApplyDefaults(); } catch (Exception e) { Plugin.Logs.LogError("[dev] reset: " + e.Message); }
                }

                var cfgReq = Path.Combine(Plugin.Root, "settings_open.txt");
                if (File.Exists(cfgReq))
                {
                    File.Delete(cfgReq);
                    try
                    {
                        SettingsUI.Ensure(this, transform);
                        SettingsUI.Instance.Open();
                        Plugin.Logs.LogInfo("[dev] settings panel opened");
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] settings: " + e.Message); }
                }

                var rowReq = Path.Combine(Plugin.Root, "install_row.txt");
                if (File.Exists(rowReq))
                {
                    File.Delete(rowReq);
                    try
                    {
                        var pd = Mayday.Passengers.PassengerDirector.Instance;
                        int slot = 3;
                        pd.ServerInstallRow(slot, true);
                        System.Threading.Thread.Sleep(300);
                        Plugin.Logs.LogInfo($"[dev] install row {slot}: installed={pd.IsRowInstalled(slot)} rowCount={pd.RowCount}");
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] install row: " + e.Message); }
                }

                var paintReq = Path.Combine(Plugin.Root, "paint_test.txt");
                if (File.Exists(paintReq))
                {
                    File.Delete(paintReq);
                    try
                    {
                        var hulls = UnityEngine.Object.FindObjectsOfType<Mayday.Systems.HullSystem>();
                        var h = hulls != null && hulls.Length > 0 ? hulls[0] : null;
                        if (h != null)
                        {
                            // 走游戏自己的喷漆接口
                            var red = new Color32(204, 31, 31, 255);
                            int panels = 0;
                            try { panels = h.mainColor.Count; } catch { }
                            for (int i = 0; i < panels; i++) h.ServerPaintTick(i, red, false, 3f);
                            h.ApplyAllSectionPaint();
                            h.ApplyDirtyPanels();
                            h.ApplySectionVisuals(false);
                            Plugin.Logs.LogInfo($"[dev] painted hull panels={panels} sync0={h.mainColor[0]} shown0={h.ShownMain(0)}");
                        }
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] paint: " + e.Message); }
                }

                var moveReq = Path.Combine(Plugin.Root, "move_item.txt");
                if (File.Exists(moveReq))
                {
                    string id = File.ReadAllText(moveReq).Trim();
                    File.Delete(moveReq);
                    try
                    {
                        var items = UnityEngine.Object.FindObjectsOfType<Mayday.Item>();
                        var p = Mayday.PlayerNet.Local;
                        var target = (p != null ? p.transform.position : Vector3.zero) + new Vector3(2f, 0f, 2f);
                        int done = 0;
                        for (int i = 0; i < items.Length && done == 0; i++)
                        {
                            var it = items[i];
                            if (it == null) continue;
                            if (!string.Equals(it.ItemId, id, StringComparison.OrdinalIgnoreCase)) continue;
                            Plugin.Logs.LogInfo($"[dev] drop {id} from {it.transform.position} to {target}");
                            it.ServerRelease(target, Quaternion.identity, Vector3.zero);
                            done++;
                        }
                        if (done == 0) Plugin.Logs.LogWarning("[dev] move: no item " + id);
                    }
                    catch (Exception e) { Plugin.Logs.LogError("[dev] move: " + e.Message); }
                }

                var spawnReq = Path.Combine(Plugin.Root, "spawn_item.txt");
                if (File.Exists(spawnReq))
                {
                    string id = File.ReadAllText(spawnReq).Trim();
                    File.Delete(spawnReq);
                    if (id.Length > 0)
                    {
                        try
                        {
                            var p = Mayday.PlayerNet.Local;
                            var pos = (p != null ? p.transform.position : Vector3.zero) + new Vector3(1.5f, 1f, 0f);
                            var it = Mayday.Item.ServerSpawn(id, pos, Quaternion.identity, null, true);
                            Plugin.Logs.LogInfo("[dev] spawn " + id + " -> " + (it != null));
                        }
                        catch (Exception e) { Plugin.Logs.LogError("[dev] spawn: " + e.Message); }
                    }
                }

                var uiReq = Path.Combine(Plugin.Root, "ui_open.txt");
                if (File.Exists(uiReq))
                {
                    File.Delete(uiReq);
                    SaveUI.Ensure(this, transform).Toggle();
                }

                var saveReq = Path.Combine(Plugin.Root, "save_slot.txt");
                if (File.Exists(saveReq))
                {
                    int slot = 0;
                    int.TryParse(File.ReadAllText(saveReq).Trim(), out slot);
                    File.Delete(saveReq);
                    if (slot >= 1 && slot <= SaveManager.SlotCount)
                    {
                        try { Plugin.Logs.LogInfo("[dev] save slot " + slot + ": " + SaveManager.Save(slot, out _, out _)); }
                        catch (Exception e) { Plugin.Logs.LogError("[dev] save: " + e.Message); }
                    }
                }

                var loadReq = Path.Combine(Plugin.Root, "load_slot.txt");
                if (File.Exists(loadReq))
                {
                    int slot = 0;
                    int.TryParse(File.ReadAllText(loadReq).Trim(), out slot);
                    File.Delete(loadReq);
                    if (slot >= 1 && slot <= SaveManager.SlotCount && File.Exists(SaveManager.SlotPath(slot)))
                    {
                        var d = SaveData.LoadFrom(SaveManager.SlotPath(slot));
                        if (GameHost.Instance == null) Plugin.Logs.LogError("[dev] GameHost.Instance is null");
                        else GameHost.Instance.StartLoad(d, "栏位 " + slot);
                    }
                }
            }
            catch (Exception e) { Plugin.Logs.LogError("[dev] trigger: " + e.Message); }
        }

        private void Tick()
        {
            DevTriggers();

            if (!uiReady && t > 3f)
            {
                uiReady = true;
                try { SaveUI.Ensure(this, transform); }
                catch (Exception e) { Plugin.Logs.LogError("ui init: " + e); }
            }

            Hotkeys();
            if (!loggedReady && t > 4f) { loggedReady = true; Plugin.Logs.LogInfo("[init] GameHost.Instance=" + (Instance != null) + " self=" + (Instance == this)); }

            if (job != null)
            {
                job.Tick();
                if (job.Done)
                {
                    Plugin.Logs.LogInfo("[load] " + job.Result);
                    job = null;
                    try { if (SaveUI.Instance != null) SaveUI.Instance.Rebuild(); } catch { }
                }
            }
        }

        private void AlarmProbeStep(int left)
        {
            try
            {
                var pf = Mayday.Flight.PlaneFlight.Instance;
                if (pf != null && left > 24)
                {
                    var sim = pf.sim;
                    sim.Stalling = true;
                    sim.StallWarning = true;
                }
                if (left == 20) SaveManager.CaptureAlarmProbe("DURING");
                if (left == 1)
                {
                    if (pf != null)
                    {
                        var sim = pf.sim;
                        sim.Stalling = false;
                        sim.StallWarning = false;
                    }
                }
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[probe] step: " + e.Message); }
        }

        internal static void ReloadHotkey()
        {
            try
            {
                int code = Plugin.CfgKeyCode != null ? Plugin.CfgKeyCode.Value : -1;
                HotkeyKey = code < 0 ? UnityEngine.InputSystem.Key.F8 : (UnityEngine.InputSystem.Key)code;
                Plugin.Logs.LogInfo("[hotkey] open key = " + HotkeyKey + " (" + code + ")");
            }
            catch (Exception e)
            {
                HotkeyKey = UnityEngine.InputSystem.Key.F8;
                Plugin.Logs.LogWarning("[hotkey] reload: " + e.Message);
            }
        }

        private void Hotkeys()
        {
            try
            {
                if (!hotkeyLoaded) { hotkeyLoaded = true; ReloadHotkey(); }
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (!keyboardSeen)
                {
                    keyboardSeen = true;
                    Plugin.Logs.LogInfo(kb == null ? "[hotkey] keyboard null" : "[hotkey] keyboard ready");
                }
                if (kb == null) return;

                // 设置界面正在等待"按下任意键"
                if (SettingsUI.IsCapturing)
                {
                    SettingsUI.PollCapture(kb);
                    return;
                }

                if (kb[HotkeyKey].wasPressedThisFrame)
                {
                    if (SettingsUI.Instance != null && SettingsUI.Instance.IsOpen) SettingsUI.Instance.Close();
                    else SaveUI.Ensure(this, transform).Toggle();
                    return;
                }

                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (SettingsUI.Instance != null && SettingsUI.Instance.IsOpen) { SettingsUI.Instance.Close(); return; }
                    if (SaveUI.Instance != null && SaveUI.Instance.IsOpen) SaveUI.Instance.Close();
                }
            }
            catch (Exception e)
            {
                if (t < 30f) Plugin.Logs.LogWarning("hotkey: " + e.Message);
            }
        }

        public void StartLoad(SaveData d, string label)
        {
            if (job != null) { Plugin.Notify("正在读档，请稍候", true); return; }
            if (!SaveManager.OnGround) { Plugin.Notify("只能在飞机起飞前读档", true); return; }
            job = new LoadJob(d);
            Plugin.Logs.LogInfo($"[load] start '{label}' -> {SaveGame.Describe(d)}");
            Plugin.Notify("读取存档：" + SaveGame.Describe(d));
        }

        // ------------------------------------------------------------------
        private class LoadJob
        {
            private readonly SaveData d;
            private int step;
            private float waitUntil;
            private int guard;
            private string finishHow = "ground";

            public bool Done;
            public string Result;

            public LoadJob(SaveData data) { d = data; }

            private void Wait(float s) { waitUntil = Time.time + s; }

            public void Tick()
            {
                if (Done) return;
                if (Time.time < waitUntil) return;
                if (++guard > 1200) { Done = true; Result = "timeout"; return; }

                var gs = SaveManager.GS;
                var gd = SaveManager.GD;
                var wn = Mayday.World.WorldNet.Instance;
                if (gs == null || gd == null || wn == null) return;

                switch (step)
                {
                    case 0:
                        if (!Mayday.NetFlow.IsHost) return;
                        Wait(0.3f); step = 1; break;

                    case 1:
                        SaveManager.ClearPassengers();
                        SaveManager.ApplyRun(d);
                        Wait(0.6f); step = 2; break;

                    case 2:
                        SaveManager.BuildWorld(d);
                        Wait(2.0f); step = 3; break;

                    case 3:
                    {
                        int savStage = d.Int("gd.stage", 0);
                        try
                        {
                            gs.ServerSetPhase(Mayday.GamePhase.PreShift);
                            gd.SetStage(Mayday.Airport.DirectorStage.PreShift);
                            gd.GenerateOptions();
                        }
                        catch (Exception e) { Plugin.Logs.LogError("[load] options: " + e.Message); }

                        if (savStage <= 0) { finishHow = "preshift"; Wait(0.8f); step = 5; break; }

                        // 1=Arrived 2=Deplaning 3=Turnaround：航段已经结束，不能再重放"接单"
                        // 否则会自动生成新合同和一批新乘客，跟存档里的乘客状态对不上（幽灵乘客 + 自动接单）
                        if (savStage >= 1 && savStage <= 3)
                        {
                            try
                            {
                                gs.ServerSetPhase(Mayday.GamePhase.Ground);
                                gd.SetStage((Mayday.Airport.DirectorStage)savStage);
                                try { gd.GenerateOptions(); } catch { }
                            }
                            catch (Exception e) { Plugin.Logs.LogError("[load] post-leg stage: " + e.Message); }
                            finishHow = savStage == 3 ? "turnaround" : "arrived";
                            Plugin.Logs.LogInfo("[load] post-leg save, stage=" + savStage + " (no contract replay)");
                            Wait(1.0f); step = 5; break;
                        }

                        try
                        {
                            int opt = d.Int("gd.chosenOption", 0);
                            gd.ServerChooseDestination(opt, Mayday.PlayerNet.Local);
                            Plugin.Logs.LogInfo("[load] replayed leg choice " + opt);
                        }
                        catch (Exception e) { Plugin.Logs.LogError("[load] choose: " + e.Message); }
                        Wait(1.2f); step = 4; break;
                    }

                    case 4:
                        SaveManager.ApplyGameState(d, gs);
                        SaveManager.ApplyWorld(d, wn);
                        SaveManager.ApplyDirector(d, gd);   // 重放接单会重置 slotEnd，这里再盖回存档里的剩余时间
                        try
                        {
                            gs.ServerSetPhase(Mayday.GamePhase.Ground);
                            gd.SetStage(Mayday.Airport.DirectorStage.Boarding);
                        }
                        catch (Exception e) { Plugin.Logs.LogError("[load] ground: " + e.Message); }
                        finishHow = "ground";
                        Wait(1.5f); step = 5; break;

                    case 5:
                        // 物品 / 背包 / 飞机与设备状态（等生成结束再套用）
                        try { SaveManager.ApplyExtras(d); } catch (Exception e) { Plugin.Logs.LogError("[load] extras: " + e.Message); }
                        SaveManager.PlaceAtAirport(d);
                        Wait(1.5f); step = 6; break;

                    case 6:
                        try { SaveManager.RestoreSystems(d); } catch (Exception e) { Plugin.Logs.LogError("[load] extras2: " + e.Message); }
                        try { SaveManager.ApplyWorldState(d); } catch (Exception e) { Plugin.Logs.LogError("[load] worldstate: " + e.Message); }
                        try { SaveManager.RestoreItemsPublic(d); } catch (Exception e) { Plugin.Logs.LogError("[load] items2: " + e.Message); }
                        Wait(2.0f); step = 7; break;

                    case 7:
                        // 再补一遍：世界稳定后飞机/车辆/玩家位置+钱再盖一次，避免被后续逻辑（加油结算等）覆盖
                        try { SaveManager.ApplyGameState(d, gs); } catch (Exception e) { Plugin.Logs.LogError("[load] money2: " + e.Message); }
                        try { SaveManager.ApplyWorldState(d); } catch (Exception e) { Plugin.Logs.LogError("[load] worldstate2: " + e.Message); }
                        try { SaveManager.RestoreItemsPublic(d); } catch (Exception e) { Plugin.Logs.LogError("[load] items3: " + e.Message); }
                        try { SaveManager.ReapplyInventoryStates(d); } catch (Exception e) { Plugin.Logs.LogError("[load] inv3: " + e.Message); }
                        // 只修剪僵死行李（act=0 的幽灵），正常行李和存档内容都不再干预
                        // 行李完全交给游戏自己：不采集、不清理、不重建
                        try { SaveManager.RestoreSeatRows(d); } catch (Exception e) { Plugin.Logs.LogError("[load] seats: " + e.Message); }
                        try { SaveManager.RestorePaxTransforms(d); } catch (Exception e) { Plugin.Logs.LogError("[load] paxtf: " + e.Message); }
                        if (Plugin.CfgBagFreeMode != null && Plugin.CfgBagFreeMode.Value)
                        {
                            try { SaveManager.ClearBagsAndDetachPax(); } catch (Exception e) { Plugin.Logs.LogError("[load] bagfree: " + e.Message); }
                            if (Plugin.CfgPartialBags != null && Plugin.CfgPartialBags.Value)
                            {
                                try { SaveManager.SpawnPartialBags(); } catch (Exception e) { Plugin.Logs.LogError("[load] partial: " + e.Message); }
                            }
                        }
                        else
                        {
                            try { SaveManager.RestoreBagFallback(d); } catch (Exception e) { Plugin.Logs.LogError("[load] bagfb: " + e.Message); }
                        }
                        try
                        {
                            double now = Mayday.World.WorldAuthority.Time;
                            double slotLeft = gd.NetworkslotEnd - now;
                            double autoLeft = gd.NetworkautoDispatchAt - now;
                            Plugin.Logs.LogInfo($"[load] timers after load: slot={slotLeft:F0}s auto={autoLeft:F0}s");
                        }
                        catch { }
                        try { seatVerifyAt = UnityEngine.Time.time + 3.0f; seatVerifyData = d; } catch { }
                        Done = true;
                        Result = finishHow + " " + SaveGame.Describe(SaveGame.Capture());
                        Plugin.Notify(finishHow == "preshift" ? "读档完成，回到出发准备" : "读档完成，回到登机阶段");
                        break;
                }
            }
        }
    }
}
