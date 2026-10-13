using System;
using System.Collections.Generic;
using UnityEngine;

namespace MagicCave.Prototype
{
    public static class MapGenerator
    {
        public static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        public static RunData NewRun(int seed, bool logSeed = true)
        {
            if (logSeed) Debug.Log("[MagicCave] Run Seed = " + seed);
            var run = new RunData
            {
                id = Guid.NewGuid().ToString("N"), seed = seed,
                mapRandom = new RandomStream(RandomStream.Derive(seed, 0x4d415001)),
                battleRandom = new RandomStream(RandomStream.Derive(seed, 0x42415402)),
                cardRandom = new RandomStream(RandomStream.Derive(seed, 0x43415203))
            };
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
                var candidates = DoorCandidates(map);
                random.Shuffle(candidates);
                if (candidates.Count == 0) continue;
                var advance = candidates[0];
                candidates.RemoveAll(c => Manhattan(c, advance) < 3);
                if (number == 3 && candidates.Count == 0) continue;
                map.Set(advance.x, advance.y, Tile.Advance);
                if (number == 3) map.Set(candidates[0].x, candidates[0].y, Tile.Return);
                PlaceObjects(run, map);
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

        static void PlaceObjects(RunData run, MapData map)
        {
            var free = new List<Vector2Int>();
            for (int y = 1; y < map.size - 1; y++) for (int x = 1; x < map.size - 1; x++)
                if (map.Walkable(x, y)) free.Add(new Vector2Int(x, y));
            run.mapRandom.Shuffle(free);
            int index = 0;
            var spawn = free[index++]; map.spawnX = spawn.x; map.spawnY = spawn.y;
            for (int i = 0; i < 2; i++)
            {
                var p = free[index++];
                map.enemies.Add(new EnemyData { id = run.nextEntityId++, x = p.x, y = p.y, kind = (EnemyKind)run.mapRandom.Range(0, 2) });
            }
            for (int i = 0; i < 3; i++) AddLoot(run, map, free[index++], ItemKind.Ore, run.mapRandom.Range(2, 5));
            for (int i = 0; i < 2; i++) AddLoot(run, map, free[index++], ItemKind.Crystal, run.mapRandom.Range(1, 3));
            for (int i = 0; i < 2; i++) AddLoot(run, map, free[index++], ItemKind.Potion, 1);
            if (run.mapRandom.Range(0, 4) == 0) AddLoot(run, map, free[index], ItemKind.Relic, 1);
        }
        public static GroundLoot AddLoot(RunData run, MapData map, Vector2Int position, ItemKind kind, int count)
        {
            var loot = new GroundLoot { id = run.nextEntityId++, x = position.x, y = position.y };
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
            var candidates = new List<Vector2Int> { new Vector2Int(run.CellX, run.CellY) };
            foreach (var direction in Directions) candidates.Add(new Vector2Int(run.CellX, run.CellY) + direction);
            candidates.Sort((a, b) => ((Vector2)a - new Vector2(run.x, run.y)).sqrMagnitude.CompareTo(((Vector2)b - new Vector2(run.x, run.y)).sqrMagnitude));
            foreach (var candidate in candidates)
            {
                if (!run.Map.CanStand(candidate.x, candidate.y)) continue;
                run.x = candidate.x; run.y = candidate.y; Reveal(run); return true;
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
