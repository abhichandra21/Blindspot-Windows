// Draws the Blindspot app icon and writes assets/Blindspot.ico.
// Same artwork as the Mac app (../Blindspot/assets/make-icon.swift), with a
// smaller margin because Windows icons fill more of their canvas.
// Run on a Mac from the repository root: swift assets/make-icon.swift
import AppKit

let output = URL(fileURLWithPath: CommandLine.arguments.dropFirst().first ?? "assets/Blindspot.ico")

// A blue tile, a white shield, and a red dot: the spot that may be exposed.
// Coordinates are fractions of the canvas, with the origin at the bottom left.
func drawIcon(size pixels: Int) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
                               bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                               colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let s = CGFloat(pixels)
    // At the smallest sizes the shield is drawn larger so its shape survives.
    let tiny = pixels <= 24
    let margin: CGFloat = tiny ? 0.0 : 0.04
    let shieldScale: CGFloat = tiny ? 1.25 : 1.1
    func p(_ x: CGFloat, _ y: CGFloat) -> NSPoint {
        NSPoint(x: (0.5 + (x - 0.5) * shieldScale) * s, y: (0.5 + (y - 0.5) * shieldScale) * s)
    }

    let tile = NSRect(x: margin * s, y: margin * s, width: (1 - 2 * margin) * s, height: (1 - 2 * margin) * s)
    let tilePath = NSBezierPath(roundedRect: tile, xRadius: 0.18 * s, yRadius: 0.18 * s)
    NSGradient(starting: NSColor(srgbRed: 0.23, green: 0.48, blue: 0.93, alpha: 1),
               ending: NSColor(srgbRed: 0.09, green: 0.23, blue: 0.62, alpha: 1))!
        .draw(in: tilePath, angle: -90)

    let shield = NSBezierPath()
    shield.move(to: p(0.5, 0.78))
    shield.curve(to: p(0.74, 0.70), controlPoint1: p(0.58, 0.74), controlPoint2: p(0.66, 0.71))
    shield.line(to: p(0.74, 0.52))
    shield.curve(to: p(0.5, 0.20), controlPoint1: p(0.74, 0.36), controlPoint2: p(0.62, 0.26))
    shield.curve(to: p(0.26, 0.52), controlPoint1: p(0.38, 0.26), controlPoint2: p(0.26, 0.36))
    shield.line(to: p(0.26, 0.70))
    shield.curve(to: p(0.5, 0.78), controlPoint1: p(0.34, 0.71), controlPoint2: p(0.42, 0.74))
    shield.close()
    NSColor.white.setFill()
    shield.fill()

    // A larger dot at the smallest sizes, so it stays visible.
    let radius = (tiny ? 0.11 * shieldScale : pixels <= 32 ? 0.12 : 0.10 * shieldScale) * s
    let center = p(0.5, 0.50)
    NSColor(srgbRed: 0.90, green: 0.18, blue: 0.15, alpha: 1).setFill()
    NSBezierPath(ovalIn: NSRect(x: center.x - radius, y: center.y - radius,
                                width: radius * 2, height: radius * 2)).fill()

    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

// An .ico file is a small header, one directory entry per size, then the
// images. Windows Vista and later read PNG images inside .ico files.
let sizes = [16, 20, 24, 32, 40, 48, 64, 256]
let images = sizes.map(drawIcon)
var ico = Data()
func le16(_ v: Int) { ico.append(contentsOf: [UInt8(v & 0xff), UInt8(v >> 8 & 0xff)]) }
func le32(_ v: Int) { le16(v & 0xffff); le16(v >> 16 & 0xffff) }
le16(0); le16(1); le16(sizes.count)
var offset = 6 + 16 * sizes.count
for (size, image) in zip(sizes, images) {
    ico.append(UInt8(size % 256)); ico.append(UInt8(size % 256)) // 0 means 256
    ico.append(0); ico.append(0)                                 // no palette, reserved
    le16(1); le16(32)                                            // planes, bits per pixel
    le32(image.count); le32(offset)
    offset += image.count
}
images.forEach { ico.append($0) }
try ico.write(to: output)
print("wrote \(output.path)")
