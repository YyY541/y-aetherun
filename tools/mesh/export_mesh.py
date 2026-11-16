#!/usr/bin/env python3
"""把 naiwa.glb 导成运行时吃的 NWM1 网格。

用法：
    python3 export_mesh.py naiwa.glb --out-dir ../../src/Aetherun.Desktop/Assets/frog

依赖：numpy、Pillow

NWM1 格式（小端）：
    magic  4 字节  b"NWM1"
    nVert  uint32
    nTri   uint32
    verts  float32 × 3 × nVert    顶点坐标，已归一化：脚在 y=0、水平居中、身高 = 1.0
    cols   float32 × 3 × nVert    每顶点颜色，从原贴图按 UV 采样
    tris   uint32 × 3 × nTri      三角索引

为什么要离线烘：运行时只做投影 + 光栅，不想背一个 glb 解析器，也不想依赖贴图解码。
顶点颜色是逐顶点从贴图采的，网格足够密时观感与贴图几乎一致。

三个密度档用 --cell 控制：数值越大顶点越密（格子越细）。
"""

from __future__ import annotations

import argparse
import json
import os
import struct
import sys

import numpy as np
from PIL import Image

# 三档默认密度：写回 Assets/frog 用的就是这套
DEFAULT_TIERS = {
    "naiwa_hi.bin": 120,
    "naiwa_mid.bin": 64,
    "naiwa_lo.bin": 30,
}


def load_glb(path: str):
    """解析单 mesh、单 primitive 的 glb。够用就行，不追求通用。"""
    data = open(path, "rb").read()
    off = 12
    chunks = []
    while off < len(data):
        clen, ctype = struct.unpack("<II", data[off:off + 8])
        chunks.append((ctype, clen, off + 8))
        off += 8 + clen
    if len(chunks) < 2:
        raise SystemExit("glb 结构异常：找不到 JSON 或 BIN 块")

    gltf = json.loads(data[chunks[0][2]:chunks[0][2] + chunks[0][1]].decode("utf-8"))
    buf = data[chunks[1][2]:chunks[1][2] + chunks[1][1]]

    def accessor(i):
        a = gltf["accessors"][i]
        bv = gltf["bufferViews"][a["bufferView"]]
        comp = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a["componentType"]]
        ncomp = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
        start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        arr = np.frombuffer(buf, dtype=comp, count=a["count"] * ncomp, offset=start)
        return arr.reshape(a["count"], ncomp) if ncomp > 1 else arr

    prim = gltf["meshes"][0]["primitives"][0]
    pos = accessor(prim["attributes"]["POSITION"]).astype(np.float64)
    uv = accessor(prim["attributes"]["TEXCOORD_0"]).astype(np.float32)
    idx = accessor(prim["indices"]).reshape(-1, 3).astype(np.int64)

    # 贴图：primitive 里给的 baseColorTexture 走 textures/images 链
    image_index = None
    mat = prim.get("material")
    if mat is not None:
        bct = gltf["materials"][mat].get("pbrMetallicRoughness", {}).get("baseColorTexture")
        if bct is not None:
            image_index = gltf["textures"][bct["index"]].get("source")

    # glTF 的节点可能带旋转（这个模型就是靠绕 X +90° 的四元数从 Z-up 立起来的），
    # 导出时必须先把这一步烘进去，否则生成的网格是躺着的。
    node = gltf["nodes"][0]
    rot = node.get("rotation", [0.0, 0.0, 0.0, 1.0])
    qx, qy, qz, qw = rot
    rot_mat = np.array([
        [1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy - qz * qw), 2 * (qx * qz + qy * qw)],
        [2 * (qx * qy + qz * qw), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz - qx * qw)],
        [2 * (qx * qz - qy * qw), 2 * (qy * qz + qx * qw), 1 - 2 * (qx * qx + qy * qy)],
    ], dtype=np.float64)

    return pos, uv, idx, rot_mat, image_index, gltf, chunks


def load_texture(image_index, gltf, chunks, glb_path):
    """从 glb 的 BIN 块里抠出贴图字节，交给 Pillow 解码。"""
    if image_index is None:
        return None
    img_def = gltf["images"][image_index]
    if "bufferView" in img_def:
        bv = gltf["bufferViews"][img_def["bufferView"]]
        start = chunks[1][2] + bv.get("byteOffset", 0)
        raw = open(glb_path, "rb").read()[start:start + bv["byteLength"]]
        import io
        return np.array(Image.open(io.BytesIO(raw)).convert("RGB")).astype(np.float32) / 255.0
    if "uri" in img_def:
        p = os.path.join(os.path.dirname(glb_path), img_def["uri"])
        return np.array(Image.open(p).convert("RGB")).astype(np.float32) / 255.0
    return None


