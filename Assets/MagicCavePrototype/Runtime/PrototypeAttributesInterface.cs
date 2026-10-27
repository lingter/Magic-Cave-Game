using System;
using System.Linq;
using UnityEngine;

namespace MagicCave.Prototype
{
    public sealed partial class PrototypeGame
    {
        AttributeKind selectedSecondary = AttributeKind.None;
        RunData departurePreview;
        int equipmentPreviewSlot = -1, equipmentPage, settlementPage, warehousePage, resultPage;

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
            if (block > 0) return "获得" + CharacterAttributes.Number(block) + "点格挡" + (card == CardKind.Steady ? "，抽1张" : "");
            return Rules.Effects[(int)card];
        }

        void DrawDeparture()
        {
            Box(new Rect(210, 93, 860, 550));
            Label(new Rect(242, 111, 780, 42), "新局出发 · 选择副属性", headingStyle, Gold);
            Label(new Rect(244, 164, 780, 48), "骑士 / 战士公会    主属性：力量    Seed：" + requestedSeed, textStyle, Cyan);
            if (departurePreview == null)
            {
                departurePreview = new RunData { seed = requestedSeed };
                CharacterAttributes.Initialize(departurePreview, AttributeKind.Agility);
                TownRules.Apply(Store.Warehouse.town, departurePreview);
            }
            var stats = CharacterAttributes.Calculate(departurePreview);
            Label(new Rect(244, 214, 780, 43), "生命 " + stats.maxHealth + "    力量 " + stats.strength + "    敏捷 " + stats.agility + "    智慧 " + stats.wisdom);
            foreach (var candidate in new[] { AttributeKind.Agility, AttributeKind.Wisdom })
            {
                var choice = candidate; float x = candidate == AttributeKind.Agility ? 244 : 654;
                Button(new Rect(x, 277, 380, 90), (selectedSecondary == candidate ? "✓ " : "") + CharacterAttributes.Name(candidate) +
                    "作为副属性\n最终伤害 +" + (stats.Value(candidate) * (decimal)CharacterAttributes.SecondaryRate(departurePreview.attributeRules) / 100m).ToString("0.##") + "%", () =>
                { selectedSecondary = choice; departurePreview.character.secondary = choice; }, true, selectedSecondary == candidate);
            }
            Label(new Rect(244, 384, 780, 52), "本局选择固定；装备、升级和增益改变数值时，加成实时更新。", smallStyle);
            if (selectedSecondary != AttributeKind.None)
                Label(new Rect(244, 432, 780, 43), "ATK " + stats.attack.ToString("0.##") + "  ·  打击 " +
                    CharacterAttributes.Number(CombatNumbers.Damage(departurePreview, CardKind.Strike, inBattle: false)) + "  ·  重击 " +
                    CharacterAttributes.Number(CombatNumbers.Damage(departurePreview, CardKind.Heavy, inBattle: false)), textStyle, Gold);
            if (Store.Session.checkpoint != null)
                Label(new Rect(244, 479, 780, 50), "确认出发将放弃已有探索存档，不进行旧局物品结算；局外仓库保留。", smallStyle, new Color(1, .65f, .45f));
            Button(new Rect(244, 559, 370, 48), "取消", () => { Panel = GamePanel.None; departurePreview = null; });
            Button(new Rect(654, 559, 380, 48), "开始探索", () => StartNewRun(requestedSeed, selectedSecondary), selectedSecondary != AttributeKind.None, true);
        }

