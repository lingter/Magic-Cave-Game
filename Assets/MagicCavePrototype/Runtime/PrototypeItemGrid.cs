using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicCave.Prototype
{
    public sealed partial class PrototypeGame
    {
        int selectedBagSlot = -1, dragBagSlot = -1, dragEquipmentSlot = -1, dragPressFrame = -1;
        int dragItemId;
        Vector2 dragOrigin;
        bool draggingItem;
        GamePanel dragPanel;
        RunData dragRun;
        static readonly EquipmentSlot[] visibleEquipmentSlots = { EquipmentSlot.MainHand, EquipmentSlot.Head, EquipmentSlot.Armor, EquipmentSlot.Hands,
            EquipmentSlot.Feet, EquipmentSlot.Ring1, EquipmentSlot.Ring2, EquipmentSlot.Ring3, EquipmentSlot.OffHand };
        static readonly Vector2[] equipmentPositions = { new Vector2(85, 295), new Vector2(255, 205), new Vector2(425, 295), new Vector2(85, 405),
            new Vector2(425, 405), new Vector2(85, 515), new Vector2(255, 515), new Vector2(425, 515), new Vector2(255, 605) };
        int VisibleEquipmentCount => Run.UsesAttributes && Run.character.equipmentLayoutVersion == 0 &&
            Run.character.equipment.Any(i => CharacterAttributes.EquippedSlot(Run, i) == EquipmentSlot.OffHand) ? 9 : 8;
        Rect BagCell(int index) => new Rect(585 + index % 5 * 123, 185 + index / 5 * 88, 117, 82);
        Rect EquipmentCell(int index) => new Rect(equipmentPositions[index].x, equipmentPositions[index].y, 104, index == 8 ? 35 : 84);
        string EquipmentSlotName(int index) => new[] { "武器", "头部", "胸甲", "护手", "靴子", "戒指 1", "戒指 2", "戒指 3", "旧局副手" }[index];
        bool ValidEquipmentPreview() => Run != null && Run.UsesAttributes && equipmentPreviewSlot >= 0 && equipmentPreviewSlot < Run.inventory.Count &&
            Run.inventory[equipmentPreviewSlot].units.Count == 1 && Rules.IsEquipment(Run.inventory[equipmentPreviewSlot].units[0].kind);
        void ItemSprite(Rect rect, ItemKind kind)
        {
            var icon = artSet == null ? null : artSet.ItemIcon(kind);
            if (icon == null) { Label(rect, Rules.IsEquipment(kind) ? "装" : "物", buttonStyle, Gold); return; }
            // Draw only this sprite's region when several item icons share an atlas.
            if (kind == ItemKind.CopperRing) Fill(rect, "ring icon backing", new Color(.32f, .37f, .43f));
            var region = icon.textureRect; var texture = icon.texture;
            GUI.DrawTextureWithTexCoords(rect, texture, new Rect(region.x / texture.width, region.y / texture.height, region.width / texture.width, region.height / texture.height));
        }
        void BeginItemPress(Rect rect, ItemUnit item, int bag, int equipment)
        {
            if (!GUI.enabled || item == null || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame ||
                dragPressFrame == Time.frameCount || !rect.Contains(GuiMouse)) return;
            dragPressFrame = Time.frameCount; dragBagSlot = bag; dragEquipmentSlot = equipment;
            dragItemId = item.id; dragOrigin = GuiMouse; dragPanel = Panel; dragRun = Run; draggingItem = false;
        }
        void DrawEquipmentAndBag()
        {
            if (Run.UsesAttributes)
            {
                var stats = CharacterAttributes.Calculate(Run);
                Label(new Rect(85, 163, 465, 40), "生命 " + CharacterAttributes.Number(Run.health) + "/" + stats.maxHealth + "   ATK " + stats.attack.ToString("0.##") + "   防御 " + stats.defense +
                    "\n力量 " + stats.strength + "   敏捷 " + stats.agility + "   智慧 " + stats.wisdom, smallStyle, Cyan);
            }
            Fill(new Rect(225, 315, 160, 175), "paperdoll", new Color(.07f, .115f, .17f));
            var avatar = artSet == null ? null : artSet.playerDown;
            if (avatar != null)
            {
                var t = avatar.texture; var r = avatar.textureRect;
                GUI.DrawTextureWithTexCoords(new Rect(265, 341, 80, 80), t, new Rect(r.x / t.width, r.y / t.height, r.width / t.width, r.height / t.height));
            }
            Label(new Rect(240, 440, 130, 30), "骑士", buttonStyle, Gold);
            for (int i = 0; i < VisibleEquipmentCount; i++)
            {
                var equipmentSlot = visibleEquipmentSlots[i]; var rect = EquipmentCell(i);
                var item = Run.UsesAttributes ? Run.character.equipment.Find(unit => CharacterAttributes.EquippedSlot(Run, unit) == equipmentSlot) : null;
                Box(rect);
                bool validTarget = dragBagSlot >= 0 && Run.UsesAttributes && Run.inventory[dragBagSlot].units.Count == 1 &&
                    CharacterAttributes.FitsSlot(Run.attributeRules.Find(Run.inventory[dragBagSlot].units[0].kind), equipmentSlot);
                if (draggingItem && validTarget) Fill(new Rect(rect.x, rect.y, rect.width, 3), "item drop target", Cyan);
                Label(new Rect(rect.x + 5, rect.y + 3, 96, 22), EquipmentSlotName(i), smallStyle, Cyan);
                if (item != null)
                {
                    if (i < 8) ItemSprite(new Rect(rect.x + 6, rect.y + 29, 30, 30), item.kind);
                    if (i < 8) Label(new Rect(rect.x + 40, rect.y + 25, 60, 39), Rules.ItemNames[(int)item.kind], smallStyle, Gold);
                    BeginItemPress(rect, item, -1, (int)equipmentSlot);
                    Button(new Rect(rect.x + 42, rect.y + rect.height - 26, 58, 26), "卸下", () => { var reason = UnequipSlot(equipmentSlot); Notify(reason.Length == 0 ? "装备已放入背包" : reason); }, ScreenMode == GameScreen.Explore);
                }
                else Label(new Rect(rect.x + 7, rect.y + 29, 90, 44), i == 0 ? "训练剑盾" : "空", smallStyle, new Color(.37f, .44f, .53f));
            }
            Label(new Rect(585, 153, 605, 28), "背包 " + Inventory.UsedSlots(Run) + "/20 · 点击查看 / 拖拽换装与整理", smallStyle, Cyan);
            for (int i = 0; i < Run.inventory.Count; i++)
            {
                int index = i; var slot = Run.inventory[i]; var rect = BagCell(i);
                Fill(rect, selectedBagSlot == i ? "bag selected" : "bag cell", selectedBagSlot == i ? new Color(.14f, .28f, .34f) : new Color(.085f, .13f, .19f));
                Fill(new Rect(rect.x, rect.y, rect.width, 1), "bag border", new Color(.22f, .31f, .4f));
                if (slot.units.Count == 0) { Label(rect, "·", buttonStyle, new Color(.29f, .37f, .44f)); continue; }
                var item = slot.units[0]; ItemSprite(new Rect(rect.x + 7, rect.y + 9, 29, 29), item.kind);
                Label(new Rect(rect.x + 41, rect.y + 9, 71, 43), Rules.ItemNames[(int)item.kind], smallStyle, Gold);
                Label(new Rect(rect.x + 7, rect.y + 56, 110, 22), Rules.IsEquipment(item.kind) ? "装备 · 1件" : "×" + slot.units.Count, smallStyle);
                BeginItemPress(rect, item, i, -1);
                if (Click(rect)) Queue(() => { selectedBagSlot = index; equipmentPreviewSlot = Rules.IsEquipment(item.kind) ? index : -1; dropSlot = -1; });
            }
            Label(new Rect(85, 610, 465, 30), "拖入装备 / 拖回卸下 · 剑盾为一件武器", smallStyle);
        }
        void ClearItemDrag() { dragBagSlot = dragEquipmentSlot = -1; dragRun = null; draggingItem = false; }
        void ReadItemDrag()
        {
            if (dragRun == null) return;
            var mouse = Mouse.current;
            if (Run != dragRun || Panel != dragPanel || ScreenMode != GameScreen.Explore || mouse == null) { ClearItemDrag(); return; }
            ItemUnit source = dragBagSlot >= 0 ? Run.inventory[dragBagSlot].units.FirstOrDefault() :
                Run.character.equipment.Find(i => CharacterAttributes.EquippedSlot(Run, i) == (EquipmentSlot)dragEquipmentSlot);
            if (source == null || source.id != dragItemId) { ClearItemDrag(); return; }
            if ((GuiMouse - dragOrigin).sqrMagnitude > 64) draggingItem = true;
            if (!mouse.leftButton.wasReleasedThisFrame) { if (!mouse.leftButton.isPressed) ClearItemDrag(); return; }
            if (!draggingItem) { ClearItemDrag(); return; }
            consumedClick = Time.frameCount;
            int bag = dragBagSlot, equipment = dragEquipmentSlot; var position = GuiMouse;
            ClearItemDrag();
            for (int i = 0; i < Run.inventory.Count; i++)
                if (BagCell(i).Contains(position))
                {
                    string reason = bag >= 0 ? Inventory.MoveSlot(Run, bag, i) : Inventory.UnequipTo(Run, (EquipmentSlot)equipment, i);
                    selectedBagSlot = i; dropSlot = -1;
                    equipmentPreviewSlot = Run.inventory[i].units.Count == 1 && Rules.IsEquipment(Run.inventory[i].units[0].kind) ? i : -1;
                    if (reason.Length > 0) Notify(reason); return;
                }
            for (int i = 0; i < VisibleEquipmentCount && bag >= 0; i++)
                if (EquipmentCell(i).Contains(position))
                {
                    var definition = Run.UsesAttributes ? Run.attributeRules.Find(source.kind) : null;
                    string reason = !CharacterAttributes.FitsSlot(definition, visibleEquipmentSlots[i]) ? "请拖到匹配的穿戴槽" : EquipInventory(bag, visibleEquipmentSlots[i]);
                    equipmentPreviewSlot = -1; dropSlot = -1;
                    Notify(reason.Length == 0 ? "装备已替换" : reason); return;
                }
        }
        void DrawItemDrag()
        {
            if (!draggingItem || dragRun != Run || dragPanel != Panel) return;
            var item = dragBagSlot >= 0 ? Run.inventory[dragBagSlot].units.FirstOrDefault() :
                Run.character.equipment.Find(i => CharacterAttributes.EquippedSlot(Run, i) == (EquipmentSlot)dragEquipmentSlot);
            if (item == null) return;
            var rect = new Rect(GuiMouse.x + 16, GuiMouse.y + 12, 190, 47); Box(rect);
            ItemSprite(new Rect(rect.x + 7, rect.y + 7, 32, 32), item.kind);
            Label(new Rect(rect.x + 46, rect.y + 9, 138, 34), Rules.ItemNames[(int)item.kind], smallStyle, Gold);
        }
    }
}
