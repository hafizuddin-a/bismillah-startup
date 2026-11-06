// Exports the three dhikr phrases as glyph outlines (SVG path data), shaped by CoreText with Geeza Pro, the same
// font the Mac app uses. The Windows app draws these outlines instead of text, so the Arabic looks the same on
// every PC and doesn't depend on which fonts Windows has.
//
//   swiftc -O tools/export_dhikr.swift -o /tmp/export_dhikr && /tmp/export_dhikr src/Bismillah.Core/Data/dhikr.json
import AppKit
import CoreText

let phrases = ["سُبْحَانَ ٱللَّهِ وَبِحَمْدِهِ", "أَسْتَغْفِرُ ٱللَّهَ ٱلْعَظِيمَ", "ٱللَّهُمَّ صَلِّ عَلَىٰ مُحَمَّدٍ"]
let size: CGFloat = 100
let font = CTFontCreateWithName("GeezaPro" as CFString, size, nil)

func f(_ x: CGFloat) -> String { String(format: "%.2f", Double(x)).replacingOccurrences(of: ".00", with: "") }

var out: [[String: Any]] = []
for phrase in phrases {
    let text = NSAttributedString(string: phrase, attributes: [.font: font])
    let line = CTLineCreateWithAttributedString(text)
    var ascent: CGFloat = 0, descent: CGFloat = 0, leading: CGFloat = 0
    let width = CTLineGetTypographicBounds(line, &ascent, &descent, &leading)
    var d = ""
    for run in CTLineGetGlyphRuns(line) as! [CTRun] {
        let attributes = CTRunGetAttributes(run) as! [CFString: Any]
        let runFont = attributes[kCTFontAttributeName] as! CTFont
        let count = CTRunGetGlyphCount(run)
        var glyphs = [CGGlyph](repeating: 0, count: count)
        var positions = [CGPoint](repeating: .zero, count: count)
        CTRunGetGlyphs(run, CFRange(location: 0, length: 0), &glyphs)
        CTRunGetPositions(run, CFRange(location: 0, length: 0), &positions)
        for i in 0..<count {
            // y-down, baseline at 0: the outline sits in the same orientation as the page
            var transform = CGAffineTransform(a: 1, b: 0, c: 0, d: -1, tx: positions[i].x, ty: -positions[i].y)
            guard let path = CTFontCreatePathForGlyph(runFont, glyphs[i], &transform) else { continue }
            path.applyWithBlock { element in
                let p = element.pointee.points
                switch element.pointee.type {
                case .moveToPoint: d += "M\(f(p[0].x)) \(f(p[0].y))"
                case .addLineToPoint: d += "L\(f(p[0].x)) \(f(p[0].y))"
                case .addQuadCurveToPoint: d += "Q\(f(p[0].x)) \(f(p[0].y)) \(f(p[1].x)) \(f(p[1].y))"
                case .addCurveToPoint: d += "C\(f(p[0].x)) \(f(p[0].y)) \(f(p[1].x)) \(f(p[1].y)) \(f(p[2].x)) \(f(p[2].y))"
                case .closeSubpath: d += "Z"
                @unknown default: break
                }
            }
        }
    }
    out.append(["text": phrase, "path": d, "width": Double(width), "ascent": Double(ascent), "descent": Double(descent)])
}
let data = try JSONSerialization.data(withJSONObject: ["fontSize": Double(size), "font": "Geeza Pro", "phrases": out], options: [])
try data.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
print("wrote \(CommandLine.arguments[1]): \(out.map { ($0["path"] as! String).count })")
