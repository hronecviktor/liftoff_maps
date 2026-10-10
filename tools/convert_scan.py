#!/usr/bin/env python3
"""Converts a textured photogrammetry OBJ (RealityScan export) into an .lmap
file the Liftoff Import plugin loads: Unity axes, centred on the scan, split
into square chunks, each with a decimated render mesh and a coarser collision
mesh, plus the texture as JPEG.

    python3 tools/convert_scan.py data/pirita_full.obj dist/maps/pirita

The scan's middle becomes the origin, where Liftoff spawns the drone, and the
top surface there becomes height 0, so the drone starts on whatever is there.

.lmap layout (little endian): b"LMAP", u32 version, u32 JSON length, the
JSON header, zero padding to 4 bytes, then the arrays the header points to
(offsets relative to the end of the padding): float32 positions (xyz), float32
UVs (uv) and uint32 triangle indices.
"""
import argparse
import json
import mmap
import os
import re
import struct
import sys
import time

import meshoptimizer as mo
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
VERSION = 1


def log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def read_obj(path):
    """Reads positions, UVs and triangles. RealityScan interleaves UV and face
    blocks, so lines are classified one by one, a block of the file at a time
    so a 1 GB OBJ needs no more than a few hundred MB."""
    v, vt, faces, materials = [], [], [], set()
    with open(path, "rb") as f:
        mm = mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ)
        block = 64 << 20
        pos = 0
        while pos < len(mm):
            stop = min(len(mm), pos + block)
            if stop < len(mm):
                stop = mm.find(b"\n", stop) + 1 or len(mm)
            lines = mm[pos:stop].split(b"\n")
            pos = stop
            v.append(np.fromstring(b" ".join(l[2:] for l in lines if l.startswith(b"v ")), dtype=np.float64, sep=" "))
            vt.append(np.fromstring(b" ".join(l[3:] for l in lines if l.startswith(b"vt ")), dtype=np.float32, sep=" "))
            fs = b" ".join(l[2:] for l in lines if l.startswith(b"f ")).replace(b"/", b" ")
            faces.append(np.fromstring(fs, dtype=np.int64, sep=" "))
            materials.update(l[7:].strip() for l in lines if l.startswith(b"usemtl "))
            log(f"  read {pos / len(mm):.0%}")
        mm.close()
    if len(materials) > 1:
        sys.exit(f"{len(materials)} materials; only single-texture scans are supported")
    v = np.concatenate(v).reshape(-1, 6)[:, :3]  # x y z r g b
    vt = np.concatenate(vt).reshape(-1, 2)
    faces = np.concatenate(faces)
    if faces.size % 6:
        sys.exit("faces must be triangles with position/UV indices (f a/b c/d e/f)")
    faces = faces.reshape(-1, 6)
    log(f"{len(v):,} vertices, {len(vt):,} UVs, {len(faces):,} triangles")
    return v, vt, faces - 1  # OBJ indices are 1-based


def to_unity(v, centre_xy, ground):
    """RealityScan local (x, y, z up, right-handed) to Unity (x, y up, z,
    left-handed). Swapping y and z mirrors the space, which also turns the
    OBJ's counter-clockwise front faces into Unity's clockwise ones."""
    out = np.empty_like(v, dtype=np.float32)
    out[:, 0] = v[:, 0] - centre_xy[0]
    out[:, 1] = v[:, 2] - ground
    out[:, 2] = v[:, 1] - centre_xy[1]
    return out


def compact(indices, *arrays):
    """Keeps only the referenced vertices; returns new indices and arrays."""
    used, inverse = np.unique(indices, return_inverse=True)
    return inverse.astype(np.uint32).reshape(indices.shape), [a[used] for a in arrays]


