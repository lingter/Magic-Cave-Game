using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicCave.Prototype
{
    public sealed partial class PrototypeGame
    {
        GUIStyle textStyle, smallStyle, titleStyle, headingStyle, buttonStyle;
        readonly Dictionary<string, Texture2D> guiTextures = new Dictionary<string, Texture2D>();
        Keyboard uiKeyboard;
        bool seedFocused;
        int consumedClick = -1, dropSlot = -1, dropQuantity = 1;
        static readonly Color Ink = new Color(0.84f, 0.89f, 0.96f);
        static readonly Color Gold = new Color(0.98f, 0.73f, 0.26f);
        static readonly Color Cyan = new Color(0.25f, 0.85f, 0.91f);

        partial void ReadInterfaceInput()
        {
            if (uiKeyboard != Keyboard.current)
            {
                if (uiKeyboard != null) uiKeyboard.onTextInput -= SeedCharacter;
                uiKeyboard = Keyboard.current;
                if (uiKeyboard != null) uiKeyboard.onTextInput += SeedCharacter;
            }
            if (!seedFocused || !customSeed || ScreenMode != GameScreen.Home || Panel != GamePanel.None || uiKeyboard == null) return;
            if (uiKeyboard.backspaceKey.wasPressedThisFrame && customSeedText.Length > 0) customSeedText = customSeedText.Substring(0, customSeedText.Length - 1);
            if (uiKeyboard.deleteKey.wasPressedThisFrame) customSeedText = "";
            if (uiKeyboard.ctrlKey.isPressed && uiKeyboard.aKey.wasPressedThisFrame) customSeedText = "";
            if (uiKeyboard.ctrlKey.isPressed && uiKeyboard.vKey.wasPressedThisFrame)
            {
                string pasted = GUIUtility.systemCopyBuffer.Trim();
                if (pasted.Length <= 11) customSeedText = pasted;
            }
        }
        void SeedCharacter(char value)
        {
            if (seedFocused && customSeed && ScreenMode == GameScreen.Home && Panel == GamePanel.None &&
                customSeedText.Length < 11 && (char.IsDigit(value) || (value == '-' && customSeedText.Length == 0))) customSeedText += value;
        }
        partial void ReleaseInterfaceInput()
        {
            if (uiKeyboard != null) uiKeyboard.onTextInput -= SeedCharacter;
            foreach (var texture in guiTextures.Values) if (texture != null) Destroy(texture);
        }
        Texture2D Texture(string key, Color color)
        {
            if (guiTextures.TryGetValue(key, out var texture)) return texture;
            texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply();
            guiTextures.Add(key, texture); return texture;
        }
        void Fill(Rect rectangle, string key, Color color) { GUI.DrawTexture(rectangle, Texture(key, color)); }
        void Box(Rect rectangle)
        {
            Fill(rectangle, "panel", new Color(0.055f, 0.083f, 0.13f, 0.97f));
            Fill(new Rect(rectangle.x, rectangle.y, rectangle.width, 2), "line", new Color(0.20f, 0.34f, 0.44f));
        }
        void Label(Rect rectangle, string content, GUIStyle style = null, Color? color = null)
        {
            Color old = GUI.contentColor; GUI.contentColor = color ?? Ink;
            GUI.Label(rectangle, content, style ?? textStyle); GUI.contentColor = old;
        }
        // IMGUI is used for drawing only. New Input System supplies all runtime UI interactions.
        bool Click(Rect rectangle)
        {
            if (!GUI.enabled || Mouse.current == null || !Mouse.current.leftButton.wasReleasedThisFrame || consumedClick == Time.frameCount || !rectangle.Contains(GuiMouse)) return false;
            consumedClick = Time.frameCount; return true;
        }
        void Button(Rect rectangle, string caption, Action action, bool enabled = true, bool primary = false)
        {
            bool active = GUI.enabled && enabled;
            bool hovered = rectangle.Contains(GuiMouse) && active;
            var color = primary ? new Color(0.16f, 0.40f, 0.48f) : new Color(0.12f, 0.19f, 0.27f);
            if (hovered) color *= 1.3f;
            if (!active) color = new Color(0.075f, 0.10f, 0.15f);
            Fill(rectangle, "button " + primary + " " + hovered + " " + active, color);
            Label(rectangle, caption, buttonStyle, active ? Ink : new Color(0.42f, 0.48f, 0.55f));
            bool old = GUI.enabled; GUI.enabled = active;
            if (Click(rectangle)) Queue(action);
            GUI.enabled = old;
        }
        void Bar(Rect rectangle, float ratio, Color color, string caption)
        {
            Fill(rectangle, "bar base", new Color(0.12f, 0.16f, 0.22f));
            Fill(new Rect(rectangle.x, rectangle.y, rectangle.width * Mathf.Clamp01(ratio), rectangle.height), "bar " + color, color);
            Label(rectangle, caption, buttonStyle, Color.white);
        }
        void Styles()
        {
            if (textStyle != null) return;
            textStyle = new GUIStyle(GUI.skin.label) { font = interfaceFont, fontSize = 18, wordWrap = true };
            smallStyle = new GUIStyle(textStyle) { fontSize = 14 };
            titleStyle = new GUIStyle(textStyle) { fontSize = 40, fontStyle = FontStyle.Bold };
            headingStyle = new GUIStyle(textStyle) { fontSize = 25, fontStyle = FontStyle.Bold };
            buttonStyle = new GUIStyle(textStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(6, 6, 3, 3) };
        }
        partial void DrawInterface()
        {
            if (interfaceFont == null) return;
            Styles();
            Matrix4x4 oldMatrix = GUI.matrix; bool oldEnabled = GUI.enabled;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2, (Screen.height - 720 * scale) / 2, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            GUI.enabled = Panel == GamePanel.None;
            if (ScreenMode != GameScreen.Explore) Fill(new Rect(0, 0, 1280, 720), "background", new Color(0.025f, 0.04f, 0.075f));
            switch (ScreenMode)
            {
                case GameScreen.Home: DrawHome(); break;
                case GameScreen.Explore: DrawExplore(); break;
                case GameScreen.Battle: DrawBattle(); break;
                case GameScreen.Rewards: DrawRewards(); break;
                case GameScreen.Settlement: DrawSettlement(); break;
                case GameScreen.Result: DrawResult(); break;
                case GameScreen.Warehouse: DrawWarehouse(); break;
            }
            GUI.enabled = true;
            if (Panel != GamePanel.None) DrawPanel();
            if (!string.IsNullOrEmpty(error))
            {
                Box(new Rect(60, 603, 1160, 72)); Label(new Rect(77, 617, 1030, 52), error, smallStyle, new Color(1f, 0.55f, 0.48f));
                Button(new Rect(1120, 620, 80, 32), "关闭", () => error = "");
            }
            else if (!string.IsNullOrEmpty(message) && Time.unscaledTime < messageUntil)
            {
                Box(new Rect(260, 604, 760, 42)); Label(new Rect(274, 610, 730, 32), message, buttonStyle, Gold);
            }
            GUI.enabled = oldEnabled; GUI.matrix = oldMatrix;
        }
        void DrawHome()
        {
            Fill(new Rect(70, 108, 5, 484), "accent", Cyan);
            Label(new Rect(98, 114, 600, 62), "MAGIC CAVE", titleStyle, Cyan);
            Label(new Rect(100, 182, 580, 50), "魔洞探索 · 最小可玩模型", headingStyle);
            Label(new Rect(101, 257, 515, 165), "探索随机房间，破墙寻找隐藏门。\n通过战斗升级，选择卡牌构筑本局卡组。\n生命归零则失败；击败首领后主动离开结算。", textStyle);
            Label(new Rect(102, 498, 480, 70), "5张地图  /  10种卡牌  /  物品带出\n存档继续保留地图与本局状态", smallStyle, new Color(0.50f, 0.63f, 0.73f));
            Box(new Rect(710, 120, 450, 490));
            Label(new Rect(736, 140, 400, 45), "准备探索", headingStyle, Gold);
            bool ready = Store != null;
            bool pending = ready && Store.Session.pending != null;
            Button(new Rect(738, 195, 394, 55), "开始新局", RequestNewRun, ready && !pending, true);
            Button(new Rect(738, 264, 394, 55), pending ? "继续未完成结算" : "继续存档", ContinueRun, ready && (pending || Store.Session.checkpoint != null));
            Button(new Rect(738, 334, 187, 44), "查看仓库", () => ScreenMode = GameScreen.Warehouse, ready);
            Button(new Rect(945, 334, 187, 44), "退出", () => { leaveApplication = true; Panel = GamePanel.Leave; });
            Button(new Rect(738, 407, 394, 40), customSeed ? "☑ 自定义种子" : "□ 随机种子（点击切换）", () => { customSeed = !customSeed; seedFocused = customSeed; });
            var input = new Rect(738, 463, 300, 43);
            Fill(input, seedFocused ? "input focus" : "input", seedFocused ? new Color(0.12f, 0.29f, 0.36f) : new Color(0.08f, 0.13f, 0.21f));
            Label(new Rect(750, 467, 275, 34), customSeed ? customSeedText + (seedFocused && Mathf.FloorToInt(Time.unscaledTime * 2) % 2 == 0 ? "|" : "") : "每局随机生成Seed", textStyle);
            if (Click(input) && customSeed) seedFocused = true;
            Button(new Rect(1050, 463, 82, 43), "清空", () => { customSeedText = ""; seedFocused = true; }, customSeed);
            Label(new Rect(738, 524, 392, 68), "支持32位整数，含0和负数。\n实际Seed在每局开始时输出到控制台。", smallStyle);
        }
        void DrawExplore()
        {
            if (Run == null) return;
            Box(new Rect(0, 0, 1280, 89));
            Label(new Rect(25, 8, 260, 32), "地图 " + (Run.mapIndex + 1) + " / 5", headingStyle, Gold);
            Label(new Rect(26, 39, 260, 21), "Seed: " + Run.seed, smallStyle, Cyan);
            Bar(new Rect(295, 19, 260, 32), Run.health / 60f, new Color(0.21f, 0.59f, 0.46f), "生命 " + Run.health + " / 60");
            Label(new Rect(586, 19, 250, 42), "等级 " + Run.level + "   经验 " + Run.experience + "/" + Rules.RequiredExperience(Run.level));
            Label(new Rect(865, 20, 180, 38), "背包 " + Inventory.UsedSlots(Run) + " / 20");
            Button(new Rect(1110, 17, 140, 41), "暂停 [Esc]", () => { Panel = GamePanel.Pause; CancelRoute(); });
            Label(new Rect(26, 61, 1220, 23), "WASD/方向键移动  左键寻路/交互  右键破墙  E交互  B背包  Tab卡组  固定视角，镜头跟随角色", smallStyle);
            Box(new Rect(0, 657, 1280, 63));
            string hint = "探索迷雾，寻找门与物品";
            var target = hover.kind != TargetKind.None ? hover : highlighted;
            if (target.kind == TargetKind.Wall)
            {
                bool clue = MapGenerator.IsDoorHintWall(Run.Map, target.x, target.y);
                hint = clue ? "门的线索 · " : "";
                hint += Run.Map.Border(target.x, target.y) ? "边界墙 · 不可破坏" : "内部墙 · 右键接近并破坏";
            }
            if (target.kind == TargetKind.Door) hint = Run.Map.At(target.x, target.y) == Tile.Advance ? "前进门 · 点击或E进入下一张" : "返回门 · 点击或E选择存档 / 结束游戏";
            if (target.kind == TargetKind.Enemy)
            {
                var enemy = Run.Map.enemies.FirstOrDefault(e => e.id == target.id);
                if (enemy != null) hint = Rules.EnemyNames[(int)enemy.kind] + " · 点击接近后战斗 / 范围内按E";
            }
            if (target.kind == TargetKind.Loot)
            {
                var loot = Run.Map.loot.FirstOrDefault(l => l.id == target.id);
                if (loot != null && loot.units.Count > 0) hint = Rules.ItemNames[(int)loot.units[0].kind] + " ×" + loot.units.Count + " · " + Rules.RarityName(loot.units[0].kind) + " · 点击 / E拾取";
            }
            Label(new Rect(26, 671, 890, 38), hint, textStyle, Gold);
            Button(new Rect(960, 669, 135, 36), "背包 [B]", () => { Panel = GamePanel.Inventory; CancelRoute(); });
            Button(new Rect(1110, 669, 140, 36), "卡组 [Tab]", () => { Panel = GamePanel.Deck; CancelRoute(); });
        }
        void Card(Rect rectangle, CardKind card, Action click, bool enabled)
        {
            Button(rectangle, "", click, enabled, true);
            Label(new Rect(rectangle.x + 10, rectangle.y + 10, rectangle.width - 45, 32), Rules.CardNames[(int)card], textStyle, Gold);
            Label(new Rect(rectangle.xMax - 35, rectangle.y + 10, 26, 30), Rules.Costs[(int)card].ToString(), buttonStyle, Cyan);
            Label(new Rect(rectangle.x + 10, rectangle.y + 63, rectangle.width - 20, rectangle.height - 72), Rules.Effects[(int)card], smallStyle);
        }
        void DrawBattle()
        {
            if (Combat == null) return;
            Label(new Rect(45, 28, 480, 42), "卡牌战斗", headingStyle, Gold);
            Button(new Rect(1110, 23, 130, 38), "暂停 [Esc]", () => Panel = GamePanel.Pause);
            Box(new Rect(105, 120, 415, 218)); Box(new Rect(755, 120, 415, 218));
            Label(new Rect(130, 143, 360, 42), "探索者", headingStyle, Cyan);
            Bar(new Rect(130, 206, 362, 30), Run.health / 60f, new Color(0.21f, 0.59f, 0.46f), "生命 " + Run.health + " / 60");
            Label(new Rect(130, 261, 360, 35), "格挡 " + Combat.playerBlock + "     战意 +" + Combat.spirit);
            Label(new Rect(781, 143, 360, 42), Rules.EnemyNames[(int)Combat.enemy.kind], headingStyle, new Color(1f, 0.53f, 0.46f));
            Bar(new Rect(782, 206, 362, 30), Combat.enemyHealth / (float)Rules.EnemyHealth[(int)Combat.enemy.kind], new Color(0.68f, 0.25f, 0.30f), "生命 " + Combat.enemyHealth + " / " + Rules.EnemyHealth[(int)Combat.enemy.kind]);
            Label(new Rect(782, 261, 360, 35), "格挡 " + Combat.enemyBlock + "     意图：" + Combat.Intent, textStyle, Gold);
            Label(new Rect(538, 165, 205, 92), "能量\n" + Combat.energy + " / 3", buttonStyle, Cyan);
            Label(new Rect(380, 367, 520, 35), Combat.playerTurn ? "你的回合 · 点击卡牌使用" : "敌人行动中…", buttonStyle, Gold);
            int count = Combat.hand.Count; float width = Mathf.Min(170, 1120f / Mathf.Max(1, count) - 10);
            float start = (1280 - count * (width + 10) + 10) / 2f;
            for (int i = 0; i < count; i++)
            {
                int index = i; CardKind card = Combat.hand[i];
                Card(new Rect(start + i * (width + 10), 425, width, 178), card, () => PlayCard(index), Combat.playerTurn && Combat.outcome == BattleOutcome.None && Combat.energy >= Rules.Costs[(int)card]);
            }
            Button(new Rect(46, 650, 180, 45), "抽牌堆 " + Combat.draw.Count, () => Panel = GamePanel.Draw);
            Button(new Rect(241, 650, 180, 45), "弃牌堆 " + Combat.discard.Count, () => Panel = GamePanel.Discard);
            Button(new Rect(985, 644, 250, 52), "结束回合", EndTurn, Combat.playerTurn && Combat.outcome == BattleOutcome.None, true);
            if (Combat.outcome == BattleOutcome.Failure)
                Button(new Rect(470, 640, 335, 54), "重试保存失败结算", () => BeginSettlement(true), true, true);
        }
        void DrawRewards()
        {
            Label(new Rect(340, 100, 600, 55), "升级！当前等级 " + Run.level, titleStyle, Gold);
            Label(new Rect(350, 173, 580, 35), "选择一张加入本局卡组，或跳过此次机会", buttonStyle);
            for (int i = 0; i < rewards.Count; i++)
            {
                int index = i;
                Card(new Rect(260 + i * 270, 255, 240, 260), rewards[i], () => SelectReward(index), true);
            }
            Button(new Rect(460, 579, 360, 49), "跳过", () => SelectReward(-1));
        }
        void DrawSettlement()
        {
            if (Store == null || Store.Session.pending == null) return;
            var pending = Store.Session.pending; var all = Inventory.Items(pending.run);
            string caption = pending.failure ? "探索失败 · 自动带出5件" : pending.run.bossDefeated ? "首领已击败 · 选择胜利带出物品" : "主动离开 · 选择带出物品";
            Label(new Rect(150, 75, 990, 60), caption, headingStyle, Gold);
            Label(new Rect(150, 139, 900, 35), "已选择 " + pending.selectedIds.Count + " / " + (pending.failure ? 5 : 10) + " 件，每件独立占1格");
            for (int kindIndex = 0; kindIndex < 4; kindIndex++)
            {
                var kind = (ItemKind)kindIndex; int total = all.Count(i => i.kind == kind); int selected = all.Count(i => i.kind == kind && pending.selectedIds.Contains(i.id));
                float y = 216 + kindIndex * 76; Box(new Rect(150, y, 980, 61));
                Label(new Rect(169, y + 13, 310, 34), Rules.ItemNames[kindIndex] + " · " + Rules.RarityName(kind));
                Label(new Rect(514, y + 13, 350, 34), "持有 " + total + "    带出 " + selected);
                Button(new Rect(884, y + 10, 85, 39), "－", () => ChangeSelection(kind, -1), !pending.failure && !pending.confirmed && selected > 0);
                Button(new Rect(994, y + 10, 85, 39), "＋", () => ChangeSelection(kind, 1), !pending.failure && !pending.confirmed && selected < total && pending.selectedIds.Count < 10);
            }
            Button(new Rect(150, 586, 280, 52), "取消，继续探索", CancelSettlement, !pending.failure && !pending.confirmed);
            Button(new Rect(726, 586, 404, 52), pending.confirmed ? "重试完成入库" : "确认结算并入库", FinishSettlement, true, true);
            Label(new Rect(154, 653, 970, 35), pending.failure ? "按稀有度、首次获取时间和首次入包顺序自动选取，不能更换。" : "确认前可以取消；未带出的物品在本局结束后不保留。", smallStyle);
        }
        void ChangeSelection(ItemKind kind, int delta)
        {
            var pending = Store.Session.pending; var ids = new List<int>(pending.selectedIds);
            var all = Inventory.Items(pending.run).Where(i => i.kind == kind).ToList();
            if (delta > 0 && ids.Count < 10) { var item = all.FirstOrDefault(i => !ids.Contains(i.id)); if (item != null) ids.Add(item.id); }
            if (delta < 0) { var item = all.LastOrDefault(i => ids.Contains(i.id)); if (item != null) ids.Remove(item.id); }
            Store.SaveSelection(ids);
        }
        void DrawResult()
        {
            if (lastSettlement == null) return;
            string result = lastSettlement.failure ? "探索失败" : lastSettlement.run.bossDefeated ? "本局胜利" : "主动离开";
            Label(new Rect(360, 80, 650, 65), result, titleStyle, Gold);
            Box(new Rect(300, 179, 680, 380));
            Label(new Rect(330, 207, 620, 88), "到达地图 " + (lastSettlement.run.mapIndex + 1) + " / 5    最终等级 " + lastSettlement.run.level + "\n本局获得经验 " + lastSettlement.run.totalExperience + "    带出物品 " + lastCarried.Count + " 件");
            int row = 0;
            foreach (var group in lastCarried.GroupBy(i => i.kind).OrderBy(g => g.Key))
                Label(new Rect(335, 320 + row++ * 46, 610, 34), Rules.ItemNames[(int)group.Key] + " ×" + group.Count());
            Button(new Rect(390, 600, 500, 56), "返回局外", GoHome, true, true);
        }
        void DrawWarehouse()
        {
            Label(new Rect(180, 95, 900, 55), "局外仓库", titleStyle, Gold);
            Label(new Rect(184, 160, 900, 40), "物品永久累计保存；第一版新局背包为空。", textStyle);
            for (int i = 0; i < 4; i++)
            {
                float y = 247 + i * 70; Box(new Rect(180, y, 920, 55));
                Label(new Rect(200, y + 10, 560, 37), Rules.ItemNames[i] + " · " + Rules.RarityName((ItemKind)i));
                Label(new Rect(850, y + 10, 210, 37), "× " + Store.Warehouse.counts[i], textStyle, Cyan);
            }
            Button(new Rect(420, 599, 440, 50), "返回", () => ScreenMode = GameScreen.Home);
        }
        void DrawPanel()
        {
            Fill(new Rect(0, 0, 1280, 720), "shade", new Color(0, 0, 0, 0.70f));
            if (Panel == GamePanel.Inventory) { DrawInventory(); return; }
            if (Panel == GamePanel.Deck || Panel == GamePanel.Draw || Panel == GamePanel.Discard) { DrawCardList(); return; }
            Box(new Rect(340, 155, 600, 420));
            switch (Panel)
            {
                case GamePanel.Pause:
                    Label(new Rect(372, 181, 520, 55), "已暂停", headingStyle, Gold);
                    Button(new Rect(377, 263, 526, 55), "继续游戏", () => Panel = GamePanel.None, true, true);
                    Button(new Rect(377, 337, 526, 55), "返回局外", () => { leaveApplication = false; Panel = GamePanel.Leave; });
                    Button(new Rect(377, 411, 526, 55), "退出游戏", () => { leaveApplication = true; Panel = GamePanel.Leave; });
                    break;
                case GamePanel.Return:
                    Label(new Rect(371, 181, 535, 55), "返回门", headingStyle, Gold);
                    Button(new Rect(378, 264, 524, 55), "存档并返回局外", SaveAndLeave, true, true);
                    Button(new Rect(378, 338, 524, 55), "结束游戏 · 选择带出物品", () => BeginSettlement(false));
                    Button(new Rect(378, 412, 524, 55), "取消", () => Panel = GamePanel.None);
                    break;
                case GamePanel.NewRun:
                    Label(new Rect(372, 181, 524, 55), "放弃旧局？", headingStyle, Gold);
                    Label(new Rect(377, 266, 526, 93), "开始新局将清除旧局存档，不进行物品带出结算。局外仓库保留。\n新Seed：" + requestedSeed);
                    Button(new Rect(377, 403, 250, 55), "取消", () => Panel = GamePanel.None);
                    Button(new Rect(650, 403, 252, 55), "确认开始", () => StartNewRun(requestedSeed), true, true);
                    break;
                case GamePanel.Leave:
                    Label(new Rect(371, 181, 532, 55), leaveApplication ? "退出游戏？" : "返回局外？", headingStyle, Gold);
                    string note = Store != null && Store.Session.pending != null ? "未完成的结算已保留，下次继续完成；物品不会重复入库。" : Run == null ? "已保存的存档与仓库会保留。" : "本次未保存进度不会保留，不额外存档，也不做物品结算。下次按最近手动存档继续。";
                    Label(new Rect(377, 266, 526, 110), note);
                    Button(new Rect(377, 411, 250, 55), "取消", () => Panel = ScreenMode == GameScreen.Home ? GamePanel.None : GamePanel.Pause);
                    Button(new Rect(650, 411, 252, 55), "确认", Leave, true, true);
                    break;
            }
        }
        void DrawInventory()
        {
            Box(new Rect(65, 118, 1150, 525));
            Label(new Rect(89, 132, 760, 40), "常规背包 " + Inventory.UsedSlots(Run) + "/20格 · 每格最多20件", headingStyle, Gold);
            for (int i = 0; i < 20; i++)
            {
                int slotIndex = i; var slot = Run.inventory[i]; float x = 89 + (i % 5) * 221, y = 183 + (i / 5) * 100;
                Fill(new Rect(x, y, 208, 91), "slot", new Color(0.09f, 0.14f, 0.21f));
                if (slot.units.Count == 0) { Label(new Rect(x + 8, y + 23, 190, 35), "空格", smallStyle, new Color(0.34f, 0.43f, 0.52f)); continue; }
                ItemKind kind = slot.units[0].kind;
                Label(new Rect(x + 8, y + 6, 190, 28), Rules.ItemNames[(int)kind] + " ×" + slot.units.Count, textStyle, Gold);
                Label(new Rect(x + 8, y + 33, 190, 23), Rules.RarityName(kind), smallStyle);
                Button(new Rect(x + 8, y + 60, 88, 26), "使用", () => { if (Inventory.Heal(Run, slotIndex)) Notify("恢复生命，当前 " + Run.health + "/60"); }, kind == ItemKind.Potion && Run.health < 60);
                Button(new Rect(x + 108, y + 60, 88, 26), "丢弃", () => { dropSlot = slotIndex; dropQuantity = 1; });
            }
            if (dropSlot >= 0 && Run.inventory[dropSlot].units.Count > 0)
            {
                dropQuantity = Mathf.Clamp(dropQuantity, 1, Run.inventory[dropSlot].units.Count);
                Label(new Rect(89, 596, 300, 33), "丢弃数量 " + dropQuantity);
                Button(new Rect(325, 590, 55, 36), "－", () => dropQuantity--, dropQuantity > 1);
                Button(new Rect(392, 590, 55, 36), "＋", () => dropQuantity++, dropQuantity < Run.inventory[dropSlot].units.Count);
                Button(new Rect(460, 590, 160, 36), "确认丢弃", () => { Inventory.Drop(Run, dropSlot, dropQuantity); dropSlot = -1; BuildWorld(false); });
            }
            Button(new Rect(1000, 591, 184, 36), "关闭 [Esc]", () => Panel = GamePanel.None);
        }
        void DrawCardList()
        {
            Box(new Rect(235, 106, 810, 550));
            bool drawPile = Panel == GamePanel.Draw, discardPile = Panel == GamePanel.Discard;
            var cards = drawPile ? Combat.draw : discardPile ? Combat.discard : Run.deck;
            Label(new Rect(263, 124, 750, 48), (drawPile ? "抽牌堆组成（顺序隐藏）" : discardPile ? "弃牌堆" : "本局卡组") + " · " + cards.Count + "张", headingStyle, Gold);
            int row = 0;
            foreach (var group in cards.GroupBy(c => c).OrderBy(g => g.Key))
            {
                float y = 195 + row++ * 38;
                Label(new Rect(263, y, 220, 33), Rules.CardNames[(int)group.Key] + " ×" + group.Count());
                Label(new Rect(499, y, 515, 33), Rules.Costs[(int)group.Key] + "能量 · " + Rules.Effects[(int)group.Key].Replace("\n", "，"), smallStyle);
            }
            Button(new Rect(720, 604, 295, 36), "关闭 [Esc]", () => Panel = GamePanel.None);
        }
    }
}
