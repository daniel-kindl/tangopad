#!/usr/bin/env python3
"""Generate Avalonia geometries from the vendored Lucide SVG subset."""

import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ICONS = ROOT / "src/Editor.App/Resources/Icons"


def geometry(element):
    attributes = element.attrib
    kind = element.tag.split("}")[-1]
    if kind == "path":
        return attributes["d"]
    if kind == "line":
        return f"M {attributes['x1']} {attributes['y1']} L {attributes['x2']} {attributes['y2']}"
    if kind in ("polyline", "polygon"):
        return "M " + attributes["points"] + (" Z" if kind == "polygon" else "")
    if kind == "circle":
        x, y, radius = (float(attributes[key]) for key in ("cx", "cy", "r"))
        return (
            f"M {x-radius} {y} a {radius} {radius} 0 1 0 {2*radius} 0 "
            f"a {radius} {radius} 0 1 0 {-2*radius} 0"
        )
    if kind == "rect":
        x = float(attributes.get("x", "0"))
        y = float(attributes.get("y", "0"))
        width = float(attributes["width"])
        height = float(attributes["height"])
        radius = float(attributes.get("rx", "0"))
        return (
            f"M {x+radius} {y} H {x+width-radius} "
            f"Q {x+width} {y} {x+width} {y+radius} V {y+height-radius} "
            f"Q {x+width} {y+height} {x+width-radius} {y+height} H {x+radius} "
            f"Q {x} {y+height} {x} {y+height-radius} V {y+radius} "
            f"Q {x} {y} {x+radius} {y} Z"
        )
    raise ValueError(f"The SVG shape is not supported: {kind}.")


def main():
    source = json.loads((ICONS / "source.json").read_text())
    resources = [
        '<ResourceDictionary xmlns="https://github.com/avaloniaui" '
        'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">'
    ]
    for key, name in source["icons"].items():
        svg = ET.parse(ICONS / (name + ".svg")).getroot()
        figures = " ".join(geometry(element) for element in svg)
        resources.append(f'  <StreamGeometry x:Key="Icon.{key}">{figures}</StreamGeometry>')
    resources.append("</ResourceDictionary>")
    (ICONS.parent / "Icons.axaml").write_text("\n".join(resources) + "\n")


if __name__ == "__main__":
    main()
