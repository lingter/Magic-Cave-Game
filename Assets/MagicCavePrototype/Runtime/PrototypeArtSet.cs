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
        public Material spriteMaterial;

        public void Validate()
        {
            if (floors.Length != 3 || walls.Length != 14 || enemies.Length != 3 || loot.Length != 4 ||
                playerDown == null || playerUp == null || playerSide == null || square == null || stairs == null || spriteMaterial == null)
                throw new InvalidOperationException("原型素材配置不完整。");
            foreach (var group in new[] { floors, walls, enemies, loot })
                foreach (var sprite in group)
                    if (sprite == null || sprite.pixelsPerUnit != 16) throw new InvalidOperationException("原型素材必须使用16 PPU。");
            if (!walls[1].texture.isReadable || !walls[10].texture.isReadable)
                throw new InvalidOperationException("墙体贴图需要启用Read/Write，以组合厚墙连接。");
        }
    }
}
