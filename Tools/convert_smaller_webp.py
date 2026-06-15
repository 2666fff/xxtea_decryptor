#!/usr/bin/env python3
"""Convert one image file to WebP in place only when the result is smaller."""

import argparse
import os
import tempfile

try:
    from PIL import Image, ImageOps, UnidentifiedImageError, features
except ImportError:
    print("error pillow_missing")
    raise SystemExit(3)


def is_webp_file(path):
    try:
        with open(path, "rb") as handle:
            header = handle.read(12)
    except OSError:
        return False
    return header[:4] == b"RIFF" and header[8:12] == b"WEBP"


def has_alpha(image):
    return (
        image.mode in ("RGBA", "LA")
        or "A" in image.getbands()
        or "transparency" in image.info
    )


def prepare_for_webp(image):
    converted = ImageOps.exif_transpose(image)
    if converted.mode in ("RGB", "RGBA"):
        return converted
    if has_alpha(converted):
        return converted.convert("RGBA")
    return converted.convert("RGB")


def make_temp_path(path):
    handle = tempfile.NamedTemporaryFile(
        dir=os.path.dirname(path) or ".",
        prefix="." + os.path.basename(path) + ".",
        suffix=".webp.tmp",
        delete=False,
    )
    temp_path = handle.name
    handle.close()
    return temp_path


def convert_file(path, quality, method):
    if not os.path.isfile(path):
        print("error file_not_found")
        return 2

    if not features.check("webp"):
        print("error webp_not_supported")
        return 3

    if is_webp_file(path):
        print("skipped already_webp")
        return 0

    original_size = os.path.getsize(path)
    temp_path = make_temp_path(path)
    try:
        try:
            with Image.open(path) as image:
                if getattr(image, "is_animated", False):
                    print("skipped animated")
                    return 0

                converted = prepare_for_webp(image)
                save_args = {
                    "format": "WEBP",
                    "quality": quality,
                    "method": method,
                    "lossless": False,
                }
                if converted.mode == "RGBA":
                    save_args["alpha_quality"] = quality

                icc_profile = image.info.get("icc_profile")
                if icc_profile:
                    save_args["icc_profile"] = icc_profile

                converted.save(temp_path, **save_args)
        except UnidentifiedImageError:
            print("skipped non_image")
            return 0
        except OSError as exc:
            message = str(exc).lower()
            if "cannot identify image file" in message:
                print("skipped non_image")
                return 0
            print("error " + str(exc).replace("\r", " ").replace("\n", " "))
            return 4

        webp_size = os.path.getsize(temp_path)
        if webp_size >= original_size:
            print("kept_not_smaller %d %d" % (original_size, webp_size))
            return 0

        try:
            os.chmod(temp_path, os.stat(path).st_mode)
        except OSError:
            pass
        os.replace(temp_path, path)
        temp_path = None
        print("converted %d %d" % (original_size, webp_size))
        return 0
    finally:
        if temp_path and os.path.exists(temp_path):
            try:
                os.remove(temp_path)
            except OSError:
                pass


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("path")
    parser.add_argument("--quality", type=int, default=85)
    parser.add_argument("--method", type=int, default=6, choices=range(0, 7))
    args = parser.parse_args()

    if args.quality < 0 or args.quality > 100:
        print("error invalid_quality")
        return 2

    return convert_file(args.path, args.quality, args.method)


if __name__ == "__main__":
    raise SystemExit(main())
