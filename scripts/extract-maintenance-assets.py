import argparse
import collections
import hashlib
import json
from pathlib import Path
import struct
import zlib


def read_png(path):
    blob = path.read_bytes()
    assert blob[:8] == b'\x89PNG\r\n\x1a\n'
    pos, compressed = 8, bytearray()
    while pos < len(blob):
        n = struct.unpack('>I', blob[pos:pos+4])[0]
        kind, data = blob[pos+4:pos+8], blob[pos+8:pos+8+n]
        if kind == b'IHDR':
            w, h, depth, color, _, _, interlace = struct.unpack('>IIBBBBB', data)
        if kind == b'IDAT':
            compressed.extend(data)
        pos += n+12
    assert depth == 8 and color in (2, 6) and interlace == 0
    channels = 3 if color == 2 else 4
    stride = w*channels
    raw, previous, pixels, p = zlib.decompress(compressed), bytearray(stride), bytearray(), 0
    for _ in range(h):
        filter_type, row = raw[p], bytearray(raw[p+1:p+1+stride])
        p += stride+1
        for x in range(stride):
            a, b, c = row[x-channels] if x >= channels else 0, previous[x], previous[x-channels] if x >= channels else 0
            q = a+b-c
            pa, pb, pc = abs(q-a), abs(q-b), abs(q-c)
            paeth = a if pa <= pb and pa <= pc else b if pb <= pc else c
            row[x] = (row[x]+(0, a, b, (a+b)//2, paeth)[filter_type]) & 255
        if channels == 4:
            pixels.extend(row)
        else:
            for x in range(w):
                pixels.extend(row[x*3:x*3+3]+b'\xff')
        previous = row
    return w, h, pixels


def write_png(path, image):
    w, h, p = image
    def chunk(kind, data):
        return struct.pack('>I', len(data))+kind+data+struct.pack('>I', zlib.crc32(kind+data) & 0xffffffff)
    rows = b''.join(b'\0'+bytes(p[y*w*4:(y+1)*w*4]) for y in range(h))
    png = b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))+chunk(b'sRGB', b'\0')+chunk(b'IDAT', zlib.compress(rows, 9))+chunk(b'IEND', b'')
    with path.open('xb') as f:
        f.write(png)


def crop(image, box):
    w, _, p = image
    left, top, right, bottom = box
    return right-left, bottom-top, bytearray(b''.join(p[(y*w+left)*4:(y*w+right)*4] for y in range(top, bottom)))


def resize(image, tw, th):
    w, h, p = image
    result = bytearray(tw*th*4)
    for y in range(th):
        sy = min(h-1, int((y+.5)*h/th))
        for x in range(tw):
            sx = min(w-1, int((x+.5)*w/tw))
            result[(y*tw+x)*4:(y*tw+x+1)*4] = p[(sy*w+sx)*4:(sy*w+sx+1)*4]
    return tw, th, result


