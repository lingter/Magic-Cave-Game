using System;
using System.Collections.Generic;
using UnityEngine;

namespace MagicCave.Prototype
{
    public enum Tile { Ground, Wall, Advance, Return }
    public enum ItemKind { Ore, Crystal, Relic, Potion }
    public enum EnemyKind { Bat, Beetle, Boss }
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
    }
    [Serializable]
    public sealed class InventorySlot { public List<ItemUnit> units = new List<ItemUnit>(); }
    [Serializable]
    public sealed class GroundLoot
    {
        public int id, x, y;
        public bool loose;
        public List<ItemUnit> units = new List<ItemUnit>();
    }
    [Serializable]
    public sealed class EnemyData
    {
        public int id, x, y;
        public EnemyKind kind;
        public bool defeated;
    }
    [Serializable]
    public sealed class MapData
    {
        public const float PlayerCollisionSize = 0.8f;
        public int number, size, spawnX, spawnY;
        public int[] cells;
        public bool[] revealed;
        public List<EnemyData> enemies = new List<EnemyData>();
        public List<GroundLoot> loot = new List<GroundLoot>();
        public bool Inside(int x, int y) { return x >= 0 && y >= 0 && x < size && y < size; }
        public bool Border(int x, int y) { return x == 0 || y == 0 || x == size - 1 || y == size - 1; }
        public int Index(int x, int y) { return y * size + x; }
        public Tile At(int x, int y) { return Inside(x, y) ? (Tile)cells[Index(x, y)] : Tile.Wall; }
        public void Set(int x, int y, Tile tile) { cells[Index(x, y)] = (int)tile; }
        public bool Walkable(int x, int y) { return Inside(x, y) && At(x, y) == Tile.Ground; }
        public bool CanTraverse(int x, int y)
        {
            if (!Walkable(x, y)) return false;
            foreach (var enemy in enemies) if (!enemy.defeated && enemy.x == x && enemy.y == y) return false;
            foreach (var item in loot) if (!item.loose && item.units.Count > 0 && item.x == x && item.y == y) return false;
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
        public int seed, mapIndex, health = 60, level = 1, experience, totalExperience;
        public int nextEntityId = 1, nextItemId = 1, nextAcquisitionOrder = 1;
        public float x, y;
        public bool bossDefeated;
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
        public int version = 1;
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
        public int version = 1;
        public long[] counts = new long[4];
        public List<string> committedRuns = new List<string>();
    }

    public static class Rules
    {
        public static readonly string[] ItemNames = { "碎矿石", "晶簇", "古代遗物", "治疗药剂" };
        public static readonly string[] EnemyNames = { "洞穴蝠", "岩甲虫", "岩洞守卫" };
        public static readonly int[] EnemyHealth = { 18, 30, 80 };
        public static readonly string[] CardNames = { "打击", "防御", "整备", "重击", "连击", "盾击", "固守", "迅击", "战意", "稳守" };
        public static readonly int[] Costs = { 1, 1, 1, 2, 1, 1, 2, 1, 1, 1 };
        public static readonly string[] Effects = { "造成6点伤害", "获得5点格挡", "抽2张牌", "造成15点伤害", "两次4点伤害", "造成当前格挡等量伤害\n不消耗格挡", "获得12点格挡", "造成5点伤害，抽1张", "本回合每段攻击+2\n可叠加", "获得4点格挡，抽1张" };
        public static int Rarity(ItemKind kind) { return kind == ItemKind.Relic ? 2 : kind == ItemKind.Crystal ? 1 : 0; }
        public static string RarityName(ItemKind kind) { return new[] { "常见", "罕见", "稀有" }[Rarity(kind)]; }
        public static int RequiredExperience(int level) { return 20 + 10 * (level - 1); }
        public static T Copy<T>(T value) { return JsonUtility.FromJson<T>(JsonUtility.ToJson(value)); }
    }
}
