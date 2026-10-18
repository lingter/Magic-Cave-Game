using System;
using System.Collections.Generic;
using UnityEngine;

namespace MagicCave.Prototype
{
    /// <summary>Reuse the numbered thin-wall tiles; compose missing thick-wall edges from their pixels.</summary>
    public sealed class PixelWallTiles : IDisposable
    {
        readonly PrototypeArtSet art;
        readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();
        readonly List<UnityEngine.Object> generated = new List<UnityEngine.Object>();
        readonly Color32[] horizontal, vertical;
        static readonly Vector2Int[] Neighbors =
        {
            new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1)
        };

        public PixelWallTiles(PrototypeArtSet art)
        {
            this.art = art; horizontal = Read(art.walls[1]); vertical = Read(art.walls[10]);
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
                if (map.Inside(cell.x, cell.y) && map.At(cell.x, cell.y) == Tile.Wall) mask |= 1 << i;
            }
            return mask;
        }

        public static int ExistingTile(int mask)
        {
            int cardinal = mask & 15;
            switch (cardinal)
            {
                case 1: return 12;
                case 2: return 1;
                case 4: return 10;
                case 8: return 3;
                case 5: return 11;
                case 10: return 2;
                case 3: return (mask & 16) == 0 ? 8 : 0;
                case 6: return (mask & 32) == 0 ? 4 : 0;
                case 12: return (mask & 64) == 0 ? 5 : 0;
                case 9: return (mask & 128) == 0 ? 9 : 0;
                case 11: return (mask & (16 | 128)) == 0 ? 13 : 0;
                case 14: return (mask & (32 | 64)) == 0 ? 14 : 0;
                case 15: return (mask & 240) == 0 ? 6 : 0;
                default: return 0;
            }
        }

        public Sprite Get(MapData map, int x, int y) => Get(Mask(map, x, y));

        public Sprite Get(int mask)
        {
            int existing = ExistingTile(mask);
            if (existing != 0) return art.walls[existing - 1];
            if (cache.TryGetValue(mask, out var sprite)) return sprite;
            var pixels = new Color32[256];
            Color32 fill = vertical[8 * 16 + 8];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            bool north = (mask & 1) != 0, east = (mask & 2) != 0, south = (mask & 4) != 0, west = (mask & 8) != 0;
            // Wall faces are always on the screen's south edge; never rotate baked shading.
            if (!north) for (int x = 0; x < 16; x++) pixels[15 * 16 + x] = horizontal[15 * 16 + x];
            if (!south) for (int y = 0; y < 10; y++) for (int x = 0; x < 16; x++) pixels[y * 16 + x] = horizontal[y * 16 + x];
            if (!west) for (int y = 0; y < 16; y++) for (int x = 0; x < 3; x++) pixels[y * 16 + x] = vertical[y * 16 + x];
            if (!east) for (int y = 0; y < 16; y++) for (int x = 13; x < 16; x++) pixels[y * 16 + x] = vertical[y * 16 + x];
            // Re-entrant corners in a thick wall keep a short outline around exposed diagonal ground.
            InnerCorner(pixels, north && east && (mask & 16) == 0, 13, 13);
            InnerCorner(pixels, south && east && (mask & 32) == 0, 13, 0);
            InnerCorner(pixels, south && west && (mask & 64) == 0, 0, 0);
            InnerCorner(pixels, north && west && (mask & 128) == 0, 0, 13);
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false) { name = "Wall topology " + mask, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name; generated.Add(sprite); generated.Add(texture); cache.Add(mask, sprite);
            return sprite;
        }

        static void InnerCorner(Color32[] pixels, bool draw, int x, int y)
        {
            if (!draw) return;
            for (int dy = 0; dy < 3; dy++) for (int dx = 0; dx < 3; dx++)
                if (dx == (x == 0 ? 2 : 0) || dy == (y == 0 ? 2 : 0)) pixels[(y + dy) * 16 + x + dx] = new Color32(0, 0, 0, 255);
        }

        public void Dispose()
        {
            foreach (var value in generated)
                if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
            generated.Clear(); cache.Clear();
        }
    }
}