        void DrawCharacter()
        {
            Box(new Rect(60, 100, 1160, 550));
            Label(new Rect(87, 117, 900, 40), "角色 · 骑士", headingStyle, Gold);
            Button(new Rect(1010, 119, 180, 34), "关闭 [Esc]", () => Panel = GamePanel.None);
            if (!Run.UsesAttributes) { Label(new Rect(87, 200, 1000, 90), "此存档使用旧版规则。开始新局可选择副属性、获得装备和三属性成长。"); return; }
            var stats = CharacterAttributes.Calculate(Run);
            var state = Run.character; var template = Run.attributeRules.template;
            Label(new Rect(87, 170, 440, 37), "生命 " + CharacterAttributes.Number(Run.health) + "/" + Run.MaxHealth + "   等级 " + Run.level);
            Label(new Rect(87, 211, 440, 80), "力量 " + stats.strength + (state.secondary == AttributeKind.Strength ? " [副]" : " [主]") +
                "   敏捷 " + stats.agility + (state.secondary == AttributeKind.Agility ? " [副]" : "") +
                "   智慧 " + stats.wisdom + (state.secondary == AttributeKind.Wisdom ? " [副]" : "") +
                "\nATK " + stats.attack.ToString("0.##") + "  防御 " + stats.defense +
                "  最终伤害 +" + ((stats.secondaryMultiplier - 1) * 100).ToString("0.##") + "%", textStyle, Cyan);
            Label(new Rect(87, 290, 440, 84), WeaponDescription(stats.weapon), smallStyle);
            Label(new Rect(87, 385, 440, 120), "属性来源（力量 / 敏捷 / 智慧）\n模板：" + template.strength + " / " + template.agility + " / " + template.wisdom +
                "\n天赋：" + state.talentStrength + " / " + state.talentAgility + " / 0" +
                "\n等级：力量 +" + Run.level / template.primaryEveryLevels + "；" + CharacterAttributes.Name(state.secondary) + " +" + Run.level / template.secondaryEveryLevels +
                "\n装备：" + state.equipment.Sum(i => Run.attributeRules.Find(i.kind).strength) + " / " +
                state.equipment.Sum(i => Run.attributeRules.Find(i.kind).agility) + " / " + state.equipment.Sum(i => Run.attributeRules.Find(i.kind).wisdom), smallStyle);
            float modifierY = 516;
            foreach (var modifier in state.modifiers)
            {
                Label(new Rect(87, modifierY, 440, 25), CharacterAttributes.Name(modifier.attribute) + " +" + modifier.value +
                    (modifier.remainingBattles < 0 ? " · 本局永久" : " · 下一战生效，剩余" + modifier.remainingBattles + "场"), smallStyle, Gold);
                modifierY += 25;
            }
            for (int index = 0; index < 3; index++)
            {
                var slot = (EquipmentSlot)index; var item = state.equipment.Find(i => Run.attributeRules.Find(i.kind).slot == slot);
                float y = 169 + index * 48;
                Label(new Rect(550, y, 435, 36), (index == 0 ? "主手" : index == 1 ? "副手" : "护甲") + "：" +
                    (item != null ? Rules.ItemNames[(int)item.kind] : index == 0 ? "训练剑（基础能力）" : "空"));
                Button(new Rect(1085, y, 100, 34), "卸下", () =>
                { string reason = UnequipSlot(slot); if (reason.Length > 0) Notify(reason); else Notify("装备已卸下"); }, item != null);
            }
            var available = Run.inventory.Select((s, i) => new { slot = s, index = i }).Where(s => s.slot.units.Count == 1 && Rules.IsEquipment(s.slot.units[0].kind)).ToArray();
            int pages = Math.Max(1, (available.Length + 3) / 4); equipmentPage = Math.Min(equipmentPage, pages - 1);
            Label(new Rect(550, 318, 420, 29), "背包装备 · 点击查看替换效果", smallStyle, Cyan);
            Button(new Rect(1010, 317, 76, 28), "上一页", () => equipmentPage--, equipmentPage > 0);
            Button(new Rect(1100, 317, 85, 28), "下一页", () => equipmentPage++, equipmentPage + 1 < pages);
            int row = 0;
            foreach (var entry in available.Skip(equipmentPage * 4).Take(4))
            {
                int index = entry.index;
                Button(new Rect(550, 355 + row++ * 35, 635, 30), Rules.ItemNames[(int)entry.slot.units[0].kind] + " · 查看对比", () => equipmentPreviewSlot = index);
            }
            if (available.Length == 0) Label(new Rect(550, 366, 635, 60), "探索中拾取武器、盾牌或护甲后可在此装备。", smallStyle);
            if (equipmentPreviewSlot >= 0 && equipmentPreviewSlot < Run.inventory.Count && Run.inventory[equipmentPreviewSlot].units.Count == 1 &&
                Rules.IsEquipment(Run.inventory[equipmentPreviewSlot].units[0].kind)) DrawEquipmentPreview(stats);
        }

        string WeaponDescription(EquipmentDefinition weapon) => "武器基础ATK " + weapon.baseAttack + "\n属性修正：力量 " +
            (weapon.strengthScaling / 100m).ToString("0.##") + "% / 敏捷 " + (weapon.agilityScaling / 100m).ToString("0.##") +
            "% / 智慧 " + (weapon.wisdomScaling / 100m).ToString("0.##") + "%";

        void DrawEquipmentPreview(CharacterStats current)
        {
            int index = equipmentPreviewSlot; var item = Run.inventory[index].units[0];
            string reason = CharacterAttributes.EquipLock(Run, item);
            if (reason.Length > 0) { Label(new Rect(550, 509, 635, 85), reason, textStyle, Gold); return; }
            var preview = Rules.Copy(Run); Inventory.Equip(preview, index);
            var next = CharacterAttributes.Calculate(preview);
            var definition = Run.attributeRules.Find(item.kind);
            Label(new Rect(550, 502, 635, 62), Rules.ItemNames[(int)item.kind] + "：ATK " + current.attack.ToString("0.##") + " → " + next.attack.ToString("0.##") +
                "\n打击 " + CharacterAttributes.Number(CombatNumbers.Damage(Run, CardKind.Strike, inBattle: false)) + " → " +
                CharacterAttributes.Number(CombatNumbers.Damage(preview, CardKind.Strike, inBattle: false)) + "   重击 " +
                CharacterAttributes.Number(CombatNumbers.Damage(Run, CardKind.Heavy, inBattle: false)) + " → " +
                CharacterAttributes.Number(CombatNumbers.Damage(preview, CardKind.Heavy, inBattle: false)) +
                (definition.slot == EquipmentSlot.MainHand ? "\n基础ATK " + definition.baseAttack + " · 力/敏/智修正 " + definition.strengthScaling / 100m + "% / " +
                    definition.agilityScaling / 100m + "% / " + definition.wisdomScaling / 100m + "%" : ""), smallStyle, Cyan);
            string changes = "力 " + current.strength + "→" + next.strength + "  敏 " + current.agility + "→" + next.agility + "  智 " + current.wisdom + "→" + next.wisdom +
                "\n生命上限 " + current.maxHealth + "→" + next.maxHealth + "  防御 " + current.defense + "→" + next.defense;
            if (preview.health < Run.health) changes += "；当前生命将降至" + CharacterAttributes.Number(preview.health);
            changes += "\n最终伤害 +" + ((current.secondaryMultiplier - 1) * 100).ToString("0.##") + "% → +" + ((next.secondaryMultiplier - 1) * 100).ToString("0.##") + "%";
            Label(new Rect(550, 567, 430, 68), changes, smallStyle, preview.health < Run.health ? Gold : Ink);
            Button(new Rect(1003, 579, 182, 39), "确认替换", () =>
            { string result = EquipInventory(index); Notify(result.Length == 0 ? "装备已替换，伤害已更新" : result); equipmentPreviewSlot = -1; }, ScreenMode == GameScreen.Explore, true);
        }
    }
}
