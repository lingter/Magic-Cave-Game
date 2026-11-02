using System;
using System.Linq;
using UnityEngine;

namespace MagicCave.Prototype
{
    public sealed partial class PrototypeGame
    {
        RunData departurePreview;
        bool attributeDetails;
        int equipmentPreviewSlot = -1, settlementPage, warehousePage, resultPage;

        public string CardDescription(RunData run, CardKind card, bool inBattle)
        {
            bool active = inBattle && Combat != null && Combat.run == run;
            if (CombatNumbers.IsAttack(card))
            {
                double damage = CombatNumbers.Damage(run, card, active ? Combat.spirit : 0, active ? Combat.playerBlock : 0, inBattle);
                string description = card == CardKind.Double ? "每次" + CharacterAttributes.Number(damage) + "伤害，共2次" :
                    "造成" + CharacterAttributes.Number(damage) + "点物理伤害";
                if (card == CardKind.Quick) description += "，抽1张";
                if (card == CardKind.Shield) description += "\n不消耗格挡";
                return description;
            }
            double block = CombatNumbers.Block(run, card, inBattle);
            if (CombatNumbers.IsDefense(card)) return "获得" + CharacterAttributes.Number(block) + "点格挡" + (card == CardKind.Steady ? "，抽1张" : "");
            return Rules.Effects[(int)card];
        }

        void DrawDeparture()
        {
            Box(new Rect(210, 125, 860, 470));
            Label(new Rect(244, 148, 780, 42), "新局出发 · 骑士", headingStyle, Gold);
            if (departurePreview == null)
            {
                departurePreview = new RunData { seed = requestedSeed };
                CharacterAttributes.Initialize(departurePreview, AttributeKind.None);
                TownRules.Apply(Store.Warehouse.town, departurePreview);
            }
            var stats = CharacterAttributes.Calculate(departurePreview);
            Label(new Rect(244, 208, 780, 38), "Seed：" + requestedSeed + "    生命 " + stats.maxHealth, textStyle, Cyan);
            Label(new Rect(244, 264, 780, 38), "力量 " + stats.strength + "       敏捷 " + stats.agility + "       智慧 " + stats.wisdom);
            Label(new Rect(244, 322, 780, 58), "三属性通过武器修正和天赋生效。\n本局采用出发时的天赋快照。", textStyle);
            Label(new Rect(244, 397, 780, 42), "ATK " + stats.attack.ToString("0.##") + "   打击 " +
                CharacterAttributes.Number(CombatNumbers.Damage(departurePreview, CardKind.Strike, inBattle: false)), textStyle, Gold);
            if (Store.Session.checkpoint != null) Label(new Rect(244, 447, 780, 48), "开始新局将放弃已有探索存档，不发放旧局带出奖励。", smallStyle, Gold);
            Button(new Rect(244, 525, 370, 45), "取消", () => { Panel = GamePanel.None; departurePreview = null; });
            Button(new Rect(654, 525, 380, 45), "开始探索", () => StartNewRun(requestedSeed), true, true);
        }

        void DrawCharacter()
        {
            DrawInventory();
        }

        void DrawAttributeDetails()
        {
            Box(new Rect(70, 205, 485, 396));
            Label(new Rect(87, 218, 340, 37), "属性与增益详情", headingStyle, Gold);
            Button(new Rect(440, 217, 100, 32), "关闭", () => attributeDetails = false);
            if (!Run.UsesAttributes) { Label(new Rect(87, 278, 440, 80), "旧局保留原固定数值规则。", smallStyle); return; }
            var stats = CharacterAttributes.Calculate(Run); var state = Run.character; var template = Run.attributeRules.template;
            Label(new Rect(87, 267, 440, 40), "等级 " + Run.level + "   生命 " + CharacterAttributes.Number(Run.health) + "/" + stats.maxHealth +
                "   ATK " + stats.attack.ToString("0.##") + "   防御 " + stats.defense, smallStyle, Cyan);
            Label(new Rect(87, 313, 440, 70), WeaponDescription(stats.weapon), smallStyle);
            Label(new Rect(87, 384, 440, 73), "模板力/敏/智：" + template.strength + "/" + template.agility + "/" + template.wisdom +
                "\n天赋加成：+" + state.talentStrength + "/+" + state.talentAgility + "/+" + state.talentWisdom +
                "\n装备加成：+" + state.equipment.Sum(i => Run.attributeRules.Find(i.kind).strength) + "/+" +
                state.equipment.Sum(i => Run.attributeRules.Find(i.kind).agility) + "/+" + state.equipment.Sum(i => Run.attributeRules.Find(i.kind).wisdom), smallStyle);
            float y = 462;
            foreach (var modifier in state.modifiers)
            {
                Label(new Rect(87, y, 440, 27), CharacterAttributes.Name(modifier.attribute) + " +" + modifier.value +
                    (modifier.remainingBattles < 0 ? " · 本局永久" : " · 剩余" + modifier.remainingBattles + "场"), smallStyle, Gold); y += 27;
            }
        }
        string WeaponDescription(EquipmentDefinition weapon) => "武器基础ATK " + weapon.baseAttack + "\n属性修正：力量 " +
            (weapon.strengthScaling / 100m).ToString("0.##") + "% / 敏捷 " + (weapon.agilityScaling / 100m).ToString("0.##") +
            "% / 智慧 " + (weapon.wisdomScaling / 100m).ToString("0.##") + "%";

        void DrawEquipmentPreview(CharacterStats current)
        {
            int index = equipmentPreviewSlot; var item = Run.inventory[index].units[0];
            string reason = CharacterAttributes.EquipLock(Run, item);
            if (reason.Length > 0) { Label(new Rect(585, 548, 600, 70), reason, smallStyle, Gold); return; }
            var preview = Rules.Copy(Run); Inventory.Equip(preview, index);
            var next = CharacterAttributes.Calculate(preview);
            string detail = Rules.ItemNames[(int)item.kind] + "  ATK " + current.attack.ToString("0.##") + " → " + next.attack.ToString("0.##") +
                "  防御 " + current.defense + " → " + next.defense + "\n打击 " + CharacterAttributes.Number(CombatNumbers.Damage(Run, CardKind.Strike, inBattle: false)) + " → " +
                CharacterAttributes.Number(CombatNumbers.Damage(preview, CardKind.Strike, inBattle: false)) +
                "   力/敏/智 " + next.strength + "/" + next.agility + "/" + next.wisdom + "   生命上限 " + current.maxHealth + " → " + next.maxHealth;
            if (preview.health < Run.health) detail += "\n当前生命将降至" + CharacterAttributes.Number(preview.health);
            Label(new Rect(585, 545, 605, 62), detail, smallStyle, Cyan);
            Button(new Rect(1010, 603, 180, 30), "装备 / 替换", () =>
            { string result = EquipInventory(index); Notify(result.Length == 0 ? "装备已替换" : result); equipmentPreviewSlot = -1; }, ScreenMode == GameScreen.Explore, true);
        }
    }
}
