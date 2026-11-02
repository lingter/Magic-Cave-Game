"""Export the project's single-frame RGBA Aseprite art without trimming its 16px canvas.

Run from the project root with Python + Pillow. Original Aseprite files stay untouched.
"""
from pathlib import Path
import struct
import zlib
import argparse

from PIL import Image


def read_sprite(path):
    data = path.read_bytes()
    _, magic, frames, width, height, depth = struct.unpack_from('<I5H', data)
    if magic != 0xA5E0 or frames != 1 or depth != 32:
        raise ValueError(f'Expected one RGBA frame: {path}')
    count = struct.unpack_from('<H', data, 134)[0]
    count = struct.unpack_from('<I', data, 140)[0] or count
    layers, cels = [], []
    offset = 144
    for _ in range(count):
        size, kind = struct.unpack_from('<IH', data, offset)
        chunk = data[offset + 6:offset + size]
        if kind == 0x2004:
            flags, layer_type, _, _, _, blend, opacity = struct.unpack_from('<6HB', chunk)
            if layer_type != 0 or blend != 0:
                raise ValueError(f'Unsupported layer: {path}')
            layers.append((flags, opacity))
        elif kind == 0x2005:
            layer, x, y, opacity, cel_type = struct.unpack_from('<HhhBH', chunk)
            if cel_type not in (0, 2):
                raise ValueError(f'Unsupported cel: {path}')
            w, h = struct.unpack_from('<HH', chunk, 16)
            raw = chunk[20:] if cel_type == 0 else zlib.decompress(chunk[20:])
            cels.append((layer, x, y, opacity, Image.frombytes('RGBA', (w, h), raw)))
        offset += size
    result = Image.new('RGBA', (width, height))
    for layer, x, y, opacity, cel in sorted(cels, key=lambda item: item[0]):
        flags, layer_opacity = layers[layer]
        if flags & 1:
            cel.putalpha(cel.getchannel('A').point(lambda a: round(a * opacity * layer_opacity / 65025)))
            result.alpha_composite(cel, (x, y))
    return result


def export_container_art(root, destination):
    """Stage new art outside Assets; import/configure it through Unity MCP."""
    destination.mkdir(parents=True, exist_ok=True)
    empty = read_sprite(root / 'Assets/ART Assets/Proactive Object/Chest/Chest_All_Empty.aseprite')
    for index, name in enumerate(('Equipment', 'Treasure', 'Supplies')):
        empty.crop((index * 16, 0, (index + 1) * 16, 16)).save(destination / f'Chest_Open_{name}.png')
    equipment = read_sprite(root / 'Assets/ART Assets/Equipmen/Equipment.aseprite')
    for name, column, row in (('Sword_Iron', 1, 0), ('Sword_Swift', 2, 0), ('Sword_Inscribed', 2, 0),
                              ('Sword_Refined', 3, 0), ('Shield_Wood', 0, 0), ('Armor_Leather', 0, 1),
                              ('Armor_Insight', 2, 1), ('Armor_Iron', 1, 1),
                              ('SwordShield', 1, 0), ('Helmet', 0, 2), ('Gloves', 0, 4),
                              ('Boots', 0, 3), ('Ring', 1, 5)):
        x, y = 16 + column * 32, 16 + row * 32
        equipment.crop((x, y, x + 16, y + 16)).save(destination / f'Item_{name}.png')


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument('--container-art-destination', type=Path,
                        help='Stage container and equipment PNGs in an external directory for Unity MCP import.')
    args = parser.parse_args()
    if args.container_art_destination:
        export_container_art(root, args.container_art_destination)
        print(f'Staged container/equipment art in {args.container_art_destination}')
        raise SystemExit(0)
    destination = root / 'Assets/ArtValidation/Sprites'
    destination.mkdir(parents=True, exist_ok=True)
    files = sorted((root / 'Assets/ART Assets').rglob('*.aseprite'))
    for source in files:
        read_sprite(source).save(destination / (source.stem + '.png'))
    Image.new('RGBA', (16, 16), 'white').save(destination / 'Whitebox.png')
    print(f'Exported {len(files)} original sprites + Whitebox.png to {destination}')