def transparent(image, box, holes=()):
    w, h, p = crop(image, box)
    def background(i):
        color = p[i*4:i*4+3]
        return min(color) >= 165 and max(color)-min(color) <= 22
    seeds = list(range(w))+[(h-1)*w+x for x in range(w)]+[y*w for y in range(h)]+[y*w+w-1 for y in range(h)]
    seeds += [(y-box[1])*w+x-box[0] for x, y in holes]
    visited, queue = bytearray(w*h), collections.deque()
    for i in seeds:
        if background(i) and not visited[i]:
            visited[i] = 1
            queue.append(i)
    while queue:
        i = queue.popleft()
        x, y = i % w, i//w
        for nx, ny in ((x-1, y), (x+1, y), (x, y-1), (x, y+1)):
            if 0 <= nx < w and 0 <= ny < h:
                j = ny*w+nx
                if not visited[j] and background(j):
                    visited[j] = 1
                    queue.append(j)
    for i, clear in enumerate(visited):
        if clear:
            p[i*4:i*4+4] = b'\0'*4
    occupied = [i for i in range(w*h) if p[i*4+3]]
    bounds = (min(i % w for i in occupied), min(i//w for i in occupied), max(i % w for i in occupied)+1, max(i//w for i in occupied)+1)
    return crop((w, h, p), bounds)


def fit(image, tw, th):
    w, h, _ = image
    scale = min((tw-4)/w, (th-4)/h)
    rw, rh = max(1, round(w*scale)), max(1, round(h*scale))
    small = resize(image, rw, rh)
    result = (tw, th, bytearray(tw*th*4))
    paste(result, small, (tw-rw)//2, (th-rh)//2)
    return result


def paste(canvas, image, left, top):
    cw, ch, cp = canvas
    w, h, p = image
    for y in range(h):
        for x in range(w):
            if 0 <= left+x < cw and 0 <= top+y < ch:
                a, b = (y*w+x)*4, ((top+y)*cw+left+x)*4
                if p[a+3]:
                    cp[b:b+4] = p[a:a+4]


def mirror_tile(image, tw, th, horizontal=True):
    sw, sh = tw//2 if horizontal else tw, th//2
    _, _, p = resize(image, sw, sh)
    result = bytearray(tw*th*4)
    for y in range(th):
        for x in range(tw):
            sx, sy = min(x, tw-1-x) if horizontal else x, min(y, th-1-y)
            result[(y*tw+x)*4:(y*tw+x+1)*4] = p[(sy*sw+sx)*4:(sy*sw+sx+1)*4]
    return tw, th, result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('preview', type=Path)
    args = parser.parse_args()
    assert not args.output.exists() and not args.preview.exists(), 'Use new output folders; existing assets are never overwritten.'
    image = read_png(args.source)
    original_hash = hashlib.sha256(args.source.read_bytes()).hexdigest()
    for folder in ('road', 'vehicles', 'maintenance', 'scenery'):
        (args.output/folder).mkdir(parents=True, exist_ok=True)
    args.preview.mkdir(parents=True)
    specs = [
        ('vehicles', 'vehicle-top', (950,16,1118,282), (), (32,64)),
        ('vehicles', 'vehicle-profile', (1160,115,1510,263), (), (128,64)),
        ('maintenance', 'oil-can', (52,299,218,485), ((161,359),), (32,32)),
        ('maintenance', 'oil-spill', (255,315,476,480), (), (48,32)),
        ('maintenance', 'oil-filter', (520,307,664,485), (), (32,32)),
        ('maintenance', 'air-filter', (707,304,943,485), (), (32,32)),
        ('maintenance', 'old-tire', (980,300,1205,491), ((1090,393),), (32,32)),
        ('maintenance', 'old-brake-pad', (1250,304,1475,497), (), (32,32)),
        ('maintenance', 'old-battery', (32,525,217,710), ((120,560),), (32,32)),
        ('maintenance', 'timing-belt', (232,540,416,710), ((320,620),), (32,32)),
        ('maintenance', 'engine', (425,508,618,720), (), (48,48)),
        ('maintenance', 'transmission', (620,548,842,708), (), (48,32)),
        ('maintenance', 'radiator', (848,533,1017,710), (), (32,32)),
        ('maintenance', 'electrical', (1025,529,1198,710), ((1106,630),), (32,32)),
        ('maintenance', 'inspection', (1207,532,1335,710), (), (32,32)),
        ('maintenance', 'general-service', (1354,545,1508,710), (), (32,32)),
        ('scenery', 'tree', (244,739,465,963), (), (48,48)),
        ('scenery', 'bush', (472,797,621,933), (), (32,32)),
        ('scenery', 'road-sign', (649,757,754,966), (), (32,48)),
    ]
    assets, manifest = [], []
    for folder, name, box, holes, size in specs:
        clean = transparent(image, box, holes)
        native = fit(clean, *size)
        write_png(args.preview/(name+'-source-clean.png'), clean)
        write_png(args.output/folder/(name+'.png'), native)
        assets.append((name, native))
        manifest.append({'name': name, 'path': folder+'/'+name+'.png', 'width': size[0], 'height': size[1], 'transparent': True, 'sourceCrop': box})
    roads = {
        'road-background': mirror_tile(crop(image, (38,46,237,246)), 64,64),
        'road-edge-left': mirror_tile(crop(image, (498,45,522,246)), 16,64, False),
        'road-edge-right': mirror_tile(crop(image, (867,45,891,246)), 16,64, False),
    }
    markings = resize(crop(image, (361,99,370,154)), 2,64)
    for i in range(0, len(markings[2]), 4):
        c = markings[2][i:i+3]
        if min(c) < 150 or max(c)-min(c) > 30:
            markings[2][i:i+4] = b'\0'*4
    markings_canvas = (16,64,bytearray(16*64*4))
    paste(markings_canvas, markings, 7,0)
    roads['road-markings'] = markings_canvas
    for name in ('road-edge-left', 'road-edge-right'):
        w,h,p = roads[name]
        canvas = (w,h,bytearray(w*h*4))
        for y in range(h):
            for x in range(2,14):
                i = (y*w+x)*4
                canvas[2][i:i+4] = p[i:i+4]
        roads[name] = canvas
    for name, native in roads.items():
        write_png(args.output/'road'/(name+'.png'), native)
        assets.append((name,native))
        manifest.append({'name':name,'path':'road/'+name+'.png','width':native[0],'height':native[1],'transparent':name!='road-background','processing':'sampled from source; reflected texture for matched tile edges' if name!='road-markings' else 'isolated source lane marking'})
    scenery = {
        'grass-tile': mirror_tile(crop(image,(43,770,210,951)),32,32),
        'dashboard-landscape': resize(crop(image,(792,766,1500,955)),256,96),
    }
    for name,native in scenery.items():
        write_png(args.output/'scenery'/(name+'.png'),native)
        assets.append((name,native))
        manifest.append({'name':name,'path':'scenery/'+name+'.png','width':native[0],'height':native[1],'transparent':False})
    # The preview preserves hard pixel edges; its ordering is recorded alongside it.
    board = (1280,1600,bytearray(bytes((11,22,51,255))*(1280*1600)))
    for i,(name,native) in enumerate(assets):
        cell_x,cell_y = (i%5)*256,(i//5)*320
        panel = (244,308,bytearray(bytes((20,36,63,255))*(244*308)))
        paste(board,panel,cell_x+6,cell_y+6)
        w,h,_ = native
        scale = max(1,min(224//w,270//h))
        paste(board,resize(native,w*scale,h*scale),cell_x+(256-w*scale)//2,cell_y+(320-h*scale)//2)
    write_png(args.preview/'asset-pack-dark-preview.png',board)
    (args.preview/'preview-order.json').write_text(json.dumps([name for name,_ in assets],indent=2),encoding='utf-8')
    (args.output/'manifest.json').write_text(json.dumps({'status':'provisional','sourceSha256':original_hash,'assets':manifest},indent=2),encoding='utf-8')
    (args.preview/'source-sheet.png').write_bytes(args.source.read_bytes())
    assert hashlib.sha256(args.source.read_bytes()).hexdigest()==original_hash
    assert len(manifest)==25
    for entry in manifest:
        w,h,p = read_png(args.output/entry['path'])
        assert (w,h)==(entry['width'],entry['height'])
        if entry['transparent']:
            assert 0 in p[3::4] and 255 in p[3::4], entry['name']
    for name in ('road-background','road-edge-left','road-edge-right'):
        w,h,p=roads[name]
        assert p[:w*4]==p[(h-1)*w*4:]
    print('Created and verified 25 PNG assets; source image unchanged.')
    print(args.preview/'asset-pack-dark-preview.png')


if __name__ == '__main__':
    main()
