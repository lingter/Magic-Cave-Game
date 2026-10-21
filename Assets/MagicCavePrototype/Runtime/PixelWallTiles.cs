using System;
using System.Collections.Generic;
using UnityEngine;

namespace MagicCave.Prototype
{
    /// <summary>Trace the exposed wall outline, then use matching full-height artwork for every connection.</summary>
    public sealed class PixelWallTiles : IDisposable
    {
        readonly PrototypeArtSet art;
        readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();
        readonly List<UnityEngine.Object> generated = new List<UnityEngine.Object>();
        readonly Color32[] horizontal, vertical, crossing;
        static readonly Vector2Int[] Neighbors =
        {
            new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1)
        };

        public PixelWallTiles(PrototypeArtSet art)
        {
            this.art = art; horizontal = Read(art.walls[1]); vertical = Read(art.walls[10]); crossing = Read(art.walls[5]);
        }

        static Color32[] Read(Sprite sprite)
        {
            var rect = sprite.rect;
            var pixels = sprite.texture.GetPixels((int)rect.x, (int)rect.y, 16, 16);
            var result = new Color32[256];
            for (int i = 0; i < pixels.Length; i++) result[i] = pixels[i];
            return result;
        }

        public static int Mask(MapData map, int x, int y)
        {
            int mask = 0;
            for (int i = 0; i < Neighbors.Length; i++)
            {
                var cell = new Vector2Int(x, y) + Neighbors[i];
                // Doors occupy openings in the finished wall topology even while hidden by fog.
                if (map.Inside(cell.x, cell.y) && map.At(cell.x, cell.y) != Tile.Ground) mask |= 1 << i;
            }
            return mask;
        }

        public static int OutlineMask(int mask)
        {
            // A wall-to-wall connection belongs to the outline only if one of the
            // four floor cells beside that shared edge is open. Both endpoints use
            // the same four cells, so thick interiors never create phantom branches.
            int outline = 0;
            if ((mask & 1) != 0 && (mask & (2 | 8 | 16 | 128)) != (2 | 8 | 16 | 128)) outline |= 1;
            if ((mask & 2) != 0 && (mask & (1 | 4 | 16 | 32)) != (1 | 4 | 16 | 32)) outline |= 2;
            if ((mask & 4) != 0 && (mask & (2 | 8 | 32 | 64)) != (2 | 8 | 32 | 64)) outline |= 4;
            if ((mask & 8) != 0 && (mask & (1 | 4 | 64 | 128)) != (1 | 4 | 64 | 128)) outline |= 8;
            return outline;
        }

        public static int ExistingTile(int mask) => ExistingOutlineTile(OutlineMask(mask));

        static int ExistingOutlineTile(int outline)
        {
            switch (outline)
            {
                case 1: return 12;
                case 2: return 1;
                case 4: return 10;
                case 8: return 3;
                case 5: return 11;
                case 10: return 2;
                case 3: return 8;
                case 6: return 4;
                case 12: return 5;
                case 9: return 9;
                case 11: return 13;
                case 14: return 14;
                case 15: return 6;
                default: return 0;
            }
        }

        public static bool IsOutlineVisible(MapData map, int x, int y)
        {
            if (!map.Inside(x, y) || map.At(x, y) != Tile.Wall) return false;
            return map.Known(x, y) || SupplementalOutline(map, x, y) != 0;
        }

        public static int SupplementalOutline(MapData map, int x, int y)
        {
            if (!map.Inside(x, y) || map.At(x, y) != Tile.Wall || map.Known(x, y)) return 0;
            int outline = OutlineMask(Mask(map, x, y));
            int visible = 0;
            // A physical T/cross may still need a visible L between known walls.
            // Draw only proven pairs of arms, never its undiscovered branches.
            // Supplemental pieces remain decorative, not discovery anchors.
            for (int i = 4; i < Neighbors.Length; i++)
            {
                var offset = Neighbors[i];
                int arms = (offset.x > 0 ? 2 : 8) | (offset.y > 0 ? 1 : 4);
                if ((outline & arms) != arms) continue;
                int groundX = x + offset.x, groundY = y + offset.y;
                if (!map.Known(groundX, groundY) || map.At(groundX, groundY) != Tile.Ground) continue;
                if (map.Known(groundX, y) && map.At(groundX, y) == Tile.Wall &&
                    map.Known(x, groundY) && map.At(x, groundY) == Tile.Wall) visible |= arms;
            }
            return visible;
        }

        public Sprite Get(MapData map, int x, int y)
        {
            int mask = Mask(map, x, y);
            int supplemental = SupplementalOutline(map, x, y);
            return GetOutline(supplemental != 0 ? supplemental : OutlineMask(mask), mask);
        }

        public Sprite Get(int mask) => GetOutline(OutlineMask(mask), mask);

        Sprite GetOutline(int outline, int mask)
        {
            int existing = ExistingOutlineTile(outline);
            if (existing != 0) return art.walls[existing - 1];
            int key = outline != 0 ? outline : (mask & 15) == 0 ? 0 : 255;
            if (cache.TryGetValue(key, out var sprite)) return sprite;
            var pixels = new Color32[256];
            if (outline == 7 || outline == 13)
            {
                // Retain the original cross's complete horizontal arm and close only
                // its missing side with all four columns of vertical outline/shadow.
                Array.Copy(crossing, pixels, pixels.Length);
                CopyColumns(pixels, vertical, outline == 7 ? 0 : 12, 4);
            }
            else if ((mask & 15) == 0)
            {
                // A one-cell pillar has both full-height side edges and the original
                // horizontal top/face. Preserve the shadow columns, not a 3px mark.
                Array.Copy(horizontal, pixels, pixels.Length);
                CopyColumns(pixels, vertical, 0, 4); CopyColumns(pixels, vertical, 12, 4);
            }
            else
            {
                Color32 fill = vertical[8 * 16 + 8];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            }
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false) { name = "Wall topology " + key, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, false);
            sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name; generated.Add(sprite); generated.Add(texture); cache.Add(key, sprite);
            return sprite;
        }

        static void CopyColumns(Color32[] pixels, Color32[] source, int x, int width)
        {
            for (int y = 0; y < 16; y++) for (int dx = 0; dx < width; dx++)
                pixels[y * 16 + x + dx] = source[y * 16 + x + dx];
        }

        public void Dispose()
        {
            foreach (var value in generated)
                if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
            generated.Clear(); cache.Clear();
        }
    }
}
