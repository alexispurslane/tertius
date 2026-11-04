// Tool code (tools/README.md, "GPU calibration"). It never ships, and no engine code depends on it. It times Apple's
// MetalFX temporal upscaler from 1280 x 720 to 1920 x 1080 on the inputs of FSR 3.1's timing test,
// Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTime: the same pattern of colored squares, two depths,
// zero motion, a reactive mask, an exposure of 1 and reversed depth. Each frame is one command buffer of its own, timed
// by its GPU start and end times, as FSR's test times each frame alone by timestamps around Record.
//
// Build (outside the dev shell, with Xcode's command line tools):
//   xcrun swiftc -O -o captures/3ug8/bin/metalfx tools/gpu-calibration/metalfx.swift -framework Metal -framework MetalFX
// Run:
//   metalfx [matched|minimal] [gap ms]
// "matched" (the default) gives MetalFX FSR's inputs. "minimal" leaves out the reactive mask and the exposure texture.
// A gap makes the CPU wait that long after each frame, so that the GPU idles between frames as it does in FSR's test,
// whose CPU records each frame after the last one ends (gpu-calibration.md, "MetalFX against FSR").
// It prints the line "MetalFX temporal from 1280 x 720 to 1920 x 1080 (<mode>): median ... over 300 frames" in the
// format of FSR's test, then "frame times, ms: ...", the 300 frames after 60 of warm-up.

import Foundation
import Metal
import MetalFX

let mode = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "matched"
let matched = mode == "matched"
let gap = CommandLine.arguments.count > 2 ? Double(CommandLine.arguments[2])! * 1e-3 : 0
let render = (width: 1280, height: 720)
let output = (width: 1920, height: 1080)

guard let device = MTLCreateSystemDefaultDevice(), let queue = device.makeCommandQueue() else {
    fatalError("No Metal device.")
}

let descriptor = MTLFXTemporalScalerDescriptor()
descriptor.colorTextureFormat = .rgba16Float
descriptor.depthTextureFormat = .depth32Float
descriptor.motionTextureFormat = .rg16Float
descriptor.outputTextureFormat = .rgba16Float
descriptor.inputWidth = render.width
descriptor.inputHeight = render.height
descriptor.outputWidth = output.width
descriptor.outputHeight = output.height
descriptor.isAutoExposureEnabled = false
descriptor.isInputContentPropertiesEnabled = false
if matched {
    descriptor.isReactiveMaskTextureEnabled = true
    descriptor.reactiveMaskTextureFormat = .r8Unorm
}
guard MTLFXTemporalScalerDescriptor.supportsDevice(device),
    let scaler = descriptor.makeTemporalScaler(device: device)
else { fatalError("MetalFX's temporal scaler does not support \(device.name).") }

func texture(_ format: MTLPixelFormat, _ size: (width: Int, height: Int), _ usage: MTLTextureUsage) -> MTLTexture {
    let d = MTLTextureDescriptor.texture2DDescriptor(
        pixelFormat: format, width: size.width, height: size.height, mipmapped: false)
    d.storageMode = .private
    d.usage = usage
    return device.makeTexture(descriptor: d)!
}

/// Copies `bytes` into `target` through a shared staging buffer.
func upload(_ target: MTLTexture, _ bytes: [UInt8], bytesPerPixel: Int) {
    let staging = device.makeBuffer(bytes: bytes, length: bytes.count, options: .storageModeShared)!
    let buffer = queue.makeCommandBuffer()!
    let blit = buffer.makeBlitCommandEncoder()!
    blit.copy(
        from: staging, sourceOffset: 0, sourceBytesPerRow: target.width * bytesPerPixel,
        sourceBytesPerImage: bytes.count, sourceSize: MTLSize(width: target.width, height: target.height, depth: 1),
        to: target, destinationSlice: 0, destinationLevel: 0, destinationOrigin: MTLOrigin(x: 0, y: 0, z: 0))
    blit.endEncoding()
    buffer.commit()
    buffer.waitUntilCompleted()
    if let error = buffer.error { fatalError("upload: \(error)") }
}

