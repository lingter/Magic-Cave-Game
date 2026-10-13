using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MagicCave.Prototype
{
    /// <summary>Separate session/warehouse files; a run-id ledger makes interrupted commits idempotent.</summary>
    public sealed class PrototypeStore
    {
        public readonly string folder;
        public SessionData Session { get; private set; }
        public WarehouseData Warehouse { get; private set; }
        string SessionPath { get { return Path.Combine(folder, "session.json"); } }
        string WarehousePath { get { return Path.Combine(folder, "warehouse.json"); } }

        public PrototypeStore(string folder)
        {
            this.folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(this.folder);
            Session = Read<SessionData>(SessionPath) ?? new SessionData();
            Warehouse = Read<WarehouseData>(WarehousePath) ?? new WarehouseData();
            ValidateSession(Session, true);
            // Keep old files intact; repair valid older saves that allowed enemy/door overlap in memory.
            if (Session.checkpoint != null) MapGenerator.EnsureSafePlayerPosition(Session.checkpoint);
            if (Session.pending != null) MapGenerator.EnsureSafePlayerPosition(Session.pending.run);
            ValidateSession(Session);
            if (Warehouse.version != 1 || Warehouse.counts == null || Warehouse.counts.Length != 4 ||
                Warehouse.counts.Any(c => c < 0) || Warehouse.committedRuns == null)
                throw new InvalidDataException("仓库数据格式无效。原文件已保留。");
        }
        static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("存档过大：" + path);
            T value = JsonUtility.FromJson<T>(File.ReadAllText(path, Encoding.UTF8));
            if (value == null) throw new InvalidDataException("无法读取存档：" + path);
            return value;
        }
        static void AtomicWrite(string path, object value)
        {
            string temporary = path + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(value, true));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        void SaveSession(SessionData next)
        {
            ValidateSession(next); AtomicWrite(SessionPath, next); Session = next;
        }
        public void SaveCheckpoint(RunData run)
        {
            SaveSession(new SessionData { checkpoint = Rules.Copy(run) });
        }
        public RunData Continue()
        {
            if (Session.pending != null) throw new InvalidOperationException("请先处理未完成的结算。");
            if (Session.checkpoint == null) throw new InvalidOperationException("没有可继续的存档。");
            var run = Rules.Copy(Session.checkpoint);
            Debug.Log("[MagicCave] Continue Run Seed = " + run.seed);
            return run;
        }
        public void DiscardCheckpoint()
        {
            if (Session.pending != null) throw new InvalidOperationException("请先处理未完成的结算。");
            SaveSession(new SessionData());
        }
        public void BeginSettlement(RunData run, bool failure)
        {
            var next = Rules.Copy(Session);
            next.pending = new PendingSettlement { run = Rules.Copy(run), failure = failure };
            if (failure)
            {
                next.checkpoint = null;
                next.pending.selectedIds = Inventory.FailureSelection(run).Select(i => i.id).ToList();
            }
            SaveSession(next);
        }
        public void SaveSelection(IList<int> selectedIds)
        {
            if (Session.pending == null || Session.pending.failure || Session.pending.confirmed) return;
            var next = Rules.Copy(Session);
            next.pending.selectedIds = selectedIds.ToList();
            SaveSession(next);
        }
        public RunData CancelSettlement()
        {
            if (Session.pending == null || Session.pending.failure || Session.pending.confirmed)
                throw new InvalidOperationException("此结算不能取消。");
            var restored = Rules.Copy(Session.pending.run);
            var next = Rules.Copy(Session); next.pending = null; SaveSession(next);
            return restored;
        }
        public List<ItemUnit> CommitSettlement()
        {
            var pending = Session.pending;
            if (pending == null) throw new InvalidOperationException("没有待结算的本局。");
            ValidateSelection(pending);
            if (!pending.confirmed)
            {
                var next = Rules.Copy(Session);
                next.checkpoint = null; next.pending.confirmed = true;
                SaveSession(next); pending = Session.pending;
            }
            var selected = Inventory.Items(pending.run).Where(i => pending.selectedIds.Contains(i.id)).ToList();
            if (!Warehouse.committedRuns.Contains(pending.run.id))
            {
                var nextWarehouse = Rules.Copy(Warehouse);
                foreach (var item in selected) nextWarehouse.counts[(int)item.kind] = checked(nextWarehouse.counts[(int)item.kind] + 1);
                nextWarehouse.committedRuns.Add(pending.run.id);
                AtomicWrite(WarehousePath, nextWarehouse); Warehouse = nextWarehouse;
            }
            SaveSession(new SessionData());
            return selected;
        }
        static void ValidateSelection(PendingSettlement pending)
        {
            var items = Inventory.Items(pending.run);
            int limit = pending.failure ? 5 : 10;
            if (pending.selectedIds == null || pending.selectedIds.Count > limit ||
                pending.selectedIds.Distinct().Count() != pending.selectedIds.Count ||
                pending.selectedIds.Any(id => items.All(i => i.id != id)))
                throw new InvalidDataException("带出物品选择无效。");
            if (pending.failure && !pending.selectedIds.SequenceEqual(Inventory.FailureSelection(pending.run).Select(i => i.id)))
                throw new InvalidDataException("失败结算的自动选取结果无效。");
        }
        static void ValidateSession(SessionData session, bool allowLegacyPosition = false)
        {
            if (session == null || session.version != 1) throw new InvalidDataException("本局存档版本不支持。");
            if (session.checkpoint != null)
            {
                ValidateRun(session.checkpoint, allowLegacyPosition);
                if (session.checkpoint.health <= 0) throw new InvalidDataException("失败本局不能恢复为探索。");
            }
            if (session.pending != null)
            {
                ValidateRun(session.pending.run, allowLegacyPosition); ValidateSelection(session.pending);
                if ((session.pending.failure || session.pending.confirmed) && session.checkpoint != null)
                    throw new InvalidDataException("结束本局的旧探索存档尚未失效。");
                if (session.pending.failure && session.pending.run.health != 0)
                    throw new InvalidDataException("失败结算生命值不一致。");
            }
        }
        public static void ValidateRun(RunData run, bool allowLegacyPosition = false)
        {
            if (run == null || string.IsNullOrEmpty(run.id) || run.maps == null || run.maps.Count != 5 ||
                run.mapIndex < 0 || run.mapIndex >= 5 || run.health < 0 || run.health > 60 ||
                run.level < 1 || run.level > 1000000 || run.experience < 0 || run.totalExperience < 0 ||
                run.deck == null || run.deck.Count < 10 || run.inventory == null || run.inventory.Count != 20 ||
                run.mapRandom == null || run.battleRandom == null || run.cardRandom == null ||
                run.mapRandom.state == 0 || run.battleRandom.state == 0 || run.cardRandom.state == 0)
                throw new InvalidDataException("玩家或随机进度数据无效。");
            if (run.deck.Any(card => (int)card < 0 || (int)card >= Rules.CardNames.Length))
                throw new InvalidDataException("卡组包含未知卡牌。");
            var ids = new HashSet<int>();
            foreach (var slot in run.inventory)
            {
                if (slot == null || slot.units == null || slot.units.Count > 20 ||
                    (slot.units.Count > 0 && slot.units.Any(i => i.kind != slot.units[0].kind)))
                    throw new InvalidDataException("背包堆叠无效。");
                foreach (var item in slot.units) ValidateItem(item, ids);
            }
            for (int index = 0; index < run.maps.Count; index++)
            {
                var map = run.maps[index]; int expected = index == 4 ? 13 : 25;
                if (map == null || map.size != expected || map.number != index + 1 ||
                    map.cells == null || map.cells.Length != expected * expected ||
                    map.revealed == null || map.revealed.Length != map.cells.Length ||
                    map.enemies == null || map.loot == null || !map.Walkable(map.spawnX, map.spawnY))
                    throw new InvalidDataException("地图数据无效。");
                for (int y = 0; y < expected; y++) for (int x = 0; x < expected; x++)
                    if (map.cells[map.Index(x, y)] < 0 || map.cells[map.Index(x, y)] > 3 || (map.Border(x, y) && map.At(x, y) != Tile.Wall))
                        throw new InvalidDataException("地图地块或边界无效。");
                foreach (var enemy in map.enemies)
                    if (enemy == null || (int)enemy.kind < 0 || (int)enemy.kind > 2 || !map.Walkable(enemy.x, enemy.y))
                        throw new InvalidDataException("敌人数据无效。");
                foreach (var loot in map.loot)
                {
                    if (loot == null || loot.units == null || !map.Walkable(loot.x, loot.y)) throw new InvalidDataException("地面物品数据无效。");
                    foreach (var item in loot.units) ValidateItem(item, ids);
                }
            }
            bool legacyBossDoor = allowLegacyPosition && run.mapIndex == 4 && run.bossDefeated &&
                run.CellX == 6 && run.CellY == 10 && run.Map.At(6, 10) == Tile.Return;
            if (float.IsNaN(run.x) || float.IsNaN(run.y) || float.IsInfinity(run.x) || float.IsInfinity(run.y) ||
                float.IsNaN(run.cameraYaw) || float.IsInfinity(run.cameraYaw) ||
                (!run.Map.Walkable(run.CellX, run.CellY) && !legacyBossDoor) ||
                (!allowLegacyPosition && !run.Map.CanStand(run.x, run.y))) throw new InvalidDataException("玩家位置无效。");
        }
        static void ValidateItem(ItemUnit item, HashSet<int> ids)
        {
            if (item == null || item.id <= 0 || (int)item.kind < 0 || (int)item.kind > 3 || !ids.Add(item.id))
                throw new InvalidDataException("物品标识或类型无效。");
        }
    }
}
