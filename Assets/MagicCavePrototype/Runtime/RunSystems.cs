using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MagicCave.Prototype
{
    public static class Inventory
    {
        public static List<ItemUnit> Items(RunData run) { return run.inventory.SelectMany(slot => slot.units).ToList(); }
        public static int UsedSlots(RunData run) { return run.inventory.Count(slot => slot.units.Count > 0); }
        public static int Pickup(RunData run, GroundLoot loot)
        {
            int taken = 0;
            while (loot.units.Count > 0)
            {
                ItemUnit item = loot.units[0];
                var slot = run.inventory.FirstOrDefault(s => s.units.Count > 0 && s.units[0].kind == item.kind && s.units.Count < 20)
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
            if (slotIndex < 0 || slotIndex >= run.inventory.Count || run.health <= 0 || run.health >= 60) return false;
            var slot = run.inventory[slotIndex];
            if (slot.units.Count == 0 || slot.units[0].kind != ItemKind.Potion) return false;
            slot.units.RemoveAt(0); run.health = Math.Min(60, run.health + 20); return true;
        }
        public static GroundLoot Drop(RunData run, int slotIndex, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= run.inventory.Count) return null;
            var slot = run.inventory[slotIndex];
            quantity = Math.Min(quantity, slot.units.Count);
            if (quantity <= 0) return null;
            var loot = new GroundLoot { id = run.nextEntityId++, x = run.CellX, y = run.CellY };
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
        public int enemyHealth, enemyBlock, playerBlock, energy, spirit, enemyTurn;
        public bool playerTurn;
        public BattleOutcome outcome;
        public readonly List<CardKind> hand = new List<CardKind>();
        public readonly List<CardKind> draw = new List<CardKind>();
        public readonly List<CardKind> discard = new List<CardKind>();

        public Battle(RunData run, EnemyData enemy)
        {
            this.run = run; this.enemy = enemy;
            enemyHealth = Rules.EnemyHealth[(int)enemy.kind];
            draw.AddRange(run.deck); run.battleRandom.Shuffle(draw);
            StartPlayerTurn();
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
            playerBlock = 0; energy = 3; Draw(5); playerTurn = true;
        }
        public bool Play(int handIndex)
        {
            if (!playerTurn || outcome != BattleOutcome.None || handIndex < 0 || handIndex >= hand.Count) return false;
            CardKind card = hand[handIndex]; int cost = Rules.Costs[(int)card];
            if (energy < cost) return false;
            energy -= cost; hand.RemoveAt(handIndex);
            switch (card)
            {
                case CardKind.Strike: Hit(6); break;
                case CardKind.Defend: playerBlock += 5; break;
                case CardKind.Prepare: Draw(2); break;
                case CardKind.Heavy: Hit(15); break;
                case CardKind.Double: Hit(4); if (outcome == BattleOutcome.None) Hit(4); break;
                case CardKind.Shield: Hit(playerBlock); break;
                case CardKind.Fortify: playerBlock += 12; break;
                case CardKind.Quick: Hit(5); if (outcome == BattleOutcome.None) Draw(1); break;
                case CardKind.Spirit: spirit += 2; break;
                case CardKind.Steady: playerBlock += 4; Draw(1); break;
            }
            discard.Add(card);
            return true;
        }
        void Hit(int baseDamage)
        {
            int damage = baseDamage + spirit;
            int blocked = Math.Min(enemyBlock, damage); enemyBlock -= blocked;
            enemyHealth = Math.Max(0, enemyHealth - (damage - blocked));
            if (enemyHealth == 0) { outcome = BattleOutcome.Victory; playerTurn = false; }
        }
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
                int blocked = Math.Min(playerBlock, attack); playerBlock -= blocked;
                run.health = Math.Max(0, run.health - (attack - blocked));
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
                if (enemy.kind == EnemyKind.Bat) return 5;
                if (enemy.kind == EnemyKind.Beetle) return enemyTurn % 2 == 1 ? 8 : 0;
                return enemyTurn % 3 == 0 ? 8 : enemyTurn % 3 == 2 ? 14 : 0;
            }
        }
        public int IntentBlock { get { return enemy.kind == EnemyKind.Beetle ? 6 : 10; } }
        public string Intent { get { return IntentAttack > 0 ? "攻击 " + IntentAttack : "格挡 " + IntentBlock; } }
    }
}
