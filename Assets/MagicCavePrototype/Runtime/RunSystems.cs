using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MagicCave.Prototype
{
    public static class Inventory
    {
        public static List<ItemUnit> Items(RunData run) { return run.inventory.SelectMany(slot => slot.units)
            .Concat(run.UsesAttributes ? run.character.equipment : Enumerable.Empty<ItemUnit>()).ToList(); }
        public static int UsedSlots(RunData run) { return run.inventory.Count(slot => slot.units.Count > 0); }
        public static int Pickup(RunData run, GroundLoot loot)
        {
            if (run.Map.LivingGuard(loot) != null) return 0;
            if (loot.units.Count > 0) loot.appearanceKind = loot.units[0].kind;
            int taken = 0;
            while (loot.units.Count > 0)
            {
                ItemUnit item = loot.units[0];
                var slot = run.inventory.FirstOrDefault(s => s.units.Count > 0 && s.units[0].kind == item.kind && s.units.Count < Rules.StackLimit(item.kind))
                    ?? run.inventory.FirstOrDefault(s => s.units.Count == 0);
                if (slot == null) break;
                if (item.acquiredTicks == 0)
                {
                    item.acquiredTicks = DateTime.UtcNow.Ticks;
                    item.acquisitionOrder = run.nextAcquisitionOrder++;
                }
                slot.units.Add(item); loot.units.RemoveAt(0); taken++;
            }
            return taken;
        }
        public static bool Heal(RunData run, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= run.inventory.Count || run.health <= 0 || run.health >= run.MaxHealth) return false;
            var slot = run.inventory[slotIndex];
            if (slot.units.Count == 0 || slot.units[0].kind != ItemKind.Potion) return false;
            slot.units.RemoveAt(0); run.health = CharacterAttributes.Quantize(Math.Min(run.MaxHealth, run.health + 20)); return true;
        }
        public static bool CanUse(RunData run, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= run.inventory.Count || run.health <= 0 || run.inventory[slotIndex].units.Count == 0) return false;
            var kind = run.inventory[slotIndex].units[0].kind;
            if (kind == ItemKind.Potion) return run.health < run.MaxHealth;
            if (!run.UsesAttributes || kind < ItemKind.StrengthEssence || kind > ItemKind.AssaultPotion) return false;
            return kind != ItemKind.AssaultPotion || !run.character.modifiers.Any(m => m.source == "assault-potion" && m.remainingBattles == 2);
        }
        public static bool Use(RunData run, int slotIndex)
        {
            if (!CanUse(run, slotIndex)) return false;
            var slot = run.inventory[slotIndex]; var kind = slot.units[0].kind;
            if (kind == ItemKind.Potion) return Heal(run, slotIndex);
            if (kind == ItemKind.AssaultPotion)
            {
                var existing = run.character.modifiers.Find(m => m.source == "assault-potion");
                if (existing != null && existing.remainingBattles == 2) return false;
                if (existing != null) existing.remainingBattles = 2;
                else run.character.modifiers.Add(new AttributeModifier { source = "assault-potion", attribute = AttributeKind.Strength, value = 2, remainingBattles = 2 });
            }
            else
            {
                var attribute = (AttributeKind)((int)kind - (int)ItemKind.StrengthEssence);
                var existing = run.character.modifiers.Find(m => m.source == "essence-" + attribute);
                if (existing != null) existing.value++;
                else run.character.modifiers.Add(new AttributeModifier { source = "essence-" + attribute, attribute = attribute, value = 1 });
            }
            slot.units.RemoveAt(0); return true;
        }
        public static string Equip(RunData run, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= run.inventory.Count || run.inventory[slotIndex].units.Count != 1) return "请选择背包中的装备";
            var slot = run.inventory[slotIndex]; var item = slot.units[0];
            string reason = CharacterAttributes.EquipLock(run, item); if (reason.Length > 0) return reason;
            var definition = run.attributeRules.Find(item.kind);
            var previous = run.character.equipment.Find(i => run.attributeRules.Find(i.kind).slot == definition.slot);
            slot.units.Clear();
            if (previous != null) { run.character.equipment.Remove(previous); slot.units.Add(previous); }
            run.character.equipment.Add(item); run.health = Math.Min(run.health, run.MaxHealth); return "";
        }
        public static string Unequip(RunData run, EquipmentSlot equipmentSlot)
        {
            if (!run.UsesAttributes) return "旧版探索没有装备槽";
            var item = run.character.equipment.Find(i => run.attributeRules.Find(i.kind).slot == equipmentSlot);
            if (item == null) return "槽位为空";
            var free = run.inventory.Find(s => s.units.Count == 0); if (free == null) return "背包已满，无法卸下";
            run.character.equipment.Remove(item); free.units.Add(item); run.health = Math.Min(run.health, run.MaxHealth); return "";
        }
        public static GroundLoot Drop(RunData run, int slotIndex, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= run.inventory.Count) return null;
            var slot = run.inventory[slotIndex];
            quantity = Math.Min(quantity, slot.units.Count);
            if (quantity <= 0) return null;
            var loot = new GroundLoot { id = run.nextEntityId++, x = run.CellX, y = run.CellY, loose = true };
            loot.units.AddRange(slot.units.Take(quantity)); slot.units.RemoveRange(0, quantity);
            run.Map.loot.Add(loot); return loot;
        }
        public static List<ItemUnit> FailureSelection(RunData run)
        {
            return Items(run).OrderByDescending(i => Rules.Rarity(i.kind)).ThenByDescending(i => i.acquiredTicks)
                .ThenByDescending(i => i.acquisitionOrder).Take(5).ToList();
        }
    }

    public static class Growth
    {
        public static void Award(RunData run, int amount)
        {
            run.totalExperience += amount; run.experience += amount;
        }
        public static List<CardKind> NextUpgrade(RunData run)
        {
            int required = Rules.RequiredExperience(run.level);
            if (run.experience < required) return null;
            run.experience -= required; run.level++;
            var pool = new List<CardKind> { CardKind.Heavy, CardKind.Double, CardKind.Shield, CardKind.Fortify, CardKind.Quick, CardKind.Spirit, CardKind.Steady };
            run.cardRandom.Shuffle(pool); return pool.GetRange(0, 3);
        }
        public static void FinishVictory(RunData run, EnemyData enemy)
        {
            if (enemy.defeated) return;
            enemy.defeated = true;
            run.health = CharacterAttributes.Quantize(Math.Min(run.MaxHealth, run.health + run.victoryHealing));
            CharacterAttributes.FinishBattle(run);
            Award(run, enemy.kind == EnemyKind.Boss ? 30 : 10);
            if (enemy.kind == EnemyKind.Boss && !run.bossDefeated)
            {
                run.bossDefeated = true;
                run.Map.Set(6, 10, Tile.Return);
                run.Map.revealed[run.Map.Index(6, 10)] = true;
                MapGenerator.AddLoot(run, run.Map, new Vector2Int(6, 9), ItemKind.Relic, 1);
                // The return door may appear on or overlap the pre-battle player footprint.
                MapGenerator.EnsureSafePlayerPosition(run);
            }
        }
    }

    public enum BattleOutcome { None, Victory, Failure }
    public sealed class Battle
    {
        public readonly RunData run;
        public readonly EnemyData enemy;
        public double enemyHealth, enemyBlock, playerBlock;
        public int energy, spirit, enemyTurn;
        public bool playerTurn;
        public BattleOutcome outcome;
        public readonly List<CardKind> hand = new List<CardKind>();
        public readonly List<CardKind> draw = new List<CardKind>();
        public readonly List<CardKind> discard = new List<CardKind>();

        public Battle(RunData run, EnemyData enemy)
        {
            this.run = run; this.enemy = enemy;
            enemyHealth = Rules.Health(enemy);
            draw.AddRange(run.deck); run.battleRandom.Shuffle(draw);
            StartPlayerTurn();
            playerBlock = run.openingBlock;
        }
        public void Draw(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (draw.Count == 0 && discard.Count > 0)
                {
                    draw.AddRange(discard); discard.Clear(); run.battleRandom.Shuffle(draw);
                }
                if (draw.Count == 0) break;
                int last = draw.Count - 1; hand.Add(draw[last]); draw.RemoveAt(last);
            }
        }
        void StartPlayerTurn()
        {
            playerBlock = 0; energy = 3 + (enemyTurn == 0 ? run.openingEnergy : 0); Draw(5); playerTurn = true;
        }
        public bool Play(int handIndex)
        {
            if (!playerTurn || outcome != BattleOutcome.None || handIndex < 0 || handIndex >= hand.Count) return false;
            CardKind card = hand[handIndex]; int cost = Rules.Costs[(int)card];
            if (energy < cost) return false;
            energy -= cost; hand.RemoveAt(handIndex);
            switch (card)
            {
                case CardKind.Strike: Hit(card); break;
                case CardKind.Defend: playerBlock += CombatNumbers.Block(run, card); break;
                case CardKind.Prepare: Draw(2); break;
                case CardKind.Heavy: Hit(card); break;
                case CardKind.Double: Hit(card); if (outcome == BattleOutcome.None) Hit(card); break;
                case CardKind.Shield: Hit(card); break;
                case CardKind.Fortify: playerBlock += CombatNumbers.Block(run, card); break;
                case CardKind.Quick: Hit(card); if (outcome == BattleOutcome.None) Draw(1); break;
                case CardKind.Spirit: spirit += 2; break;
                case CardKind.Steady: playerBlock += CombatNumbers.Block(run, card); Draw(1); break;
            }
            discard.Add(card);
            return true;
        }
        void Hit(CardKind card)
        {
            double damage = CombatNumbers.Damage(run, card, spirit, playerBlock);
            CombatNumbers.Apply(damage, run.UsesAttributes ? enemy.physicalDefense : 0, ref enemyBlock, ref enemyHealth);
            if (enemyHealth == 0) { outcome = BattleOutcome.Victory; playerTurn = false; }
        }
        public double Preview(CardKind card)
        {
            double block = enemyBlock, health = enemyHealth, total = 0;
            if (!CombatNumbers.IsAttack(card)) return 0;
            for (int segment = 0; segment < CombatNumbers.Segments(card) && health > 0; segment++)
                total += CombatNumbers.Apply(CombatNumbers.Damage(run, card, spirit, playerBlock),
                    run.UsesAttributes ? enemy.physicalDefense : 0, ref block, ref health);
            return CharacterAttributes.Quantize(total);
        }
        public double ExpectedHealthLoss => Math.Max(0, IntentAttack -
            (run.UsesAttributes ? CharacterAttributes.Calculate(run, true).defense : 0) - playerBlock);
        public void EndPlayerTurn()
        {
            if (!playerTurn || outcome != BattleOutcome.None) return;
            playerTurn = false; discard.AddRange(hand); hand.Clear(); spirit = 0;
        }
        public void ResolveEnemyTurn()
        {
            if (playerTurn || outcome != BattleOutcome.None) return;
            enemyBlock = 0;
            int attack = IntentAttack;
            if (attack > 0)
            {
                CombatNumbers.Apply(attack, run.UsesAttributes ? CharacterAttributes.Calculate(run, true).defense : 0,
                    ref playerBlock, ref run.health);
            }
            else enemyBlock += IntentBlock;
            enemyTurn++;
            if (run.health == 0) { outcome = BattleOutcome.Failure; return; }
            StartPlayerTurn();
        }
        public int IntentAttack
        {
            get
            {
                int bonus = enemy.Level - 1;
                if (enemy.kind == EnemyKind.Bat) return 5 + bonus;
                if (enemy.kind == EnemyKind.Beetle) return enemyTurn % 2 == 1 ? 8 + bonus : 0;
                return enemyTurn % 3 == 0 ? 8 + bonus : enemyTurn % 3 == 2 ? 14 + bonus : 0;
            }
        }
        public int IntentBlock { get { return enemy.kind == EnemyKind.Beetle ? 6 : 10; } }
        public string Intent { get { return IntentAttack > 0 ? "攻击 " + IntentAttack : "格挡 " + IntentBlock; } }
    }
}