def simplify(indices, positions, ratio, error):
    """Decimates with chunk borders locked so neighbouring chunks still meet."""
    flat = np.ascontiguousarray(indices.reshape(-1), dtype=np.uint32)
    target = max(3, int(len(flat) * ratio) // 3 * 3)
    dest = np.zeros(len(flat), dtype=np.uint32)
    n = mo.simplify(dest, flat, np.ascontiguousarray(positions, dtype=np.float32),
                    target_index_count=target, target_error=error,
                    options=mo.SIMPLIFY_LOCK_BORDER)
    out = dest[:n]
    cache = np.zeros_like(out)
    mo.optimize_vertex_cache(cache, out, vertex_count=len(positions))
    return cache.reshape(-1, 3)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("obj")
    ap.add_argument("out_dir")
    ap.add_argument("--name", help="map name (default: OBJ file name)")
    ap.add_argument("--texture", help="texture image (default: from the .mtl)")
    ap.add_argument("--chunk", type=float, default=25.0, help="chunk size in metres")
    ap.add_argument("--render-ratio", type=float, default=0.12, help="fraction of triangles kept for rendering")
    ap.add_argument("--collision-ratio", type=float, default=0.02, help="fraction kept for collisions")
    ap.add_argument("--texture-size", type=int, default=8192, help="maximum texture side")
    args = ap.parse_args()

    name = args.name or os.path.splitext(os.path.basename(args.obj))[0]
    texture = args.texture
    if not texture:
        mtl = os.path.splitext(args.obj)[0] + ".mtl"
        found = re.search(r"^map_Kd\s+(.+)$", open(mtl).read(), re.M)
        if not found:
            sys.exit(f"no map_Kd in {mtl}; pass --texture")
        texture = os.path.join(os.path.dirname(args.obj), found.group(1).strip())
    os.makedirs(args.out_dir, exist_ok=True)

    v, vt, faces = read_obj(args.obj)
    vi = faces[:, 0::2]  # position index per corner
    ti = faces[:, 1::2]  # UV index per corner

    lo, hi = v.min(0), v.max(0)
    centre = (lo[:2] + hi[:2]) / 2
    near = np.hypot(v[:, 0] - centre[0], v[:, 1] - centre[1])
    top = near < 0.5
    ground = v[top, 2].max() if top.any() else np.percentile(v[near < 3, 2], 90)
    rim = (np.minimum(v[:, 0] - lo[0], hi[0] - v[:, 0]) < 0.08 * (hi[0] - lo[0])) | \
          (np.minimum(v[:, 1] - lo[1], hi[1] - v[:, 1]) < 0.08 * (hi[1] - lo[1]))
    # the level's grid floor continues the scan outwards, just below its lowest edges
    floor = float(np.percentile(v[rim, 2], 2) - ground)
    log(f"extent {hi - lo} m, centre {centre}, spawn surface z {ground:.2f}, floor {floor:.2f} m")

    pos = to_unity(v, centre, ground)
    del v, near, top, rim

    # chunk by triangle centroid on the ground plane
    cx = pos[vi, 0].mean(1)
    cz = pos[vi, 2].mean(1)
    gx = np.floor(cx / args.chunk).astype(np.int32)
    gz = np.floor(cz / args.chunk).astype(np.int32)

    blobs = []
    offset = 0

    def put(arr):
        nonlocal offset
        data = np.ascontiguousarray(arr).tobytes()
        start = offset
        blobs.append(data)
        offset += len(data)
        return start

    chunks = []
    total_render = total_collision = 0
    for x, z in sorted(set(zip(gx.tolist(), gz.tolist()))):
        sel = (gx == x) & (gz == z)
        cvi, cti = vi[sel], ti[sel]

        # render mesh: a vertex per (position, UV) pair, so UV seams stay sharp
        key = cvi.astype(np.int64) * len(vt) + cti
        uniq, inv = np.unique(key.reshape(-1), return_inverse=True)
        rpos = pos[uniq // len(vt)]
        ruv = vt[uniq % len(vt)]
        rind = simplify(inv.reshape(-1, 3), rpos, args.render_ratio, 1.0)
        rind, (rpos, ruv) = compact(rind, rpos, ruv)

        # collision mesh: positions only, coarser
        cind, (cpos,) = compact(cvi, pos)
        cind = simplify(cind, cpos, args.collision_ratio, 1.0)
        cind, (cpos,) = compact(cind, cpos)

        bmin, bmax = rpos.min(0), rpos.max(0)
        chunks.append({
            "name": f"{x}_{z}",
            "bounds": [bmin.tolist(), bmax.tolist()],
            "render": {"vertices": len(rpos), "triangles": len(rind),
                       "positions": put(rpos), "uvs": put(ruv), "indices": put(rind)},
            "collision": {"vertices": len(cpos), "triangles": len(cind),
                          "positions": put(cpos), "indices": put(cind)},
        })
        total_render += len(rind)
        total_collision += len(cind)
        log(f"  chunk {x},{z}: {sel.sum():,} → {len(rind):,} render, {len(cind):,} collision triangles")

    log("writing texture")
    tex_name = f"{name}.jpg"
    img = Image.open(texture).convert("RGB")
    if max(img.size) > args.texture_size:
        img = img.resize((args.texture_size, args.texture_size), Image.LANCZOS)
    img.save(os.path.join(args.out_dir, tex_name), quality=90, optimize=True)

    header = {
        "format": "lmap", "version": VERSION, "name": name, "texture": tex_name,
        "floor": floor, "chunkSize": args.chunk,
        "bounds": [pos.min(0).tolist(), pos.max(0).tolist()],
        "chunks": chunks,
    }
    head = json.dumps(header, separators=(",", ":")).encode()
    with open(os.path.join(args.out_dir, f"{name}.lmap"), "wb") as f:
        f.write(b"LMAP" + struct.pack("<II", VERSION, len(head)) + head)
        f.write(b"\0" * (-(12 + len(head)) % 4))
        for b in blobs:
            f.write(b)
    log(f"done: {len(chunks)} chunks, {total_render:,} render and {total_collision:,} collision triangles")


if __name__ == "__main__":
    main()
