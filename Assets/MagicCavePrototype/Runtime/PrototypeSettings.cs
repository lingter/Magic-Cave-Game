using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace MagicCave.Prototype
{
    [Serializable]
    public sealed class CustomCardDefinition
    {
        public int id;
        public string name = "自定义卡牌";
        public CardKind template = CardKind.Strike;
        public int count = 1, cost = 1, power = 100, draw, spirit = 2;
        // Version zero retains the earlier template cards and their saved behavior.
        public int effectsVersion;
        public List<CardEffectEntry> effects = new List<CardEffectEntry>();
        public bool UsesEffects => effectsVersion == 1;
    }

    [Serializable]
    public sealed class PrototypeSettings
    {
        public bool fog = true, torch = true, merchant = true, teeth = true, talents = true;
        public bool growth = true, cardRewards = true, digging = true, consumables = true, equipment = true;
        public bool itemGuards = true, corridorGuards = true;
        public int[] cardCounts = { 5, 4, 1, 0, 0, 0, 0, 0, 0, 0 };
        public int[] cardCosts = { 1, 1, 1, 2, 1, 1, 2, 1, 1, 1 };
        // Percent of the existing formula, applied before final integer rounding.
        public int[] cardPower = { 100, 100, 100, 100, 100, 100, 100, 100, 100, 100 };
        public int[] cardDraw = { 0, 0, 2, 0, 0, 0, 0, 1, 0, 1 };
        public List<CustomCardDefinition> customCards = new List<CustomCardDefinition>();
        public List<EnemyTemplateDefinition> enemyTemplates = new List<EnemyTemplateDefinition>();
        public int DeckCount => cardCounts.Sum() + (customCards?.Sum(c => c.count) ?? 0);
        public int spirit = 2, bossRelicChance = 100;
        public int[] enemyHealth = { 18, 30, 80 }, enemyAttack = { 5, 8, 8 }, enemyHeavy = { 5, 8, 14 };
        public int[] enemyBlock = { 0, 6, 10 }, enemyDefense = { -1, -1, -1 };
        // -1 preserves generated roaming enemies. Guards are controlled separately.
        public int[] roamingCounts = { -1, -1 };
        public ContainerLootRules drops = ContainerLootRules.Defaults();
        public static PrototypeSettings For(RunData run) => run != null && run.settingsVersion == 1 ? run.settings : null;
        public static int Health(RunData run, EnemyData enemy) => (EnemyTemplates.Find(run, enemy)?.health ?? For(run)?.enemyHealth[(int)enemy.kind] ?? Rules.EnemyHealth[(int)enemy.kind]) + (EnemyTemplates.Find(run, enemy)?.healthGrowth ?? 4) * (enemy.Level - 1);
        public static CustomCardDefinition Custom(RunData run, CardKind card) => For(run)?.customCards?.FirstOrDefault(c => c.id == (int)card);
        public static CardKind Template(RunData run, CardKind card) => Custom(run, card)?.template ?? card;
        public static string Name(RunData run, CardKind card) => Custom(run, card)?.name ?? Rules.CardNames[(int)card];
        public static bool Known(RunData run, CardKind card) => (int)card >= 0 && ((int)card < 10 || Custom(run, card) != null);
        public static int Spirit(RunData run, CardKind card) => Custom(run, card)?.spirit ?? For(run)?.spirit ?? 2;
        public static int Cost(RunData run, CardKind card) => Custom(run, card)?.cost ?? For(run)?.cardCosts[(int)card] ?? Rules.Costs[(int)card];
        public static decimal Power(RunData run, CardKind card) => (Custom(run, card)?.power ?? For(run)?.cardPower[(int)card] ?? 100) / 100m;
        public static int Draw(RunData run, CardKind card) => Custom(run, card)?.draw ?? For(run)?.cardDraw[(int)card] ?? (card == CardKind.Prepare ? 2 : card == CardKind.Quick || card == CardKind.Steady ? 1 : 0);
        public void Validate()
        {
            Check(cardCounts, 10, 0, 30); Check(cardCosts, 10, 0, 9); Check(cardPower, 10, 0, 1000); Check(cardDraw, 10, 0, 20);
            if (customCards == null) customCards = new List<CustomCardDefinition>();
            if (customCards.Count > 100 || customCards.Any(c => c == null || c.id < 10 || c.id > 1000000 ||
                string.IsNullOrWhiteSpace(c.name) || c.name.Trim().Length > 16 || c.name.Any(char.IsControl) ||
                (int)c.template < 0 || (int)c.template >= 10 || c.count < 0 || c.count > 30 || c.cost < 0 || c.cost > 9 ||
                c.power < 0 || c.power > 1000 || c.draw < 0 || c.draw > 20 || c.spirit < 0 || c.spirit > 100) ||
                customCards.Select(c => c.id).Distinct().Count() != customCards.Count)
                throw new InvalidOperationException("自定义卡牌配置无效：名称1～16字，最多100种，数值须在标注范围内。");
            foreach (var card in customCards) CardEffects.Validate(card);
            if (enemyTemplates == null) enemyTemplates = new List<EnemyTemplateDefinition>();
            EnemyTemplates.Validate(enemyTemplates);
            if (DeckCount < 1 || DeckCount > 100) throw new InvalidOperationException("初始牌组需要1～100张牌。");
            Check(enemyHealth, 3, 1, 10000); Check(enemyAttack, 3, 0, 1000); Check(enemyHeavy, 3, 0, 1000);
            Check(enemyBlock, 3, 0, 1000); Check(enemyDefense, 3, -1, 1000); Check(roamingCounts, 2, -1, 30);
            if (bossRelicChance < 0 || bossRelicChance > 100) throw new InvalidOperationException("首领遗物出率范围为0～100%。");
            if (spirit < 0 || spirit > 100) throw new InvalidOperationException("战意增益范围为0～100。");
            if (drops == null) throw new InvalidOperationException("缺少掉落配置。");
            drops.Validate();
        }
        static void Check(int[] values, int length, int min, int max)
        {
            if (values == null || values.Length != length || values.Any(v => v < min || v > max))
                throw new InvalidOperationException("设置数值超出范围或配置不完整。");
        }
        public void Apply(RunData run)
        {
            Validate(); run.settingsVersion = 1; run.settings = Rules.Copy(this);
            run.containerRules = Rules.Copy(drops);
            run.deck.Clear();
            for (int i = 0; i < cardCounts.Length; i++) for (int n = 0; n < cardCounts[i]; n++) run.deck.Add((CardKind)i);
            foreach (var card in customCards) for (int n = 0; n < card.count; n++) run.deck.Add((CardKind)card.id);
            var random = new RandomStream(RandomStream.Derive(run.seed, 0x53455431));
            foreach (var map in run.maps)
            {
                if (!merchant) map.hasMerchant = false;
                if (!fog) Array.Fill(map.revealed, true);
                if (!itemGuards) { map.enemies.RemoveAll(e => e.role == EnemyRole.ItemGuard && e.kind != EnemyKind.Boss); foreach (var loot in map.loot) loot.combatApproach = false; }
                if (!corridorGuards) map.enemies.RemoveAll(e => e.role == EnemyRole.CorridorGuard && e.kind != EnemyKind.Boss);
                if (map.number < run.maps.Count)
                {
                    for (int kind = 0; kind < 2; kind++)
                    {
                        int count = roamingCounts[kind]; if (count < 0) continue;
                        map.enemies.RemoveAll(e => e.role == EnemyRole.Roaming && (int)e.kind == kind);
                        var candidates = Enumerable.Range(0, map.cells.Length).Select(i => new Vector2Int(i % map.size, i / map.size))
                            .Where(v => map.CanTraverse(v.x, v.y) && (v.x != map.spawnX || v.y != map.spawnY) &&
                                (!map.hasMerchant || Math.Abs(v.x - map.merchantX) > 1 || Math.Abs(v.y - map.merchantY) > 1)).ToList();
                        random.Shuffle(candidates);
                        if (candidates.Count < count) throw new InvalidOperationException("地面空间不足，请降低自由怪数量。");
                        foreach (var v in candidates.Take(count)) map.enemies.Add(new EnemyData { id = run.nextEntityId++, x = v.x, y = v.y, kind = (EnemyKind)kind, role = EnemyRole.Roaming, level = map.number });
                    }
                }
                foreach (var enemy in map.enemies) if (enemyDefense[(int)enemy.kind] >= 0) enemy.physicalDefense = enemyDefense[(int)enemy.kind];
            }
            EnemyTemplates.Apply(run, enemyTemplates);
        }
    }
}
