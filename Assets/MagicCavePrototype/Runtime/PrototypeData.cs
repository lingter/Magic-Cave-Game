using System;
using System.Collections.Generic;
using UnityEngine;

namespace MagicCave.Prototype
{
    public enum Tile { Ground, Wall, Advance, Return }
    // Existing values are save identifiers. Append new kinds; never reorder them.
    public enum ItemKind { Ore, Crystal, Relic, Potion, IronSword, SwiftSword, InscribedSword, RefinedSword,
        WoodShield, LeatherArmor, InsightArmor, IronArmor, StrengthEssence, AgilityEssence, WisdomEssence, AssaultPotion, SwordShield, LeatherHelm, LeatherGloves, LeatherBoots, CopperRing, Torch }
    public enum EnemyKind { Bat, Beetle, Boss }
    public enum EnemyRole { Roaming, ItemGuard, CorridorGuard }
    public enum ContainerKind { Legacy, Equipment, Treasure, Supplies }
    public enum CardKind { Strike, Defend, Prepare, Heavy, Double, Shield, Fortify, Quick, Spirit, Steady }

    [Serializable]
    public sealed class RandomStream
    {
        public uint state;
        public RandomStream(uint seed) { state = seed == 0 ? 0x6d2b79f5u : seed; }
        public uint Next()
        {
            uint value = state;
            value ^= value << 13; value ^= value >> 17; value ^= value << 5;
            return state = value;
        }
        public int Range(int minimum, int exclusiveMaximum)
        {
            if (exclusiveMaximum <= minimum) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            uint range = (uint)(exclusiveMaximum - minimum);
            uint threshold = unchecked(0u - range) % range;
            uint value;
            do { value = Next(); } while (value < threshold);
            return minimum + (int)(value % range);
        }
        public void Shuffle<T>(IList<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int other = Range(0, i + 1);
                T value = items[i]; items[i] = items[other]; items[other] = value;
            }
        }
        public static uint Derive(int seed, uint salt)
        {
            uint value = unchecked((uint)seed) ^ salt;
            value ^= value >> 16; value *= 0x7feb352du;
            value ^= value >> 15; value *= 0x846ca68bu;
            value ^= value >> 16;
            return value == 0 ? 1u : value;
        }
    }

    [Serializable]
    public sealed class ItemUnit
    {
        public int id;
        public ItemKind kind;
        public long acquiredTicks;
        public int acquisitionOrder;
        public int equippedSlot = -1; // Actual ring position; old saves fall back to the definition slot.
    }
    [Serializable]
    public sealed class InventorySlot { public List<ItemUnit> units = new List<ItemUnit>(); }
    [Serializable]
    public sealed class GroundLoot
    {
        public int id, x, y;
        public int guardEnemyId;
        public int secondGuardEnemyId;
        // New layouts remain connected with containers as permanent obstacles. Old saves keep their behavior.
        public bool permanentObstacle;
        // Layout intent only; never used as an opening or pickup lock.
        public bool combatApproach;
        public bool GuardedBy(int enemyId) => enemyId != 0 && (guardEnemyId == enemyId || secondGuardEnemyId == enemyId);
        public bool loose;
        // Missing fields in existing saves keep their already generated contents.
        public ContainerKind containerKind;
        public bool unopened, opened;
        // Retain container artwork after its last item is removed (including after loading).
        public ItemKind appearanceKind;
        public List<ItemUnit> units = new List<ItemUnit>();
        public bool IsContainer => !loose;
        public bool HasContents => unopened || units.Count > 0;
        public bool IsOpened => IsContainer && (opened || !HasContents);
        public bool BlocksMovement => IsContainer && (permanentObstacle || HasContents);
    }
    [Serializable]
    public sealed class EnemyData
    {
        public int id, x, y;
        public EnemyKind kind;
        public EnemyRole role;
        public int level = 1;
        public int physicalDefense;
        public bool defeated;
        public int Level => Math.Max(1, level);
    }
    [Serializable]
    public sealed class MapData
    {
        public const float PlayerCollisionSize = 0.8f;
        public int number, size, spawnX, spawnY;
        public bool dugTerrain;
        public bool hasMerchant;
        public int merchantX, merchantY;
        public int[] cells;
        public bool[] revealed;
        public List<EnemyData> enemies = new List<EnemyData>();
        public List<GroundLoot> loot = new List<GroundLoot>();
        public EnemyData LivingGuard(GroundLoot item)
        {
            if (item.loose) return null;
            return enemies.Find(e => item.GuardedBy(e.id) && !e.defeated);
        }
        public bool Inside(int x, int y) { return x >= 0 && y >= 0 && x < size && y < size; }
        public bool Border(int x, int y) { return x == 0 || y == 0 || x == size - 1 || y == size - 1; }
        public int Index(int x, int y) { return y * size + x; }
        public Tile At(int x, int y) { return Inside(x, y) ? (Tile)cells[Index(x, y)] : Tile.Wall; }
        public void Set(int x, int y, Tile tile) { cells[Index(x, y)] = (int)tile; }
        public bool Walkable(int x, int y) { return Inside(x, y) && At(x, y) == Tile.Ground; }
        public bool CanTraverse(int x, int y)
        {
            if (!Walkable(x, y)) return false;
            if (hasMerchant && merchantX == x && merchantY == y) return false;
            foreach (var enemy in enemies) if (!enemy.defeated && enemy.x == x && enemy.y == y) return false;
            foreach (var item in loot) if (item.BlocksMovement && item.x == x && item.y == y) return false;
            return true;
        }
        public bool CanStand(float x, float y)
        {
            const float radius = PlayerCollisionSize * 0.5f;
            return CanTraverse(Mathf.FloorToInt(x - radius + 0.5f), Mathf.FloorToInt(y - radius + 0.5f)) &&
                CanTraverse(Mathf.FloorToInt(x + radius + 0.5f), Mathf.FloorToInt(y - radius + 0.5f)) &&
                CanTraverse(Mathf.FloorToInt(x - radius + 0.5f), Mathf.FloorToInt(y + radius + 0.5f)) &&
                CanTraverse(Mathf.FloorToInt(x + radius + 0.5f), Mathf.FloorToInt(y + radius + 0.5f));
        }
        public bool Known(int x, int y) { return Inside(x, y) && revealed[Index(x, y)]; }
    }
    [Serializable]
    public sealed class RunData
    {
        public string id;
        public int seed, mapIndex, level = 1, experience, totalExperience;
        public double health = 60;
        public int maxHealth = 60, attackBonus, defenseBonus, openingEnergy, victoryHealing, openingBlock;
        public int MaxHealth => UsesAttributes ? CharacterAttributes.Calculate(this).maxHealth : maxHealth == 0 ? 60 : maxHealth;
        // Zero is deliberately the legacy format, including when old JSON has no such fields.
        public int attributeVersion;
        public CharacterState character;
        public AttributeRules attributeRules;
        public RandomStream lootRandom;
        public int containerVersion;
        // Zero keeps the original five-floor save layout; one adds three excavation trials.
        public int terrainVersion;
        public ContainerLootRules containerRules;
        public RandomStream containerRandom;
        public bool UsesAttributes => attributeVersion == 1;
        public int nextEntityId = 1, nextItemId = 1, nextAcquisitionOrder = 1;
        public float x, y;
        public bool bossDefeated;
        // A torch is consumed on ignition. Zero means no flame; old saves remain unlit.
        public int torchStepsRemaining;
        // Run currency, never an inventory item or a warehouse reward. Missing in old saves => zero.
        public int teeth;
        public RandomStream mapRandom, battleRandom, cardRandom;
        public List<MapData> maps = new List<MapData>();
        public List<CardKind> deck = new List<CardKind>();
        public List<InventorySlot> inventory = new List<InventorySlot>();
        public MapData Map { get { return maps[mapIndex]; } }
        public int CellX { get { return Mathf.FloorToInt(x + 0.5f); } }
        public int CellY { get { return Mathf.FloorToInt(y + 0.5f); } }
    }
    [Serializable]
    public sealed class PendingSettlement
    {
        public RunData run;
        public bool failure, confirmed;
        public List<int> selectedIds = new List<int>();
    }
    [Serializable]
    public sealed class SessionData : ISerializationCallbackReceiver
    {
        public int version = 2;
        // Inline Unity serialization materializes null classes. Presence flags retain empty states.
        public bool hasCheckpoint, hasPending;
        public RunData checkpoint;
        public PendingSettlement pending;
        public void OnBeforeSerialize() { hasCheckpoint = checkpoint != null; hasPending = pending != null; }
        public void OnAfterDeserialize()
        {
            if (!hasCheckpoint) checkpoint = null;
            if (!hasPending) pending = null;
        }
    }
    [Serializable]
    public sealed class WarehouseData
    {
        public int version = 5;
        public long[] counts = new long[Rules.ItemNames.Length];
        public List<StoredEquipment> equipment = new List<StoredEquipment>();
        public List<string> committedRuns = new List<string>();
        public long baseCapacity = 100;
        public TownProgress town = new TownProgress();
    }

    public static class Rules
    {
        public static readonly string[] ItemNames = { "碎矿石", "晶簇", "古代遗物", "治疗药剂", "铁制剑盾", "迅捷剑盾", "铭文剑盾", "精制剑盾",
            "木盾", "皮甲", "启迪皮甲", "铁甲", "力量精华", "敏捷精华", "智慧精华", "强攻药剂", "制式剑盾", "皮盔", "皮护手", "皮靴", "铜戒指", "火把" };
        public static readonly string[] EnemyNames = { "洞穴蝠", "岩甲虫", "岩洞守卫" };
        public static readonly int[] EnemyHealth = { 18, 30, 80 };
        public static int Health(EnemyData enemy) { return EnemyHealth[(int)enemy.kind] + 4 * (enemy.Level - 1); }
        public static string EnemyLabel(EnemyData enemy) { return EnemyNames[(int)enemy.kind] + " Lv." + enemy.Level; }
        public static readonly string[] CardNames = { "打击", "防御", "整备", "重击", "连击", "盾击", "固守", "迅击", "战意", "稳守" };
        public static readonly int[] Costs = { 1, 1, 1, 2, 1, 1, 2, 1, 1, 1 };
        public static readonly string[] Effects = { "造成6点伤害", "获得5点格挡", "抽2张牌", "造成15点伤害", "两次4点伤害", "造成当前格挡等量伤害\n不消耗格挡", "获得12点格挡", "造成5点伤害，抽1张", "本回合每段攻击+2\n可叠加", "获得4点格挡，抽1张" };
        public static bool IsEquipment(ItemKind kind) => kind >= ItemKind.IronSword && kind <= ItemKind.IronArmor || kind >= ItemKind.SwordShield && kind <= ItemKind.CopperRing;
        public static int StackLimit(ItemKind kind) => IsEquipment(kind) ? 1 : 20;
        public static int ArtIndex(ItemKind kind) => kind <= ItemKind.Potion ? (int)kind : IsEquipment(kind) ? 0 : 3;
        public static int Rarity(ItemKind kind) { return kind == ItemKind.Relic ? 2 : kind == ItemKind.Crystal ||
            kind == ItemKind.RefinedSword || kind == ItemKind.IronArmor ||
            kind >= ItemKind.StrengthEssence && kind <= ItemKind.WisdomEssence ? 1 : 0; }
        public static string RarityName(ItemKind kind) { return new[] { "常见", "罕见", "稀有" }[Rarity(kind)]; }
        public static int RequiredExperience(int level) { return 20 + 10 * (level - 1); }
        public static T Copy<T>(T value) { return JsonUtility.FromJson<T>(JsonUtility.ToJson(value)); }
    }
}
