using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MagicCave.Prototype
{
    // Prototype economy values: adjustable, no inventory limit on merchant stock yet.
    public static class ToothMerchant
    {
        public static readonly ItemKind[] Goods = { ItemKind.Potion, ItemKind.Torch, ItemKind.AssaultPotion };
        public static int Price(ItemKind kind) => kind == ItemKind.Potion ? 5 : kind == ItemKind.Torch ? 3 : kind == ItemKind.AssaultPotion ? 8 : 0;
        public static int VictoryTeeth(EnemyData enemy) => enemy.kind == EnemyKind.Boss ? 10 : 3;
        public static bool[] PlanFloors(int seed, int floorCount)
        {
            if (floorCount < 5) throw new ArgumentOutOfRangeException(nameof(floorCount));
            var plan = new bool[floorCount];
            var random = new RandomStream(RandomStream.Derive(seed, 0x4d455205u));
            // Prototype base chance: 50%. The final boss floor stays empty, but counts in windows.
            for (int i = 0; i < floorCount - 1; i++) plan[i] = random.Range(0, 2) == 1;
            for (int start = 0; start <= floorCount - 5; start++)
            {
                int count = 0;
                var available = new List<int>();
                for (int i = start; i < start + 5; i++)
                    if (plan[i]) count++;
                    else if (i < floorCount - 1) available.Add(i);
                random.Shuffle(available);
                // Adding a merchant cannot invalidate an earlier overlapping five-floor window.
                for (int i = 0; count < 2; i++, count++) plan[available[i]] = true;
            }
            return plan;
        }
        public static bool InRange(RunData run) => run != null && run.Map.hasMerchant &&
            run.Map.Known(run.Map.merchantX, run.Map.merchantY) &&
            Math.Abs(run.CellX - run.Map.merchantX) + Math.Abs(run.CellY - run.Map.merchantY) == 1;

        static InventorySlot Destination(RunData run, ItemKind kind) => run.inventory.FirstOrDefault(s =>
            s.units.Count > 0 && s.units[0].kind == kind && s.units.Count < Rules.StackLimit(kind)) ??
            run.inventory.FirstOrDefault(s => s.units.Count == 0);

        public static string PurchaseLock(RunData run, ItemKind kind)
        {
            if (run == null || run.health <= 0 || !InRange(run)) return "请先靠近集齿者";
            int price = Price(kind);
            if (price <= 0 || kind == ItemKind.AssaultPotion && !run.UsesAttributes) return "商品暂未出售";
            if (run.teeth < price) return "牙齿不足";
            return Destination(run, kind) == null ? "背包已满" : "";
        }

        public static string Buy(RunData run, ItemKind kind)
        {
            string reason = PurchaseLock(run, kind);
            if (reason.Length > 0) return reason;
            // Validate first; payment and one uniquely identified item then change together.
            Destination(run, kind).units.Add(new ItemUnit { id = run.nextItemId++, kind = kind,
                acquiredTicks = DateTime.UtcNow.Ticks, acquisitionOrder = run.nextAcquisitionOrder++ });
            run.teeth -= Price(kind);
            return "";
        }

        public static bool Place(RunData run, MapData map)
        {
            var candidates = new List<Vector2Int>();
            for (int y = 1; y < map.size - 1; y++) for (int x = 1; x < map.size - 1; x++)
            {
                if (!map.Walkable(x, y) || x == map.spawnX && y == map.spawnY ||
                    map.loot.Any(l => l.x == x && l.y == y) ||
                    map.enemies.Any(e => Math.Abs(e.x - x) <= 1 && Math.Abs(e.y - y) <= 1)) continue;
                candidates.Add(new Vector2Int(x, y));
            }
            // Independent stream: adding merchants does not consume loot/battle/card randomness.
            new RandomStream(RandomStream.Derive(run.seed, 0x544f4f00u + (uint)map.number)).Shuffle(candidates);
            foreach (var p in candidates)
            {
                map.hasMerchant = true; map.merchantX = p.x; map.merchantY = p.y;
                if (MapGenerator.ValidateContainerRoutes(map) && MapGenerator.ValidateCombatApproaches(map)) return true;
            }
            map.hasMerchant = false; return false;
        }
    }
}
