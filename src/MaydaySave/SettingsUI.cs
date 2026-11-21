using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace MaydaySave
{
    /// <summary>设置面板：热键 + 四个开关 + 重置。风格与存读档面板一致（游戏原生 UIF 组合）。</summary>
    public class SettingsUI
    {
        public static SettingsUI Instance;

        private const float PanelW = 980f;
        private const float PanelH = 720f;

        private readonly Il2CppSystem.Object owner;
        private RectTransform layer;
        private GameObject host;
        private GameObject dimmerGo;
        private GameObject panelGo;
        private GameObject confirmGo;
        private bool open;

        public bool IsOpen => open;

        // ── 按键捕获状态（由 GameHost.Hotkeys 驱动）──
        public static bool IsCapturing;

        public SettingsUI(Il2CppObjectBase owner)
        {
            this.owner = owner == null ? null : owner.Cast<Il2CppSystem.Object>();
        }

        public static SettingsUI Ensure(Il2CppObjectBase owner, Transform fallbackParent)
        {
            if (Instance != null) return Instance;
            Instance = new SettingsUI(owner);
            Instance.BuildHost(fallbackParent);
            return Instance;
        }

        public static void TogglePanel(Il2CppObjectBase owner, Transform fallbackParent)
        {
            var ui = Ensure(owner, fallbackParent);
            if (ui.IsOpen) ui.Close();
            else ui.Open();
        }

        // ------------------------------------------------------------------
        private void BuildHost(Transform fallbackParent)
        {
            RectTransform uiLayer = null;
            try
            {
                var root = Mayday.UI.UIRoot.Instance;
                if (root != null) uiLayer = root.PopupLayer;
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[cfg] uilayer: " + e.Message); }

            host = new GameObject("MaydaySettingsUI", Il2CppType.Of<RectTransform>());
            host.transform.SetParent(uiLayer != null ? (Transform)uiLayer : fallbackParent, false);
            try { host.transform.SetAsLastSibling(); } catch { }

            layer = host.GetComponent(Il2CppType.Of<RectTransform>()).Cast<RectTransform>();
            if (uiLayer == null)
            {
                UnityEngine.Object.DontDestroyOnLoad(host);
                var canvas = host.AddComponent(Il2CppType.Of<Canvas>()).Cast<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 20001;
                var scaler = host.AddComponent(Il2CppType.Of<UnityEngine.UI.CanvasScaler>()).Cast<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                host.AddComponent(Il2CppType.Of<UnityEngine.UI.GraphicRaycaster>());
            }

            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = Vector2.zero;
            layer.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------
        private static void Place(RectTransform rt, float x, float y, float w, float h, Vector2 pivot)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static RectTransform MakeButton(Transform parent, string name, string text, Action onClick,
            Color bg, Color fg, float w, float h, float fontSize, Vector2 pos, Vector2 pivot)
        {
            var img = Mayday.UI.UIF.Round(parent, name, bg, 10f, true);
            var rt = img.rectTransform;
            Place(rt, pos.x, pos.y, w, h, pivot);

            var btn = img.gameObject.AddComponent(Il2CppType.Of<UnityEngine.UI.Button>()).Cast<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            try
            {
                var cb = btn.colors;
                cb.normalColor = new Color(1f, 1f, 1f, 1f);
                cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
                cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                cb.disabledColor = new Color(1f, 1f, 1f, 0.32f);
                cb.colorMultiplier = 1f;
                btn.colors = cb;
            }
            catch { }

            if (onClick != null)
                btn.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(onClick));

            var label = Mayday.UI.UIF.Text(img.transform, "Label", text, fontSize, fg,
                TMPro.TextAlignmentOptions.Center, false, false);
            var lrt = label.Rect;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4f, 2f);
            lrt.offsetMax = new Vector2(-4f, -2f);
            return rt;
        }

        private static string KeyName()
        {
            try { return GameHost.HotkeyKey.ToString(); } catch { return "F8"; }
        }

        // 简化：直接读 Plugin 的配置项
        private static bool Notify => Plugin.CfgNotify != null && Plugin.CfgNotify.Value;
        private static bool BagFree => Plugin.CfgBagFreeMode != null && Plugin.CfgBagFreeMode.Value;
        private static bool Partial => Plugin.CfgPartialBags != null && Plugin.CfgPartialBags.Value;
        private static bool Fallback => Plugin.CfgBagFallback != null && Plugin.CfgBagFallback.Value;

        // ------------------------------------------------------------------
        public void Open()
        {
            try
            {
                CloseConfirm();
                if (panelGo != null) { UnityEngine.Object.Destroy(panelGo); panelGo = null; }
                if (dimmerGo != null) { UnityEngine.Object.Destroy(dimmerGo); dimmerGo = null; }

                var dimmer = Mayday.UI.UIF.Dimmer(layer, 0.62f);
                // 整体提到最上层，避免盖在存读档面板下面
                try { host.transform.SetAsLastSibling(); } catch { }
                dimmerGo = dimmer.gameObject;
                var drt = dimmer.rectTransform;
                drt.anchorMin = Vector2.zero;
                drt.anchorMax = Vector2.one;
                drt.offsetMin = Vector2.zero;
                drt.offsetMax = Vector2.zero;
                dimmerGo.transform.SetAsFirstSibling();

                var panel = Mayday.UI.UIF.Panel(layer, "SettingsPanel",
                    new Color(0.07f, 0.09f, 0.11f, 0.985f), 22f, true, true);
                var proot = panel.Root;
                panelGo = proot.gameObject;

                var layerSize = layer.rect.size;
                Place(proot, layerSize.x * 0.5f, layerSize.y * 0.5f, PanelW, PanelH, new Vector2(0.5f, 0.5f));

                BuildContent(proot);
                open = true;
                try { Mayday.UIState.Push(owner); } catch { }
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Plugin.Logs.LogInfo("[cfg] settings opened");
            }
            catch (Exception e)
            {
                open = false;
                Plugin.Logs.LogError("[cfg] open failed: " + e);
            }
        }

        public void Close()
        {
            open = false;
            IsCapturing = false;
            try
            {
                CloseConfirm();
                if (panelGo != null) UnityEngine.Object.Destroy(panelGo);
                if (dimmerGo != null) UnityEngine.Object.Destroy(dimmerGo);
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[cfg] close: " + e.Message); }
            panelGo = null;
            dimmerGo = null;
            try { Mayday.UIState.Pop(owner); } catch { }
            Plugin.Logs.LogInfo("[cfg] settings closed");
        }

        private void Rebuild()
        {
            if (!open) return;
            var wasOpen = open;
            try
            {
                if (panelGo != null) { UnityEngine.Object.Destroy(panelGo); panelGo = null; }
                if (dimmerGo != null) { UnityEngine.Object.Destroy(dimmerGo); dimmerGo = null; }
            }
            catch { }
            open = false;
            if (wasOpen) Open();
        }

        // ------------------------------------------------------------------
        private void BuildContent(RectTransform proot)
        {
            var title = Mayday.UI.UIF.Text(proot, "Title", "设置", 38f,
                Mayday.UI.UITheme.Cream, TMPro.TextAlignmentOptions.Center, true, false);
            Place(title.Rect, PanelW * 0.5f, PanelH - 54f, 600f, 50f, new Vector2(0.5f, 0.5f));

            var sub = Mayday.UI.UIF.Text(proot, "Sub",
                IsCapturing ? "按下任意键作为新的打开键（ESC 取消）" : "改动会立即写入配置文件 · 标 ⚠ 的项会改变读档行为",
                18f, IsCapturing ? Mayday.UI.UITheme.Orange : new Color(0.55f, 0.63f, 0.67f),
                TMPro.TextAlignmentOptions.Center, false, false);
            Place(sub.Rect, PanelW * 0.5f, PanelH - 92f, PanelW - 80f, 26f, new Vector2(0.5f, 0.5f));

            float top = PanelH - 150f;
            float step = 84f;

            // 1) 热键
            Row(proot, 0, top, "打开界面的按键", "点右侧按钮后按任意键即可更改");
            MakeButton(proot, "KeyBind", IsCapturing ? "按下任意键…" : KeyName(),
                () => { IsCapturing = !IsCapturing; Rebuild(); },
                IsCapturing ? Mayday.UI.UITheme.Orange : new Color(0.16f, 0.20f, 0.23f, 1f),
                Mayday.UI.UITheme.Cream, 210f, 46f, 22f,
                new Vector2(PanelW - 78f, top + step * 0f - 6f), new Vector2(1f, 0.5f));

            // 2) 提示
            Row(proot, 1, top - step, "存读档提示", "存/读档成功时在游戏内弹出小提示");
            MakeToggle(proot, "Notify", Notify, top - step, (v) =>
            {
                SetBool(Plugin.CfgNotify, v);
                Plugin.Notify("提示已" + (v ? "开启" : "关闭"));
                Rebuild();
            });

            // 3) 无行李模式（危险）
            Row(proot, 2, top - step * 2, "⚠ 无行李模式", "读档时清空所有行李，并把乘客设为「无行李」");
            MakeToggle(proot, "BagFree", BagFree, top - step * 2, (v) =>
            {
                Confirm("无行李模式", v
                        ? "开启后，读档会**销毁世界里所有行李**，并把所有乘客设为「没有行李」。\n\n这样可以跳过行李环节、不会因为没行李扣钱，但行李不会按存档还原。"
                        : "关闭后，读档不再清空行李，行李保持当前状态。\n\n可能出现「看得见拿不动」的幽灵行李，乘客也可能因没行李超时扣钱。",
                    () => { SetBool(Plugin.CfgBagFreeMode, v); Rebuild(); });
            });

            // 4) 随机部分行李（危险）
            Row(proot, 3, top - step * 3, "⚠ 随机部分行李", "随机挑几位乘客保留行李需求，发给玩家");
            MakeToggle(proot, "Partial", Partial, top - step * 3, (v) =>
            {
                Confirm("随机部分行李", v
                        ? "开启后，在「无行李模式」基础上随机挑 1~N 位乘客保留行李需求，\n并把行李补发到玩家脚边（拿不到则进背包）。\n\n这会额外生成行李实体，会计入游戏内的行李总数。"
                        : "关闭后，读档不再随机发放行李。",
                    () => { SetBool(Plugin.CfgPartialBags, v); Rebuild(); });
            });

            // 5) 行李兜底（危险）
            Row(proot, 4, top - step * 4, "⚠ 行李兜底补发", "无行李模式关闭时：行李不足就补发（占总数）");
            MakeToggle(proot, "Fallback", Fallback, top - step * 4, (v) =>
            {
                Confirm("行李兜底补发", v
                        ? "开启后，读档时若行李数量不够，会补发生成可交互的行李。\n\n注意：这些行李会计入游戏内的行李总数。"
                        : "关闭后，读档不再补发行李。",
                    () => { SetBool(Plugin.CfgBagFallback, v); Rebuild(); });
            });

            // 底部说明
            var ver = Mayday.UI.UIF.Text(proot, "Ver",
                "MaydaySave v" + (Plugin.VersionText ?? "?") + " · 配置保存在 " + Plugin.ConfigPathText,
                15f, new Color(0.42f, 0.50f, 0.54f), TMPro.TextAlignmentOptions.Center, false, false);
            Place(ver.Rect, PanelW * 0.5f, 148f, PanelW - 120f, 22f, new Vector2(0.5f, 0.5f));

            var warn = Mayday.UI.UIF.Text(proot, "Warn",
                "快捷键在聊天/输入框里也会响应，建议选不常用的键", 16f,
                new Color(0.48f, 0.56f, 0.60f), TMPro.TextAlignmentOptions.Center, false, false);
            Place(warn.Rect, PanelW * 0.5f, 118f, PanelW - 120f, 24f, new Vector2(0.5f, 0.5f));

            MakeButton(proot, "Reset", "重置设置", OnReset,
                new Color(0.55f, 0.22f, 0.20f, 0.92f), new Color(1f, 0.92f, 0.90f),
                200f, 48f, 22f, new Vector2(PanelW * 0.5f - 118f, 56f), new Vector2(0.5f, 0.5f));

            MakeButton(proot, "Close", "关闭", Close,
                new Color(1f, 1f, 1f, 0.12f), Mayday.UI.UITheme.Cream,
                200f, 48f, 22f, new Vector2(PanelW * 0.5f + 118f, 56f), new Vector2(0.5f, 0.5f));
        }

        private void Row(RectTransform proot, int index, float y, string label, string hint)
        {
            var bg = Mayday.UI.UIF.Round(proot, "RowBg" + index, new Color(0.12f, 0.15f, 0.17f, 0.75f), 14f, false);
            Place(bg.rectTransform, 60f, y - 6f, PanelW - 120f, 70f, new Vector2(0f, 0.5f));

            var t = Mayday.UI.UIF.Text(proot, "RowLabel" + index, label, 24f,
                Mayday.UI.UITheme.Cream, TMPro.TextAlignmentOptions.Left, false, false);
            Place(t.Rect, 92f, y + 9f, 520f, 30f, new Vector2(0f, 0.5f));

            var h = Mayday.UI.UIF.Text(proot, "RowHint" + index, hint, 16f,
                new Color(0.50f, 0.58f, 0.62f), TMPro.TextAlignmentOptions.Left, false, false);
            Place(h.Rect, 92f, y - 19f, 560f, 24f, new Vector2(0f, 0.5f));
        }

        private void MakeToggle(RectTransform proot, string name, bool value, float y, Action<bool> onFlip)
        {
            var bg = value
                ? new Color(0.13f, 0.62f, 0.57f, 1f)
                : new Color(0.18f, 0.22f, 0.25f, 1f);
            var text = value ? "开启" : "关闭";
            var fg = value ? Color.white : new Color(0.62f, 0.70f, 0.74f);
            MakeButton(proot, "Tg_" + name, text,
                () => { var nv = !value; onFlip(nv); },
                bg, fg, 132f, 46f, 21f,
                new Vector2(PanelW - 78f, y - 6f), new Vector2(1f, 0.5f));
        }

        private static void SetBool(ConfigEntry<bool> e, bool v)
        {
            if (e == null) return;
            try { e.Value = v; Plugin.Logs.LogInfo($"[cfg] {e.Key} = {v}"); } catch (Exception ex) { Plugin.Logs.LogError("[cfg] set: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        private void OnReset()
        {
            Confirm("重置设置",
                "确定把所有设置恢复默认吗？\n\n打开键：F8\n提示：开启\n无行李模式：开启\n随机部分行李：关闭\n行李兜底：开启",
                () => { ApplyDefaults(); Rebuild(); });
        }

        /// <summary>开发调试：弹一个示例确认框。</summary>
        public static void DebugConfirm()
        {
            if (Instance == null) return;
            Instance.Confirm("无行李模式",
                "开启后，读档会销毁世界里所有行李，并把所有乘客设为「没有行李」。\n\n这样可以跳过行李环节、不会因为没行李扣钱，但行李不会按存档还原。",
                () => { });
        }

        /// <summary>恢复默认设置（界面按钮与开发触发器共用）。</summary>
        public static void ApplyDefaults()
        {
            try
            {
                SetBool(Plugin.CfgNotify, true);
                SetBool(Plugin.CfgBagFreeMode, true);
                SetBool(Plugin.CfgPartialBags, false);
                SetBool(Plugin.CfgBagFallback, true);
                if (Plugin.CfgKeyCode != null)
                {
                    Plugin.CfgKeyCode.Value = -1;
                    GameHost.ReloadHotkey();
                }
                try { if (Plugin.CfgNotify != null) Plugin.CfgNotify.Save(); } catch { }
                Plugin.Notify("设置已重置");
                Plugin.Logs.LogInfo("[cfg] reset to defaults");
            }
            catch (Exception e) { Plugin.Logs.LogError("[cfg] reset: " + e.Message); }
        }

        // ------------------------------------------------------------------
        private void Confirm(string title, string message, Action onYes)
        {
            CloseConfirm();
            try
            {
                var dim = Mayday.UI.UIF.Round(layer, "CfgConfirmDim", new Color(0f, 0f, 0f, 0.55f), 0f, true);
                var drt = dim.rectTransform;
                drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
                drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;
                confirmGo = dim.gameObject;
                confirmGo.transform.SetAsLastSibling();

                var box = Mayday.UI.UIF.Panel(layer, "CfgConfirmBox", new Color(0.09f, 0.11f, 0.13f, 0.98f), 18f, true, true);
                confirmGo = dim.gameObject;
                box.Root.SetParent(confirmGo.transform, false);
                var size = layer.rect.size;
                Place(box.Root, size.x * 0.5f, size.y * 0.5f, 660f, 340f, new Vector2(0.5f, 0.5f));

                var t = Mayday.UI.UIF.Text(box.Root, "T", title, 28f,
                    Mayday.UI.UITheme.Orange, TMPro.TextAlignmentOptions.Center, false, false);
                Place(t.Rect, 330f, 290f, 600f, 40f, new Vector2(0.5f, 0.5f));

                var m = Mayday.UI.UIF.Text(box.Root, "M", message, 20f,
                    Mayday.UI.UITheme.Cream, TMPro.TextAlignmentOptions.Center, false, true);
                Place(m.Rect, 330f, 176f, 600f, 180f, new Vector2(0.5f, 0.5f));

                MakeButton(box.Root, "Yes", "确定", () => { CloseConfirm(); onYes(); },
                    new Color(0.62f, 0.24f, 0.22f, 1f), Color.white, 160f, 46f, 22f,
                    new Vector2(182f, 52f), new Vector2(0.5f, 0.5f));
                MakeButton(box.Root, "No", "取消", CloseConfirm,
                    new Color(1f, 1f, 1f, 0.14f), Mayday.UI.UITheme.Cream, 160f, 46f, 22f,
                    new Vector2(478f, 52f), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e) { Plugin.Logs.LogError("[cfg] confirm: " + e); }
        }

        private void CloseConfirm()
        {
            if (confirmGo == null) return;
            try { UnityEngine.Object.Destroy(confirmGo); } catch { }
            confirmGo = null;
        }

        // ------------------------------------------------------------------
        /// <summary>由 GameHost 每帧调用（仅捕获状态）。</summary>
        public static void PollCapture(UnityEngine.InputSystem.Keyboard kb)
        {
            try
            {
                if (!IsCapturing || kb == null) return;
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    IsCapturing = false;
                    Plugin.Notify("已取消更改按键");
                    if (Instance != null) Instance.Rebuild();
                    return;
                }
                var keys = kb.allKeys;
                if (keys == null) return;
                for (int i = 0; i < keys.Count; i++)
                {
                    var kc = keys[i];
                    if (kc == null) continue;
                    if (!kc.wasPressedThisFrame) continue;
                    var key = kc.keyCode;
                    if (key == UnityEngine.InputSystem.Key.None) continue;
                    ApplyKey(key);
                    return;
                }
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[cfg] capture: " + e.Message); }
        }

        internal static void ApplyKey(UnityEngine.InputSystem.Key key)
        {
            IsCapturing = false;
            try
            {
                if (Plugin.CfgKeyCode != null) Plugin.CfgKeyCode.Value = (int)key;
                GameHost.ReloadHotkey();
                Plugin.Notify("打开键已改为 " + key);
                Plugin.Logs.LogInfo("[cfg] hotkey -> " + key + " (" + (int)key + ")");
            }
            catch (Exception e) { Plugin.Logs.LogError("[cfg] key: " + e.Message); }
            if (Instance != null) Instance.Rebuild();
        }
    }
}
