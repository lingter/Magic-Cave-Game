"""Export the uploaded town art losslessly; no generated replacement art."""
from pathlib import Path
import sys
sys.dont_write_bytecode = True
from export_validation_sprites import read_sprite
from PIL import Image

root = Path(__file__).resolve().parents[1]
source = root / 'Assets/ART Assets/Town'
destination = root / 'Assets/MagicCavePrototype/Resources/WorldMapArt'
destination.mkdir(parents=True, exist_ok=True)
for name in ('Town_Building_Tent', 'Town_Building_TheCave'):
    read_sprite(source / 'Building' / (name + '.aseprite')).save(destination / (name + '.png'))
atlas = read_sprite(source / 'Tile Map/Town_Map.aseprite')
for y in range(3):
    for x in range(4):
        atlas.crop((x * 16, y * 16, (x + 1) * 16, (y + 1) * 16)).save(destination / f'Town_Tile_{x}_{y}.png')
# Assemble one continuous surface before runtime scaling, using only original source pixels.
# Top and bottom use the six connected boundary cells. Interior rows repeat the
# bottom eight rows of the top boundary (no boundary pixels in the middle).
surface = Image.new('RGBA', (26 * 16, 15 * 16), 'white')
for y in range(15):
    for x in range(26):
        edge = 0 if x == 0 else 2 if x == 25 else 1
        if y in (0, 14):
            tile_y = 1 if y == 0 else 2
            cell = atlas.crop((edge * 16, tile_y * 16, (edge + 1) * 16, (tile_y + 1) * 16))
            surface.alpha_composite(cell, (x * 16, y * 16))
        else:
            strip = atlas.crop((edge * 16, 24, (edge + 1) * 16, 32))
            surface.alpha_composite(strip, (x * 16, y * 16))
            surface.alpha_composite(strip, (x * 16, y * 16 + 8))
surface.save(destination / 'Town_GrassSurface.png')
npcs = read_sprite(source / 'NPCs/Town_NPC.aseprite')
for name, bounds in [('Town_Chairman', (0, 0, 16, 16)), ('Town_Mentor', (16, 0, 32, 16)), ('Town_Warehouse', (0, 16, 32, 32))]:
    npcs.crop(bounds).save(destination / (name + '.png'))
print('Exported 17 town images and one continuous grass surface from the four uploaded Aseprite files.')
