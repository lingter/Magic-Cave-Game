"""Export the project's single-frame RGBA Aseprite art without trimming its 16px canvas.

Run from the project root with Python + Pillow. Original Aseprite files stay untouched.
"""
from pathlib import Path
import struct
import zlib

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


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[1]
    destination = root / 'Assets/ArtValidation/Sprites'
    destination.mkdir(parents=True, exist_ok=True)
    files = sorted((root / 'Assets/ART Assets').rglob('*.aseprite'))
    for source in files:
        read_sprite(source).save(destination / (source.stem + '.png'))
    Image.new('RGBA', (16, 16), 'white').save(destination / 'Whitebox.png')
    print(f'Exported {len(files)} original sprites + Whitebox.png to {destination}')