def build(up, col, idx, cell, mx, mn, out_path):
    """按格子聚类顶点 → 去重三角 → 归一化 → 写 NWM1。"""
    scale = cell / np.maximum(mx - mn, 1e-9)
    key = np.floor((up - mn) * scale).astype(np.int64)
    key -= key.min(0)
    dims = key.max(0) + 1
    flat = (key[:, 0] * dims[1] + key[:, 1]) * dims[2] + key[:, 2]

    uniq, inv = np.unique(flat, return_inverse=True)
    n = len(uniq)

    vsum = np.zeros((n, 3))
    csum = np.zeros((n, 3))
    cnt = np.zeros(n)
    np.add.at(vsum, inv, up)
    np.add.at(csum, inv, col)
    np.add.at(cnt, inv, 1)
    cnt = np.maximum(cnt, 1)[:, None]
    verts = vsum / cnt
    cols = csum / cnt

    tri = inv[idx]
    ok = (tri[:, 0] != tri[:, 1]) & (tri[:, 1] != tri[:, 2]) & (tri[:, 0] != tri[:, 2])
    tri = tri[ok]
    sorted_tri = np.sort(tri, axis=1)
    _, keep = np.unique(sorted_tri, axis=0, return_index=True)
    tri = tri[np.sort(keep)]

    # 归一化：水平居中、脚贴 y=0、身高压到 1.0（运行时按像素高度缩放即可）
    verts = verts - np.array([
        (verts[:, 0].min() + verts[:, 0].max()) / 2,
        verts[:, 1].min(),
        (verts[:, 2].min() + verts[:, 2].max()) / 2,
    ])
    verts = verts / verts[:, 1].max()

    # 颜色略微提亮：贴图本身偏暗，直接照搬在深色界面上糊成一团
    cols = (np.clip(cols, 0, 1) ** 0.92).astype(np.float32)

    blob = bytearray()
    blob += b"NWM1"
    blob += struct.pack("<II", n, len(tri))
    blob += verts.astype(np.float32).tobytes()
    blob += cols.tobytes()
    blob += tri.astype(np.uint32).tobytes()
    open(out_path, "wb").write(bytes(blob))

    radius = float(np.sqrt(verts[:, 0] ** 2 + verts[:, 2] ** 2).max())
    print(f"{out_path}: {n} 顶点 / {len(tri)} 三角 / {len(blob)} B / 半径 {radius:.4f}")


def main():
    ap = argparse.ArgumentParser(description="glb → NWM1 网格")
    ap.add_argument("glb", help="输入 glb 路径")
    ap.add_argument("--out-dir", default=".", help="输出目录")
    ap.add_argument("--cell", type=int, default=0, help="只导一档时指定格子密度")
    ap.add_argument("--name", default="naiwa.bin", help="只导一档时的文件名")
    args = ap.parse_args()

    pos, uv, idx, rot_mat, image_index, gltf, chunks = load_glb(args.glb)
    tex = load_texture(image_index, gltf, chunks, args.glb)

    up = pos @ rot_mat.T          # 应用节点旋转 → 站立坐标系
    if tex is None:
        col = np.full((len(pos), 3), 0.72, dtype=np.float32)
        print("! 模型没有 baseColorTexture，退化成纯色")
    else:
        th, tw, _ = tex.shape
        u = np.clip((uv[:, 0] % 1.0) * tw, 0, tw - 1).astype(np.int32)
        v = np.clip((uv[:, 1] % 1.0) * th, 0, th - 1).astype(np.int32)
        col = tex[v, u]

    mn, mx = up.min(0), up.max(0)
    print(f"源网格 {len(pos)} 顶点 / {len(idx)} 三角，站立 bbox {mn} .. {mx}")

    os.makedirs(args.out_dir, exist_ok=True)
    if args.cell > 0:
        build(up, col, idx, args.cell, mx, mn, os.path.join(args.out_dir, args.name))
    else:
        for name, cell in DEFAULT_TIERS.items():
            build(up, col, idx, cell, mx, mn, os.path.join(args.out_dir, name))


if __name__ == "__main__":
    sys.exit(main())
