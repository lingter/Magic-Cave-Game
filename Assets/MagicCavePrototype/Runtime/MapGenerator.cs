using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MagicCave.Prototype
{
    public static class MapGenerator
    {
        public static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        public static RunData NewRun(int seed, bool logSeed = true)
            => CreateRun(seed, AttributeKind.None, logSeed);

        public static RunData NewRun(int seed, AttributeKind secondary, bool logSeed = true)
            => CreateRun(seed, secondary, logSeed);

        // The original overload retains the legacy rules for old-save regression fixtures.
        static RunData CreateRun(int seed, AttributeKind secondary, bool logSeed)
        {
            if (logSeed) Debug.Log("[MagicCave] Run Seed = " + seed);
            var run = new RunData
            {
                id = Guid.NewGuid().ToString("N"), seed = seed,
                mapRandom = new RandomStream(RandomStream.Derive(seed, 0x4d415001)),
                battleRandom = new RandomStream(RandomStream.Derive(seed, 0x42415402)),
                cardRandom = new RandomStream(RandomStream.Derive(seed, 0x43415203))
            };
            if (secondary != AttributeKind.None) CharacterAttributes.Initialize(run, secondary);
            for (int i = 0; i < 5; i++) run.deck.Add(CardKind.Strike);
            for (int i = 0; i < 4; i++) run.deck.Add(CardKind.Defend);
            run.deck.Add(CardKind.Prepare);
            for (int i = 0; i < 20; i++) run.inventory.Add(new InventorySlot());
            for (int number = 1; number <= 4; number++) run.maps.Add(Generate(run, number));
            var boss = EmptyMap(5, 13);
            for (int y = 1; y < 12; y++) for (int x = 1; x < 12; x++) boss.Set(x, y, Tile.Ground);
            for (int i = 0; i < boss.revealed.Length; i++) boss.revealed[i] = true;
            boss.spawnX = 6; boss.spawnY = 2;
            boss.enemies.Add(new EnemyData { id = run.nextEntityId++, x = 6, y = 9, kind = EnemyKind.Boss });
            run.maps.Add(boss);
            run.x = run.Map.spawnX; run.y = run.Map.spawnY;
            Reveal(run);
            return run;
        }

        static MapData EmptyMap(int number, int size)
        {
            var map = new MapData { number = number, size = size, cells = new int[size * size], revealed = new bool[size * size] };
            for (int i = 0; i < map.cells.Length; i++) map.cells[i] = (int)Tile.Wall;
            return map;
        }

        static MapData Generate(RunData run, int number)
        {
            var random = run.mapRandom;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var map = EmptyMap(number, 25);
                var centers = new Vector2Int[9];
                for (int row = 0; row < 3; row++) for (int col = 0; col < 3; col++)
                {
                    int x = 4 + col * 8 + random.Range(-1, 2), y = 4 + row * 8 + random.Range(-1, 2);
                    centers[row * 3 + col] = new Vector2Int(x, y);
                    int width = random.Range(3, 6), height = random.Range(3, 6);
                    for (int dy = 0; dy < height; dy++) for (int dx = 0; dx < width; dx++)
                        map.Set(x - width / 2 + dx, y - height / 2 + dy, Tile.Ground);
                }
                for (int row = 0; row < 3; row++) for (int col = 0; col < 3; col++)
                {
                    var start = centers[row * 3 + col];
                    if (col < 2) Corridor(map, start, centers[row * 3 + col + 1], random.Range(0, 2) == 0);
                    if (row < 2) Corridor(map, start, centers[(row + 1) * 3 + col], random.Range(0, 2) == 0);
                }
                if (!ValidateTerrain(map, out _, out _)) continue;
                var candidates = OpenDoorCandidates(map);
                random.Shuffle(candidates);
                if (candidates.Count == 0) continue;
                var advance = candidates[0];
                candidates.RemoveAll(c => Manhattan(c, advance) < 3);
                if (number == 3 && candidates.Count == 0) continue;
                map.Set(advance.x, advance.y, Tile.Advance);
                if (number == 3) map.Set(candidates[0].x, candidates[0].y, Tile.Return);
                if (!ValidateTerrain(map, out _, out _)) continue;
                int entityId = run.nextEntityId, itemId = run.nextItemId;
                uint lootState = run.UsesAttributes ? run.lootRandom.state : 0;
                if (!PlaceObjects(run, map))
                {
                    run.nextEntityId = entityId; run.nextItemId = itemId;
                    if (run.UsesAttributes) run.lootRandom.state = lootState;
                    continue;
                }
                return map;
            }
            throw new InvalidOperationException("地图" + number + "在100次尝试后仍未通过验收。Seed=" + run.seed);
        }

        static void Corridor(MapData map, Vector2Int start, Vector2Int end, bool horizontalFirst)
        {
            var bend = horizontalFirst ? new Vector2Int(end.x, start.y) : new Vector2Int(start.x, end.y);
            CarveLine(map, start, bend); CarveLine(map, bend, end);
        }
        static void CarveLine(MapData map, Vector2Int start, Vector2Int end)
        {
            while (start != end)
            {
                map.Set(start.x, start.y, Tile.Ground);
                start += new Vector2Int(Math.Sign(end.x - start.x), Math.Sign(end.y - start.y));
            }
            map.Set(end.x, end.y, Tile.Ground);
        }
        public static int Manhattan(Vector2Int a, Vector2Int b) { return Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y); }
        static bool IsWall(MapData map, int x, int y) { return map.Inside(x, y) && map.At(x, y) != Tile.Ground; }

        public static bool ValidateTerrain(MapData map, out int wallGroups, out int thickGroups)
        {
            wallGroups = 0; thickGroups = 0;
            int groundCount = 0, groundReached = 0;
            var seen = new bool[map.cells.Length];
            var queue = new Queue<Vector2Int>();
            for (int y = 0; y < map.size; y++) for (int x = 0; x < map.size; x++)
            {
                if (map.Border(x, y) && map.At(x, y) != Tile.Wall) return false;
                if (map.At(x, y) == Tile.Ground)
                {
                    groundCount++;
                    if (queue.Count == 0 && groundReached == 0) { queue.Enqueue(new Vector2Int(x, y)); seen[map.Index(x, y)] = true; groundReached = -1; }
                }
            }
            if (groundCount == 0) return false;
            groundReached = 0;
            while (queue.Count > 0)
            {
                var point = queue.Dequeue(); groundReached++;
                foreach (var direction in Directions)
                {
                    var next = point + direction;
                    if (!map.Walkable(next.x, next.y) || seen[map.Index(next.x, next.y)]) continue;
                    seen[map.Index(next.x, next.y)] = true; queue.Enqueue(next);
                }
            }
            if (groundReached != groundCount) return false;
            for (int y = 0; y < map.size; y++) for (int x = 0; x < map.size; x++)
            {
                if (!IsWall(map, x, y) || seen[map.Index(x, y)]) continue;
                wallGroups++;
                bool thick = false;
                queue.Enqueue(new Vector2Int(x, y)); seen[map.Index(x, y)] = true;
                while (queue.Count > 0)
                {
                    var point = queue.Dequeue();
                    if (!thick && point.x >= 2 && point.y >= 2 && point.x < map.size - 2 && point.y < map.size - 2)
                    {
                        bool square = true;
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                            if (!IsWall(map, point.x + dx, point.y + dy)) square = false;
                        thick = square;
                    }
                    foreach (var direction in Directions)
                    {
                        var next = point + direction;
                        if (!IsWall(map, next.x, next.y) || seen[map.Index(next.x, next.y)]) continue;
                        seen[map.Index(next.x, next.y)] = true; queue.Enqueue(next);
                    }
                }
                if (thick) thickGroups++;
            }
            return wallGroups >= 4 && thickGroups >= 2;
        }

        public static List<Vector2Int> DoorCandidates(MapData map)
        {
            var candidates = new List<Vector2Int>();
            for (int y = 1; y < map.size - 1; y++) for (int x = 1; x < map.size - 1; x++)
            {
                if (map.At(x, y) != Tile.Wall) continue;
                var point = new Vector2Int(x, y);
                bool surrounded = true, nearGround = false;
                foreach (var direction in Directions)
                {
                    var wall = point + direction;
                    if (map.At(wall.x, wall.y) != Tile.Wall) surrounded = false;
                    foreach (var step in Directions)
                    {
                        var ground = wall + step;
                        if (map.At(ground.x, ground.y) == Tile.Ground) nearGround = true;
                    }
                }
                if (surrounded && nearGround) candidates.Add(point);
            }
            return candidates;
        }

        static List<Vector2Int> OpenDoorCandidates(MapData map)
        {
            var result = new List<Vector2Int>();
            for (int y = 1; y < map.size - 1; y++) for (int x = 1; x < map.size - 1; x++)
            {
                if (!map.Walkable(x, y)) continue;
                int neighbors = 0;
                foreach (var d in Directions) if (map.Walkable(x + d.x, y + d.y)) neighbors++;
                if (neighbors >= 3) result.Add(new Vector2Int(x, y));
            }
            return result;
        }

        // A room corner has two perpendicular solid walls, rather than just a corridor bend.
        public static bool IsLootCorner(MapData map, Vector2Int p)
        {
            if (!map.Walkable(p.x, p.y)) return false;
            for (int i = 0; i < Directions.Length; i++)
            {
                var a = p + Directions[i]; var b = p + Directions[(i + 1) % 4];
                var openA = p - Directions[i]; var openB = p - Directions[(i + 1) % 4];
                if (map.At(a.x, a.y) == Tile.Wall && map.At(b.x, b.y) == Tile.Wall &&
                    map.Walkable(openA.x, openA.y) && map.Walkable(openB.x, openB.y) &&
                    map.Walkable(openA.x + openB.x - p.x, openA.y + openB.y - p.y)) return true;
            }
            return false;
        }

        public static bool IsCorridor(MapData map, Vector2Int p)
        {
            if (!map.Walkable(p.x, p.y)) return false;
            return map.Walkable(p.x - 1, p.y) && map.Walkable(p.x + 1, p.y) &&
                map.At(p.x, p.y - 1) == Tile.Wall && map.At(p.x, p.y + 1) == Tile.Wall ||
                map.Walkable(p.x, p.y - 1) && map.Walkable(p.x, p.y + 1) &&
                map.At(p.x - 1, p.y) == Tile.Wall && map.At(p.x + 1, p.y) == Tile.Wall;
        }

        static EnemyData AddEnemy(RunData run, MapData map, Vector2Int p, EnemyRole role)
        {
            var enemy = new EnemyData { id = run.nextEntityId++, x = p.x, y = p.y,
                kind = (EnemyKind)run.mapRandom.Range(0, 2), role = role };
            map.enemies.Add(enemy); return enemy;
        }

        static bool PlaceObjects(RunData run, MapData map)
        {
            var free = new List<Vector2Int>(); var corners = new List<Vector2Int>();
            for (int y = 1; y < map.size - 1; y++) for (int x = 1; x < map.size - 1; x++)
            {
                var p = new Vector2Int(x, y);
                if (map.Walkable(x, y)) free.Add(p);
                if (IsLootCorner(map, p)) corners.Add(p);
            }
            run.mapRandom.Shuffle(free); run.mapRandom.Shuffle(corners);
            var occupied = new HashSet<Vector2Int>();
            var guarded = new List<KeyValuePair<Vector2Int, EnemyData>>();
            // Eight corner piles, seven guards: one guard protects two adjacent corner piles.
            foreach (var p in free)
            {
                var adjacent = corners.FindAll(c => Manhattan(c, p) == 1);
                if (adjacent.Count < 2 || IsLootCorner(map, p)) continue;
                var guard = AddEnemy(run, map, p, EnemyRole.ItemGuard);
                occupied.Add(p);
                for (int i = 0; i < 2; i++)
                {
                    occupied.Add(adjacent[i]); guarded.Add(new KeyValuePair<Vector2Int, EnemyData>(adjacent[i], guard));
                }
                break;
            }
            if (guarded.Count != 2) return false;
            foreach (var corner in corners)
            {
                if (guarded.Count == 8) break;
                if (occupied.Contains(corner)) continue;
                var options = new List<Vector2Int>();
                foreach (var d in Directions)
                {
                    var p = corner + d;
                    if (map.Walkable(p.x, p.y) && !occupied.Contains(p) && !IsLootCorner(map, p)) options.Add(p);
                }
                if (options.Count == 0) continue;
                var position = options[run.mapRandom.Range(0, options.Count)];
                var guard = AddEnemy(run, map, position, EnemyRole.ItemGuard);
                occupied.Add(position); occupied.Add(corner);
                guarded.Add(new KeyValuePair<Vector2Int, EnemyData>(corner, guard));
            }
            if (guarded.Count != 8) return false;

            var kinds = new List<ItemKind> { ItemKind.Ore, ItemKind.Ore, ItemKind.Ore, ItemKind.Ore,
                ItemKind.Crystal, ItemKind.Crystal, ItemKind.Crystal, ItemKind.Potion, ItemKind.Potion,
                run.mapRandom.Range(0, 4) == 0 ? ItemKind.Relic : ItemKind.Ore };
            if (run.UsesAttributes)
            {
                kinds[0] = map.number == 1 || run.lootRandom.Range(0, 10) < 6 ? DrawWeapon(run, map.number == 1) : DrawArmor(run);
                kinds[1] = new[] { ItemKind.StrengthEssence, ItemKind.AgilityEssence, ItemKind.WisdomEssence, ItemKind.AssaultPotion }[run.lootRandom.Range(0, 4)];
            }
            run.mapRandom.Shuffle(kinds);
            kinds.Sort((a, b) => Rules.Rarity(b).CompareTo(Rules.Rarity(a)));
            // Assign scarce high levels to the rarest guarded loot first.
            for (int i = 0; i < guarded.Count; i++)
            {
                var pair = guarded[i];
                int level = Rules.Rarity(kinds[i]) + 1;
                pair.Value.level = Math.Max(pair.Value.level, level);
                AddPile(run, map, pair.Key, kinds[i]).guardEnemyId = pair.Value.id;
            }
            foreach (var p in free)
            {
                if (map.loot.Count == 10) break;
                if (occupied.Contains(p) || IsLootCorner(map, p) || IsCorridor(map, p)) continue;
                AddPile(run, map, p, kinds[map.loot.Count]); occupied.Add(p);
            }
            if (map.loot.Count != 10) return false;
            foreach (var p in free)
            {
                if (occupied.Contains(p) || !IsCorridor(map, p)) continue;
                AddEnemy(run, map, p, EnemyRole.CorridorGuard); occupied.Add(p); break;
            }
            if (map.enemies.Count != 8) return false;
            foreach (var p in free)
            {
                if (map.enemies.Count == 10) break;
                if (occupied.Contains(p) || IsCorridor(map, p)) continue;
                AddEnemy(run, map, p, EnemyRole.Roaming); occupied.Add(p);
            }
            if (map.enemies.Count != 10) return false;
            foreach (var p in free)
            {
                if (occupied.Contains(p) || IsCorridor(map, p) ||
                    map.enemies.Exists(e => Manhattan(p, new Vector2Int(e.x, e.y)) < 3)) continue;
                map.spawnX = p.x; map.spawnY = p.y; return true;
            }
            return false;
        }

        public static ItemKind DrawWeapon(RunData run, bool commonOnly = false)
        {
            var candidates = run.attributeRules.equipment.Where(e => e.slot == EquipmentSlot.MainHand &&
                e.ownerCharacterId == run.character.templateId && Rules.IsEquipment(e.kind) &&
                run.attributeRules.template.allowedTypes.Contains(e.type) && (!commonOnly || Rules.Rarity(e.kind) == 0)).ToArray();
            if (candidates.Length == 0) return ItemKind.IronSword;
            int total = candidates.Sum(e => Rules.Rarity(e.kind) == 0 ? 3 : 1);
            int roll = run.lootRandom.Range(0, total);
            foreach (var candidate in candidates)
            {
                roll -= Rules.Rarity(candidate.kind) == 0 ? 3 : 1;
                if (roll < 0) return candidate.kind;
            }
            return ItemKind.IronSword;
        }
        static ItemKind DrawArmor(RunData run)
        {
            var pool = new[] { ItemKind.WoodShield, ItemKind.LeatherArmor, ItemKind.InsightArmor, ItemKind.IronArmor };
            int roll = run.lootRandom.Range(0, 10);
            return pool[Math.Min(3, roll / 3)];
        }

        static GroundLoot AddPile(RunData run, MapData map, Vector2Int p, ItemKind kind)
        {
            int count = kind == ItemKind.Ore ? run.mapRandom.Range(2, 5) :
                kind == ItemKind.Crystal ? run.mapRandom.Range(1, 3) : 1;
            return AddLoot(run, map, p, kind, count);
        }
        public static GroundLoot AddLoot(RunData run, MapData map, Vector2Int position, ItemKind kind, int count)
        {
            var loot = new GroundLoot { id = run.nextEntityId++, x = position.x, y = position.y, appearanceKind = kind };
            for (int i = 0; i < count; i++) loot.units.Add(new ItemUnit { id = run.nextItemId++, kind = kind });
            map.loot.Add(loot);
            return loot;
        }
        public static bool Visible(RunData run, int x, int y)
        {
            return run.mapIndex == 4 || Math.Abs(run.CellX - x) + Math.Abs(run.CellY - y) <= 1;
        }
        public static bool IsDoorHintWall(MapData map, int x, int y)
        {
            if (!map.Known(x, y) || map.At(x, y) != Tile.Wall) return false;
            foreach (var direction in Directions)
            {
                Tile neighbor = map.At(x + direction.x, y + direction.y);
                if (neighbor == Tile.Advance || neighbor == Tile.Return) return true;
            }
            return false;
        }
        public static bool EnsureSafePlayerPosition(RunData run)
        {
            if (run.Map.CanStand(run.x, run.y)) return false;
            // Older saves used a smaller footprint and allowed standing on item containers.
            // Search terrain breadth-first so a chain of newly solid containers cannot strand a save.
            var candidates = new Queue<Vector2Int>(); var visited = new HashSet<Vector2Int>();
            var origin = new Vector2Int(run.CellX, run.CellY); candidates.Enqueue(origin); visited.Add(origin);
            while (candidates.Count > 0)
            {
                var candidate = candidates.Dequeue();
                if (run.Map.CanStand(candidate.x, candidate.y))
                { run.x = candidate.x; run.y = candidate.y; Reveal(run); return true; }
                foreach (var direction in Directions)
                {
                    var next = candidate + direction;
                    if (run.Map.Walkable(next.x, next.y) && visited.Add(next)) candidates.Enqueue(next);
                }
            }
            throw new InvalidOperationException("玩家附近没有可站立的位置。");
        }
        public static void Reveal(RunData run)
        {
            foreach (var direction in Directions)
            {
                int x = run.CellX + direction.x, y = run.CellY + direction.y;
                if (run.Map.Inside(x, y)) run.Map.revealed[run.Map.Index(x, y)] = true;
            }
            if (run.Map.Inside(run.CellX, run.CellY)) run.Map.revealed[run.Map.Index(run.CellX, run.CellY)] = true;
        }

        public static List<Vector2Int> Path(MapData map, Vector2Int start, Vector2Int target, bool adjacent)
        {
            var parent = new int[map.cells.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = -2;
            var queue = new Queue<Vector2Int>();
            if (!map.CanTraverse(start.x, start.y)) return null;
            queue.Enqueue(start); parent[map.Index(start.x, start.y)] = -1;
            while (queue.Count > 0)
            {
                var point = queue.Dequeue();
                if ((!adjacent && point == target) || (adjacent && Manhattan(point, target) <= 1))
                {
                    var result = new List<Vector2Int>(); int current = map.Index(point.x, point.y);
                    while (parent[current] >= 0) { result.Add(new Vector2Int(current % map.size, current / map.size)); current = parent[current]; }
                    result.Reverse(); return result;
                }
                foreach (var direction in Directions)
                {
                    var next = point + direction;
                    if (!map.CanTraverse(next.x, next.y) || !map.Known(next.x, next.y) || parent[map.Index(next.x, next.y)] != -2) continue;
                    parent[map.Index(next.x, next.y)] = map.Index(point.x, point.y); queue.Enqueue(next);
                }
            }
            return null;
        }
    }
}
