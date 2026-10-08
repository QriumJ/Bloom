"""Static Wanted Sans weights for Bloom (src/Bloom/Fonts).

  python tools/make_fonts.py <folder with WantedSans-Regular.ttf, -Medium.ttf, -SemiBold.ttf> [out folder]

The static TTFs come from https://github.com/wanteddev/wanted-sans (packages/wanted-sans/fonts/ttf). Each weight the
launcher uses is subset to what a launcher shows (Latin, Hangul, punctuation, arrows and a few symbols) and renamed
"Bloom Sans" so it never mixes with an installed Wanted Sans. One family covers Latin and Hangul (Hangul and Latin were
drawn together), so there is no separate KR family any more. Wanted Sans is SIL OFL 1.1 with no Reserved Font Name;
its copyright and the licence are in Fonts/OFL.txt. WPF draws only a variable font's default instance, which is why
the static files are used.
"""
import pathlib
import sys

from fontTools import subset
from fontTools.ttLib import TTFont

ROOT = pathlib.Path(__file__).resolve().parents[1]
WEIGHTS = {400: "Regular", 500: "Medium", 600: "SemiBold"}


def ranges(*pairs):
    return {c for a, b in pairs for c in range(a, b + 1)}


TEXT = ranges(
    (0x20, 0x7E), (0xA0, 0x24F),  # Latin
    (0x2000, 0x20CF), (0x2100, 0x214F), (0x2190, 0x23FF), (0x25A0, 0x25FF), (0x2600, 0x26FF),  # punctuation, arrows, symbols
    (0xAC00, 0xD7A3), (0x1100, 0x11FF), (0x3130, 0x318F), (0x3000, 0x303F), (0xFF00, 0xFFEF),  # Hangul, CJK punctuation
)


def set_names(font, family, weight):
    style = f"W{weight}"
    ps = family.replace(" ", "") + f"-{style}"
    name = font["name"]
    for rec in list(name.names):
        if rec.nameID in (1, 2, 3, 4, 6, 16, 17, 21, 22, 25):
            name.removeNames(nameID=rec.nameID)
    for nid, text in ((1, f"{family} {style}"), (2, "Regular"), (3, f"{ps};Bloom"), (4, f"{family} {style}"), (6, ps), (16, family), (17, style)):
        name.setName(text, nid, 3, 1, 0x409)
    font["OS/2"].usWeightClass = weight
    sel = font["OS/2"].fsSelection
    sel &= ~(1 << 0 | 1 << 5)  # not italic, not bold: the weight class says it
    sel |= 1 << 6  # REGULAR
    font["OS/2"].fsSelection = sel
    font["head"].macStyle = 0
    if "STAT" in font:
        del font["STAT"]


def make(src, out, weight):
    font = TTFont(src)
    opts = subset.Options()
    opts.layout_features = ["*"]
    opts.name_IDs = ["*"]
    opts.name_languages = ["*"]
    opts.notdef_outline = True
    opts.hinting = False
    sub = subset.Subsetter(opts)
    sub.populate(unicodes=TEXT)
    sub.subset(font)
    set_names(font, "Bloom Sans", weight)
    path = out / f"BloomSans-{weight}.ttf"
    font.save(path)
    return path.stat().st_size


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    src = pathlib.Path(sys.argv[1])
    out = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else ROOT / "src" / "Bloom" / "Fonts"
    out.mkdir(parents=True, exist_ok=True)
    total = 0
    for weight, style in WEIGHTS.items():
        size = make(src / f"WantedSans-{style}.ttf", out, weight)
        total += size
        print(f"{weight}: Bloom Sans {size / 1024:.0f} KB")
    print(f"total {total / 1024 / 1024:.1f} MB in {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
