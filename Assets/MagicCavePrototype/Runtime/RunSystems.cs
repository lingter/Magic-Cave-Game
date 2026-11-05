using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MagicCave.Prototype
{
    public static class TorchLight
    {
        public const int Durability = 20;
        public static bool IsLit(RunData run) => run.torchStepsRemaining > 0 && !run.torchPaused;
        public static bool Resume(RunData run)
        {
            if (run.torchStepsRemaining <= 0 || !run.torchPaused || run.health <= 0 ||
                PrototypeSettings.For(run)?.consumables == false || PrototypeSettings.For(run)?.torch == false) return false;
            run.torchPaused = false; MapGenerator.Reveal(run); return true;
        }
        public static bool Extinguish(RunData run)
        {
            if (!IsLit(run)) return false;
            run.torchPaused = true; return true;
        }
        // Called only for successful grid movement, never for collision, interaction or time passing.
        public static void Step(RunData run)
        {
            if (IsLit(run)) run.torchStepsRemaining--;
        }
    }
    public static class QuickBar
    {
        public const int Capacity = 3;
        public static bool IsConsumable(ItemKind kind) => kind == ItemKind.Potion || kind == ItemKind.Torch ||
            kind >= ItemKind.StrengthEssence && kind <= ItemKind.AssaultPotion;
        public static int Count(RunData run, ItemKind kind) => run.inventory.Sum(s => s.units.Count(i => i.kind == kind));
        public static void Ensure(RunData run)
        {
            if (run.quickSlots == null || run.quickSlots.Length != Capacity)
            {
                var previous = run.quickSlots;
                run.quickSlots = new[] { -1, -1, -1 };
                if (previous != null) Array.Copy(previous, run.quickSlots, Math.Min(previous.Length, Capacity));
                else foreach (var kind in run.inventory.SelectMany(s => s.units).Select(i => i.kind).Distinct())
                    if (IsConsumable(kind) && Array.IndexOf(run.quickSlots, -1) >= 0)
                        run.quickSlots[Array.IndexOf(run.quickSlots, -1)] = (int)kind;
            }
            for (int i = 0; i < Capacity; i++)
                if (!IsConsumable((ItemKind)run.quickSlots[i]) ||
                    (Count(run, (ItemKind)run.quickSlots[i]) == 0 && !HasTorch(run, i)) ||
                    Array.IndexOf(run.quickSlots, run.quickSlots[i]) < i) run.quickSlots[i] = -1;
        }
        public static bool IsBurningTorch(RunData run, int index) => run.quickSlots != null && index >= 0 &&
            index < run.quickSlots.Length && run.quickSlots[index] == (int)ItemKind.Torch && TorchLight.IsLit(run);
        public static bool HasTorch(RunData run, int index) => run.quickSlots != null && index >= 0 &&
            index < run.quickSlots.Length && run.quickSlots[index] == (int)ItemKind.Torch && run.torchStepsRemaining > 0;
        public static void OnAcquired(RunData run, ItemKind kind)
        {
            Ensure(run);
            if (!IsConsumable(kind) || Count(run, kind) == 0 || Array.IndexOf(run.quickSlots, (int)kind) >= 0) return;
            int empty = Array.IndexOf(run.quickSlots, -1);
            if (empty >= 0) run.quickSlots[empty] = (int)kind;
        }
        public static int BagSlot(RunData run, int index)
        {
            Ensure(run);
            if (index < 0 || index >= Capacity || run.quickSlots[index] < 0) return -1;
            return run.inventory.FindIndex(s => s.units.Count > 0 && (int)s.units[0].kind == run.quickSlots[index]);
        }
        public static bool Assign(RunData run, int index, ItemKind kind)
        {
            Ensure(run);
            if (index < 0 || index >= Capacity || !IsConsumable(kind) || Count(run, kind) == 0) return false;
            int previous = Array.IndexOf(run.quickSlots, (int)kind);
            if (previous >= 0) run.quickSlots[previous] = -1;
            run.quickSlots[index] = (int)kind; return true;
        }
    }
    public static class Inventory
    {
        public static List<ItemUnit> Items(RunData run) { return run.inventory.SelectMany(slot => slot.units)
            .Concat(run.UsesAttributes ? run.character.equipment : Enumerable.Empty<ItemUnit>()).ToList(); }
        public static int UsedSlots(RunData run) { return run.inventory.Count(slot => slot.units.Count > 0); }
        public static int Pickup(RunData run, GroundLoot loot)
        {
            ContainerLoot.Open(run, loot);
            if (loot.IsContainer && loot.units.Count > 0) loot.opened = true;
            if (loot.units.Count > 0 && loot.containerKind == ContainerKind.Legacy) loot.appearanceKind = loot.units[0].kind;
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
                QuickBar.OnAcquired(run, item.kind);
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
            if (PrototypeSettings.For(run)?.consumables == false) return false;
            if (kind == ItemKind.Torch) return PrototypeSettings.For(run)?.torch != false && !TorchLight.IsLit(run);
            if (kind == ItemKind.Potion) return run.health < run.MaxHealth;
            if (!run.UsesAttributes || kind < ItemKind.StrengthEssence || kind > ItemKind.AssaultPotion) return false;
            return kind != ItemKind.AssaultPotion || !run.character.modifiers.Any(m => m.source == "assault-potion" && m.remainingBattles == 2);
        }
        public static bool Use(RunData run, int slotIndex)
        {
            if (!CanUse(run, slotIndex)) return false;
            var slot = run.inventory[slotIndex]; var kind = slot.units[0].kind;
            if (kind == ItemKind.Torch)
            {
                if (run.torchStepsRemaining > 0) return TorchLight.Resume(run);
                slot.units.RemoveAt(0); run.torchStepsRemaining = TorchLight.Durability; run.torchPaused = false;
                MapGenerator.Reveal(run); return true;
            }
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
        public static string Equip(RunData run, int slotIndex, EquipmentSlot? targetSlot = null)
        {
            if (slotIndex < 0 || slotIndex >= run.inventory.Count || run.inventory[slotIndex].units.Count != 1) return "请选择背包中的装备";
            if (PrototypeSettings.For(run)?.equipment == false) return "本局已关闭装备穿戴";
            var slot = run.inventory[slotIndex]; var item = slot.units[0];
            string reason = CharacterAttributes.EquipLock(run, item); if (reason.Length > 0) return reason;
            var definition = run.attributeRules.Find(item.kind);
            var destination = targetSlot ?? definition.slot;
            if (!targetSlot.HasValue && definition.type == EquipmentType.Ring)
                destination = new[] { EquipmentSlot.Ring1, EquipmentSlot.Ring2, EquipmentSlot.Ring3 }
                    .Where(candidate => !run.character.equipment.Any(i => CharacterAttributes.EquippedSlot(run, i) == candidate)).DefaultIfEmpty(EquipmentSlot.Ring1).First();
            if (!CharacterAttributes.FitsSlot(definition, destination)) return "请拖到匹配的穿戴槽";
            var previous = run.character.equipment.Find(i => CharacterAttributes.EquippedSlot(run, i) == destination);
            slot.units.Clear();
            if (previous != null) { run.character.equipment.Remove(previous); previous.equippedSlot = -1; slot.units.Add(previous); }
            item.equippedSlot = (int)destination; run.character.equipment.Add(item);
            run.health = Math.Min(run.health, run.MaxHealth); return "";
        }
        public static string Unequip(RunData run, EquipmentSlot equipmentSlot)
        {
            if (!run.UsesAttributes) return "旧版探索没有装备槽";
            int target = run.inventory.FindIndex(s => s.units.Count == 0);
            return target < 0 ? "背包已满，无法卸下" : UnequipTo(run, equipmentSlot, target);
        }
        // Transfer whole slots: instance identity and acquisition order stay intact.
        public static string MoveSlot(RunData run, int source, int target)
        {
            if (source < 0 || target < 0 || source >= run.inventory.Count || target >= run.inventory.Count) return "无效的背包格";
            if (source == target) return "";
            var units = run.inventory[source].units;
            run.inventory[source].units = run.inventory[target].units;
            run.inventory[target].units = units;
            return "";
        }
        public static string UnequipTo(RunData run, EquipmentSlot equipmentSlot, int target)
        {
            if (!run.UsesAttributes || target < 0 || target >= run.inventory.Count) return "无效的背包格";
            var item = run.character.equipment.Find(i => CharacterAttributes.EquippedSlot(run, i) == equipmentSlot);
            if (item == null) return "槽位为空";
            var destination = run.inventory[target];
            if (destination.units.Count > 0)
            {
                if (destination.units.Count != 1 || CharacterAttributes.EquipLock(run, destination.units[0]).Length > 0 ||
                    !CharacterAttributes.FitsSlot(run.attributeRules.Find(destination.units[0].kind), equipmentSlot)) return "请选择空格或同槽位装备";
                return Equip(run, target, equipmentSlot);
            }
            run.character.equipment.Remove(item); item.equippedSlot = -1; destination.units.Add(item);
            run.health = Math.Min(run.health, run.MaxHealth); return "";
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
            if (PrototypeSettings.For(run)?.growth == false) return;
            run.totalExperience += amount; run.experience += amount;
        }
        public static List<CardKind> NextUpgrade(RunData run)
        {
            int required = Rules.RequiredExperience(run.level);
            if (run.experience < required) return null;
            run.experience -= required; run.level++;
            var pool = new List<CardKind> { CardKind.Heavy, CardKind.Double, CardKind.Shield, CardKind.Fortify, CardKind.Quick, CardKind.Spirit, CardKind.Steady };
            run.cardRandom.Shuffle(pool);
            if (PrototypeSettings.For(run)?.cardRewards == false) return NextUpgrade(run);
            return pool.GetRange(0, 3);
        }
        public static void FinishVictory(RunData run, EnemyData enemy)
        {
            if (enemy.defeated) return;
            enemy.defeated = true;
            if (PrototypeSettings.For(run)?.teeth != false) run.teeth = (int)Math.Min(int.MaxValue, (long)run.teeth + ToothMerchant.VictoryTeeth(enemy));
            run.health = CharacterAttributes.Quantize(Math.Min(run.MaxHealth, run.health + run.victoryHealing));
            CharacterAttributes.FinishBattle(run);
            Award(run, enemy.kind == EnemyKind.Boss ? 30 : 10);
            if (enemy.kind == EnemyKind.Boss && !run.bossDefeated)
            {
                run.bossDefeated = true;
                run.Map.Set(6, 10, Tile.Return);
                run.Map.revealed[run.Map.Index(6, 10)] = true;
                int chance = PrototypeSettings.For(run)?.bossRelicChance ?? 100;
                if (chance == 100 || new RandomStream(RandomStream.Derive(run.seed, 0x52454c49)).Range(0, 100) < chance)
                {
                    var reward = MapGenerator.AddLoot(run, run.Map, new Vector2Int(6, 9), ItemKind.Relic, 1);
                    // Continue the layout's collision rule, including for a boss reached after loading an old save.
                    reward.permanentObstacle = run.maps.Any(m => m.loot.Any(l => l.permanentObstacle));
                }
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
            enemyHealth = PrototypeSettings.Health(run, enemy);
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
            CardKind card = hand[handIndex]; int cost = PrototypeSettings.Cost(run, card);
            if (energy < cost) return false;
            energy -= cost; hand.RemoveAt(handIndex);
            var custom = PrototypeSettings.Custom(run, card);
            if (custom != null && custom.UsesEffects)
            {
                var state = EffectState();
                CardEffects.Resolve(run, custom, run.UsesAttributes ? enemy.physicalDefense : 0,
                    run.UsesAttributes ? CharacterAttributes.Calculate(run, true).defense : 0, state, Draw);
                enemyHealth = state.enemyHealth; enemyBlock = state.enemyBlock; run.health = state.playerHealth;
                playerBlock = state.playerBlock; spirit = state.spirit;
                if (run.health <= 0) { outcome = BattleOutcome.Failure; playerTurn = false; }
                else if (enemyHealth <= 0) { outcome = BattleOutcome.Victory; playerTurn = false; }
                discard.Add(card);
                return true;
            }
            switch (PrototypeSettings.Template(run, card))
            {
                case CardKind.Strike: Hit(card); break;
                case CardKind.Defend: playerBlock += CombatNumbers.Block(run, card); break;
                case CardKind.Prepare: break;
                case CardKind.Heavy: Hit(card); break;
                case CardKind.Double: Hit(card); if (outcome == BattleOutcome.None) Hit(card); break;
                case CardKind.Shield: Hit(card); break;
                case CardKind.Fortify: playerBlock += CombatNumbers.Block(run, card); break;
                case CardKind.Quick: Hit(card); break;
                case CardKind.Spirit: spirit += PrototypeSettings.Spirit(run, card); break;
                case CardKind.Steady: playerBlock += CombatNumbers.Block(run, card); break;
            }
            if (outcome == BattleOutcome.None) Draw(PrototypeSettings.Draw(run, card));
            discard.Add(card);
            return true;
        }
        void Hit(CardKind card)
        {
            double damage = CombatNumbers.Damage(run, card, spirit, playerBlock);
            CombatNumbers.Apply(damage, run.UsesAttributes ? enemy.physicalDefense : 0, ref enemyBlock, ref enemyHealth);
            if (enemyHealth == 0) { outcome = BattleOutcome.Victory; playerTurn = false; }
        }
        public CardEffectState EffectState() => new CardEffectState
        {
            enemyHealth = enemyHealth, enemyBlock = enemyBlock, playerHealth = run.health, playerBlock = playerBlock, spirit = spirit
        };
        public double Preview(CardKind card)
        {
            var custom = PrototypeSettings.Custom(run, card);
            if (custom != null && custom.UsesEffects)
            {
                var state = EffectState();
                CardEffects.Resolve(run, custom, run.UsesAttributes ? enemy.physicalDefense : 0,
                    run.UsesAttributes ? CharacterAttributes.Calculate(run, true).defense : 0, state);
                return CharacterAttributes.Quantize(enemyHealth - state.enemyHealth);
            }
            double block = enemyBlock, health = enemyHealth, total = 0;
            if (!CombatNumbers.IsAttack(PrototypeSettings.Template(run, card))) return 0;
            for (int segment = 0; segment < CombatNumbers.Segments(PrototypeSettings.Template(run, card)) && health > 0; segment++)
                total += CombatNumbers.Apply(CombatNumbers.Damage(run, card, spirit, playerBlock),
                    run.UsesAttributes ? enemy.physicalDefense : 0, ref block, ref health);
            return CharacterAttributes.Quantize(total);
        }
        public double ExpectedHealthLoss => EnemyTemplates.Find(run, enemy) != null ? CharacterAttributes.Quantize(run.health - EnemyTemplates.Preview(this).enemyHealth) : Math.Max(0, IntentAttack -
            (run.UsesAttributes ? CharacterAttributes.Calculate(run, true).defense : 0) - playerBlock);
        public void EndPlayerTurn()
        {
            if (!playerTurn || outcome != BattleOutcome.None) return;
            playerTurn = false; discard.AddRange(hand); hand.Clear(); spirit = 0;
        }
        public void ResolveEnemyTurn()
        {
            if (playerTurn || outcome != BattleOutcome.None) return;
            var template = EnemyTemplates.Find(run, enemy);
            if (template != null)
            {
                var state = EnemyTemplates.Preview(this);
                enemyHealth = state.playerHealth; enemyBlock = state.playerBlock;
                run.health = state.enemyHealth; playerBlock = state.enemyBlock;
                enemyTurn++;
                if (run.health <= 0) { outcome = BattleOutcome.Failure; return; }
                if (enemyHealth <= 0) { outcome = BattleOutcome.Victory; return; }
                StartPlayerTurn(); return;
            }
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
                if (EnemyTemplates.Find(run, enemy) != null) return EnemyTemplates.IntentAmount(this, CardEffectKind.Damage, CardEffectTarget.Enemy);
                int bonus = enemy.Level - 1;
                var settings = PrototypeSettings.For(run);
                int attack = settings?.enemyAttack[(int)enemy.kind] ?? (enemy.kind == EnemyKind.Bat ? 5 : 8);
                int heavy = settings?.enemyHeavy[(int)enemy.kind] ?? 14;
                if (enemy.kind == EnemyKind.Bat) return attack + bonus;
                if (enemy.kind == EnemyKind.Beetle) return enemyTurn % 2 == 1 ? attack + bonus : 0;
                return enemyTurn % 3 == 0 ? attack + bonus : enemyTurn % 3 == 2 ? heavy + bonus : 0;
            }
        }
        public int IntentBlock { get { if (EnemyTemplates.Find(run, enemy) != null) return EnemyTemplates.IntentAmount(this, CardEffectKind.Block, CardEffectTarget.Self); return PrototypeSettings.For(run)?.enemyBlock[(int)enemy.kind] ?? (enemy.kind == EnemyKind.Beetle ? 6 : 10); } }
        public string Intent { get { if (EnemyTemplates.Find(run, enemy) != null) return EnemyTemplates.Intent(this); return IntentAttack > 0 ? "攻击 " + IntentAttack : "格挡 " + IntentBlock; } }
    }
}
