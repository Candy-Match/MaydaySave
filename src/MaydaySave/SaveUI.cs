using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.UI;

namespace MaydaySave
{
    /// <summary>存读档界面：用游戏自带的 UIF / UITheme，绝对坐标排版。</summary>
    public class SaveUI
    {
        public static SaveUI Instance;

        private const float PanelW = 1180f;
        private const float PanelH = 900f;

        private readonly Il2CppSystem.Object owner;
        private GameObject host;
        private RectTransform layer;      // 所有元素的父节点
        private GameObject dimmerGo;
        private GameObject panelGo;
        private GameObject confirmGo;
private RectTransform panelRoot;
        private RectTransform rowsRoot;
        private Mayday.UI.UIText statusText;
        private TMPro.TMP_InputField nameField;
        private bool built;
        private bool open;

        public bool IsOpen => open;

        public SaveUI(Il2CppObjectBase owner)
        {
            this.owner = owner == null ? null : owner.Cast<Il2CppSystem.Object>();
        }

        public static SaveUI Ensure(Il2CppObjectBase owner, Transform fallbackParent)
        {
            if (Instance != null) return Instance;
            Instance = new SaveUI(owner);
            Instance.Build(fallbackParent);
            return Instance;
        }

        // ------------------------------------------------------------------
        private void Build(Transform fallbackParent)
        {
            RectTransform uiLayer = null;
            try
            {
                var root = Mayday.UI.UIRoot.Instance;
                if (root != null) uiLayer = root.PopupLayer;
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[ui] uilayer: " + e.Message); }

            host = new GameObject("MaydaySaveUI", Il2CppType.Of<RectTransform>());
            host.transform.SetParent(uiLayer != null ? (Transform)uiLayer : fallbackParent, false);
            try { host.transform.SetAsLastSibling(); } catch { }

            layer = host.GetComponent(Il2CppType.Of<RectTransform>()).Cast<RectTransform>();
            if (uiLayer == null)
            {
                // 没找到游戏 UI 层时自建 canvas
                UnityEngine.Object.DontDestroyOnLoad(host);
                var canvas = host.AddComponent(Il2CppType.Of<Canvas>()).Cast<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 20000;
                var scaler = host.AddComponent(Il2CppType.Of<CanvasScaler>()).Cast<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                host.AddComponent(Il2CppType.Of<GraphicRaycaster>());
            }

            // 让 host 铺满父层
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = Vector2.zero;
            layer.offsetMax = Vector2.zero;

            var size = layer.rect.size;

            // 右下角常驻按钮
            MakeButton(host.transform, "OpenSave", "存档", () => Toggle(),
                new Color(0.10f, 0.13f, 0.15f, 0.85f), Mayday.UI.UITheme.Cream,
                132f, 46f, 22f, new Vector2(size.x - 24f, 24f), new Vector2(1f, 0f));
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

        // ------------------------------------------------------------------
        public void Toggle()
        {
            Plugin.Logs.LogInfo("[ui] toggle, open=" + open);
            if (open) Close();
            else Open();
        }

        public void Open()
        {
            if (!SaveManager.InSession) return;
            try
            {
                if (!built) BuildPanel();
                if (panelGo == null) { Plugin.Logs.LogError("[ui] no panel"); return; }
                open = true;
                Rebuild();
                BringEntryToFront();
                try { Mayday.UIState.Push(owner); } catch (Exception e) { Plugin.Logs.LogWarning("uistate push: " + e.Message); }
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            catch (Exception e)
            {
                open = false;
                Plugin.Logs.LogError("[ui] open failed: " + e);
            }
        }

        public void Close()
        {
            open = false;
            try
            {
                if (panelGo != null) UnityEngine.Object.Destroy(panelGo);
                if (dimmerGo != null && dimmerGo != panelGo) UnityEngine.Object.Destroy(dimmerGo);
            }
            catch (Exception e) { Plugin.Logs.LogWarning("[ui] destroy failed: " + e.Message); }
            CloseConfirm();
            panelGo = null;
            dimmerGo = null;
            panelRoot = null;
            rowsRoot = null;
            statusText = null;
            built = false;
            try { Mayday.UIState.Pop(owner); } catch { }
            Plugin.Logs.LogInfo("[ui] closed");
        }

        private void BringEntryToFront()
        {
            try
            {
                var btn = host.transform.Find("OpenSave");
                if (btn != null) btn.SetAsLastSibling();
            }
            catch { }
        }
        private void BuildPanel()
        {
            var dimmer = Mayday.UI.UIF.Dimmer(layer, 0.62f);
            dimmerGo = dimmer.gameObject;
            CloseConfirm();
            panelGo = null;
            CloseConfirm();
            panelGo = null;
            var drt = dimmer.rectTransform;
            drt.anchorMin = Vector2.zero;
            drt.anchorMax = Vector2.one;
            drt.offsetMin = Vector2.zero;
            drt.offsetMax = Vector2.zero;
            dimmerGo.transform.SetAsFirstSibling();

            var panel = Mayday.UI.UIF.Panel(layer, "SavePanel",
                new Color(0.07f, 0.09f, 0.11f, 0.97f), 22f, true, true);
            panelRoot = panel.Root;
            panelGo = panelRoot.gameObject;

            var layerSize = layer.rect.size;
            float cx = layerSize.x * 0.5f;
            float cy = layerSize.y * 0.5f;
            Place(panelRoot, cx, cy, PanelW, PanelH, new Vector2(0.5f, 0.5f));

            // 面板内坐标：以面板左下角为原点


            // 左上角：设置入口
            MakeButton(panelRoot, "Settings", "设置",
                () => SettingsUI.TogglePanel(owner, host.transform),
                new Color(0.16f, 0.20f, 0.23f, 0.95f), Mayday.UI.UITheme.Cream,
                118f, 44f, 20f, new Vector2(52f, PanelH - 52f), new Vector2(0f, 0.5f));

            var title = Mayday.UI.UIF.Text(panelRoot, "Title", "存档 / 读档", 40f,
                Mayday.UI.UITheme.Cream, TMPro.TextAlignmentOptions.Center, true, false);
            Place(title.Rect, PanelW * 0.5f, PanelH - 58f, 900f, 56f, new Vector2(0.5f, 0.5f));

            statusText = Mayday.UI.UIF.Text(panelRoot, "Status", "", 21f,
                new Color(0.65f, 0.74f, 0.78f), TMPro.TextAlignmentOptions.Center, false, false);
            Place(statusText.Rect, PanelW * 0.5f, PanelH - 102f, 1000f, 28f, new Vector2(0.5f, 0.5f));



            var rows = Mayday.UI.UIF.Rect("Rows", panelRoot);
            Place(rows, 0f, 0f, PanelW, PanelH, new Vector2(0f, 0f));
            rowsRoot = rows;

            MakeButton(panelRoot, "Close", "关闭", Close,
                new Color(1f, 1f, 1f, 0.12f), Mayday.UI.UITheme.Cream,
                180f, 50f, 24f, new Vector2(PanelW * 0.5f, 38f), new Vector2(0.5f, 0.5f));

            var hint = Mayday.UI.UIF.Text(panelRoot, "Hint", "只能在飞机起飞前存档；读档会回到该段的登机阶段。", 19f,
                new Color(0.55f, 0.63f, 0.67f), TMPro.TextAlignmentOptions.Center, false, false);
            Place(hint.Rect, PanelW * 0.5f, 146f, 1060f, 26f, new Vector2(0.5f, 0.5f));
            var hint2 = Mayday.UI.UIF.Text(panelRoot, "Hint2", "读档会清空所有行李，并将乘客设为「无行李」状态（可在配置文件里关闭）", 17f,
                new Color(0.50f, 0.58f, 0.62f), TMPro.TextAlignmentOptions.Center, false, false);
            Place(hint2.Rect, PanelW * 0.5f, 114f, 1060f, 24f, new Vector2(0.5f, 0.5f));

            built = true;

        }

        // ------------------------------------------------------------------
        private void ClearRows()
        {
            if (rowsRoot == null) return;
            for (int i = rowsRoot.childCount - 1; i >= 0; i--)
            {
                var c = rowsRoot.GetChild(i);
                if (c != null) UnityEngine.Object.Destroy(c.gameObject);
            }
        }

        public void Rebuild()
        {
            if (!built) return;
            ClearRows();

            bool ground = SaveManager.OnGround;
            bool hostRole = SaveManager.IsHost;
            var gs = SaveManager.GS;

            if (statusText != null)
            {
                if (gs == null) statusText.Text = "尚未进入房间";
                else
                {
                    statusText.Text = $"第 {gs.Leg + 1} 段 · {gs.CurrentAirportName} · {SaveManager.PhaseText((byte)gs.Phase)} · ¥{gs.Money}"
                                      + (ground ? "" : "   ·   飞行中不可存档");
                    statusText.Color = ground ? new Color(0.65f, 0.78f, 0.72f) : Mayday.UI.UITheme.Orange;
                }
            }

            const float rowH = 54f;
            float top = PanelH - 140f;

            for (int i = 0; i < SaveManager.SlotCount; i++)
            {
                int slot = i + 1;
                var info = SaveManager.ReadSlot(slot);
                float y = top - i * 60f;

                var row = Mayday.UI.UIF.Panel(rowsRoot, "Row" + slot,
                    new Color(1f, 1f, 1f, 0.055f), 10f, false, true);
                Place(row.Root, 40f, y, PanelW - 80f, rowH, new Vector2(0f, 1f));

                var idx = Mayday.UI.UIF.Text(row.Root, "Idx", slot.ToString(), 24f,
                    Mayday.UI.UITheme.TealLt, TMPro.TextAlignmentOptions.Center, false, false);
                Place(idx.Rect, 32f, rowH * 0.5f, 40f, 36f, new Vector2(0.5f, 0.5f));

                var infoText = Mayday.UI.UIF.Text(row.Root, "Info", SaveManager.SlotSummary(info), 20f,
                    info.Exists ? Mayday.UI.UITheme.Cream : new Color(0.52f, 0.58f, 0.62f),
                    TMPro.TextAlignmentOptions.Left, false, false);
                Place(infoText.Rect, 68f, rowH * 0.5f, 606f, 36f, new Vector2(0f, 0.5f));

                int captured = slot;
                bool canSave = hostRole && ground && SaveManager.InSession;
                bool canLoad = hostRole && SaveManager.InSession;

                var delRt = MakeButton(row.Root, "Del", "删除", () => OnDeleteClicked(captured),
                    new Color(0.55f, 0.22f, 0.20f, 0.85f), new Color(1f, 0.92f, 0.90f), 80f, 36f, 18f,
                    new Vector2(PanelW - 80f - 415f, rowH * 0.5f), new Vector2(0.5f, 0.5f));
                var delComp = delRt.GetComponent(Il2CppType.Of<UnityEngine.UI.Button>()).Cast<UnityEngine.UI.Button>();
                delComp.interactable = info.Exists && hostRole;

                var renameRt = MakeButton(row.Root, "Rename", "改名", () => OnRenameClicked(captured),
                    new Color(1f, 1f, 1f, 0.12f), Mayday.UI.UITheme.Cream, 70f, 36f, 18f,
                    new Vector2(PanelW - 80f - 310f, rowH * 0.5f), new Vector2(0.5f, 0.5f));
                var renameComp = renameRt.GetComponent(Il2CppType.Of<UnityEngine.UI.Button>()).Cast<UnityEngine.UI.Button>();
                renameComp.interactable = info.Exists && hostRole;

                var saveRt = MakeButton(row.Root, "Save", "保存", () => OnSaveClicked(captured),
                    Mayday.UI.UITheme.Teal, Color.white, 100f, 36f, 19f,
                    new Vector2(PanelW - 80f - 205f, rowH * 0.5f), new Vector2(0.5f, 0.5f));
                var saveComp = saveRt.GetComponent(Il2CppType.Of<UnityEngine.UI.Button>()).Cast<UnityEngine.UI.Button>();
                saveComp.interactable = canSave;

                var loadRt = MakeButton(row.Root, "Load", "读取", () => OnLoadClicked(captured),
                    new Color(1f, 1f, 1f, 0.14f), Mayday.UI.UITheme.Cream, 100f, 36f, 19f,
                    new Vector2(PanelW - 80f - 90f, rowH * 0.5f), new Vector2(0.5f, 0.5f));
                var loadComp = loadRt.GetComponent(Il2CppType.Of<UnityEngine.UI.Button>()).Cast<UnityEngine.UI.Button>();
                loadComp.interactable = canLoad && info.Exists;
            }
        }

        // ------------------------------------------------------------------
        private void OnSaveClicked(int slot)
        {
            try
            {
                string cname = SaveManager.ReadSlot(slot).Name;
                string what = SaveManager.Save(slot, cname, out int items, out int systems);
                Plugin.Logs.LogInfo($"[ui] saved slot{slot}: {what} (items={items}, systems={systems})");
                Plugin.Notify($"已保存到栏位 {slot}：{what}");
            }
            catch (Exception e)
            {
                Plugin.Logs.LogError("[ui] save failed: " + e.Message);
                Plugin.Notify("保存失败：" + e.Message, true);
            }
            Rebuild();
        }

        private TMPro.TMP_InputField renameField;
        private int renameSlot;

        private void OnRenameClicked(int slot)
        {
            if (!SaveManager.IsHost) { Plugin.Notify("只有房主可以改名", true); return; }
            ShowRename(slot);
        }

        private void ShowRename(int slot)
        {
            CloseConfirm();
            renameSlot = slot;
            try
            {
                var dim = Mayday.UI.UIF.Round(layer, "RenameDim", new Color(0f, 0f, 0f, 0.55f), 0f, true);
                var drt = dim.rectTransform;
                drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
                drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;
                confirmGo = dim.gameObject;
                confirmGo.transform.SetAsLastSibling();

                var box = Mayday.UI.UIF.Panel(layer, "RenameBox", new Color(0.09f, 0.11f, 0.13f, 0.98f), 18f, true, true);
                var size = layer.rect.size;
                box.Root.SetParent(confirmGo.transform, false);
                Place(box.Root, size.x * 0.5f, size.y * 0.5f, 560f, 260f, new Vector2(0.5f, 0.5f));

                var label = Mayday.UI.UIF.Text(box.Root, "Msg", $"栏位 {slot} 的名称", 24f,
                    Mayday.UI.UITheme.Cream, TMPro.TextAlignmentOptions.Center, false, false);
                Place(label.Rect, 280f, 196f, 480f, 34f, new Vector2(0.5f, 0.5f));

                renameField = Mayday.UI.UIF.Input(box.Root, "RenameInput", SaveManager.ReadSlot(slot).Name,
                    "输入名称（留空清除）", 24, null, 440f, 44f);
                var nrt = renameField.GetComponent(Il2CppType.Of<RectTransform>()).Cast<RectTransform>();
                Place(nrt, 280f, 138f, 440f, 44f, new Vector2(0.5f, 0.5f));

                MakeButton(box.Root, "Ok", "确定", () => { DoRename(renameSlot); },
                    Mayday.UI.UITheme.Teal, Color.white, 150f, 46f, 22f,
                    new Vector2(140f, 52f), new Vector2(0.5f, 0.5f));
                MakeButton(box.Root, "Cancel", "取消", CloseConfirm,
                    new Color(1f, 1f, 1f, 0.14f), Mayday.UI.UITheme.Cream, 150f, 46f, 22f,
                    new Vector2(420f, 52f), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e) { Plugin.Logs.LogError("[ui] rename dialog: " + e); }
        }

        private void DoRename(int slot)
        {
            string name = "";
            try { if (renameField != null) name = renameField.text == null ? "" : renameField.text.Trim(); } catch { }
            CloseConfirm();
            try
            {
                SaveManager.RenameSlot(slot, name);
                Plugin.Notify(string.IsNullOrEmpty(name) ? $"已清除栏位 {slot} 的名称" : $"栏位 {slot} 已命名为「{name}」");
                Plugin.Logs.LogInfo($"[ui] rename slot{slot} -> '{name}'");
            }
            catch (Exception e)
            {
                Plugin.Logs.LogError("[ui] rename: " + e.Message);
                Plugin.Notify("改名失败：" + e.Message, true);
            }
            renameField = null;
            Rebuild();
        }

        private void OnDeleteClicked(int slot)
        {
            if (!SaveManager.IsHost) { Plugin.Notify("只有房主可以删除存档", true); return; }
            ShowConfirm(slot);
        }

        private void ShowConfirm(int slot)
        {
            CloseConfirm();
            try
            {
                var dim = Mayday.UI.UIF.Round(layer, "ConfirmDim", new Color(0f, 0f, 0f, 0.55f), 0f, true);
                var drt = dim.rectTransform;
                drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
                drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;
                confirmGo = dim.gameObject;
                confirmGo.transform.SetAsLastSibling();

                var box = Mayday.UI.UIF.Panel(layer, "ConfirmBox", new Color(0.09f, 0.11f, 0.13f, 0.98f), 18f, true, true);
                var size = layer.rect.size;
                Place(box.Root, size.x * 0.5f, size.y * 0.5f, 560f, 240f, new Vector2(0.5f, 0.5f));
                box.Root.SetParent(confirmGo.transform, false);
                Place(box.Root, size.x * 0.5f, size.y * 0.5f, 560f, 240f, new Vector2(0.5f, 0.5f));

                var label = Mayday.UI.UIF.Text(box.Root, "Msg", $"确定删除栏位 {slot} 的存档吗？\n此操作不可撤销。", 24f,
                    Mayday.UI.UITheme.Cream, TMPro.TextAlignmentOptions.Center, false, true);
                Place(label.Rect, 280f, 150f, 500f, 90f, new Vector2(0.5f, 0.5f));

                MakeButton(box.Root, "Yes", "删除", () => { DoDelete(slot); },
                    new Color(0.62f, 0.24f, 0.22f, 1f), Color.white, 150f, 46f, 22f,
                    new Vector2(140f, 46f), new Vector2(0.5f, 0.5f));
                MakeButton(box.Root, "No", "取消", CloseConfirm,
                    new Color(1f, 1f, 1f, 0.14f), Mayday.UI.UITheme.Cream, 150f, 46f, 22f,
                    new Vector2(420f, 46f), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e) { Plugin.Logs.LogError("[ui] confirm: " + e); }
        }

        private void CloseConfirm()
        {
            if (confirmGo == null) return;
            try { UnityEngine.Object.Destroy(confirmGo); } catch { }
            confirmGo = null;
            renameField = null;
        }

        private void DoDelete(int slot)
        {
            CloseConfirm();
            try
            {
                var path = SaveManager.SlotPath(slot);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                Plugin.Logs.LogInfo($"[ui] deleted slot{slot}");
                Plugin.Notify($"已删除栏位 {slot}");
            }
            catch (Exception e)
            {
                Plugin.Logs.LogError("[ui] delete: " + e.Message);
                Plugin.Notify("删除失败：" + e.Message, true);
            }
            Rebuild();
        }

        private void OnLoadClicked(int slot)
        {
            if (!SaveManager.IsHost) { Plugin.Notify("只有房主可以读档", true); return; }
            if (!SaveManager.InSession) { Plugin.Notify("还没有进入房间", true); return; }
            if (!SaveManager.OnGround) { Plugin.Notify("只能在飞机起飞前读档", true); return; }
            if (!System.IO.File.Exists(SaveManager.SlotPath(slot))) return;

            Close();
            try
            {
                var d = SaveData.LoadFrom(SaveManager.SlotPath(slot));
                GameHost.Instance.StartLoad(d, "栏位 " + slot);
            }
            catch (Exception e)
            {
                Plugin.Logs.LogError("[ui] load failed: " + e);
                Plugin.Notify("读取失败：" + e.Message, true);
            }
        }
    }
}
