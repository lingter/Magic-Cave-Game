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
            if (UpgradeSecondaryRules(Session) && File.Exists(SessionPath) && !File.Exists(SessionPath + ".pre-secondary-1pct.bak"))
                File.Copy(SessionPath, SessionPath + ".pre-secondary-1pct.bak");
            ValidateSession(Session, true);
            // Keep old files intact; repair valid older saves that allowed enemy/door overlap in memory.
            if (Session.checkpoint != null) MapGenerator.EnsureSafePlayerPosition(Session.checkpoint);
            if (Session.pending != null) MapGenerator.EnsureSafePlayerPosition(Session.pending.run);
            ValidateSession(Session);
            if ((Warehouse.version < 1 || Warehouse.version > 3) || Warehouse.counts == null ||
                Warehouse.counts.Length != (Warehouse.version < 3 ? 4 : Rules.ItemNames.Length) ||
                Warehouse.counts.Any(c => c < 0) || Warehouse.committedRuns == null)
                throw new InvalidDataException("仓库数据格式无效。原文件已保留。");
            if (Warehouse.equipment == null) Warehouse.equipment = new List<StoredEquipment>();
            if (Warehouse.version == 1)
            {
                Warehouse.town = new TownProgress();
                // Legacy warehouses were unlimited: retain every existing item when adding a capacity.
                Warehouse.baseCapacity = Math.Max(100, TownRules.Used(Warehouse));
                Warehouse.version = 2;
            }
            if (Warehouse.version == 2)
            {
                if (File.Exists(WarehousePath) && !File.Exists(WarehousePath + ".pre-attributes.bak"))
                    File.Copy(WarehousePath, WarehousePath + ".pre-attributes.bak");
                var expanded = new long[Rules.ItemNames.Length]; Array.Copy(Warehouse.counts, expanded, 4);
                Warehouse.counts = expanded; Warehouse.version = 3;
            }
            ValidateTown(Warehouse);
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
            UpgradeSecondaryRules(next);
            next.version = 2;
            if (Session.version == 1 && File.Exists(SessionPath) && !File.Exists(SessionPath + ".pre-attributes.bak"))
                File.Copy(SessionPath, SessionPath + ".pre-attributes.bak");
            ValidateSession(next); AtomicWrite(SessionPath, next); Session = next;
        }
        static bool UpgradeSecondaryRules(SessionData session)
        {
            bool checkpoint = session.checkpoint != null && session.checkpoint.UsesAttributes && CharacterAttributes.UpgradeSecondaryRules(session.checkpoint.attributeRules);
            bool pending = session.pending != null && session.pending.run != null && session.pending.run.UsesAttributes && CharacterAttributes.UpgradeSecondaryRules(session.pending.run.attributeRules);
            return checkpoint || pending;
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
            if (!Warehouse.committedRuns.Contains(pending.run.id) &&
                pending.selectedIds.Count > TownRules.Capacity(Warehouse) - TownRules.Used(Warehouse))
                throw new InvalidOperationException("仓库空间不足，请先出售物品或扩容；本次结算已保留。");
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
                foreach (var item in selected)
                {
                    if (Rules.IsEquipment(item.kind)) nextWarehouse.equipment.Add(new StoredEquipment
                    {
                        runId = pending.run.id, item = Rules.Copy(item),
                        definition = Rules.Copy(pending.run.attributeRules.Find(item.kind))
                    });
                    else nextWarehouse.counts[(int)item.kind] = checked(nextWarehouse.counts[(int)item.kind] + 1);
                }
                nextWarehouse.town.gold = checked(nextWarehouse.town.gold + TownRules.SettlementGold(pending));
                nextWarehouse.town.reputation = checked(nextWarehouse.town.reputation + TownRules.SettlementReputation(pending));
                nextWarehouse.committedRuns.Add(pending.run.id);
                AtomicWrite(WarehousePath, nextWarehouse); Warehouse = nextWarehouse;
            }
            SaveSession(new SessionData());
            return selected;
        }
        static void ValidateTown(WarehouseData warehouse)
        {
            var town = warehouse.town;
            var equipmentIds = new HashSet<string>();
            if (warehouse.equipment == null || warehouse.equipment.Any(e => e == null || string.IsNullOrEmpty(e.runId) ||
                e.item == null || !Rules.IsEquipment(e.item.kind) || e.item.id <= 0 || e.definition == null || e.definition.kind != e.item.kind ||
                !equipmentIds.Add(e.runId + ":" + e.item.id)) || warehouse.counts.Where((n, i) => Rules.IsEquipment((ItemKind)i) && n != 0).Any())
                throw new InvalidDataException("仓库装备实例无效，原文件已保留。");
            if (town == null || town.gold < 0 || town.reputation < 0 || town.guildLevel < 1 || town.guildLevel > 3 ||
                town.warehouseUpgrades < 0 || town.warehouseUpgrades > 10 || warehouse.baseCapacity < 100 ||
                town.talents == null || town.talents.Length != TownRules.Talents.Length)
                throw new InvalidDataException("城镇养成数据无效，原文件已保留。");
            if (TownRules.Used(warehouse) > TownRules.Capacity(warehouse) || town.talents[0] != 1)
                throw new InvalidDataException("仓库容量或职业数据无效，原文件已保留。");
            for (int i = 0; i < town.talents.Length; i++)
            {
                var talent = TownRules.Talents[i];
                if (town.talents[i] < 0 || town.talents[i] > talent.maximum ||
                    (town.talents[i] > 0 && (town.guildLevel < talent.guildLevel ||
                        (talent.parent >= 0 && town.talents[talent.parent] < talent.parentRank))))
                    throw new InvalidDataException("骑士天赋数据无效，原文件已保留。");
            }
        }
        void ChangeWarehouse(Action<WarehouseData> change)
        {
            if (Session.pending != null && Session.pending.confirmed) throw new InvalidOperationException("请先完成已确认的结算。");
            var next = Rules.Copy(Warehouse); change(next); ValidateTown(next);
            AtomicWrite(WarehousePath, next); Warehouse = next;
        }
        public void LearnTalent(int id)
        {
            string reason = TownRules.TalentLock(Warehouse.town, id);
            if (reason.Length > 0) throw new InvalidOperationException(reason);
            ChangeWarehouse(next => { next.town.gold -= TownRules.TalentGold(next.town, id); next.town.talents[id]++; });
        }
        public void ResetTalents()
        {
            ChangeWarehouse(next =>
            {
                for (int i = 1; i < next.town.talents.Length; i++)
                {
                    long rank = next.town.talents[i];
                    next.town.gold = checked(next.town.gold + TownRules.Talents[i].gold * rank * (rank + 1) / 2);
                    next.town.talents[i] = 0;
                }
            });
        }
        public void UpgradeGuild()
        {
            var town = Warehouse.town;
            if (town.guildLevel >= 3) throw new InvalidOperationException("公会已达到初版最高等级。");
            if (town.gold < TownRules.UpgradeGold(town) || town.reputation < TownRules.UpgradeReputation(town))
                throw new InvalidOperationException("金币不足或声望未达要求。");
            ChangeWarehouse(next => { next.town.gold -= TownRules.UpgradeGold(next.town); next.town.guildLevel++; });
        }
        public void ExpandWarehouse()
        {
            var town = Warehouse.town;
            if (town.warehouseUpgrades >= 10 || town.gold < TownRules.ExpansionGold(town))
                throw new InvalidOperationException("金币不足或仓库已扩容至上限。");
            ChangeWarehouse(next => { next.town.gold -= TownRules.ExpansionGold(next.town); next.town.warehouseUpgrades++; });
        }
        public void SellWarehouse(ItemKind kind, int quantity)
        {
            if ((int)kind < 0 || (int)kind >= 4 || quantity <= 0 || Warehouse.counts[(int)kind] < quantity)
                throw new InvalidOperationException("出售数量无效。");
            ChangeWarehouse(next =>
            {
                next.counts[(int)kind] -= quantity;
                next.town.gold = checked(next.town.gold + (long)quantity * TownRules.SellPrices[(int)kind]);
            });
        }
        public void DonateRelic()
        {
            if (Warehouse.counts[(int)ItemKind.Relic] <= 0) throw new InvalidOperationException("仓库中没有古代遗物。");
            ChangeWarehouse(next => { next.counts[(int)ItemKind.Relic]--; next.town.reputation = checked(next.town.reputation + 10); });
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
            if (session == null || session.version < 1 || session.version > 2) throw new InvalidDataException("本局存档版本不支持。");
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
            if (run == null) throw new InvalidDataException("玩家数据不存在。");
            if (run.attributeVersion != 0 && run.attributeVersion != 1) throw new InvalidDataException("角色规则版本不支持。");
            if (run.UsesAttributes) ValidateCharacter(run);
            if (run == null || string.IsNullOrEmpty(run.id) || run.maps == null || run.maps.Count != 5 ||
                run.mapIndex < 0 || run.mapIndex >= 5 || run.health < 0 || run.health > run.MaxHealth ||
                double.IsNaN(run.health) || double.IsInfinity(run.health) || CharacterAttributes.Quantize(run.health) != run.health ||
                (!run.UsesAttributes && (run.MaxHealth < 60 || run.MaxHealth > 75)) || run.attackBonus < 0 || run.attackBonus > 3 ||
                run.defenseBonus < 0 || run.defenseBonus > 3 || run.openingEnergy < 0 || run.openingEnergy > 1 ||
                run.victoryHealing < 0 || run.victoryHealing > 3 || run.openingBlock < 0 || run.openingBlock > 6 ||
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
                    (slot.units.Count > 0 && slot.units.Count > Rules.StackLimit(slot.units[0].kind)) ||
                    (slot.units.Count > 0 && slot.units.Any(i => i.kind != slot.units[0].kind)))
                    throw new InvalidDataException("背包堆叠无效。");
                foreach (var item in slot.units) ValidateItem(item, ids);
            }
            if (run.UsesAttributes) foreach (var item in run.character.equipment) ValidateItem(item, ids);
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
                    if (enemy == null || (int)enemy.kind < 0 || (int)enemy.kind > 2 || !map.Walkable(enemy.x, enemy.y) ||
                        enemy.level < 0 || enemy.level > 3 || enemy.physicalDefense < 0 || enemy.physicalDefense > 1000 || (int)enemy.role < 0 || (int)enemy.role > 2)
                        throw new InvalidDataException("敌人数据无效。");
                foreach (var loot in map.loot)
                {
                    if (loot == null || loot.units == null || !map.Walkable(loot.x, loot.y)) throw new InvalidDataException("地面物品数据无效。");
                    if ((int)loot.appearanceKind < 0 || (int)loot.appearanceKind >= Rules.ItemNames.Length)
                        throw new InvalidDataException("物品容器外观无效。");
                    if (loot.guardEnemyId != 0 && !map.enemies.Any(e => e.id == loot.guardEnemyId && e.role == EnemyRole.ItemGuard))
                        throw new InvalidDataException("物品镇守关系无效。");
                    foreach (var item in loot.units) ValidateItem(item, ids);
                }
            }
            bool legacyBossDoor = allowLegacyPosition && run.mapIndex == 4 && run.bossDefeated &&
                run.CellX == 6 && run.CellY == 10 && run.Map.At(6, 10) == Tile.Return;
            if (float.IsNaN(run.x) || float.IsNaN(run.y) || float.IsInfinity(run.x) || float.IsInfinity(run.y) ||
                (!run.Map.Walkable(run.CellX, run.CellY) && !legacyBossDoor) ||
                (!allowLegacyPosition && !run.Map.CanStand(run.x, run.y))) throw new InvalidDataException("玩家位置无效。");
        }
        static void ValidateCharacter(RunData run)
        {
            try { CharacterAttributes.ValidateRules(run.attributeRules); }
            catch (InvalidOperationException exception) { throw new InvalidDataException(exception.Message); }
            var state = run.character;
            if (state == null || state.templateId != run.attributeRules.template.id || state.secondary < AttributeKind.Strength ||
                state.secondary > AttributeKind.Wisdom || state.secondary == run.attributeRules.template.primary ||
                state.talentStrength < 0 || state.talentStrength > 3 || state.talentAgility < 0 || state.talentAgility > 3 ||
                state.talentHealth < 0 || state.talentHealth > 15 || state.talentHealth % 5 != 0 ||
                state.equipment == null || state.equipment.Count > 3 || state.modifiers == null || state.modifiers.Count > 4 ||
                run.lootRandom == null || run.lootRandom.state == 0)
                throw new InvalidDataException("本局主副属性、装备或成长快照无效。");
            var slots = new HashSet<EquipmentSlot>();
            foreach (var item in state.equipment)
            {
                if (CharacterAttributes.EquipLock(run, item).Length > 0 || !slots.Add(run.attributeRules.Find(item.kind).slot))
                    throw new InvalidDataException("装备槽或武器绑定无效。");
            }
            var sources = new HashSet<string>();
            foreach (var modifier in state.modifiers)
            {
                if (modifier == null || !sources.Add(modifier.source) || modifier.attribute < AttributeKind.Strength || modifier.attribute > AttributeKind.Wisdom ||
                    modifier.value <= 0 || modifier.value > 1000000 ||
                    (modifier.source == "assault-potion" ? modifier.attribute != AttributeKind.Strength || modifier.value != 2 ||
                        modifier.remainingBattles < 1 || modifier.remainingBattles > 2 :
                        modifier.source != "essence-" + modifier.attribute || modifier.remainingBattles != -1))
                    throw new InvalidDataException("属性增益的来源或持续时间无效。");
            }
        }
        static void ValidateItem(ItemUnit item, HashSet<int> ids)
        {
            if (item == null || item.id <= 0 || (int)item.kind < 0 || (int)item.kind >= Rules.ItemNames.Length ||
                item.acquiredTicks < 0 || item.acquisitionOrder < 0 || !ids.Add(item.id))
                throw new InvalidDataException("物品标识或类型无效。");
        }
    }
}
