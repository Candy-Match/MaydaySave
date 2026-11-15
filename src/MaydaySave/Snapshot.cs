using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace MaydaySave
{
    /// <summary>
    /// 通用网络对象快照：把游戏里各类 NetworkBehaviour 的 SyncVar 按"类型+层级路径"记下来，
    /// 覆盖飞机系统、车辆、乘客、座位等。物品由 SaveManager 单独处理。
    /// </summary>
    public static class Snapshot
    {
        private static void AddAll<T>(List<Il2CppObjectBase> into) where T : UnityEngine.Object
        {
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<T>();
                if (arr == null) return;
                for (int i = 0; i < arr.Length; i++)
                    if (arr[i] != null) into.Add(arr[i]);
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[snap] gather " + typeof(T).Name + ": " + e.Message); }
        }

        private static List<Il2CppObjectBase> Gather()
        {
            var list = new List<Il2CppObjectBase>(256);
            AddAll<Mayday.PlayerInteractor>(list);
            AddAll<Mayday.Systems.CapedBoss>(list);
            AddAll<Mayday.Systems.DoorSystem>(list);
            AddAll<Mayday.Systems.ElectricalSystem>(list);
            AddAll<Mayday.Systems.EngineUnit>(list);
            AddAll<Mayday.Systems.FailureDirector>(list);
            AddAll<Mayday.Systems.FireNode>(list);
            AddAll<Mayday.Systems.FuelSystem>(list);
            AddAll<Mayday.Systems.HullSystem>(list);
            AddAll<Mayday.Systems.IgnitionSystem>(list);
            AddAll<Mayday.Systems.TetherRescue>(list);
            AddAll<Mayday.Service.ConsumeNet>(list);
            AddAll<Mayday.Service.DrinkTrolley>(list);
            AddAll<Mayday.Service.GalleySystem>(list);
            AddAll<Mayday.Service.TrashBin>(list);
            AddAll<Mayday.Service.LavSystem>(list);
            AddAll<Mayday.Service.Mess>(list);
            AddAll<Mayday.Service.ServiceDirector>(list);
            AddAll<Mayday.Players.PlayerSync>(list);
            // 乘客不参与通用快照：状态机字段还原不完整会导致幽灵乘客，改由专用流程驱动
            AddAll<Mayday.Passengers.PassengerDirector>(list);
            // 座椅排由专门的 RestoreSeatRows 通过 PassengerDirector.ServerInstallRow 恢复
            AddAll<Mayday.Net.PlayerVoice>(list);
            AddAll<Mayday.Flight.PASystem>(list);
            AddAll<Mayday.Flight.PlaneFlight>(list);
            AddAll<Mayday.Airport.AirStairs>(list);
            AddAll<Mayday.Airport.BagDrop>(list);
            AddAll<Mayday.Airport.BaggageCart>(list);
            AddAll<Mayday.Airport.BaggageTug>(list);
            AddAll<Mayday.Airport.Bank>(list);
            AddAll<Mayday.Airport.CargoLoaders>(list);
            AddAll<Mayday.Airport.Casino>(list);
            AddAll<Mayday.Airport.DispatchBoard>(list);
            AddAll<Mayday.Airport.DrivableVehicle>(list);
            AddAll<Mayday.Airport.Dumpster>(list);
            AddAll<Mayday.Airport.FuelTruck>(list);
            AddAll<Mayday.Airport.FuelTruckDrive>(list);
            AddAll<Mayday.Airport.GroundService>(list);
            AddAll<Mayday.Airport.GroundCrew>(list);
            AddAll<Mayday.Airport.SlotMachine>(list);
            AddAll<Mayday.Airport.SupplyShop>(list);
            return list;
        }

        private static List<PropertyInfo> SyncProps(Type t)
        {
            var list = new List<PropertyInfo>();
            var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                if (p.Name.Length > 7 && p.Name.StartsWith("Network", StringComparison.Ordinal) && p.CanWrite && p.CanRead)
                    list.Add(p);
                else if (IsSyncList(p.PropertyType) && p.CanRead)
                    list.Add(p);
            }
            return list;
        }

        internal static string PathOf(Transform t)
        {
            var sb = new System.Text.StringBuilder(t.name);
            var cur = t.parent;
            int guard = 0;
            while (cur != null && guard++ < 24)
            {
                sb.Insert(0, cur.name + "/");
                cur = cur.parent;
            }
            return sb.ToString();
        }

        private static bool IsSyncList(Type t) =>
            t.FullName != null && t.FullName.StartsWith("Mirror.SyncList", StringComparison.Ordinal);

        private static string EncodeSyncList(object list)
        {
            if (list == null) return "sl:";
            var lt = list.GetType();
            var cntP = lt.GetProperty("Count");
            var itemP = lt.GetProperty("Item");
            if (cntP == null || itemP == null) return null;
            int cnt = (int)cntP.GetValue(list);
            var sb = new System.Text.StringBuilder("sl:");
            for (int i = 0; i < cnt; i++)
            {
                object v;
                try { v = itemP.GetValue(list, new object[] { i }); } catch { return null; }
                if (v == null) { sb.Append("~,"); continue; }
                if (v is float ff) sb.Append(ff.ToString("R", CultureInfo.InvariantCulture));
                else if (v is int ii) sb.Append(ii);
                else if (v is byte bb) sb.Append(bb);
                else if (v is string ss) sb.Append(ss.Replace(",", " ").Replace("(", "[").Replace(")", "]"));
                else if (v is Color32 cc) sb.Append("c(").Append(cc.r).Append(' ').Append(cc.g).Append(' ').Append(cc.b).Append(' ').Append(cc.a).Append(')');
                else if (v is Vector4 vv) sb.Append("v(").Append(vv.x.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                        .Append(vv.y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                        .Append(vv.z.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                        .Append(vv.w.ToString("R", CultureInfo.InvariantCulture)).Append(')');
                else return null;
                sb.Append(',');
            }
            return sb.ToString();
        }

        private static void ApplySyncList(object list, string body)
        {
            if (list == null) return;
            var lt = list.GetType();
            var add = lt.GetMethod("Add");
            var clear = lt.GetMethod("Clear");
            if (add == null || clear == null) return;
            clear.Invoke(list, null);
            var itemType = add.GetParameters()[0].ParameterType;
            foreach (var part in body.Split(','))
            {
                if (part.Length == 0 || part == "~") continue;
                object v;
                if (part.StartsWith("c(", StringComparison.Ordinal) && itemType == typeof(Color32))
                {
                    var nums = part.Trim('c', '(', ')').Split(' ');
                    v = new Color32(byte.Parse(nums[0]), byte.Parse(nums[1]), byte.Parse(nums[2]), byte.Parse(nums[3]));
                }
                else if (part.StartsWith("v(", StringComparison.Ordinal) && itemType == typeof(Vector4))
                {
                    var nums = part.Trim('v', '(', ')').Split(' ');
                    v = new Vector4(float.Parse(nums[0], CultureInfo.InvariantCulture), float.Parse(nums[1], CultureInfo.InvariantCulture),
                                    float.Parse(nums[2], CultureInfo.InvariantCulture), float.Parse(nums[3], CultureInfo.InvariantCulture));
                }
                else if (itemType == typeof(float)) v = float.Parse(part, CultureInfo.InvariantCulture);
                else if (itemType == typeof(int)) v = int.Parse(part, CultureInfo.InvariantCulture);
                else if (itemType == typeof(byte)) v = byte.Parse(part, CultureInfo.InvariantCulture);
                else if (itemType == typeof(string)) v = part;
                else continue;
                add.Invoke(list, new[] { v });
            }
        }
        /// <summary>机场的参考点：世界重建后绝对坐标会变，所有位置都存"相对这个点"的偏移。</summary>
        private static bool TryGetRef(int airportIndex, out Vector3 pos)
        {
            pos = Vector3.zero;
            try
            {
                var site = Mayday.Airport.AirportSites.Find(airportIndex);
                if (site == null) return false;
                Mayday.Airport.AirportSites.WorldPose(site, Mayday.Airport.AnchorKind.SpawnPoint, out pos, out _);
                return true;
            }
            catch { return false; }
        }
        private static uint PaxOrder(Il2CppObjectBase o)
        {
            try { return o.Cast<Mirror.NetworkBehaviour>().netId; } catch { return 0; }
        }

        // ------------------------------------------------------------------
        public static int Capture(SaveData d)
        {
            var objs = Gather();
            var pax = new List<Il2CppObjectBase>();
            var others = new List<Il2CppObjectBase>();
            for (int i = 0; i < objs.Count; i++)
            {
                var t = objs[i].GetType();
                if (t.FullName == "Mayday.Passengers.Passenger") pax.Add(objs[i]);
                else others.Add(objs[i]);
            }
            pax.Sort((a, b) => PaxOrder(a).CompareTo(PaxOrder(b)));

            int n = 0;
            n = CaptureBatch(d, others, n, false);
            n = CaptureBatch(d, pax, n, true);
            d.Set("nb.count", n);
            Plugin.Logs.LogInfo($"[save] snapshot objects={n} (pax={pax.Count})");
            return n;
        }

        private static int CaptureBatch(SaveData d, List<Il2CppObjectBase> objs, int n, bool byIndex)
        {
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                var t = o.GetType();
                var props = SyncProps(t);
                if (props.Count == 0) continue;
                Transform tr = null;
                try { tr = o.Cast<Component>().transform; } catch { }
                if (!byIndex && tr == null) continue;

                string key;
                try { key = t.Name + "|" + (byIndex ? "#" + i : PathOf(tr)); }
                catch { continue; }

                string k = "nb." + n;
                int pi = 0;
                foreach (var p in props)
                {
                    if (IsSyncList(p.PropertyType))
                    {
                        string sl = null;
                        try { sl = EncodeSyncList(p.GetValue(o)); } catch { }
                        if (sl != null) { d.Set(k + ".p" + pi, p.Name + "=" + sl); pi++; }
                        continue;
                    }
                    object v;
                    try { v = p.GetValue(o); } catch { continue; }
                    string enc = Encode(v, p.PropertyType);
                    if (enc == null) continue;
                    d.Set(k + ".p" + pi, p.Name + "=" + enc);
                    pi++;
                }
                if (pi == 0) continue;
                d.Set(k + ".key", key);
                d.Set(k + ".n", pi);
                n++;
                if (n >= 1500) return n;
            }
            return n;
        }

        public static int Restore(SaveData d)
        {
            int n = d.Int("nb.count");
            if (n <= 0) return 0;

            var objs = Gather();
            var pax = new List<Il2CppObjectBase>();
            var others = new List<Il2CppObjectBase>();
            for (int i = 0; i < objs.Count; i++)
            {
                var t = objs[i].GetType();
                if (t.FullName == "Mayday.Passengers.Passenger") pax.Add(objs[i]);
                else others.Add(objs[i]);
            }
            pax.Sort((a, b) => PaxOrder(a).CompareTo(PaxOrder(b)));

            var map = new Dictionary<string, Il2CppObjectBase>(StringComparer.Ordinal);
            for (int i = 0; i < others.Count; i++)
            {
                var o = others[i];
                try
                {
                    var tr = o.Cast<Component>().transform;
                    string key = o.GetType().Name + "|" + PathOf(tr);
                    if (!map.ContainsKey(key)) map[key] = o;
                }
                catch { }
            }
            for (int i = 0; i < pax.Count; i++)
            {
                string key = "Passenger|#" + i;
                if (!map.ContainsKey(key)) map[key] = pax[i];
            }

            int applied = 0, missing = 0, props = 0;
            for (int i = 0; i < n; i++)
            {
                string k = "nb." + i;
                string key = d.Str(k + ".key");
                if (string.IsNullOrEmpty(key)) continue;
                if (!map.TryGetValue(key, out var o)) { missing++; continue; }
                int cnt = d.Int(k + ".n");
                for (int p = 0; p < cnt; p++)
                {
                    string line = d.Str(k + ".p" + p);
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var prop = o.GetType().GetProperty(line.Substring(0, eq),
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop == null || !prop.CanWrite) continue;
                    try
                    {
                        string raw = line.Substring(eq + 1);
                        if (raw.StartsWith("sl:", StringComparison.Ordinal))
                        {
                            ApplySyncList(prop.GetValue(o), raw.Substring(3));
                            props++;
                            continue;
                        }
                        object v = Decode(raw, prop.PropertyType);
                        if (v != null) { prop.SetValue(o, v); props++; }
                    }
                    catch { }
                }
                applied++;
            }
            Plugin.Logs.LogInfo($"[load] snapshot applied={applied} missing={missing} props={props}");

            // 让机身的喷漆/破洞/凹陷立刻重建
            try
            {
                var hulls = UnityEngine.Object.FindObjectsOfType<Mayday.Systems.HullSystem>();
                if (hulls != null)
                {
                    for (int i = 0; i < hulls.Length; i++)
                    {
                        var h = hulls[i];
                        if (h == null) continue;
                        try { h.ApplyAllSectionPaint(); } catch { }
                        try { h.ApplyDirtyPanels(); } catch { }
                        try { h.ApplySectionVisuals(false); } catch { }
                    }
                    Plugin.Logs.LogInfo("[load] hull visuals refreshed: " + hulls.Length);
                    var h0 = hulls.Length > 0 ? hulls[0] : null;
                    if (h0 != null)
                    {
                        try
                        {
                            var dm = h0.damage;
                            var mc = h0.mainColor;
                            Plugin.Logs.LogInfo($"[load] hull damage[0..4]={dm[0]:F2},{dm[1]:F2},{dm[2]:F2},{dm[3]:F2},{dm[4]:F2} color0={mc[0]}");
                        }
                        catch (Exception e) { Plugin.Logs.LogWarning("[load] hull peek: " + e.Message); }
                    }
                }
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] hull refresh: " + e.Message); }

            return applied;
        }

        // ---------------- 载具/飞机 位置 ----------------
        private static readonly string[] VehicleTypes =
        {
            "AirStairs", "CargoLoaders", "BaggageCart", "BaggageTug",
            "FuelTruck", "FuelTruckDrive", "DrivableVehicle", "BagDrop", "SupplyShop"
        };

        private static string DescribeTf(Transform tr)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("'").Append(tr.name).Append("' pos=").Append(tr.position);
                sb.Append(" chain:");
                var cur = tr.parent;
                int guard = 0;
                while (cur != null && guard++ < 6)
                {
                    sb.Append(" [").Append(cur.name).Append(' ');
                    sb.Append(cur.position.ToString("F2")).Append(']');
                    cur = cur.parent;
                }
                var comps = tr.gameObject.GetComponents(Il2CppType.Of<Component>());
                if (comps != null)
                {
                    sb.Append(" comps=");
                    for (int i = 0; i < comps.Length && i < 12; i++)
                        if (comps[i] != null) sb.Append(comps[i].GetType().Name).Append(',');
                }
                return sb.ToString();
            }
            catch (Exception e) { return "describe failed: " + e.Message; }
        }

        private static List<Transform> VehicleList(string type)
        {
            var objs = new List<Il2CppObjectBase>();
            if (type == "AirStairs") AddAll<Mayday.Airport.AirStairs>(objs);
            else if (type == "CargoLoaders") AddAll<Mayday.Airport.CargoLoaders>(objs);
            else if (type == "BaggageCart") AddAll<Mayday.Airport.BaggageCart>(objs);
            else if (type == "BaggageTug") AddAll<Mayday.Airport.BaggageTug>(objs);
            else if (type == "FuelTruck") AddAll<Mayday.Airport.FuelTruck>(objs);
            else if (type == "FuelTruckDrive") AddAll<Mayday.Airport.FuelTruckDrive>(objs);
            else if (type == "DrivableVehicle") AddAll<Mayday.Airport.DrivableVehicle>(objs);
            else if (type == "BagDrop") AddAll<Mayday.Airport.BagDrop>(objs);
            else if (type == "SupplyShop") AddAll<Mayday.Airport.SupplyShop>(objs);

            var list = new List<Transform>(objs.Count);
            for (int i = 0; i < objs.Count; i++)
            {
                try { list.Add(objs[i].Cast<Component>().transform); } catch { }
            }
            list.Sort((a, b) => string.CompareOrdinal(PathOf(a), PathOf(b)));
            return list;
        }

        internal static Vector3 StandRef(int airportIndex)
        {
            try
            {
                var site = Mayday.Airport.AirportSites.Find(airportIndex);
                if (site != null)
                {
                    Mayday.Flight.PlaneFlight.StandWorldPose(site, out var p, out _);
                    return p;
                }
            }
            catch { }
            return Vector3.zero;
        }

        /// <summary>读档时的整体平移量：新世界基准点 - 存档时基准点。</summary>
        private static Vector3 Delta = Vector3.zero;

        public static void PrepareRestore(SaveData d)
        {
            Delta = Vector3.zero;
            try
            {
                if (d.Has("ref.stand"))
                {
                    var saved = ParseVec(d.Str("ref.stand"));
                    var now = StandRef(d.Int("gs.current"));
                    Delta = now - saved;
                    Plugin.Logs.LogInfo($"[load] world delta {Delta} (saved {saved} -> now {now})");
                }
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] delta: " + e.Message); }
        }

        internal static Vector3 RefFor(int airportIndex)
        {
            return TryGetRef(airportIndex, out var p) ? p : Vector3.zero;
        }

        public static void CaptureTransforms(SaveData d)
        {
            try { if (TryGetRef(GS != null ? GS.CurrentAirport : 0, out var r0)) CaptureRef = r0; } catch { }
            int n = 0;
            try
            {
                var pf = Mayday.Flight.PlaneFlight.Instance;
                if (pf != null)
                {
                    try { Plugin.Logs.LogInfo("[save] plane " + DescribeTf(pf.transform)); } catch { }
                    // sim 才是权威的飞行状态（transform 只是逻辑挂点，永远在原点）
                    var sim = pf.sim;
                    // PlaneWorldPos / sim.Pos 都是"世界坐标"，不要做任何转换
                    d.Set("plane.wpos", Vec(sim.Pos));
                    d.Set("plane.pos", Vec(sim.Pos));
                    Plugin.Logs.LogInfo($"[save] plane world={sim.Pos} hdg={sim.Heading:F1}");
                    d.Set("plane.heading", sim.Heading);
                    d.Set("plane.onGround", sim.OnGround);
                    d.Set("plane.speed", sim.Speed);
                    d.Set("plane.gear", sim.GearPos);
                    d.Set("plane.flap", sim.FlapPos);
                    d.Set("plane.steer", sim.SteerAngle);
                    Plugin.Logs.LogInfo($"[save] plane sim Pos={sim.Pos} Hdg={sim.Heading:F1} onGround={sim.OnGround} gear={sim.GearPos:F2}");
                    n++;
                }
            }
            catch { }

            for (int t = 0; t < VehicleTypes.Length; t++)
            {
                string type = VehicleTypes[t];
                List<Transform> list;
                try { list = VehicleList(type); } catch { continue; }
                for (int i = 0; i < list.Count; i++)
                {
                    string k = "tf." + n;
                    d.Set(k + ".key", type + "#" + i);
                    d.Set(k + ".path", PathOf(list[i]));
                    d.Set(k + ".pos", Vec(list[i].position));
                    try { d.Set(k + ".parent", list[i].parent == null ? "" : PathOf(list[i].parent)); } catch { }
                    d.Set(k + ".rot", Rot(list[i].rotation));
                    n++;
                }
            }
            d.Set("tf.count", n);
            Plugin.Logs.LogInfo("[save] transforms " + n);
        }

        public static void RestoreTransforms(SaveData d)
        {
            int n = d.Int("tf.count");
            if (n <= 0) return;
            try { if (TryGetRef(d.Int("gs.current"), out var r1)) RestoreRef = r1; } catch { }
            int applied = 0;
            try
            {
                var counts = "";
                for (int t = 0; t < VehicleTypes.Length; t++)
                {
                    int c = 0;
                    try { c = VehicleList(VehicleTypes[t]).Count; } catch { }
                    if (c > 0) counts += VehicleTypes[t] + "=" + c + " ";
                }
                Plugin.Logs.LogInfo("[load] live vehicles: " + counts);
            }
            catch { }
            for (int i = 0; i < n; i++)
            {
                string k = "tf." + i;
                string key = d.Str(k + ".key");
                if (string.IsNullOrEmpty(key)) continue;

                Transform tr = null;
                if (key.StartsWith("PLANE", StringComparison.Ordinal)) continue;
                else
                {
                    int hash = key.LastIndexOf('#');
                    if (hash <= 0) continue;
                    string type = key.Substring(0, hash);
                    int idx = 0;
                    int.TryParse(key.Substring(hash + 1), out idx);
                    List<Transform> list;
                    try { list = VehicleList(type); } catch { continue; }
                    if (list.Count == 0) continue;

                    // 优先按路径找，否则按序号
                    string path = d.Str(k + ".path");
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (string.Equals(PathOf(list[j]), path, StringComparison.Ordinal)) { tr = list[j]; break; }
                    }
                    if (tr == null && idx < list.Count) tr = list[idx];
                }
                if (tr == null) continue;

                try
                {
                    // 先还原父子关系（拖车/行李框被拖走后，读档要断开这个挂接）
                    try
                    {
                        string wantParent = d.Str(k + ".parent");
                        string curParent = tr.parent == null ? "" : PathOf(tr.parent);
                        if (!string.Equals(wantParent, curParent, StringComparison.Ordinal))
                        {
                            Transform np = null;
                            if (!string.IsNullOrEmpty(wantParent))
                            {
                                var go = UnityEngine.GameObject.Find(wantParent);
                                if (go != null) np = go.transform;
                            }
                            if (string.IsNullOrEmpty(wantParent) || np != null)
                            {
                                tr.SetParent(np, true);
                                Plugin.Logs.LogInfo($"[load] parent {key}: '{(string.IsNullOrEmpty(curParent) ? "none" : curParent)}' -> '{wantParent}'");
                            }
                        }
                    }
                    catch (Exception e0) { Plugin.Logs.LogWarning("[load] parent: " + e0.Message); }

                    var pos = ParseVec(d.Str(k + ".pos"));
                    var rot = ParseRot(d.Str(k + ".rot"));
                    bool done = false;
                    var dvComp = tr.gameObject.GetComponent(Il2CppType.Of<Mayday.Airport.DrivableVehicle>());
                    if (dvComp != null)
                    {
                        var dv = dvComp.Cast<Mayday.Airport.DrivableVehicle>();
                        dv.DevTeleport(pos, rot.eulerAngles.y);
                        try { dv.PollPose(); } catch { }
                        done = true;
                    }
                    if (!done)
                    {
                        tr.position = pos;
                        tr.rotation = rot;
                    }
                    applied++;
                }
                catch (Exception e1) { Plugin.Logs.LogWarning("[load] tf " + key + ": " + e1.Message); continue; }
                try
                {
                    var rb = tr.gameObject.GetComponent(Il2CppType.Of<Rigidbody>());
                    if (rb != null)
                    {
                        var body = rb.Cast<Rigidbody>();
                        body.velocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                    }
                }
                catch { }
            }
            Plugin.Logs.LogInfo($"[load] transforms {applied}/{n}");
        }
        private static Transform FindByPath(string type, string path)
        {
            var objs = new List<Il2CppObjectBase>();
            if (type == "AirStairs") AddAll<Mayday.Airport.AirStairs>(objs);
            else if (type == "CargoLoaders") AddAll<Mayday.Airport.CargoLoaders>(objs);
            else if (type == "BaggageCart") AddAll<Mayday.Airport.BaggageCart>(objs);
            else if (type == "BaggageTug") AddAll<Mayday.Airport.BaggageTug>(objs);
            else if (type == "FuelTruck") AddAll<Mayday.Airport.FuelTruck>(objs);
            else if (type == "FuelTruckDrive") AddAll<Mayday.Airport.FuelTruckDrive>(objs);
            else if (type == "DrivableVehicle") AddAll<Mayday.Airport.DrivableVehicle>(objs);
            else if (type == "BagDrop") AddAll<Mayday.Airport.BagDrop>(objs);
            else if (type == "SupplyShop") AddAll<Mayday.Airport.SupplyShop>(objs);
            for (int i = 0; i < objs.Count; i++)
            {
                try
                {
                    var tr = objs[i].Cast<Component>().transform;
                    if (string.Equals(PathOf(tr), path, StringComparison.Ordinal)) return tr;
                }
                catch { }
            }
            return null;
        }
        // ------------------------------------------------------------------
        public static void CapturePlane(SaveData d)
        {
            try
            {
                var p = Mayday.Flight.PlaneFlight.Instance;
                if (p == null) return;
                d.Set("plane.pos", Vec(p.transform.position));
                d.Set("plane.rot", Rot(p.transform.rotation));
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[save] plane: " + e.Message); }
        }

        public static void RestorePlane(SaveData d)
        {
            try
            {
                var pf = Mayday.Flight.PlaneFlight.Instance;
                if (pf == null) return;

                Vector3 target;
                float heading;
                if (d.Has("plane.wpos")) target = ParseVec(d.Str("plane.wpos"));
                else if (d.Has("plane.pos")) target = ParseVec(d.Str("plane.pos"));
                else return;
                heading = d.Float("plane.heading", 0f);

                if (target.magnitude > 60000f)
                {
                    Plugin.Logs.LogWarning("[load] plane target out of range, skipped");
                    return;
                }

                pf.ServerTeleport(target, heading);
                var sim = pf.sim;
                try { sim.GearPos = d.Float("plane.gear", sim.GearPos); } catch { }
                try { sim.FlapPos = d.Float("plane.flap", sim.FlapPos); } catch { }
                Plugin.Logs.LogInfo($"[load] plane sim -> {target} hdg={heading:F1} (asked onGround={d.Bool("plane.onGround")})");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] plane: " + e.Message); }
        }
        // ------------------------------------------------------------------
        private static Mayday.GameState GS { get { try { return Mayday.GameState.Instance; } catch { return null; } } }
        private static Vector3 PlaneSimPos()
        {
            try
            {
                var pf = Mayday.Flight.PlaneFlight.Instance;
                if (pf != null) return pf.sim.Pos;
            }
            catch { }
            return Vector3.zero;
        }

        private static Vector3 CaptureRef = Vector3.zero;
        private static Vector3 RestoreRef = Vector3.zero;

        public static void CapturePlayers(SaveData d)
        {
            try { if (TryGetRef(GS != null ? GS.CurrentAirport : 0, out var r)) CaptureRef = r; } catch { }
            try
            {
                var players = Mayday.PlayerNet.All;
                if (players == null) return;
                int n = 0;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null) continue;
                    string k = "pl." + n;
                    d.Set(k + ".steam", ((long)p.SteamId).ToString(CultureInfo.InvariantCulture));
                    d.Set(k + ".name", p.DisplayName);
                    d.Set(k + ".rel", Vec(p.transform.position - PlaneSimPos()));
                    d.Set(k + ".pos", Vec(p.transform.position));
                    d.Set(k + ".rot", Rot(p.transform.rotation));
                    d.Set(k + ".dead", p.IsDead);
                    d.Set(k + ".deaths", p.Deaths);
                    n++;
                }
                d.Set("pl.count", n);
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[save] players: " + e.Message); }
        }

        public static void RestorePlayers(SaveData d)
        {
            try { if (TryGetRef(d.Int("gs.current"), out var r)) RestoreRef = r; } catch { }
            try
            {
                int n = d.Int("pl.count");
                if (n <= 0) return;
                var players = Mayday.PlayerNet.All;
                if (players == null) return;
                int moved = 0;
                for (int i = 0; i < n; i++)
                {
                    string k = "pl." + i;
                    long steam = d.Long(k + ".steam");
                    Mayday.PlayerNet target = null;
                    for (int p = 0; p < players.Count; p++)
                    {
                        if (players[p] == null) continue;
                        if ((long)players[p].SteamId == steam) { target = players[p]; break; }
                    }
                    if (target == null)
                    {
                        Plugin.Logs.LogWarning($"[load] player steam={steam} not found (have {players.Count})");
                        continue;
                    }
                    try
                    {
                        target.NetworkIsDead = d.Bool(k + ".dead");
                        target.NetworkDeaths = (byte)d.Int(k + ".deaths");
                        float yaw = ParseRot(d.Str(k + ".rot")).eulerAngles.y;
                        Vector3 pos;
                        pos = ParseVec(d.Str(k + ".pos"));

                        // 场景坐标的合理范围检查，避免被扔到远海
                        if (pos.magnitude > 12000f)
                        {
                            Plugin.Logs.LogWarning($"[load] player {target.DisplayName} target {pos} out of range, skipped");
                            continue;
                        }
                        target.ServerTeleport(pos, yaw);
                        moved++;
                    }
                    catch (Exception e) { Plugin.Logs.LogWarning("[load] player move: " + e.Message); }
                }
                Plugin.Logs.LogInfo($"[load] players placed {moved}/{n}");
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[load] players: " + e.Message); }
        }

        // -------------------------- 编码 --------------------------
        private static string Num(float f) => f.ToString("R", CultureInfo.InvariantCulture);
        private static string Vec(Vector3 v) => Num(v.x) + "," + Num(v.y) + "," + Num(v.z);
        private static string Rot(Quaternion q) => Num(q.x) + "," + Num(q.y) + "," + Num(q.z) + "," + Num(q.w);

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
            if (v is float f) return "f:" + Num(f);
            if (v is double dd) return "d:" + dd.ToString("R", CultureInfo.InvariantCulture);
            if (v is string s) return "s:" + s;
            if (v is Vector3 v3) return "v3:" + Vec(v3);
            if (v is Vector2 v2) return "v2:" + Num(v2.x) + "," + Num(v2.y);
            if (v is Quaternion q) return "q:" + Rot(q);
            if (v is Color32 c32) return "c32:" + c32.r + "," + c32.g + "," + c32.b + "," + c32.a;
            if (v is Color c) return "c:" + Num(c.r) + "," + Num(c.g) + "," + Num(c.b) + "," + Num(c.a);
            if (t.IsValueType && !t.IsPrimitive)
            {
                var sb = new System.Text.StringBuilder("st:");
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
            string kind = enc.Substring(0, c), body = enc.Substring(c + 1);
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

        private static float[] ParseNums(string s)
        {
            var parts = s.Split(',');
            var r = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]);
            return r;
        }

        internal static Vector3 ParseVec(string s)
        {
            var p = ParseNums(s);
            return p.Length >= 3 ? new Vector3(p[0], p[1], p[2]) : Vector3.zero;
        }

        internal static Quaternion ParseRot(string s)
        {
            var p = ParseNums(s);
            return p.Length >= 4 ? new Quaternion(p[0], p[1], p[2], p[3]) : Quaternion.identity;
        }
    }
}
