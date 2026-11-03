using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MagicCave.Prototype
{
    public enum AttributeKind { None = -1, Strength, Agility, Wisdom }
    public enum EquipmentSlot { MainHand, OffHand, Armor, Head, Hands, Feet, Ring1, Ring2, Ring3 }
    public enum EquipmentType { Sword, Shield, LightArmor, HeavyArmor, SwordShield, Ring }
    public enum DefenseScalingSource { MainAttribute, ActualAttack }

    [Serializable]
    public sealed class DefenseCardDefinition
    {
        public CardKind card;
        public int multiplier = 10000;
        public DefenseScalingSource source;
    }

    [Serializable]
    public sealed class CharacterTemplate
    {
        public string id = "knight", name = "骑士", baseWeaponId = "training-sword";
        public AttributeKind primary = AttributeKind.Strength;
        public int health = 60, strength = 10, agility = 5, wisdom = 5;
        public int healthPerLevel = 4, primaryEveryLevels = 2, secondaryEveryLevels = 3;
        public EquipmentType[] allowedTypes = { EquipmentType.Sword, EquipmentType.Shield, EquipmentType.LightArmor, EquipmentType.HeavyArmor, EquipmentType.SwordShield, EquipmentType.Ring };
    }

    [Serializable]
    public sealed class EquipmentDefinition
    {
        public string id, ownerCharacterId;
        public ItemKind kind;
        public EquipmentSlot slot;
        public EquipmentType type;
        public int baseAttack, strengthScaling, agilityScaling, wisdomScaling;
        public int strength, agility, wisdom, health, defense;
        public decimal Attack(int str, int agi, int wis) => baseAttack + (str * (decimal)strengthScaling +
            agi * (decimal)agilityScaling + wis * (decimal)wisdomScaling) / 10000m;
    }

    [Serializable]
    public sealed class AttributeRules
    {
        public const int CurrentVersion = 2, SecondaryBonusPerPoint = 100;
        public int version = CurrentVersion, secondaryPerPoint = SecondaryBonusPerPoint;
        public CharacterTemplate template = new CharacterTemplate();
        public EquipmentDefinition[] equipment;
        public int[] cardMultipliers = { 6000, 0, 0, 15000, 3500, 0, 0, 5000, 0, 0 };
        public DefenseCardDefinition[] defenseCards;
        public EquipmentDefinition Find(ItemKind kind) => equipment.FirstOrDefault(e => e.kind == kind);
        public EquipmentDefinition Weapon(string id) => equipment.FirstOrDefault(e => e.id == id && e.slot == EquipmentSlot.MainHand);
        public DefenseCardDefinition DefenseCard(CardKind card) => (defenseCards == null || defenseCards.Length == 0 ? DefaultDefenseCards() : defenseCards).FirstOrDefault(c => c.card == card);
        public static DefenseCardDefinition[] DefaultDefenseCards() => new[]
        {
            new DefenseCardDefinition { card = CardKind.Defend },
            new DefenseCardDefinition { card = CardKind.Fortify, multiplier = 20000 },
            new DefenseCardDefinition { card = CardKind.Steady, source = DefenseScalingSource.ActualAttack }
        };
        public static AttributeRules Defaults() => new AttributeRules
        {
            defenseCards = DefaultDefenseCards(),
            equipment = new[]
            {
                Sword("training-sword", (ItemKind)(-1), 1, 5000),
                Sword("iron-sword", ItemKind.IronSword, 2, 6000),
                Sword("swift-sword", ItemKind.SwiftSword, 2, 3000, 6000),
                Sword("inscribed-sword", ItemKind.InscribedSword, 2, 3000, 0, 6000),
                Sword("refined-sword", ItemKind.RefinedSword, 3, 7000),
                new EquipmentDefinition { id = "wood-shield", kind = ItemKind.WoodShield, slot = EquipmentSlot.OffHand, type = EquipmentType.Shield, agility = 1 },
                new EquipmentDefinition { id = "leather", kind = ItemKind.LeatherArmor, slot = EquipmentSlot.Armor, type = EquipmentType.LightArmor, health = 6 },
                new EquipmentDefinition { id = "insight", kind = ItemKind.InsightArmor, slot = EquipmentSlot.Armor, type = EquipmentType.LightArmor, health = 4, wisdom = 1 },
                new EquipmentDefinition { id = "iron-armor", kind = ItemKind.IronArmor, slot = EquipmentSlot.Armor, type = EquipmentType.HeavyArmor, defense = 1 },
                // Trial values for testing the eight independent slots.
                new EquipmentDefinition { id = "iron-sword-shield", kind = ItemKind.SwordShield, slot = EquipmentSlot.MainHand, type = EquipmentType.SwordShield, ownerCharacterId = "knight", baseAttack = 2, strengthScaling = 6000, defense = 1 },
                new EquipmentDefinition { id = "leather-helm", kind = ItemKind.LeatherHelm, slot = EquipmentSlot.Head, type = EquipmentType.LightArmor, health = 2 },
                new EquipmentDefinition { id = "leather-gloves", kind = ItemKind.LeatherGloves, slot = EquipmentSlot.Hands, type = EquipmentType.LightArmor, strength = 1 },
                new EquipmentDefinition { id = "leather-boots", kind = ItemKind.LeatherBoots, slot = EquipmentSlot.Feet, type = EquipmentType.LightArmor, agility = 1 },
                new EquipmentDefinition { id = "copper-ring", kind = ItemKind.CopperRing, slot = EquipmentSlot.Ring1, type = EquipmentType.Ring, wisdom = 1 }
            }
        };
        static EquipmentDefinition Sword(string id, ItemKind kind, int attack, int str, int agi = 0, int wis = 0) =>
            new EquipmentDefinition { id = id, kind = kind, ownerCharacterId = "knight", slot = EquipmentSlot.MainHand,
                type = EquipmentType.SwordShield, baseAttack = attack, strengthScaling = str, agilityScaling = agi, wisdomScaling = wis };
    }

    [Serializable]
    public sealed class AttributeModifier
    {
        public string source;
        public AttributeKind attribute;
        public int value, remainingBattles = -1;
    }

    [Serializable]
    public sealed class CharacterState
    {
        public string templateId;
        public int equipmentLayoutVersion;
        public AttributeKind secondary = AttributeKind.None;
        public int talentStrength, talentAgility, talentHealth, talentWisdom;
        public List<ItemUnit> equipment = new List<ItemUnit>();
        public List<AttributeModifier> modifiers = new List<AttributeModifier>();
    }

    [Serializable]
    public sealed class StoredEquipment
    {
        public string runId;
        public ItemUnit item;
        public EquipmentDefinition definition;
    }

    public sealed class CharacterStats
    {
        public int strength, agility, wisdom, maxHealth, defense;
        public decimal attack, secondaryMultiplier;
        public EquipmentDefinition weapon;
        public int Value(AttributeKind kind) => kind == AttributeKind.Strength ? strength : kind == AttributeKind.Agility ? agility : wisdom;
    }

    public static class CharacterAttributes
    {
        public static string Name(AttributeKind kind) => kind == AttributeKind.Strength ? "力量" : kind == AttributeKind.Agility ? "敏捷" : kind == AttributeKind.Wisdom ? "智慧" : "未选择";
        public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        public static double Quantize(decimal value) => (double)decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        public static double Quantize(double value) => Quantize((decimal)value);

        public static void Initialize(RunData run, AttributeKind secondary, AttributeRules rules = null)
        {
            var snapshot = Rules.Copy(rules ?? PrototypeAttributeSet.LoadRules());
            UpgradeSecondaryRules(snapshot);
            UpgradeDefenseRules(snapshot);
            ValidateRules(snapshot);
            if (secondary != AttributeKind.None && (secondary < AttributeKind.Strength || secondary > AttributeKind.Wisdom || secondary == snapshot.template.primary))
                throw new InvalidOperationException("请从主属性以外的两项属性中选择副属性。");
            run.attributeVersion = 1; run.attributeRules = snapshot;
            run.character = new CharacterState { templateId = snapshot.template.id, secondary = secondary, equipmentLayoutVersion = secondary == AttributeKind.None ? 1 : 0 };
            run.lootRandom = new RandomStream(RandomStream.Derive(run.seed, 0x4c4f4f04));
            run.health = run.MaxHealth;
        }

        public static CharacterStats Calculate(RunData run, bool inBattle = false)
        {
            var state = run.character; var rules = run.attributeRules; var template = rules.template;
            var stats = new CharacterStats
            {
                strength = template.strength + state.talentStrength,
                agility = template.agility + state.talentAgility,
                wisdom = template.wisdom + state.talentWisdom,
                maxHealth = template.health + state.talentHealth + (run.level - 1) * template.healthPerLevel,
                weapon = rules.Weapon(template.baseWeaponId)
            };
            Add(stats, template.primary, run.level / template.primaryEveryLevels);
            Add(stats, state.secondary, run.level / template.secondaryEveryLevels);
            foreach (var item in state.equipment)
            {
                var definition = rules.Find(item.kind);
                stats.strength += definition.strength; stats.agility += definition.agility; stats.wisdom += definition.wisdom;
                stats.maxHealth += definition.health; stats.defense += definition.defense;
                if (definition.slot == EquipmentSlot.MainHand) stats.weapon = definition;
            }
            foreach (var modifier in state.modifiers)
                if (modifier.remainingBattles == -1 || inBattle && modifier.remainingBattles > 0) Add(stats, modifier.attribute, modifier.value);
            stats.strength = Math.Max(0, stats.strength); stats.agility = Math.Max(0, stats.agility); stats.wisdom = Math.Max(0, stats.wisdom);
            stats.maxHealth = Math.Max(1, stats.maxHealth); stats.defense = Math.Max(0, stats.defense);
            stats.attack = Math.Max(0m, stats.weapon.Attack(stats.strength, stats.agility, stats.wisdom));
            stats.secondaryMultiplier = state.secondary == AttributeKind.None ? 1m : 1m + stats.Value(state.secondary) * (decimal)SecondaryRate(rules) / 10000m;
            return stats;
        }
        public static int SecondaryRate(AttributeRules rules) => rules.version == 1 && rules.secondaryPerPoint == 1000 ?
            AttributeRules.SecondaryBonusPerPoint : rules.secondaryPerPoint;

        public static bool UpgradeSecondaryRules(AttributeRules rules)
        {
            if (rules == null || rules.version != 1 || rules.secondaryPerPoint != 1000) return false;
            rules.version = AttributeRules.CurrentVersion;
            rules.secondaryPerPoint = AttributeRules.SecondaryBonusPerPoint;
            return true;
        }
        public static bool UpgradeDefenseRules(AttributeRules rules)
        {
            if (rules == null || rules.defenseCards != null && rules.defenseCards.Length > 0) return false;
            rules.defenseCards = AttributeRules.DefaultDefenseCards();
            return true;
        }
        static void Add(CharacterStats stats, AttributeKind attribute, int amount)
        {
            if (attribute == AttributeKind.Strength) stats.strength += amount;
            else if (attribute == AttributeKind.Agility) stats.agility += amount;
            else if (attribute == AttributeKind.Wisdom) stats.wisdom += amount;
        }
        public static bool IsRingSlot(EquipmentSlot slot) => slot >= EquipmentSlot.Ring1 && slot <= EquipmentSlot.Ring3;
        public static EquipmentSlot EquippedSlot(RunData run, ItemUnit item) => item.equippedSlot >= 0 ? (EquipmentSlot)item.equippedSlot : run.attributeRules.Find(item.kind).slot;
        public static bool FitsSlot(EquipmentDefinition definition, EquipmentSlot target) => definition != null &&
            (definition.slot == target || definition.type == EquipmentType.Ring && IsRingSlot(target));
        public static string EquipLock(RunData run, ItemUnit item)
        {
            if (!run.UsesAttributes || item == null || !Rules.IsEquipment(item.kind)) return "此物品不能装备";
            var definition = run.attributeRules.Find(item.kind);
            if (run.character.equipmentLayoutVersion == 1 && definition != null && definition.slot == EquipmentSlot.OffHand) return "剑盾作为一件武器装备；此局没有独立副手槽";
            if (definition == null || !run.attributeRules.template.allowedTypes.Contains(definition.type)) return "角色不允许此装备类型";
            if (definition.slot == EquipmentSlot.MainHand && definition.ownerCharacterId != run.character.templateId) return "武器绑定其他角色模板";
            return "";
        }
        public static void FinishBattle(RunData run)
        {
            if (!run.UsesAttributes) return;
            foreach (var modifier in run.character.modifiers) if (modifier.remainingBattles > 0) modifier.remainingBattles--;
            run.character.modifiers.RemoveAll(m => m.remainingBattles == 0);
        }

        public static void ValidateRules(AttributeRules rules)
        {
            if (rules == null || rules.version != AttributeRules.CurrentVersion || rules.template == null || rules.template.id != "knight" ||
                rules.template.primary != AttributeKind.Strength || rules.template.health < 1 || rules.template.health > 100000 ||
                rules.template.strength < 0 || rules.template.strength > 1000 || rules.template.agility < 0 || rules.template.agility > 1000 ||
                rules.template.wisdom < 0 || rules.template.wisdom > 1000 || rules.template.healthPerLevel < 0 || rules.template.healthPerLevel > 100 ||
                rules.template.primaryEveryLevels < 2 || rules.template.secondaryEveryLevels < 2 || rules.template.allowedTypes == null ||
                rules.secondaryPerPoint != AttributeRules.SecondaryBonusPerPoint || rules.equipment == null || rules.equipment.Length < 2 || rules.equipment.Length > 100 ||
                rules.cardMultipliers == null || rules.cardMultipliers.Length != 10 || rules.cardMultipliers.Any(n => n < 0 || n > 100000))
                throw new InvalidOperationException("角色属性配置无效。");
            var defenseCards = rules.defenseCards == null || rules.defenseCards.Length == 0 ? AttributeRules.DefaultDefenseCards() : rules.defenseCards;
            if (defenseCards.Length != 3 || defenseCards.Any(c => c == null || !CombatNumbers.IsDefense(c.card) ||
                c.multiplier < 0 || c.multiplier > 100000 ||
                c.source != DefenseScalingSource.MainAttribute && c.source != DefenseScalingSource.ActualAttack) ||
                defenseCards.Select(c => c.card).Distinct().Count() != 3)
                throw new InvalidOperationException("防御卡牌模板配置无效。");
            var ids = new HashSet<string>(); var kinds = new HashSet<ItemKind>();
            foreach (var e in rules.equipment)
                if (e == null || string.IsNullOrEmpty(e.id) || !ids.Add(e.id) || !kinds.Add(e.kind) ||
                    ((int)e.kind != -1 && !Rules.IsEquipment(e.kind)) || (int)e.slot < 0 || (int)e.slot > 8 ||
                    (int)e.type < 0 || (int)e.type > 5 || e.baseAttack < 0 || e.baseAttack > 10000 ||
                    e.strengthScaling < 0 || e.strengthScaling > 100000 || e.agilityScaling < 0 || e.agilityScaling > 100000 ||
                    e.wisdomScaling < 0 || e.wisdomScaling > 100000 || e.strength < 0 || e.strength > 1000 ||
                    e.agility < 0 || e.agility > 1000 || e.wisdom < 0 || e.wisdom > 1000 || e.health < 0 || e.health > 10000 || e.defense < 0 || e.defense > 1000 ||
                    e.slot != EquipmentSlot.MainHand && (e.baseAttack != 0 || e.strengthScaling != 0 || e.agilityScaling != 0 || e.wisdomScaling != 0) ||
                    e.slot == EquipmentSlot.MainHand && (string.IsNullOrEmpty(e.ownerCharacterId) || e.type != EquipmentType.Sword && e.type != EquipmentType.SwordShield) ||
                    e.slot == EquipmentSlot.OffHand && e.type != EquipmentType.Shield ||
                    (e.slot == EquipmentSlot.Armor || e.slot == EquipmentSlot.Head || e.slot == EquipmentSlot.Hands || e.slot == EquipmentSlot.Feet) && e.type != EquipmentType.LightArmor && e.type != EquipmentType.HeavyArmor ||
                    IsRingSlot(e.slot) && e.type != EquipmentType.Ring)
                    throw new InvalidOperationException("装备模板配置无效。");
            var baseWeapon = rules.Weapon(rules.template.baseWeaponId);
            if (baseWeapon == null || baseWeapon.ownerCharacterId != rules.template.id || !rules.template.allowedTypes.Contains(baseWeapon.type))
                throw new InvalidOperationException("缺少本角色的基础武器模板。");
            if (rules.Find(ItemKind.IronSword)?.ownerCharacterId != rules.template.id)
                throw new InvalidOperationException("缺少本角色的保底剑盾模板。");
        }
    }

    public static class CombatNumbers
    {
        // Round only after all outgoing damage multipliers; 4.5 becomes 5.
        public static double RoundDamage(decimal value) => (double)decimal.Round(value, 0, MidpointRounding.AwayFromZero);
        public static bool IsAttack(CardKind card) => card == CardKind.Strike || card == CardKind.Heavy || card == CardKind.Double || card == CardKind.Quick || card == CardKind.Shield;
        public static bool IsDefense(CardKind card) => card == CardKind.Defend || card == CardKind.Fortify || card == CardKind.Steady;
        public static int Segments(CardKind card) => card == CardKind.Double ? 2 : 1;
        public static double Damage(RunData run, CardKind card, int spirit = 0, double block = 0, bool inBattle = true)
        {
            if (!IsAttack(card)) return 0;
            if (!run.UsesAttributes)
            {
                double basis = card == CardKind.Strike ? 6 : card == CardKind.Heavy ? 15 : card == CardKind.Double ? 4 : card == CardKind.Quick ? 5 : block;
                return RoundDamage(Math.Max(0m, (decimal)basis + spirit + (card == CardKind.Shield ? 0 : run.attackBonus)));
            }
            var stats = CharacterAttributes.Calculate(run, inBattle);
            decimal value = card == CardKind.Shield ? (decimal)block : stats.attack * run.attributeRules.cardMultipliers[(int)card] / 10000m;
            return RoundDamage(Math.Max(0m, value + spirit) * stats.secondaryMultiplier * PrototypeSettings.Power(run, card));
        }
        public static double Block(RunData run, CardKind card, bool inBattle = true)
        {
            if (!IsDefense(card)) return 0;
            int basis = card == CardKind.Defend ? 5 : card == CardKind.Fortify ? 12 : card == CardKind.Steady ? 4 : 0;
            if (!run.UsesAttributes) return basis + run.defenseBonus;
            var stats = CharacterAttributes.Calculate(run, inBattle);
            var definition = run.attributeRules.DefenseCard(card);
            decimal value = definition.source == DefenseScalingSource.ActualAttack ? stats.attack : stats.Value(run.attributeRules.template.primary);
            return RoundDamage(Math.Max(0m, value * definition.multiplier / 10000m * PrototypeSettings.Power(run, card)));
        }
        public static double Apply(double damage, int defense, ref double block, ref double health)
        {
            decimal remaining = Math.Max(0m, (decimal)damage - defense);
            decimal blocked = Math.Min((decimal)block, remaining);
            block = CharacterAttributes.Quantize((decimal)block - blocked);
            double loss = CharacterAttributes.Quantize(remaining - blocked);
            health = CharacterAttributes.Quantize(Math.Max(0m, (decimal)health - (decimal)loss));
            return loss;
        }
    }
}
