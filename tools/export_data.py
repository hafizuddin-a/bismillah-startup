#!/usr/bin/env python3
"""Exports the bismillah outline and pen strokes from the Mac app's generated BismillahData.swift to JSON.

    python3 tools/export_data.py ../bismillah-menubar/Sources/BismillahData.swift src/Bismillah.Core/Data/bismillah.json
"""
import json, re, sys

src = open(sys.argv[1], encoding="utf-8").read()
num = lambda name: float(re.search(rf"static let {name}: Double = ([0-9.]+)", src).group(1))
outline = re.search(r'static let outline: String = "([^"]*)"', src).group(1)
strokes = []
for m in re.finditer(r"PenStroke\(points: \[([^\]]*)\], width: ([0-9.]+), start: ([0-9.]+), duration: ([0-9.]+)\)", src):
    strokes.append({"points": [float(x) for x in m.group(1).split(",")], "width": float(m.group(2)),
                    "start": float(m.group(3)), "duration": float(m.group(4))})
data = {"width": num("width"), "height": num("height"), "penEnd": num("penEnd"), "outline": outline, "strokes": strokes}
json.dump(data, open(sys.argv[2], "w"), separators=(",", ":"))
print(f"{len(strokes)} strokes, outline {len(outline)} chars -> {sys.argv[2]}")
