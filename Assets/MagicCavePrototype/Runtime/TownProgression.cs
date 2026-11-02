using System;
using System.Linq;
using UnityEngine;

namespace MagicCave.Prototype
{
    [Serializable]
    public sealed class TownProgress
    {
        public long gold = 120;
        public int reputation, guildLevel = 1, warehouseUpgrades;
        public int talentPoints = 3, totalTalentPoints = 3; // Prototype test allowance; each rank costs one point.
        public bool knight = true;
        public int[] talents = { 1, 0, 0, 0, 0, 0, 0, 0 };
    }

    public sealed class KnightTalent
    {
        public readonly string name, effect;
        public readonly int maximum, parent, parentRank, guildLevel, gold, reputation;
        public KnightTalent(string name, string effect, int maximum, int parent, int parentRank, int guildLevel, int gold, int reputation)
        {
            this.name = name; this.effect = effect; this.maximum = maximum; this.parent = parent;
            this.parentRank = parentRank; this.guildLevel = guildLevel; this.gold = gold; this.reputation = reputation;
        }
    }

    /// <summary>Prototype balance values, separate from the design document's confirmed structure.</summary>
    public static class TownRules
    {
        public static readonly int[] SellPrices = { 5, 15, 50, 8 };
        public static readonly KnightTalent[] Talents =
        {
            new KnightTalent("骑士誓言", "战士公会 · 骑士职业\n连接力量、敏捷、智慧三区", 1, -1, 0, 1, 0, 0),
            new KnightTalent("力量训练", "小节点 · 每级力量 +1\n通过武器属性修正提高实际ATK", 3, 0, 1, 1, 30, 0),
            new KnightTalent("体魄训练", "小节点 · 每级最大生命 +5\n出发时以新的最大生命开始", 3, 0, 1, 1, 30, 0),
            new KnightTalent("盾术训练", "小节点 · 每级敏捷 +1\n通过敏捷修正影响武器ATK", 3, 2, 1, 1, 40, 0),
            new KnightTalent("破阵先锋", "大节点 · 每场战斗首回合\n获得 1 点额外能量", 1, 1, 2, 2, 100, 10),
            new KnightTalent("不屈意志", "大节点 · 每场胜利后\n恢复 3 点生命，不超过上限", 1, 2, 2, 2, 100, 10),
            new KnightTalent("盾壁", "大节点 · 每场战斗开始\n获得 6 点初始格挡", 1, 3, 2, 3, 150, 35),
            new KnightTalent("智慧训练", "小节点 · 每级智慧 +1\n通过智慧修正影响武器ATK（首轮试配）", 3, 0, 1, 1, 30, 0)
        };
        public static long Used(WarehouseData warehouse) => checked(warehouse.counts.Sum() + warehouse.equipment.Count);
        public static long Capacity(WarehouseData warehouse) => checked(warehouse.baseCapacity + warehouse.town.warehouseUpgrades * 50L);
        public static int UpgradeGold(TownProgress town) => town.guildLevel == 1 ? 100 : 250;
        public static int UpgradeReputation(TownProgress town) => town.guildLevel == 1 ? 10 : 35;
        public static int ExpansionGold(TownProgress town) => 75 * (town.warehouseUpgrades + 1);
        public static int TalentPointCost(int id) => id == 0 ? 0 : 1;
        public static string TalentLock(TownProgress town, int id)
        {
            if (id < 0 || id >= Talents.Length) return "未知天赋";
            var talent = Talents[id];
            if (!town.knight) return "请先选择骑士职业";
            if (town.talents[id] >= talent.maximum) return "已达到上限";
            if (talent.parent >= 0 && town.talents[talent.parent] < talent.parentRank)
                return "前置：" + Talents[talent.parent].name + " " + talent.parentRank + "级";
            if (town.guildLevel < talent.guildLevel) return "需要公会 " + talent.guildLevel + "级";
            if (town.reputation < talent.reputation) return "需要声望 " + talent.reputation;
            if (town.talentPoints < TalentPointCost(id)) return "天赋点不足";
            return "";
        }
        public static string RetractTalentLock(TownProgress town, int id)
        {
            if (id <= 0 || id >= Talents.Length) return "基础职业节点不能回撤";
            if (town.talents[id] == 0) return "此天赋尚未点亮";
            for (int child = 1; child < Talents.Length; child++)
                if (Talents[child].parent == id && town.talents[child] > 0 && town.talents[id] - 1 < Talents[child].parentRank)
                    return "请先回撤：" + Talents[child].name;
            return "";
        }
        public static void Apply(TownProgress town, RunData run)
        {
            if (!town.knight) return;
            if (run.UsesAttributes)
            {
                run.character.talentHealth = town.talents[2] * 5;
                run.character.talentStrength = town.talents[1];
                run.character.talentAgility = town.talents[3];
                run.character.talentWisdom = town.talents[7];
                run.health = run.MaxHealth;
            }
            else
            {
            run.maxHealth = 60 + town.talents[2] * 5; run.health = run.MaxHealth;
            run.attackBonus = town.talents[1]; run.defenseBonus = town.talents[3];
            }
            run.openingEnergy = town.talents[4]; run.victoryHealing = town.talents[5] * 3;
            run.openingBlock = town.talents[6] * 6;
        }
        public static long SettlementGold(PendingSettlement pending)
        {
            int ordinary = pending.run.maps.Sum(m => m.enemies.Count(e => e.defeated && e.kind != EnemyKind.Boss));
            return ordinary * 8L + (pending.run.bossDefeated ? 50 : 0);
        }
        public static int SettlementReputation(PendingSettlement pending)
        {
            int ordinary = pending.run.maps.Sum(m => m.enemies.Count(e => e.defeated && e.kind != EnemyKind.Boss));
            return ordinary * 2 + (pending.run.bossDefeated ? 20 : 0);
        }
    }
}
