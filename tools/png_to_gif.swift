// Joins numbered PNG frames into a looping GIF. Each line of <name>.txt in the frames folder is "<file> <seconds>".
//   png_to_gif <frames folder> <name> <output.gif>
import Foundation
import ImageIO
import UniformTypeIdentifiers

let args = CommandLine.arguments
guard args.count == 4 else { fatalError("usage: png_to_gif <frames folder> <name> <output.gif>") }
let folder = URL(fileURLWithPath: args[1])
let lines = try String(contentsOf: folder.appendingPathComponent("\(args[2]).txt"), encoding: .utf8)
    .split(whereSeparator: \.isNewline).map(String.init)
guard let gif = CGImageDestinationCreateWithURL(URL(fileURLWithPath: args[3]) as CFURL, UTType.gif.identifier as CFString, lines.count, nil)
else { fatalError("can't create \(args[3])") }
CGImageDestinationSetProperties(gif, [kCGImagePropertyGIFDictionary as String: [kCGImagePropertyGIFLoopCount as String: 0]] as CFDictionary)
var total = 0.0
for line in lines {
    let parts = line.split(separator: " ")
    guard parts.count == 2, let delay = Double(parts[1]),
          let source = CGImageSourceCreateWithURL(folder.appendingPathComponent(String(parts[0])) as CFURL, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else { fatalError("bad frame line: \(line)") }
    total += delay
    CGImageDestinationAddImage(gif, image, [kCGImagePropertyGIFDictionary as String: [kCGImagePropertyGIFDelayTime as String: delay]] as CFDictionary)
}
guard CGImageDestinationFinalize(gif) else { fatalError("couldn't write \(args[3])") }
print("wrote \(args[3]): \(lines.count) frames, \(String(format: "%.1f", total)) s loop")
