"""Deterministic UI slice export/alpha validation; artwork is from image_gen.

Run beside generation-config.json. No game-project files are modified.
"""
import json
import shutil
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
C = json.loads((ROOT / "generation-config.json").read_text())
RESAMPLE = Image.Resampling.LANCZOS


def save_json(name, obj):
    (ROOT / name).write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n")


def sprite_export(source, size, padding=3):
    im = Image.open(source).convert("RGBA")
    data = np.array(im)
    # Generated RGB may contain haze in fully transparent pixels. Do not color-key
    # white petals/pearls: clean only negligible alpha, retaining antialiased edges.
    a = data[:, :, 3].astype(float)
    a = np.clip((a - 4) * 255 / 249, 0, 255).astype("uint8")
    data[:, :, 3] = a
    data[a == 0, :3] = 0
    im = Image.fromarray(data)
    core = Image.fromarray((a >= 32).astype("uint8") * 255).getbbox()
    assert core, str(source)
    l, t, r, b = core
    crop = (max(0, l - 2), max(0, t - 2), min(im.width, r + 2), min(im.height, b + 2))
    im = im.crop(crop)
    if padding == 0:
        # Internal progress fill is a stretchable strip. Cover its exact runtime
        # rect so Filled + marker share the same 0..1 line without invisible ends.
        return im.resize(size, RESAMPLE), crop
    out = Image.new("RGBA", size)
    max_w, max_h = size[0] - padding * 2, size[1] - padding * 2
    scale = min(max_w / im.width, max_h / im.height)
    im = im.resize((round(im.width * scale), round(im.height * scale)), RESAMPLE)
    out.alpha_composite(im, ((size[0] - im.width) // 2, (size[1] - im.height) // 2))
    return out, crop


def text_at(im, spec, content):
    d = ImageDraw.Draw(im)
    f = ImageFont.truetype(C["preview_font"], spec["font_size"])
    x, y, w, h = spec["rect"]
    d.text((x + w / 2, y + h / 2), content, font=f, fill=spec["color"], anchor="mm", stroke_width=1, stroke_fill=spec["color"])


def compose(background, sprites, progress, text=True):
    im = background.convert("RGBA")
    for item in C["assets"][:3]:
        im.alpha_composite(sprites[item["id"]], tuple(item["xy"]))
    fill = C["assets"][3]
    width = round(sprites[fill["id"]].width * progress)
    if width:
        im.alpha_composite(sprites[fill["id"]].crop((0, 0, width, fill["size"][1])), tuple(fill["xy"]))
        marker = C["marker"]
        x = marker["x_range"][0] + progress * (marker["x_range"][1] - marker["x_range"][0])
        im.alpha_composite(sprites["loading_progress_marker"], (round(x - marker["pivot_px"][0]), marker["center_y"] - marker["pivot_px"][1]))
    if text:
        text_at(im, C["status"], C["status"]["normal"])
        text_at(im, C["percentage"], str(round(progress * 100)) + "%")
    return im.convert("RGB")


def main():
    for folder in ["assets", "sources", "previews", "checks", "atlas"]:
        (ROOT / folder).mkdir(exist_ok=True)
    shutil.copy2(C["source_image"], ROOT / "sources/reference-original.png")
    source = Image.open(C["source_image"]).convert("RGB")
    source.resize(tuple(C["canvas"]), RESAMPLE).save(ROOT / "previews/reference-750x1334.png")
    shutil.copy2(C["background"], ROOT / "sources/background-reconstructed.png")
    background = Image.open(C["background"]).convert("RGB").resize(tuple(C["canvas"]), RESAMPLE)
    # Preserve exact source pixels across the unoccluded central scene, including
    # the foreground platform bow that the inpainting model inadvertently changed.
    # Only the two occluded UI regions depend on generated reconstruction.
    original_scaled = source.resize(tuple(C["canvas"]), RESAMPLE)
    mask = np.zeros((1334, 750), dtype="uint8")
    mask[430:1017, :] = 255
    for j in range(12):
        mask[418+j, :] = round(255 * (j+1)/12)
        mask[1017+j, :] = round(255 * (12-j)/12)
    background = Image.composite(original_scaled, background, Image.fromarray(mask))
    background.save(ROOT / "assets/loading_background.png", optimize=True)
    sprites, records, checks = {}, [], {}
    for i, item in enumerate(C["assets"]):
        raw = Path(C["generation_root"]) / item["source"]
        shutil.copy2(raw, ROOT / ("sources/" + item["id"] + "-generated.png"))
        sprite, crop = sprite_export(raw, tuple(item["size"]), padding=0 if "fill" in item["id"] else 3)
        sprite.save(ROOT / ("assets/" + item["id"] + ".png"), optimize=True)
        sprites[item["id"]] = sprite
        alpha = np.array(sprite.getchannel("A"))
        assert (alpha == 0).any() and (alpha > 0).any(), item["id"]
        assert (alpha == 255).any(), item["id"]
        checks[item["id"]] = {"size": list(sprite.size), "alpha_min": int(alpha.min()), "alpha_max": int(alpha.max()), "zero_alpha_pixels": int((alpha == 0).sum()), "visible_pixels": int((alpha > 0).sum()), "alpha_bbox": sprite.getchannel("A").getbbox(), "passed": True}
        records.append({"id": item["id"], "file": "assets/" + item["id"] + ".png", "position_xy": item["xy"], "size": item["size"], "layer": i + 1, "contains_text": item["contains_text"], "role": item["role"], "source_crop_xyxy": crop, "nine_slice_lbrt": [0, 0, 0, 0], "preserve_aspect": True})

    for progress in [0, 0.65, 1]:
        compose(background, sprites, progress).save(ROOT / f"previews/assembled-{round(progress*100):03}-750x1334.png", optimize=True)
    compose(background, sprites, 0.65, False).save(ROOT / "previews/assembled-no-dynamic-text.png", optimize=True)
    proof = Image.new("RGB", (1500, 1334))
    proof.paste(source.resize((750, 1334), RESAMPLE), (0, 0))
    proof.paste(compose(background, sprites, 0.65), (750, 0))
    proof.save(ROOT / "previews/reference-vs-reassembled.png", optimize=True)

    # Separate light/dark alpha contact sheets are never imported as sprites.
    sheet = Image.new("RGB", (1440, 5 * 340), (248, 244, 236))
    draw = ImageDraw.Draw(sheet)
    draw.rectangle((720, 0, 1440, sheet.height), fill=(30, 34, 48))
    font = ImageFont.truetype(C["preview_font"], 20)
    for i, item in enumerate(C["assets"]):
        s = sprites[item["id"]]
        for left in [0, 720]:
            draw.text((left + 20, i * 340 + 10), item["id"], font=font, fill=(70, 45, 50) if left == 0 else (255, 240, 220))
            sheet.paste(s, (left + (720 - s.width)//2, i * 340 + 36 + (304-s.height)//2), s)
    sheet.save(ROOT / "checks/alpha-light-dark.png", optimize=True)

    # Optional 1024 atlas: control sprites only; no background; no rotation.
    atlas = Image.new("RGBA", (1024, 1024))
    atlas_frames = {}
    x, y, row_h = 4, 4, 0
    for item in C["assets"]:
        s = sprites[item["id"]]
        if x + s.width + 4 > 1024:
            x, y, row_h = 4, y + row_h + 8, 0
        assert y + s.height + 4 <= 1024
        atlas.alpha_composite(s, (x, y))
        atlas_frames[item["id"]] = {"xywh": [x, y, s.width, s.height], "rotated": False, "trimmed": False}
        x += s.width + 8
        row_h = max(row_h, s.height)
    atlas.save(ROOT / "atlas/loading-controls-1024.png", optimize=True)
    save_json("atlas/frames.json", {"size": [1024, 1024], "origin": "top-left", "padding": 8, "extrude": 0, "rotation": False, "background_excluded": True, "frames": atlas_frames, "note": "Optional art atlas; use Unity SpriteAtlas or standalone sprites, not both. No extrusion: do not use mipmaps; for compressed build regenerate with 2px extrusion."})
    manifest = {"version": 1, "canvas": C["canvas"], "source_size": list(source.size), "coordinate_origin": "top-left", "background": {"file": "assets/loading_background.png", "size": C["canvas"], "layer": 0, "adaptation": "uniform scale to cover, centered crop; never nonuniform stretch"}, "assets": records, "runtime_text": {"status": C["status"], "percentage": C["percentage"]}, "progress": {"range": [0, 1], "fill": "Image.Type.Filled, Horizontal, OriginLeft; no scaleX", "marker": C["marker"], "percentage": "RoundToInt(Clamp01(p)*100) + %", "preview": 0.65}, "adaptation": {"logo": "top-center, below system safe region", "loading_group": "bottom-center, all panel/track/fill/marker/text in one parent; design bottom inset 79", "scale_reference": "Respect project Canvas 1080x1920. Best use a local 750x1334 art space / uniform 1080/750 scale, without changing global scaler.", "safe_area": "Runtime platform safe area and capsule rect; keep background full bleed. At 750x1334 reproduce listed positions; adapt only group anchors on tall/short screens."}, "import": {"texture_type": "Sprite Single", "mesh_type": "FullRect", "alpha_is_transparency": True, "mipmaps": False, "wrap": "Clamp", "filter": "Bilinear", "max_texture_size_background": 2048, "max_texture_size_controls": 1024}, "font_note": "Logo/tagline are AI-rendered art, not an identifiable font. Preview dynamic text uses macOS STHeiti Medium (not redistributed). Game must use its licensed CJK font or approved open-source CJK font, not copy macOS fonts.", "source_note": C["method"], "nine_slice_note": "All zero: fixed proportion ornate panel, pearls and central bow would distort under nine-slice."}
    save_json("layout.json", manifest)
    save_json("checks/asset-validation.json", {"canvas_verified": background.size == (750,1334), "transparent_assets": checks, "atlas_size": list(atlas.size), "visual_review": "pending", "engine_integration": "not yet submitted"})
    with zipfile.ZipFile(ROOT.parent / (ROOT.name + ".zip"), "w", zipfile.ZIP_DEFLATED) as z:
        for p in sorted(ROOT.rglob("*")):
            if p.is_file() and "__pycache__" not in p.parts:
                z.write(p, p.relative_to(ROOT.parent))
    print(json.dumps({"output": str(ROOT), "assets": len(sprites)+1, "canvas": C["canvas"], "alpha_checks": checks}, ensure_ascii=False))


if __name__ == "__main__":
    main()
