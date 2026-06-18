from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageChops, ImageEnhance, ImageFilter, ImageOps


ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Post-process a bitmap into app icon PNG/ICO assets.")
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--png", required=True, type=Path)
    parser.add_argument("--ico", required=True, type=Path)
    parser.add_argument("--size", default=1024, type=int)
    parser.add_argument("--padding", default=48, type=int)
    parser.add_argument("--chroma-tolerance", default=34, type=int)
    return parser.parse_args()


def remove_chroma_background(image: Image.Image, tolerance: int) -> Image.Image:
    rgba = image.convert("RGBA")
    pixels = rgba.load()
    width, height = rgba.size
    corners = [
        pixels[0, 0],
        pixels[width - 1, 0],
        pixels[0, height - 1],
        pixels[width - 1, height - 1],
    ]
    key = max(corners, key=lambda color: max(color[:3]) - min(color[:3]))

    if max(key[:3]) - min(key[:3]) < 90:
        return rgba

    key_r, key_g, key_b, _ = key
    data = []
    for red, green, blue, alpha in rgba.getdata():
        distance = abs(red - key_r) + abs(green - key_g) + abs(blue - key_b)
        if distance <= tolerance * 3:
            data.append((red, green, blue, 0))
        else:
            data.append((red, green, blue, alpha))

    rgba.putdata(data)
    return rgba


def crop_to_content(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    bounds = alpha.getbbox()
    if bounds is None:
        return image

    return image.crop(bounds)


def fit_to_icon_canvas(image: Image.Image, size: int, padding: int) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    usable = size - padding * 2
    fitted = ImageOps.contain(image, (usable, usable), method=Image.Resampling.LANCZOS)
    x = (size - fitted.width) // 2
    y = (size - fitted.height) // 2
    canvas.alpha_composite(fitted, (x, y))
    return canvas


def polish(image: Image.Image) -> Image.Image:
    rgb = Image.new("RGBA", image.size, (0, 0, 0, 0))
    rgb.alpha_composite(image)
    rgb = ImageEnhance.Contrast(rgb).enhance(1.05)
    rgb = ImageEnhance.Sharpness(rgb).enhance(1.18)
    return rgb


def save_ico(source: Image.Image, path: Path) -> None:
    variants = []
    for size in ICO_SIZES:
        icon = source.resize(size, Image.Resampling.LANCZOS)
        if size[0] <= 32:
            icon = icon.filter(ImageFilter.UnsharpMask(radius=0.65, percent=120, threshold=2))
        variants.append(icon)

    path.parent.mkdir(parents=True, exist_ok=True)
    variants[-1].save(path, sizes=ICO_SIZES, append_images=variants[:-1])


def main() -> None:
    args = parse_args()
    image = Image.open(args.input)
    image = remove_chroma_background(image, args.chroma_tolerance)
    image = crop_to_content(image)
    image = fit_to_icon_canvas(image, args.size, args.padding)
    image = polish(image)

    args.png.parent.mkdir(parents=True, exist_ok=True)
    image.save(args.png)
    save_ico(image, args.ico)

    print(args.png.resolve())
    print(args.ico.resolve())


if __name__ == "__main__":
    main()
