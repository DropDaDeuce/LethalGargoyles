"""
Gargoyle Thunderstore icon builder.

Composites the real gargoyle render (Screenshot_3448-Photoroom.png, which already has
a clean alpha channel) over four different backgrounds and writes 256x256 candidates
plus a 64px legibility check for each.

Run:  py thumbnail_build.py
Out:  Thumbnails/<name>_256.png  (the real icon)
      Thumbnails/<name>_64x.png  (that icon at 64px, blown back up - this is roughly
                                  what Gale and r2modman actually show in a mod list)

Tuning knobs worth touching:
  SC       how much of the frame the gargoyle fills (>1.0 means limbs bleed off the edge)
  EY       where the eyes sit in the frame, as a fraction of width/height
  ES       eye glow size
  silhouette=  0.0 keeps the stone material, 1.0 crushes to a flat black silhouette
"""
from PIL import Image, ImageFilter, ImageEnhance, ImageChops, ImageDraw
import math, os

IMG = r"D:\Projects\Lethal Company\LethalGargoyles\AssetSources\Images"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Thumbnails")
os.makedirs(OUT, exist_ok=True)
S = 1024                      # supersample working size
CUT = os.path.join(IMG, "Screenshot_3448-Photoroom.png")
AIBG = os.path.join(IMG, "71c60f4d-5812-4f46-a96f-35ad38ccf9f0.png")
OLD  = os.path.join(IMG, "LethalGargoyleThumbnail.png")

# ---------- subject ----------
def subject(scale_w, eye_at, silhouette=0.0, lift=1.0):
    """Return an S x S RGBA layer with the gargoyle placed so its eyes land at eye_at."""
    src = Image.open(CUT).convert("RGBA")
    bb = (33, 30, 864, 580)          # measured alpha bbox
    src = src.crop(bb)
    ew, eh = src.size                        # 831 x 550
    eye_rel = ((418 - 33) / ew, (251 - 30) / eh)
    tw = int(S * scale_w)
    th = int(tw * eh / ew)
    src = src.resize((tw, th), Image.LANCZOS)

    r, g, b, a = src.split()
    rgb = Image.merge("RGB", (r, g, b))
    if lift != 1.0:
        rgb = ImageEnhance.Brightness(rgb).enhance(lift)
    if silhouette > 0:                        # crush toward black
        rgb = Image.blend(rgb, Image.new("RGB", rgb.size, (6, 5, 8)), silhouette)
    src = Image.merge("RGBA", (*rgb.split(), a))

    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    px = int(S * eye_at[0] - tw * eye_rel[0])
    py = int(S * eye_at[1] - th * eye_rel[1])
    layer.paste(src, (px, py), src)
    return layer, (px, py, tw, th)

