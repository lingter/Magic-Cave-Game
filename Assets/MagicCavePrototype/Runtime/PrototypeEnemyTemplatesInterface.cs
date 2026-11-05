using System;
using System.Linq;
using UnityEngine;

namespace MagicCave.Prototype
{
    public sealed partial class PrototypeGame
    {
        int enemyTemplateIndex, enemyTurnIndex, enemyPresetKind;
        bool enemyTemplateActions;
        RunData enemyTemplatePreview;
        void DrawEnemyTemplateSettings()
        {
            var templates = settingsDraft.enemyTemplates;
            Button(new Rect(110, 192, 170, 34), "+ 新增怪物", () =>
            {
                if (!CommitSettingsNumber()) return;
                int id = templates.Count == 0 ? 4 : Math.Max(4, templates.Max(t => t.id) + 1);
                var template = EnemyTemplates.FromExisting(settingsDraft, (EnemyKind)enemyPresetKind);
                template.id = id; template.name = "自定义怪物" + id;
                templates.Add(template); enemyTemplateIndex = templates.Count - 1; enemyTurnIndex = customEffectIndex = customTermIndex = 0;
            }, templates.Count < 100);
            Button(new Rect(290, 192, 260, 34), "编辑现有：" + Rules.EnemyNames[enemyPresetKind] + " ▸", () =>
            { if (CommitSettingsNumber()) enemyPresetKind = (enemyPresetKind + 1) % 3; });
            Button(new Rect(565, 192, 120, 34), "载入编辑", () =>
            {
                if (!CommitSettingsNumber()) return;
                int index = templates.FindIndex(t => t.id == enemyPresetKind + 1);
                if (index < 0)
                {
                    if (templates.Count >= 100) { settingsError = "怪物模板最多100种。"; return; }
                    templates.Add(EnemyTemplates.FromExisting(settingsDraft, (EnemyKind)enemyPresetKind)); index = templates.Count - 1;
                }
                enemyTemplateIndex = index; enemyTurnIndex = customEffectIndex = customTermIndex = 0;
            });
            Button(new Rect(810, 192, 310, 34), enemyTemplateActions ? "行动词条 ▸ 切换基础属性" : "基础属性 ▸ 切换行动词条", () =>
            { if (CommitSettingsNumber()) enemyTemplateActions = !enemyTemplateActions; });
            if (templates.Count == 0)
            {
                Label(new Rect(110, 265, 1020, 140), "新增怪物模板，或选择原怪并载入编辑。\n每个模板可编辑基础属性、出率权重和循环行动；行动由目标、效果及属性数值项拼接。\n模板保存后在新局使用，已有存档继续沿用快照。", textStyle, Cyan); return;
            }
            enemyTemplateIndex = Math.Max(0, Math.Min(templates.Count - 1, enemyTemplateIndex));
            var selected = templates[enemyTemplateIndex];
            Button(new Rect(110, 240, 90, 34), "上一个", () => { if (CommitSettingsNumber()) { enemyTemplateIndex--; enemyTurnIndex = customEffectIndex = customTermIndex = 0; } }, enemyTemplateIndex > 0);
            Label(new Rect(210, 240, 400, 34), (enemyTemplateIndex + 1) + "/" + templates.Count + "  " + selected.name, smallStyle);
            Button(new Rect(620, 240, 100, 34), "下一个", () => { if (CommitSettingsNumber()) { enemyTemplateIndex++; enemyTurnIndex = customEffectIndex = customTermIndex = 0; } }, enemyTemplateIndex < templates.Count - 1);
            Button(new Rect(740, 240, 150, 34), "删除此模板", () =>
            { if (CommitSettingsNumber()) { templates.Remove(selected); enemyTemplateIndex = Math.Max(0, enemyTemplateIndex - 1); } });
            Label(new Rect(110, 290, 95, 34), "名称");
            Button(new Rect(210, 290, 510, 34), settingsFocus == "enemyName" ? settingsText + "│" : selected.name, () =>
            {
                if (!CommitSettingsNumber()) return;
                settingsFocus = "enemyName"; settingsText = selected.name; settingsNameSetter = value => selected.name = value;
            });
            if (!enemyTemplateActions)
            {
                Label(new Rect(110, 340, 1020, 27), "新模板按权重分配到同类出现位置；原怪默认权重1，载入原怪后由该模板接管其权重与行为。", smallStyle, Cyan);
                Label(new Rect(110, 380, 180, 34), "基础生命(1～10000)", smallStyle);
                SettingsNumber(300, 380, 180, "monsterHP", selected.health, v => selected.health = v, 1, 10000);
                Label(new Rect(650, 380, 230, 34), "攻击(0～1000)", smallStyle);
                SettingsNumber(900, 380, 180, "monsterATK", selected.attack, v => selected.attack = v, 0, 1000);
                Label(new Rect(110, 425, 180, 34), "防御(0～1000)", smallStyle);
                SettingsNumber(300, 425, 180, "monsterDEF", selected.defense, v => selected.defense = v, 0, 1000);
                Label(new Rect(650, 425, 230, 34), "重击攻击力(0～1000)", smallStyle);
                SettingsNumber(900, 425, 180, "monsterHeavy", selected.heavy, v => selected.heavy = v, 0, 1000);
                Label(new Rect(110, 470, 180, 34), "每级生命+(0～1000)", smallStyle);
                SettingsNumber(300, 470, 180, "monsterHPGrowth", selected.healthGrowth, v => selected.healthGrowth = v, 0, 1000);
                Label(new Rect(650, 470, 230, 34), "每级攻击／重击+(0～1000)", smallStyle);
                SettingsNumber(900, 470, 180, "monsterATKGrowth", selected.attackGrowth, v => selected.attackGrowth = v, 0, 1000);
                Label(new Rect(110, 515, 180, 34), "出率权重(0停用)", smallStyle);
                SettingsNumber(300, 515, 180, "monsterWeight", selected.weight, v => selected.weight = v, 0, 10000);
                Button(new Rect(650, 515, 430, 34), "沿用外观／分布：" + Rules.EnemyNames[(int)selected.appearance] + (selected.id > 3 ? " ▸" : ""), () =>
                { if (CommitSettingsNumber()) selected.appearance = (EnemyKind)(((int)selected.appearance + 1) % 3); }, selected.id > 3);
                Label(new Rect(110, 555, 1020, 27), "只替换同类位置的怪物模板，沿用奖励与镇守职责。权重0保留模板；每类至少保留一个可生成模板。", smallStyle, Cyan);
                return;
            }
            enemyTurnIndex = Math.Max(0, Math.Min(selected.turns.Count - 1, enemyTurnIndex));
            var turn = selected.turns[enemyTurnIndex];
            Button(new Rect(110, 335, 75, 34), "上行动", () => { if (CommitSettingsNumber()) { enemyTurnIndex--; customEffectIndex = customTermIndex = 0; } }, enemyTurnIndex > 0);
            Label(new Rect(190, 335, 85, 34), "行动" + (enemyTurnIndex + 1) + "/" + selected.turns.Count, smallStyle);
            Button(new Rect(278, 335, 30, 34), "↑", () =>
            {
                if (!CommitSettingsNumber()) return;
                int previous = enemyTurnIndex - 1;
                selected.turns[enemyTurnIndex] = selected.turns[previous]; selected.turns[previous] = turn; enemyTurnIndex = previous;
            }, enemyTurnIndex > 0);
            Button(new Rect(313, 335, 30, 34), "↓", () =>
            {
                if (!CommitSettingsNumber()) return;
                int next = enemyTurnIndex + 1;
                selected.turns[enemyTurnIndex] = selected.turns[next]; selected.turns[next] = turn; enemyTurnIndex = next;
            }, enemyTurnIndex < selected.turns.Count - 1);
            Button(new Rect(350, 335, 75, 34), "下行动", () => { if (CommitSettingsNumber()) { enemyTurnIndex++; customEffectIndex = customTermIndex = 0; } }, enemyTurnIndex < selected.turns.Count - 1);
            Button(new Rect(440, 335, 130, 34), "+ 循环行动", () =>
            { if (CommitSettingsNumber()) { selected.turns.Add(new EnemyTurnDefinition()); enemyTurnIndex = selected.turns.Count - 1; customEffectIndex = customTermIndex = 0; } }, selected.turns.Count < 8);
            Button(new Rect(585, 335, 135, 34), "删除此行动", () =>
            { if (CommitSettingsNumber()) { selected.turns.Remove(turn); enemyTurnIndex = Math.Max(0, enemyTurnIndex - 1); customEffectIndex = customTermIndex = 0; } }, selected.turns.Count > 1);
            DrawEffectEditor(turn.effects, true);
            var effect = turn.effects[Math.Max(0, Math.Min(turn.effects.Count - 1, customEffectIndex))];
            Box(new Rect(790, 290, 330, 320));
            Label(new Rect(810, 302, 290, 40), selected.name, smallStyle, Gold);
            Label(new Rect(810, 344, 290, 26), "循环行动 " + (enemyTurnIndex + 1) + "/" + selected.turns.Count, smallStyle, Cyan);
            Label(new Rect(810, 377, 290, 82), CardEffects.Sentence(effect).Replace("指定敌人", "指定玩家"), smallStyle, Gold);
            if (enemyTemplatePreview == null)
            {
                enemyTemplatePreview = new RunData { settingsVersion = 1, battleRandom = new RandomStream(1) };
                CharacterAttributes.Initialize(enemyTemplatePreview, AttributeKind.None);
            }
            var previewRun = enemyTemplatePreview; previewRun.settings = settingsDraft;
            var enemy = new EnemyData { kind = selected.appearance, templateId = selected.id, physicalDefense = selected.defense };
            var battle = new Battle(previewRun, enemy) { enemyTurn = enemyTurnIndex };
            Label(new Rect(810, 465, 290, 135), "1级预览（玩家无格挡）\n" + battle.Intent + "\n预计玩家失血：" + CharacterAttributes.Number(battle.ExpectedHealthLoss), smallStyle);
        }
    }
}
