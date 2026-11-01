using System;
using System.Collections.Generic;
using System.Linq;

namespace MagicCave.Prototype
{
    [Serializable]
    public sealed class ContainerDrop
    {
        public ItemKind kind;
        public int weight, minimum = 1, maximum = 1;
        public ContainerDrop(ItemKind kind, int weight, int minimum = 1, int maximum = 1)
        { this.kind = kind; this.weight = weight; this.minimum = minimum; this.maximum = maximum; }
    }

    [Serializable]
    public sealed class ContainerLootRules
    {
        // Prototype trial values. Each run saves a copy so tuning cannot reroll a saved run.
        public int equipmentCount = 2, treasureCount = 5, suppliesCount = 3;
        public List<ContainerDrop> firstEquipment, equipment, treasure, supplies;
        public static ContainerLootRules Defaults() => new ContainerLootRules
        {
            firstEquipment = new List<ContainerDrop> { new ContainerDrop(ItemKind.IronSword, 1),
                new ContainerDrop(ItemKind.SwiftSword, 1), new ContainerDrop(ItemKind.InscribedSword, 1) },
            equipment = new List<ContainerDrop> { new ContainerDrop(ItemKind.IronSword, 9),
                new ContainerDrop(ItemKind.SwiftSword, 9), new ContainerDrop(ItemKind.InscribedSword, 9),
                new ContainerDrop(ItemKind.RefinedSword, 3), new ContainerDrop(ItemKind.WoodShield, 6),
                new ContainerDrop(ItemKind.LeatherArmor, 6), new ContainerDrop(ItemKind.InsightArmor, 6), new ContainerDrop(ItemKind.IronArmor, 2) },
            treasure = new List<ContainerDrop> { new ContainerDrop(ItemKind.Ore, 60, 2, 4),
                new ContainerDrop(ItemKind.Crystal, 30, 1, 2), new ContainerDrop(ItemKind.Relic, 10) },
            supplies = new List<ContainerDrop> { new ContainerDrop(ItemKind.Potion, 6), new ContainerDrop(ItemKind.StrengthEssence, 1),
                new ContainerDrop(ItemKind.AgilityEssence, 1), new ContainerDrop(ItemKind.WisdomEssence, 1), new ContainerDrop(ItemKind.AssaultPotion, 1) }
        };
        public List<ContainerDrop> Pool(ContainerKind kind, int floor) => kind == ContainerKind.Equipment ?
            (floor == 1 ? firstEquipment : equipment) : kind == ContainerKind.Treasure ? treasure :
            kind == ContainerKind.Supplies ? supplies : throw new InvalidOperationException("未知容器类型。");

        public void Validate()
        {
            if (equipmentCount < 0 || treasureCount < 0 || suppliesCount < 0 ||
                (long)equipmentCount + treasureCount + suppliesCount != 10) throw new InvalidOperationException("每层容器数量必须合计10个。");
            foreach (var kind in new[] { ContainerKind.Equipment, ContainerKind.Treasure, ContainerKind.Supplies })
            foreach (int floor in new[] { 1, 2 })
            {
                var pool = Pool(kind, floor);
                if (pool == null || pool.Count == 0 || pool.Count > 100) throw new InvalidOperationException("容器物品池为空或过大。");
                foreach (var drop in pool)
                    if (drop == null || drop.weight < 1 || drop.weight > 100000 || drop.minimum < 1 || drop.maximum < drop.minimum ||
                        drop.maximum > 20 || !ContainerLoot.Matches(kind, drop.kind)) throw new InvalidOperationException("容器物品池配置无效。");
            }
        }
    }

    public static class ContainerLoot
    {
        public static bool Matches(ContainerKind container, ItemKind item) => container == ContainerKind.Equipment ? Rules.IsEquipment(item) :
            container == ContainerKind.Treasure ? item >= ItemKind.Ore && item <= ItemKind.Relic :
            container == ContainerKind.Supplies && (item == ItemKind.Potion || item >= ItemKind.StrengthEssence && item <= ItemKind.AssaultPotion);

        public static int GuardRarity(RunData run, GroundLoot loot, int floor) => loot.containerKind == ContainerKind.Legacy ?
            (loot.units.Count > 0 ? Rules.Rarity(loot.units[0].kind) : Rules.Rarity(loot.appearanceKind)) :
            run.containerRules.Pool(loot.containerKind, floor).Max(d => Rules.Rarity(d.kind));

        public static string Name(GroundLoot loot) => loot.containerKind == ContainerKind.Equipment ? "装备箱" :
            loot.containerKind == ContainerKind.Treasure ? "财宝箱" : loot.containerKind == ContainerKind.Supplies ? "消耗品袋" :
            Rules.ArtIndex(loot.appearanceKind) == 3 ? "消耗品袋" : Rules.ArtIndex(loot.appearanceKind) == 2 ? "财宝箱" : "装备箱";

        public static int ArtIndex(GroundLoot loot) => loot.containerKind == ContainerKind.Legacy ? Rules.ArtIndex(loot.appearanceKind) :
            loot.containerKind == ContainerKind.Equipment ? 0 : loot.containerKind == ContainerKind.Treasure ? 2 : 3;
        public static int OpenArtIndex(GroundLoot loot) => ArtIndex(loot) == 3 ? 2 : ArtIndex(loot) == 2 ? 1 : 0;

        public static bool Open(RunData run, GroundLoot loot)
        {
            if (!loot.unopened || loot.loose || run.Map.LivingGuard(loot) != null) return false;
            if (run.containerVersion != 1 || run.containerRandom == null || run.containerRandom.state == 0)
                throw new InvalidOperationException("容器随机进度无效。");
            run.containerRules.Validate();
            var pool = run.containerRules.Pool(loot.containerKind, run.Map.number).Where(d => !Rules.IsEquipment(d.kind) ||
                run.attributeRules.Find(d.kind) != null && (run.attributeRules.Find(d.kind).slot != EquipmentSlot.MainHand ||
                run.attributeRules.Find(d.kind).ownerCharacterId == run.character.templateId &&
                run.attributeRules.template.allowedTypes.Contains(run.attributeRules.Find(d.kind).type))).ToList();
            if (pool.Count == 0 && loot.containerKind == ContainerKind.Equipment)
            {
                var fallback = run.attributeRules.Find(ItemKind.IronSword);
                if (fallback != null && fallback.ownerCharacterId == run.character.templateId && run.attributeRules.template.allowedTypes.Contains(fallback.type))
                    pool.Add(new ContainerDrop(ItemKind.IronSword, 1));
            }
            if (pool.Count == 0) throw new InvalidOperationException("容器中没有适用于本角色的物品。");
            int roll = run.containerRandom.Range(0, pool.Sum(d => d.weight));
            var chosen = pool[0];
            foreach (var drop in pool) { roll -= drop.weight; if (roll < 0) { chosen = drop; break; } }
            int count = chosen.minimum == chosen.maximum ? chosen.minimum : run.containerRandom.Range(chosen.minimum, chosen.maximum + 1);
            for (int i = 0; i < count; i++) loot.units.Add(new ItemUnit { id = run.nextItemId++, kind = chosen.kind });
            loot.unopened = false; loot.opened = true;
            return true;
        }
    }
}