def vignette(strength=0.85, radius=0.72):
    v = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(v)
    steps = 60
    for i in range(steps, 0, -1):          # outside-in, so the bright centre lands last
        t = i / steps
        rr = int(S * (radius + 0.55 * t))
        d.ellipse([S//2 - rr, S//2 - rr, S//2 + rr, S//2 + rr],
                  fill=int(255 * (1 - t) ** 1.6))
    v = v.filter(ImageFilter.GaussianBlur(S * 0.06))
    v = v.point(lambda x: 255 - int(x * strength))
    dark = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    dark.putalpha(v)
    return dark

# ---------- backgrounds ----------
def bg_radial(inner, outer):
    b = Image.new("RGB", (S, S), outer)
    d = ImageDraw.Draw(b)
    steps = 90
    for i in range(steps, 0, -1):
        t = i / steps
        rr = int(S * 0.78 * t)
        c = tuple(int(outer[k] + (inner[k] - outer[k]) * (1 - t) ** 1.4) for k in range(3))
        d.ellipse([S//2 - rr, int(S*0.44) - rr, S//2 + rr, int(S*0.44) + rr], fill=c)
    b = b.filter(ImageFilter.GaussianBlur(S * 0.03))
    return b.convert("RGBA")

def save(im, name):
    im = im.convert("RGB")
    im.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, f"{name}_256.png"))
    im.resize((64, 64), Image.LANCZOS).resize((256, 256), Image.NEAREST)\
      .save(os.path.join(OUT, f"{name}_64x.png"))
    return name



def vgrad(top=255, bot=55, power=1.3):
    g = Image.new("L", (1, S))
    for y in range(S):
        t = (y / S) ** power
        g.putpixel((0, y), int(top + (bot - top) * t))
    return g.resize((S, S))

def glow(layer, color, spread, strength, gamma=1.6):
    a = layer.getchannel("A").filter(ImageFilter.MaxFilter(5))
    a = a.filter(ImageFilter.GaussianBlur(spread))
    a = a.point(lambda v: int(min(255, 255 * ((v/255) ** gamma) * strength)))
    g = Image.new("RGBA", (S, S), color + (0,)); g.putalpha(a)
    return g

def rim2(layer, color, width, blur, offset):
    """Directional rim: edge band, faded top-to-bottom so it reads as light, not an outline."""
    a = layer.getchannel("A")
    grown = ImageChops.offset(a.filter(ImageFilter.MaxFilter(width)), *offset)
    band = ImageChops.subtract(grown, a)
    band = ImageChops.multiply(band, a.filter(ImageFilter.MaxFilter(3)))
    band = ImageChops.multiply(band, vgrad())
    band = band.filter(ImageFilter.GaussianBlur(blur))
    r = Image.new("RGBA", (S, S), color + (0,)); r.putalpha(band)
    return r

def eyes3(halo, size):
    e = Image.new("RGBA", (S, S), (0,0,0,0)); d = ImageDraw.Draw(e)
    for cx, cy in EYE_POS:
        d.ellipse([cx-size*1.6, cy-size*1.6, cx+size*1.6, cy+size*1.6], fill=halo + (150,))
    e = e.filter(ImageFilter.GaussianBlur(size*1.1))
    m = Image.new("RGBA", (S, S), (0,0,0,0)); d = ImageDraw.Draw(m)
    for cx, cy in EYE_POS:
        d.ellipse([cx-size, cy-size*0.78, cx+size, cy+size*0.78], fill=halo + (255,))
    e.alpha_composite(m.filter(ImageFilter.GaussianBlur(size*0.42)))
    k = Image.new("RGBA", (S, S), (0,0,0,0)); d = ImageDraw.Draw(k)
    for cx, cy in EYE_POS:
        r = size*0.46
        d.ellipse([cx-r, cy-r*0.7, cx+r, cy+r*0.7], fill=(255,255,255,255))
    e.alpha_composite(k.filter(ImageFilter.GaussianBlur(size*0.2)))
    return e

def bg_facility2(blur=0.013, bright=0.86, sat=0.85):
    """Crop the corridor floor ONLY - every piece of AI-rendered text is outside this box."""
    b = Image.open(AIBG).convert("RGB").crop((456, 400, 1080, 1024)).resize((S, S), Image.LANCZOS)
    b = b.filter(ImageFilter.GaussianBlur(S*blur))
    b = ImageEnhance.Color(b).enhance(sat)
    return ImageEnhance.Brightness(b).enhance(bright).convert("RGBA")

def bg_red2():
    b = Image.open(OLD).convert("RGB").resize((S, S), Image.LANCZOS)
    b = b.filter(ImageFilter.GaussianBlur(S*0.09))
    b = ImageEnhance.Color(b).enhance(1.15)
    b = ImageEnhance.Brightness(b).enhance(0.62)
    b = ImageEnhance.Contrast(b).enhance(1.25)
    return b.convert("RGBA")

def punch(layer, contrast=1.18):
    r,g,b,a = layer.split()
    rgb = ImageEnhance.Contrast(Image.merge("RGB",(r,g,b))).enhance(contrast)
    return Image.merge("RGBA", (*rgb.split(), a))

SC, EY = 1.38, (0.50, 0.435)
def eyepos(box):
    return [(int(box[0]+box[2]*(390-33)/831), int(box[1]+box[3]*(251-30)/550)),
            (int(box[0]+box[2]*(451-33)/831), int(box[1]+box[3]*(251-30)/550))]
ES = int(S*0.014)

sub, box = subject(SC, EY, silhouette=0.48, lift=1.28); sub = punch(sub)
EYE_POS = eyepos(box)

c = bg_red2()
c.alpha_composite(glow(sub, (255,55,35), S*0.014, 1.30))
c.alpha_composite(sub)
c.alpha_composite(rim2(sub, (255,150,115), 9, 2.0, (0,-4)))
c.alpha_composite(eyes3((255,150,180), ES))
c.alpha_composite(vignette(0.92, 0.62)); save(c, "A_red")

c = bg_facility2()
c.alpha_composite(glow(sub, (140,255,110), S*0.016, 1.25))
c.alpha_composite(sub)
c.alpha_composite(rim2(sub, (185,255,155), 9, 2.0, (0,-4)))
c.alpha_composite(eyes3((200,255,170), ES))
c.alpha_composite(vignette(0.94, 0.60)); save(c, "B_facility")

c = bg_radial((30,58,26), (4,6,5))
c.alpha_composite(glow(sub, (110,255,80), S*0.017, 1.40))
c.alpha_composite(sub)
c.alpha_composite(rim2(sub, (200,255,170), 11, 2.5, (0,-5)))
c.alpha_composite(eyes3((200,255,170), ES))
c.alpha_composite(vignette(0.86, 0.64)); save(c, "C_graphic")

sub3, box3 = subject(SC, EY, silhouette=0.08, lift=1.55); sub3 = punch(sub3, 1.12)
EYE_POS = eyepos(box3)
c = bg_facility2(blur=0.016, bright=0.62, sat=0.7)
c.alpha_composite(glow(sub3, (255,70,45), S*0.015, 1.30))
c.alpha_composite(sub3)
c.alpha_composite(rim2(sub3, (255,185,145), 9, 2.0, (0,-4)))
c.alpha_composite(eyes3((255,150,180), ES))
c.alpha_composite(vignette(0.88, 0.62)); save(c, "D_stone")
print("ok")
