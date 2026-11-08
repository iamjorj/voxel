"""Makes Voxel.Mac/Assets/voxel.icns from the brand logo, sized the way macOS icons are drawn:
the rounded square is 824 px on a 1024 px canvas with a soft shadow below it (Apple's icon grid),
so Voxel sits at the same size as other apps in the Dock instead of looking oversized.

Usage: python installer/make-mac-icon.py   (from the repository root; needs Pillow)
"""
from PIL import Image, ImageFilter

logo = Image.open("docs/brand/png/voxel-1024.png").convert("RGBA").resize((824, 824), Image.LANCZOS)

canvas = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
shadow = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
shadow.paste((0, 0, 0, 128), (100, 112, 924, 936), logo.getchannel("A"))
canvas = Image.alpha_composite(canvas, shadow.filter(ImageFilter.GaussianBlur(14)))
canvas.alpha_composite(logo, (100, 100))

canvas.save("Voxel.Mac/Assets/voxel.icns")
canvas.save("docs/brand/png/voxel-mac-1024.png")
print("saved Voxel.Mac/Assets/voxel.icns")
