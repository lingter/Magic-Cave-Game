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
        Action<string> settingsNameSetter;
        int customCardIndex, customEffectIndex, customTermIndex;
        RunData customCardPreview;
        bool customPreviewTalents;
        int settingsMin, settingsMax;
        public void OpenSettings()
        {
            if (ScreenMode != GameScreen.Home || Store == null) return;
            settingsDraft = Rules.Copy(Store.Settings); settingsTab = settingsPage = settingsPool = 0;
            ClearSettingsFocus(); settingsError = ""; customCardIndex = customEffectIndex = customTermIndex = 0; customCardPreview = null; enemyTemplateIndex = enemyTurnIndex = enemyPresetKind = 0; enemyTemplateActions = false; enemyTemplatePreview = null; CancelRoute(); Panel = GamePanel.Settings;
        }
        void ClearSettingsFocus() { settingsFocus = null; settingsSetter = null; settingsNameSetter = null; }
        bool CommitSettingsNumber()
        {
            if (settingsFocus == null) return true;
            if (settingsNameSetter != null)
            {
                string name = settingsText.Trim();
                if (name.Length < 1 || name.Length > 16 || name.Any(char.IsControl)) { settingsError = "名称需要1～16字。"; return false; }
                settingsNameSetter(name); ClearSettingsFocus(); settingsError = ""; return true;
            }
            if (!int.TryParse(settingsText, out int value) || value < settingsMin || value > settingsMax)
            { settingsError = "请输入 " + settingsMin + "～" + settingsMax + " 范围内的整数。"; return false; }
            settingsSetter(value); ClearSettingsFocus(); settingsError = ""; return true;
        }
        void SettingsCharacter(char c)
        {
            if (Panel != GamePanel.Settings || settingsFocus == null || char.IsControl(c)) return;
            if (settingsNameSetter != null) { if (settingsText.Length < 16) settingsText += c; }
            else if (settingsText.Length < 7 && (char.IsDigit(c) || c == '-' && settingsText.Length == 0)) settingsText += c;
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
            string[] tabs = { "功能开关", "卡牌", "怪物", "物品掉落", "自定义卡牌", "怪物模板" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int tab = i; Button(new Rect(100 + i * 180, 139, 170, 38), tabs[i], () => { if (CommitSettingsNumber()) { settingsTab = tab; settingsPage = 0; } }, true, settingsTab == i);
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
                Label(new Rect(110, 537, 1020, 47), "初始牌组共 " + settingsDraft.DeckCount + " 张，允许1～100张。倍率作用于攻击／防御结算；整备使用抽牌数，战意使用独立增益。张数为0只移出初始牌组。", smallStyle);
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
                Label(new Rect(110, 530, 1020, 52), "模板怪优先用模板配置。原怪生命每级+4，攻击每级+1。物品／通道镇守在功能页独立开关，数量由入口决定；首领固定1只。自由怪另加至每层，不占用商人邻格。", smallStyle);
            }
            else if (settingsTab == 4) DrawCustomCardSettings();
            else if (settingsTab == 5) DrawEnemyTemplateSettings();
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
            Button(new Rect(110, 632, 250, 36), "恢复全部默认（待保存）", () => { ClearSettingsFocus(); settingsDraft = new PrototypeSettings(); customCardIndex = 0; settingsPage = 0; settingsError = ""; });
            Button(new Rect(650, 632, 225, 36), "取消", () => { ClearSettingsFocus(); Panel = GamePanel.None; settingsDraft = null; });
            Button(new Rect(905, 632, 235, 36), "保存设置", () =>
            {
                if (!CommitSettingsNumber()) return;
                try { Store.SaveSettings(settingsDraft); Panel = GamePanel.None; departurePreview = null; Notify("设置已保存，下次新局生效"); }
                catch (Exception exception) { settingsError = exception.Message; }
            }, true, true);
        }
        void DrawCustomCardSettings()
        {
            var cards = settingsDraft.customCards;
            Button(new Rect(110, 192, 220, 34), "+ 新增卡牌", () =>
            {
                if (!CommitSettingsNumber()) return;
                int id = cards.Count == 0 ? 10 : cards.Max(c => c.id) + 1;
                cards.Add(new CustomCardDefinition { id = id, name = "自定义牌" + (cards.Count + 1), effectsVersion = 1, effects = new List<CardEffectEntry> { new CardEffectEntry() } });
                customCardIndex = cards.Count - 1; customEffectIndex = customTermIndex = 0;
            }, cards.Count < 100);
            Label(new Rect(350, 192, 770, 34), "共" + cards.Count + "种 · 初始牌组 " + settingsDraft.DeckCount + "/100张", smallStyle, Cyan);
            if (cards.Count == 0) { Label(new Rect(110, 260, 1030, 90), "点击新增卡牌，用目标、效果与属性数值项拼接卡面。\n保存后按入组张数加入新局；取消将放弃草稿。", textStyle); return; }
            customCardIndex = Math.Max(0, Math.Min(cards.Count - 1, customCardIndex));
            var card = cards[customCardIndex];
            Button(new Rect(110, 240, 100, 34), "上一张", () => { if (CommitSettingsNumber()) customCardIndex--; }, customCardIndex > 0);
            Label(new Rect(220, 240, 390, 34), (customCardIndex + 1) + "/" + cards.Count + "  " + card.name, smallStyle);
            Button(new Rect(620, 240, 100, 34), "下一张", () => { if (CommitSettingsNumber()) customCardIndex++; }, customCardIndex < cards.Count - 1);
            Button(new Rect(740, 240, 150, 34), "删除此牌", () => { if (CommitSettingsNumber()) { cards.Remove(card); ClearSettingsFocus(); customCardIndex = Math.Max(0, customCardIndex - 1); } });
            Label(new Rect(110, 290, 95, 34), "名称");
            Button(new Rect(210, 290, 510, 34), settingsFocus == "customName" ? settingsText + "│" : card.name, () =>
            {
                if (!CommitSettingsNumber()) return;
                settingsFocus = "customName"; settingsText = card.name; settingsNameSetter = v => card.name = v;
            });
            Label(new Rect(110, 335, 100, 34), "入组0～30");
            SettingsNumber(220, 335, 180, "customCount", card.count, v => card.count = v, 0, 30);
            Label(new Rect(430, 335, 100, 34), "能量0～9");
            SettingsNumber(540, 335, 180, "customCost", card.cost, v => card.cost = v, 0, 9);
            if (!card.UsesEffects)
            {
                Button(new Rect(110, 385, 610, 43), "改用词条编辑（重设此牌效果）", () =>
                {
                    if (!CommitSettingsNumber()) return;
                    card.effectsVersion = 1; card.effects = new List<CardEffectEntry> { new CardEffectEntry() };
                    customEffectIndex = customTermIndex = 0;
                });
                Label(new Rect(110, 450, 610, 94), "这是旧模板卡，保存或继续旧局会保留原效果。点击上方按钮才会在草稿中重设为100%实际攻击力伤害，随后可自由拼接词条。", smallStyle, Cyan);
            }
            else DrawEffectEditor(card.effects, false);
            if (customCardPreview == null || customPreviewTalents != settingsDraft.talents)
            {
                customCardPreview = new RunData { settingsVersion = 1 };
                CharacterAttributes.Initialize(customCardPreview, AttributeKind.None);
                if (settingsDraft.talents) TownRules.Apply(Store.Warehouse.town, customCardPreview);
                customPreviewTalents = settingsDraft.talents;
            }
            customCardPreview.settings = settingsDraft;
            var rect = new Rect(790, 290, 330, 320); Box(rect);
            Label(new Rect(810, 302, 290, 40), card.name, smallStyle, Gold);
            Label(new Rect(810, 344, 290, 26), card.cost + "能量 · 按顺序执行", smallStyle, Cyan);
            if (card.UsesEffects)
            {
                var effect = card.effects[Math.Max(0, Math.Min(card.effects.Count - 1, customEffectIndex))];
                Label(new Rect(810, 377, 290, 82), "词条句式：" + CardEffects.Sentence(effect), smallStyle, Gold);
            }
            Label(new Rect(810, 465, 290, 138), CardDescription(customCardPreview, (CardKind)card.id, false), card.UsesEffects && card.effects.Count > 6 ? denseCardStyle : smallStyle);
        }
        void DrawEffectEditor(List<CardEffectEntry> effects, bool monster)
        {
            customEffectIndex = Math.Max(0, Math.Min(effects.Count - 1, customEffectIndex));
            var effect = effects[customEffectIndex];
            Button(new Rect(110, 380, 75, 34), "上一条", () => { if (CommitSettingsNumber()) { customEffectIndex--; customTermIndex = 0; } }, customEffectIndex > 0);
            Label(new Rect(190, 380, 85, 34), "词条" + (customEffectIndex + 1) + "/" + effects.Count, smallStyle);
            Button(new Rect(278, 380, 30, 34), "↑", () =>
            {
                if (!CommitSettingsNumber()) return;
                int previous = customEffectIndex - 1;
                effects[customEffectIndex] = effects[previous]; effects[previous] = effect; customEffectIndex = previous;
            }, customEffectIndex > 0);
            Button(new Rect(313, 380, 30, 34), "↓", () =>
            {
                if (!CommitSettingsNumber()) return;
                int next = customEffectIndex + 1;
                effects[customEffectIndex] = effects[next]; effects[next] = effect; customEffectIndex = next;
            }, customEffectIndex < effects.Count - 1);
            Button(new Rect(350, 380, 75, 34), "下一条", () => { if (CommitSettingsNumber()) { customEffectIndex++; customTermIndex = 0; } }, customEffectIndex < effects.Count - 1);
            Button(new Rect(440, 380, 130, 34), "+ 效果词条", () =>
            {
                if (!CommitSettingsNumber()) return;
                effects.Add(new CardEffectEntry()); customEffectIndex = effects.Count - 1; customTermIndex = 0;
            }, effects.Count < CardEffects.MaxEffects);
            Button(new Rect(585, 380, 135, 34), "删除此词条", () =>
            { if (CommitSettingsNumber()) { effects.Remove(effect); customEffectIndex = Math.Max(0, customEffectIndex - 1); customTermIndex = 0; } }, effects.Count > 1);
            Button(new Rect(110, 425, 205, 34), CardEffects.KindNames[(int)effect.kind] + " ▸", () =>
            {
                if (!CommitSettingsNumber()) return;
                effect.kind = (CardEffectKind)(((int)effect.kind + 1) % 4);
                if (monster && effect.kind == CardEffectKind.Draw) effect.kind = CardEffectKind.Spirit;
                effect.repeat = 1;
                effect.target = effect.kind == CardEffectKind.Damage ? CardEffectTarget.Enemy : CardEffectTarget.Self;
            });
            bool targetChoice = effect.kind == CardEffectKind.Damage || effect.kind == CardEffectKind.Block;
            Button(new Rect(330, 425, 210, 34), "目标：" + (monster && effect.target == CardEffectTarget.Enemy ? "指定玩家" : CardEffects.TargetName(effect.target)) + (targetChoice ? " ▸" : ""), () =>
            {
                if (CommitSettingsNumber()) effect.target = effect.target == CardEffectTarget.Enemy ? CardEffectTarget.Self : CardEffectTarget.Enemy;
            }, targetChoice);
            if (effect.kind == CardEffectKind.Damage)
            {
                Label(new Rect(550, 425, 55, 34), "段数", smallStyle);
                SettingsNumber(610, 425, 110, "effectRepeat", effect.repeat, v => effect.repeat = v, 1, 10);
            }
            customTermIndex = Math.Max(0, Math.Min(effect.terms.Count - 1, customTermIndex));
            var term = effect.terms[customTermIndex];
            Button(new Rect(110, 470, 60, 34), "上项", () => { if (CommitSettingsNumber()) customTermIndex--; }, customTermIndex > 0);
            Label(new Rect(175, 470, 95, 34), "数值 " + (customTermIndex + 1) + "/" + effect.terms.Count, buttonStyle);
            Button(new Rect(275, 470, 60, 34), "下项", () => { if (CommitSettingsNumber()) customTermIndex++; }, customTermIndex < effect.terms.Count - 1);
            Button(new Rect(350, 470, 170, 34), "+ 相加数值项", () =>
            { if (CommitSettingsNumber()) { effect.terms.Add(new CardFormulaTerm()); customTermIndex = effect.terms.Count - 1; } }, effect.terms.Count < CardEffects.MaxTerms);
            Button(new Rect(535, 470, 185, 34), "删除此数值项", () =>
            { if (CommitSettingsNumber()) { effect.terms.Remove(term); customTermIndex = Math.Max(0, customTermIndex - 1); } }, effect.terms.Count > 1);
            Button(new Rect(110, 515, 230, 34), CardEffects.SourceNames[(int)term.source] + " ▸", () =>
            {
                if (!CommitSettingsNumber()) return;
                var sources = monster ? EnemyTemplates.Sources : new[] { CardValueSource.ActualAttack, CardValueSource.Strength, CardValueSource.Agility, CardValueSource.Wisdom, CardValueSource.CurrentBlock, CardValueSource.Defense, CardValueSource.Fixed };
                term.source = sources[(Array.IndexOf(sources, term.source) + 1) % sources.Length];
            });
            SettingsNumber(365, 515, 180, "effectAmount", term.amount, v => term.amount = v, 0, 1000, term.source == CardValueSource.Fixed ? 1 : 10);
            Label(new Rect(560, 515, 160, 34), term.source == CardValueSource.Fixed ? "固定值(0～1000)" : "倍率%(0～1000)", smallStyle);
            Label(new Rect(110, 555, 610, 27), "先求数值项之和，再统一取整。当前单敌；伤害逐段扣防御／格挡。", smallStyle, Cyan);
        }
        bool DropEnabled(ItemKind kind) => (!Rules.IsEquipment(kind) || settingsDraft.equipment) &&
            (!ContainerLoot.Matches(ContainerKind.Supplies, kind) || settingsDraft.consumables) && (kind != ItemKind.Torch || settingsDraft.torch);
    }
}
