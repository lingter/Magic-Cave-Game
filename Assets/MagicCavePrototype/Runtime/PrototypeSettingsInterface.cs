using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicCave.Prototype
{
    public sealed partial class PrototypeGame
    {
        PrototypeSettings settingsDraft;
        int settingsTab, settingsPage, settingsPool;
        string settingsFocus, settingsText = "", settingsError = "";
        Action<int> settingsSetter;
        int settingsMin, settingsMax;
        public void OpenSettings()
        {
            if (ScreenMode != GameScreen.Home || Store == null) return;
            settingsDraft = Rules.Copy(Store.Settings); settingsTab = settingsPage = settingsPool = 0;
            settingsFocus = null; settingsError = ""; CancelRoute(); Panel = GamePanel.Settings;
        }
        void ClearSettingsFocus() { settingsFocus = null; settingsSetter = null; }
        bool CommitSettingsNumber()
        {
            if (settingsFocus == null) return true;
            if (!int.TryParse(settingsText, out int value) || value < settingsMin || value > settingsMax)
            { settingsError = "请输入 " + settingsMin + "～" + settingsMax + " 范围内的整数。"; return false; }
            settingsSetter(value); ClearSettingsFocus(); settingsError = ""; return true;
        }
        void SettingsCharacter(char c)
        {
            if (Panel == GamePanel.Settings && settingsFocus != null && settingsText.Length < 7 && (char.IsDigit(c) || c == '-' && settingsText.Length == 0)) settingsText += c;
        }
        void ReadSettingsInput()
        {
            if (Panel != GamePanel.Settings || Keyboard.current == null) return;
            var k = Keyboard.current;
            if (k.escapeKey.wasPressedThisFrame) { ClearSettingsFocus(); settingsDraft = null; Panel = GamePanel.None; return; }
            if (settingsFocus == null) return;
            if (k.ctrlKey.isPressed && k.aKey.wasPressedThisFrame) settingsText = "";
            if (k.backspaceKey.wasPressedThisFrame && settingsText.Length > 0) settingsText = settingsText.Substring(0, settingsText.Length - 1);
            if (k.deleteKey.wasPressedThisFrame) settingsText = "";
            if (k.ctrlKey.isPressed && k.vKey.wasPressedThisFrame) settingsText = GUIUtility.systemCopyBuffer.Trim();
            if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) CommitSettingsNumber();
        }
        void SettingsNumber(float x, float y, float width, string id, int value, Action<int> setter, int min, int max, int step = 1)
        {
            Button(new Rect(x, y, 27, 34), "−", () => AdjustSetting(id, value, setter, min, max, -step));
            Button(new Rect(x + 30, y, width - 60, 34), settingsFocus == id ? settingsText + "│" : value.ToString(), () =>
            {
                if (!CommitSettingsNumber()) return;
                settingsFocus = id; settingsText = value.ToString(); settingsSetter = setter; settingsMin = min; settingsMax = max;
            }, true, settingsFocus == id);
            Button(new Rect(x + width - 27, y, 27, 34), "+", () => AdjustSetting(id, value, setter, min, max, step));
        }
        void AdjustSetting(string id, int value, Action<int> setter, int min, int max, int delta)
        {
            int current = settingsFocus == id && int.TryParse(settingsText, out int typed) ? typed : value;
            if (CommitSettingsNumber()) setter(Math.Max(min, Math.Min(max, current + delta)));
        }
        void SettingsToggle(float x, float y, string caption, bool value, Action<bool> setter)
        { Button(new Rect(x, y, 480, 43), (value ? "● 开启  " : "○ 关闭  ") + caption, () => setter(!value), true, value); }
        void DrawSettings()
        {
            Box(new Rect(70, 35, 1140, 650));
            Label(new Rect(100, 54, 750, 40), "设置 · 原型玩法调节", headingStyle, Gold);
            Label(new Rect(100, 99, 1060, 27), "保存后仅影响新局；继续存档沿用原快照。点击数字可输入，Enter确认；Esc取消本次修改。", smallStyle, Cyan);
            string[] tabs = { "功能开关", "卡牌", "怪物", "物品掉落" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int tab = i; Button(new Rect(100 + i * 270, 139, 250, 38), tabs[i], () => { if (CommitSettingsNumber()) { settingsTab = tab; settingsPage = 0; } }, true, settingsTab == i);
            }
            int pages = 1;
            if (settingsTab == 0)
            {
                SettingsToggle(110, 210, "探索迷雾", settingsDraft.fog, v => settingsDraft.fog = v);
                SettingsToggle(650, 210, "火把照明", settingsDraft.torch, v => settingsDraft.torch = v);
                SettingsToggle(110, 261, "集齿者", settingsDraft.merchant, v => settingsDraft.merchant = v);
                SettingsToggle(650, 261, "牙齿奖励与交易", settingsDraft.teeth, v => settingsDraft.teeth = v);
                SettingsToggle(110, 312, "出发时应用天赋", settingsDraft.talents, v => settingsDraft.talents = v);
                SettingsToggle(650, 312, "本局经验与升级", settingsDraft.growth, v => settingsDraft.growth = v);
                SettingsToggle(110, 363, "升级选牌", settingsDraft.cardRewards, v => settingsDraft.cardRewards = v);
                SettingsToggle(650, 363, "破墙", settingsDraft.digging, v => settingsDraft.digging = v);
                SettingsToggle(110, 414, "消耗品掉落与使用", settingsDraft.consumables, v => settingsDraft.consumables = v);
                SettingsToggle(650, 414, "装备掉落与穿戴", settingsDraft.equipment, v => settingsDraft.equipment = v);
                SettingsToggle(110, 465, "物品镇守怪", settingsDraft.itemGuards, v => settingsDraft.itemGuards = v);
                SettingsToggle(650, 465, "通道镇守怪", settingsDraft.corridorGuards, v => settingsDraft.corridorGuards = v);
                Label(new Rect(110, 520, 1020, 49), "核心移动、探索、卡牌战斗、首领和结算保留。关闭物品镇守后可直接接近箱袋。关闭装备仍保留基础训练剑盾。", smallStyle);
            }
            else if (settingsTab == 1)
            {
                pages = 2;
                Label(new Rect(110, 193, 200, 24), "卡牌", smallStyle, Gold);
                Label(new Rect(325, 193, 175, 24), "初始张数(0～30)", smallStyle, Gold);
                Label(new Rect(515, 193, 175, 24), "能量(0～9)", smallStyle, Gold);
                Label(new Rect(705, 193, 205, 24), "效果倍率%(0～1000)", smallStyle, Gold);
                Label(new Rect(920, 193, 175, 24), "抽牌数(0～20)", smallStyle, Gold);
                for (int i = settingsPage * 7; i < Math.Min(10, settingsPage * 7 + 7); i++)
                {
                    int n = i; float y = 224 + (i % 7) * 43;
                    Label(new Rect(110, y, 200, 32), Rules.CardNames[i]);
                    SettingsNumber(325, y, 165, "count" + i, settingsDraft.cardCounts[i], v => settingsDraft.cardCounts[n] = v, 0, 30);
                    SettingsNumber(515, y, 165, "cost" + i, settingsDraft.cardCosts[i], v => settingsDraft.cardCosts[n] = v, 0, 9);
                    if (CombatNumbers.IsAttack((CardKind)i) || CombatNumbers.IsDefense((CardKind)i)) SettingsNumber(705, y, 180, "power" + i, settingsDraft.cardPower[i], v => settingsDraft.cardPower[n] = v, 0, 1000, 10);
                    SettingsNumber(920, y, 165, "draw" + i, settingsDraft.cardDraw[i], v => settingsDraft.cardDraw[n] = v, 0, 20);
                }
                if (settingsPage == 1) { Label(new Rect(110, 370, 400, 32), "战意 · 每次获得的攻击增益"); SettingsNumber(705, 370, 180, "spirit", settingsDraft.spirit, v => settingsDraft.spirit = v, 0, 100); }
                Label(new Rect(110, 537, 1020, 47), "初始牌组共 " + settingsDraft.cardCounts.Sum() + " 张，允许1～100张。倍率作用于攻击／防御结算；整备使用抽牌数，战意使用独立增益。张数为0只移出初始牌组。", smallStyle);
            }
            else if (settingsTab == 2)
            {
                string[] headers = { "基础生命", "普通攻击", "首领重击", "格挡", "防御(-1默认)" };
                for (int c = 0; c < 5; c++) Label(new Rect(360 + c * 162, 202, 160, 25), headers[c], smallStyle, Gold);
                for (int i = 0; i < 3; i++)
                {
                    int n = i; float y = 241 + i * 53; Label(new Rect(110, y, 240, 32), Rules.EnemyNames[i]);
                    SettingsNumber(350, y, 150, "hp" + i, settingsDraft.enemyHealth[i], v => settingsDraft.enemyHealth[n] = v, 1, 10000, 5);
                    SettingsNumber(512, y, 150, "atk" + i, settingsDraft.enemyAttack[i], v => settingsDraft.enemyAttack[n] = v, 0, 1000);
                    if (i == 2) SettingsNumber(674, y, 150, "heavy" + i, settingsDraft.enemyHeavy[i], v => settingsDraft.enemyHeavy[n] = v, 0, 1000);
                    if (i > 0) SettingsNumber(836, y, 150, "block" + i, settingsDraft.enemyBlock[i], v => settingsDraft.enemyBlock[n] = v, 0, 1000);
                    SettingsNumber(998, y, 150, "def" + i, settingsDraft.enemyDefense[i], v => settingsDraft.enemyDefense[n] = v, -1, 1000);
                }
                for (int i = 0; i < 2; i++) { int n = i; float y = 419 + i * 47; Label(new Rect(110, y, 550, 32), Rules.EnemyNames[i] + " · 每层自由怪数量（-1保持原生成）"); SettingsNumber(705, y, 180, "roam" + i, settingsDraft.roamingCounts[i], v => settingsDraft.roamingCounts[n] = v, -1, 30); }
                Label(new Rect(110, 530, 1020, 52), "生命每级+4，攻击每级+1。物品／通道镇守在功能页独立开关，数量由入口决定；首领固定1只。自由怪另加至每层，不占用商人邻格。", smallStyle);
            }
            else
            {
                string[] names = { "首层装备箱", "后续装备箱", "财宝箱", "消耗品袋", "首领遗物奖励" };
                var pools = new[] { settingsDraft.drops.firstEquipment, settingsDraft.drops.equipment, settingsDraft.drops.treasure, settingsDraft.drops.supplies };
                Button(new Rect(110, 190, 300, 30), names[settingsPool] + "  ▸ 切换物品池", () => { if (CommitSettingsNumber()) { settingsPool = (settingsPool + 1) % 5; settingsPage = 0; } });
                if (settingsPool == 4)
                {
                    Label(new Rect(110, 255, 450, 36), "击败首领后：古代遗物出率（%）");
                    SettingsNumber(705, 255, 180, "bossRelic", settingsDraft.bossRelicChance, v => settingsDraft.bossRelicChance = v, 0, 100, 5);
                    Label(new Rect(110, 320, 1000, 60), "0%不生成遗物，100%必定生成1件。击败首领后的返回门始终生成。", smallStyle);
                }
                else
                {
                var pool = pools[settingsPool]; pages = Math.Max(1, (pool.Count + 6) / 7);
                Label(new Rect(420, 193, 170, 27), "权重(0停用)", smallStyle, Gold);
                Label(new Rect(620, 193, 155, 27), "最少件数", smallStyle, Gold);
                Label(new Rect(805, 193, 155, 27), "最多件数", smallStyle, Gold);
                Label(new Rect(1000, 193, 150, 27), "池内概率", smallStyle, Gold);
                int total = pool.Where(d => DropEnabled(d.kind)).Sum(d => d.weight);
                for (int i = settingsPage * 7; i < Math.Min(pool.Count, settingsPage * 7 + 7); i++)
                {
                    var drop = pool[i]; float y = 235 + (i % 7) * 42;
                    Label(new Rect(110, y, 280, 32), Rules.ItemNames[(int)drop.kind]);
                    SettingsNumber(420, y, 170, "weight" + i, drop.weight, v => drop.weight = v, 0, 100000);
                    SettingsNumber(620, y, 155, "min" + i, drop.minimum, v => drop.minimum = v, 1, 20);
                    SettingsNumber(805, y, 155, "max" + i, drop.maximum, v => drop.maximum = v, 1, 20);
                    Label(new Rect(1000, y, 150, 32), !DropEnabled(drop.kind) ? "功能已关闭" : (total == 0 ? "0" : (100d * drop.weight / total).ToString("0.##")) + "%", smallStyle, Cyan);
                }
                Label(new Rect(110, 537, 1040, 45), "每箱／袋抽取一种物品，再随机件数。某池全部权重为0或对应功能关闭时开出空箱／袋。首领遗物出率可切换到独立页面设置。最少件数不能大于最多件数。", smallStyle);
                }
            }
            if (pages > 1)
            {
                Button(new Rect(885, 585, 82, 29), "上一页", () => { if (CommitSettingsNumber()) settingsPage--; }, settingsPage > 0);
                Label(new Rect(970, 585, 80, 29), (settingsPage + 1) + " / " + pages, buttonStyle);
                Button(new Rect(1055, 585, 82, 29), "下一页", () => { if (CommitSettingsNumber()) settingsPage++; }, settingsPage < pages - 1);
            }
            Label(new Rect(110, 582, 750, 34), settingsError, smallStyle, new Color(1, .55f, .48f));
            Button(new Rect(110, 632, 250, 36), "恢复全部默认（待保存）", () => { ClearSettingsFocus(); settingsDraft = new PrototypeSettings(); settingsError = ""; });
            Button(new Rect(650, 632, 225, 36), "取消", () => { ClearSettingsFocus(); Panel = GamePanel.None; settingsDraft = null; });
            Button(new Rect(905, 632, 235, 36), "保存设置", () =>
            {
                if (!CommitSettingsNumber()) return;
                try { Store.SaveSettings(settingsDraft); Panel = GamePanel.None; departurePreview = null; Notify("设置已保存，下次新局生效"); }
                catch (Exception exception) { settingsError = exception.Message; }
            }, true, true);
        }
        bool DropEnabled(ItemKind kind) => (!Rules.IsEquipment(kind) || settingsDraft.equipment) &&
            (!ContainerLoot.Matches(ContainerKind.Supplies, kind) || settingsDraft.consumables) && (kind != ItemKind.Torch || settingsDraft.torch);
    }
}
