using System;
using UnityEngine;

namespace MagicCave.Prototype
{
    [CreateAssetMenu(menuName = "Magic Cave/Prototype Art Set")]
    public sealed class PrototypeArtSet : ScriptableObject
    {
        public Sprite[] floors = new Sprite[3];
        public Sprite[] walls = new Sprite[14];
        public Sprite playerDown, playerUp, playerSide, square, stairs;
        // Array order follows the existing save-compatible EnemyKind / ItemKind enum values.
        public Sprite[] enemies = new Sprite[3];
        public Sprite[] loot = new Sprite[4];
        // Equipment chest, treasure chest, supplies bag (Chest_All_Empty, left to right).
        public Sprite[] openedLoot = new Sprite[3];
        public Sprite[] itemIcons = new Sprite[16];
        public Material spriteMaterial;

        public Sprite LootSprite(GroundLoot item)
        {
            if (item.IsOpened && openedLoot != null && openedLoot.Length == 3 && openedLoot[ContainerLoot.OpenArtIndex(item)] != null)
                return openedLoot[ContainerLoot.OpenArtIndex(item)];
            if (item.loose && item.units.Count > 0 && itemIcons != null && itemIcons.Length > (int)item.units[0].kind && itemIcons[(int)item.units[0].kind] != null)
                return itemIcons[(int)item.units[0].kind];
            return loot[item.loose && item.units.Count > 0 ? Rules.ArtIndex(item.units[0].kind) : ContainerLoot.ArtIndex(item)];
        }

        public Sprite ItemIcon(ItemKind kind) => itemIcons != null && itemIcons.Length > (int)kind ? itemIcons[(int)kind] : null;

        public void Validate()
        {
            if (floors.Length != 3 || walls.Length != 14 || enemies.Length != 3 || loot.Length != 4 ||
                playerDown == null || playerUp == null || playerSide == null || square == null || stairs == null || spriteMaterial == null)
                throw new InvalidOperationException("原型素材配置不完整。");
            foreach (var group in new[] { floors, walls, enemies, loot })
                foreach (var sprite in group)
                    if (sprite == null || sprite.pixelsPerUnit != 16) throw new InvalidOperationException("原型素材必须使用16 PPU。");
            if (!walls[1].texture.isReadable || !walls[5].texture.isReadable || !walls[10].texture.isReadable)
                throw new InvalidOperationException("墙体贴图需要启用Read/Write，以组合厚墙连接。");
        }
    }
}