func bytes<T>(_ values: [T]) -> [UInt8] { values.withUnsafeBytes { Array($0) } }

// FSR's test pattern (Fsr3Bench.FillPattern): squares of 8 pixels, red 3 or 0.1, green x / 64, blue y / 48, and the
// depths 0.25 and 0.0625.
var colors = [Float16](repeating: 0, count: render.width * render.height * 4)
var depths = [Float](repeating: 0, count: render.width * render.height)
for y in 0..<render.height {
    for x in 0..<render.width {
        let pixel = y * render.width + x
        let square = (x / 8 + y / 8) % 2 == 0
        colors[pixel * 4] = Float16(square ? 3 : 0.1)
        colors[pixel * 4 + 1] = Float16(Float(x) / 64)
        colors[pixel * 4 + 2] = Float16(Float(y) / 48)
        colors[pixel * 4 + 3] = 1
        depths[pixel] = square ? 0.25 : 0.0625
    }
}
let color = texture(.rgba16Float, render, scaler.colorTextureUsage)
upload(color, bytes(colors), bytesPerPixel: 8)
let depth = texture(.depth32Float, render, scaler.depthTextureUsage)
upload(depth, bytes(depths), bytesPerPixel: 4)
let motion = texture(.rg16Float, render, scaler.motionTextureUsage)
upload(motion, [UInt8](repeating: 0, count: render.width * render.height * 4), bytesPerPixel: 4)
let target = texture(.rgba16Float, output, scaler.outputTextureUsage)
scaler.colorTexture = color
scaler.depthTexture = depth
scaler.motionTexture = motion
scaler.outputTexture = target
scaler.isDepthReversed = true
scaler.motionVectorScaleX = Float(render.width)
scaler.motionVectorScaleY = Float(render.height)
if matched {
    // FSR's test clears its reactive mask to 0 and holds an exposure of 0 stops.
    let reactive = texture(.r8Unorm, render, scaler.reactiveMaskTextureUsage)
    upload(reactive, [UInt8](repeating: 0, count: render.width * render.height), bytesPerPixel: 1)
    scaler.reactiveMaskTexture = reactive
    let exposure = texture(.r16Float, (1, 1), .shaderRead)
    upload(exposure, bytes([Float16(1)]), bytesPerPixel: 2)
    scaler.exposureTexture = exposure
}

/// Element `index` of the Halton sequence of `base`.
func halton(_ index: Int, _ base: Int) -> Float {
    var f: Float = 1
    var r: Float = 0
    var i = index
    while i > 0 {
        f /= Float(base)
        r += f * Float(i % base)
        i /= base
    }
    return r
}

// AMD's jitter: 8 * (output / render)^2 phases of Halton (2, 3), minus a half (ffxFsr3GetJitterPhaseCount).
let phases = Int((8 * pow(Float(output.width) / Float(render.width), 2)).rounded(.up))
var times: [Double] = []
for frame in 0..<360 {
    let phase = frame % phases + 1
    scaler.jitterOffsetX = halton(phase, 2) - 0.5
    scaler.jitterOffsetY = halton(phase, 3) - 0.5
    scaler.reset = frame == 0
    let buffer = queue.makeCommandBuffer()!
    scaler.encode(commandBuffer: buffer)
    buffer.commit()
    buffer.waitUntilCompleted()
    if let error = buffer.error { fatalError("frame \(frame): \(error)") }
    if frame >= 60 { times.append((buffer.gpuEndTime - buffer.gpuStartTime) * 1e3) }
    if gap > 0 { Thread.sleep(forTimeInterval: gap) }
}

let sorted = times.sorted()
print(
    String(
        format: "MetalFX temporal from 1280 x 720 to 1920 x 1080 (%@, gap %.1f ms) on %@: median %.3f ms, 10th percentile %.3f ms, "
            + "90th percentile %.3f ms over %d frames", mode, gap * 1e3, device.name, sorted[sorted.count / 2],
        sorted[sorted.count / 10], sorted[sorted.count * 9 / 10], sorted.count))
print("frame times, ms: " + times.map { String(format: "%.4f", $0) }.joined(separator: " "))
