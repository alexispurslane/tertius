// Tool code (tools/README.md, "GPU calibration"). It never ships, and no engine code depends on it. The native Metal
// twins of the Vulkan probes in tests/Tertius.Rendering.Tests/GpuCalibrationTests.cs: compute kernels of a known amount
// of work, each timed by its command buffer's GPU start and end times, and each checked against its value on the CPU.
//
// Build (outside the dev shell, with Xcode's command line tools):
//   xcrun swiftc -O -o captures/gpu-calibration/bin/probes tools/gpu-calibration/probes.swift -framework Metal
// Run:
//   probes <probe> [dispatches]       fma, fma-rolled, texture-cached, texture-stream or bandwidth, 30 timed dispatches by default
//   probes cold [dispatches]          the FMA probe, every dispatch in order from the first, no warm-up
//   probes sustain <seconds> [n]      the FMA probe back to back for <seconds>, then n timed dispatches (default 30)
//   probes pulse <period ms> <seconds> a short FMA probe every period, one line per probe, until <seconds> pass
//   probes fma-variants [dispatches]  the FMA probe's variants: 1, 2, 4 and 8 steps per pass of the loop with 4 float4
//                                     chains, 1 step per pass with 2, 8 and 16 float4 chains, and the plain form
//   probes dump-msl                   the kernels' source, for xcrun metal
//
// Each timed probe prints one line "probe=<name> ... median=<ms> p10=<ms> p90=<ms> rate=<unit/s> share=<of peak>",
// then "check=pass" or "check=FAIL ..." and exits 1 on a failed check. Every size matches the Vulkan probe's. The timed
// dispatches of each probe run between the lines "timed-start" and "timed-end", for the clock over the timed span.

import Foundation
import Metal

// The kernels. Each is the twin of a GLSL probe in tests/Tertius.Rendering.Tests/Shaders: the same work per invocation,
// the same constants, the same result. Metal's default compile options are fast math, as MoltenVK's default is.
let baseSource = """
#include <metal_stdlib>
using namespace metal;

struct FmaConstants { uint iterations; float a; float b; uint pad; };

// 16 independent chains per invocation, four float4, each step one FMA per chain: x = x * a + b. The FMA probe runs
// `iterations` steps in a loop whose body holds eight steps, because a loop of one step reaches only about 41 % of the
// peak on this GPU (docs/research/temporal-renderer/gpu-calibration.md, "The FMA probe"). The rolled probe keeps one
// step per pass of its loop, and measures that.
#define FMA_STEP x0 = fma(x0, c.a, c.b); x1 = fma(x1, c.a, c.b); x2 = fma(x2, c.a, c.b); x3 = fma(x3, c.a, c.b);
#define FMA_START \
    float start = 1.0f + float(id & 1023u) * (1.0f / 1024.0f); \
    float4 x0 = start + float4(0.0f, 0.125f, 0.25f, 0.375f); \
    float4 x1 = x0 + 0.5f; \
    float4 x2 = x0 + 1.0f; \
    float4 x3 = x0 + 1.5f;
#define FMA_END \
    float4 sum = x0 + x1 + x2 + x3; \
    results[id] = sum.x + sum.y + sum.z + sum.w;

kernel void probe_fma(device float *results [[buffer(0)]],
                      constant FmaConstants &c [[buffer(1)]],
                      uint id [[thread_position_in_grid]])
{
    FMA_START
    for (uint i = 0; i < c.iterations / 8u; i++) {
        FMA_STEP FMA_STEP FMA_STEP FMA_STEP FMA_STEP FMA_STEP FMA_STEP FMA_STEP
    }
    FMA_END
}

kernel void probe_fma_rolled(device float *results [[buffer(0)]],
                             constant FmaConstants &c [[buffer(1)]],
                             uint id [[thread_position_in_grid]])
{
    FMA_START
    for (uint i = 0; i < c.iterations; i++) {
        FMA_STEP
    }
    FMA_END
}

// The plain form: x = x * a, then x = x + b, four steps per pass, with 4 float4 chains. Fast math may fold the steps.
kernel void probe_plain(device float *results [[buffer(0)]],
                        constant FmaConstants &c [[buffer(1)]],
                        uint id [[thread_position_in_grid]])
{
    FMA_START
    for (uint i = 0; i < c.iterations / 4u; i++) {
        for (uint s = 0; s < 4u; s++) {
            x0 = x0 * c.a; x1 = x1 * c.a; x2 = x2 * c.a; x3 = x3 * c.a;
            x0 = x0 + c.b; x1 = x1 + c.b; x2 = x2 + c.b; x3 = x3 + c.b;
        }
    }
    FMA_END
}

struct TextureConstants { uint samples; uint stride; uint stepX; uint stepY; uint maskX; uint shiftY; uint pad0; uint pad1; };

// Bilinear samples at texel corners, so that every sample blends four texels with weights of one half. Sample s of
// invocation (x, y) reads the corner (x * stride + (s & maskX) * stepX, y * stride + (s >> shiftY) * stepY), wrapped.
kernel void probe_texture(device float *results [[buffer(0)]],
                          constant TextureConstants &c [[buffer(1)]],
                          texture2d<float> image [[texture(0)]],
                          sampler linear [[sampler(0)]],
                          uint2 id [[thread_position_in_grid]],
                          uint2 grid [[threads_per_grid]])
{
    uint2 mask = uint2(image.get_width() - 1u, image.get_height() - 1u);
    float2 scale = 1.0f / float2(image.get_width(), image.get_height());
    float sum = 0.0f;
    for (uint s = 0; s < c.samples; s++) {
        uint2 corner = uint2(id.x * c.stride + (s & c.maskX) * c.stepX, id.y * c.stride + (s >> c.shiftY) * c.stepY) & mask;
        float4 texel = image.sample(linear, float2(corner) * scale, level(0.0f));
        sum += texel.x + texel.y + texel.z + texel.w;
    }
    results[id.y * grid.x + id.x] = sum;
}

struct BandwidthConstants { uint reads; uint pad0; uint pad1; uint pad2; };

// Each invocation reads `reads` elements of 16 bytes, one grid apart, so that every read of a SIMD group is contiguous.
kernel void probe_bandwidth(device uint *results [[buffer(0)]],
                            constant BandwidthConstants &c [[buffer(1)]],
                            const device uint4 *source [[buffer(2)]],
                            uint id [[thread_position_in_grid]],
                            uint grid [[threads_per_grid]])
{
    uint sum = 0u;
    for (uint r = 0; r < c.reads; r++) {
        uint4 v = source[id + r * grid];
        sum += v.x + v.y + v.z + v.w;
    }
    results[id] = sum;
}
"""

// The variants of the FMA probe (gpu-calibration.md, "The variants of the FMA probe"): `vectors` independent float4
// chains, and `steps` steps of every chain in each pass of the loop, written out statement by statement, because the
// compiler kept loops over an array of chains rolled and in memory. probe_fma is the variant of 4 vectors and 8 steps,
// and probe_fma_rolled the variant of 4 vectors and 1 step. Vector v starts at the probe's start + 0.5 v, as x0 to x3
// of the probe do.
let fmaVariantShapes = [(4, 1), (4, 2), (4, 4), (4, 8), (2, 1), (8, 1), (16, 1)]

func fmaVariantSource(vectors: Int, steps: Int) -> String {
    let chains = 0..<vectors
    let starts = chains.map { "    float4 x\($0) = start + float4(0.0f, 0.125f, 0.25f, 0.375f) + \(Double($0) * 0.5)f;" }
    let step = chains.map { "x\($0) = fma(x\($0), c.a, c.b);" }.joined(separator: " ")
    return """
        kernel void probe_fma_v\(vectors)_s\(steps)(device float *results [[buffer(0)]],
                                       constant FmaConstants &c [[buffer(1)]],
                                       uint id [[thread_position_in_grid]])
        {
            float start = 1.0f + float(id & 1023u) * (1.0f / 1024.0f);
        \(starts.joined(separator: "\n"))
            for (uint i = 0; i < c.iterations / \(steps)u; i++) {
        \(Array(repeating: "        " + step, count: steps).joined(separator: "\n"))
            }
            float4 sum = \(chains.map { "x\($0)" }.joined(separator: " + "));
            results[id] = sum.x + sum.y + sum.z + sum.w;
        }

        """
}

let source = baseSource + fmaVariantShapes.map { fmaVariantSource(vectors: $0.0, steps: $0.1) }.joined()

// The sizes, shared with the Vulkan probes (GpuCalibrationTests.Sizes).
let fmaInvocations = 1 << 20
let fmaIterations: UInt32 = 1024
let fmaA: Float = 0.9999
let fmaB: Float = 0.0001
let fmaChains = 16
let pulseIterations: UInt32 = 64
// The check's tolerance, CalibrationReference.FmaTolerance of the Vulkan probes: one dropped pass of 8 steps moves a
// result by at least 3.7e-4 of it, and rounding each product before its add by at most 3.4e-5.
let fmaTolerance: Float = 1e-4

let textureGrid = (4096, 256)
let cachedSide = 32
let cachedSamples: UInt32 = 512
// The streamed resources hold 1 GiB each, 21 times the 48 MB system level cache, and each probe reads every byte once.
let streamSide = 16384
let streamSamples: UInt32 = 64

let bandwidthElements = 1 << 26  // 16 B each: 1 GiB
let bandwidthInvocations = 1 << 20

// The peaks of the 24-core M1 Max (docs/research/temporal-renderer/gpu-calibration.md, "The peaks").
let peakFlops = 3072.0 * 2.0 * 1.296e9
let peakTexels = 192.0 * 1.296e9
let peakBytes = 400e9

guard let device = MTLCreateSystemDefaultDevice(), let queue = device.makeCommandQueue() else {
    fatalError("No Metal device.")
}
let library = try! device.makeLibrary(source: source, options: nil)
func pipeline(_ name: String) -> MTLComputePipelineState {
    try! device.makeComputePipelineState(function: library.makeFunction(name: name)!)
}

func percentile(_ sorted: [Double], _ fraction: Double) -> Double {
    sorted[min(sorted.count - 1, Int(Double(sorted.count) * fraction))]
}

/// One probe: the work of one dispatch in its unit, its encoder, and its check against the CPU.
struct Probe {
    let name: String
    let work: Double
    let unit: String
    let peak: Double
    let encode: (MTLComputeCommandEncoder) -> Void
    let check: () -> String?
}

/// Runs `encode` in a command buffer of its own, waits for it, and returns its GPU time in ms.
func timed(_ probe: Probe) -> Double {
    let buffer = queue.makeCommandBuffer()!
    let encoder = buffer.makeComputeCommandEncoder()!
    probe.encode(encoder)
    encoder.endEncoding()
    buffer.commit()
    buffer.waitUntilCompleted()
    if let error = buffer.error { fatalError("\(probe.name): \(error)") }
    return (buffer.gpuEndTime - buffer.gpuStartTime) * 1e3
}

func report(_ probe: Probe, _ times: [Double], label: String = "") {
    let sorted = times.sorted()
    let median = percentile(sorted, 0.5)
    let rate = probe.work / (median * 1e-3)
    let line = String(
        format: "probe=%@%@ api=metal dispatches=%d median=%.4f p10=%.4f p90=%.4f rate=%.4e %@/s share=%.4f",
        probe.name, label, times.count, median, percentile(sorted, 0.1), percentile(sorted, 0.9), rate, probe.unit,
        rate / probe.peak)
    print(line)
    print("times=" + times.map { String(format: "%.4f", $0) }.joined(separator: " "))
    if let failure = probe.check() {
        print("check=FAIL \(failure)")
        exit(1)
    }
    print("check=pass")
}

// MARK: FMA

let fmaResults = device.makeBuffer(length: fmaInvocations * 4, options: .storageModeShared)!
let fmaState = pipeline("probe_fma")
let fmaRolledState = pipeline("probe_fma_rolled")

/// The FMA probe of `iterations` steps, a multiple of 8, unrolled or rolled.
func fmaProbe(iterations: UInt32, rolled: Bool = false, invocations: Int = fmaInvocations) -> Probe {
    Probe(
        name: rolled ? "fma-rolled" : "fma", work: Double(invocations) * Double(iterations) * Double(fmaChains) * 2, unit: "FLOP",
        peak: peakFlops,
        encode: { encoder in
            var constants = (iterations, fmaA, fmaB, UInt32(0))
            encoder.setComputePipelineState(rolled ? fmaRolledState : fmaState)
            encoder.setBuffer(fmaResults, offset: 0, index: 0)
            encoder.setBytes(&constants, length: 16, index: 1)
            encoder.dispatchThreads(
                MTLSize(width: invocations, height: 1, depth: 1),
                threadsPerThreadgroup: MTLSize(width: 256, height: 1, depth: 1))
        },
        check: {
            let results = fmaResults.contents().bindMemory(to: Float.self, capacity: invocations)
            for id in stride(from: 0, to: invocations, by: 997) {
                let expected = fmaReference(id, iterations: iterations)
                if abs(results[id] - expected) > fmaTolerance * abs(expected) {
                    return "fma invocation \(id): \(results[id]) against \(expected) on the CPU"
                }
            }
            return nil
        })
}

/// The reference with x * a + b as one fused step, which is what fma() computes on an Apple GPU, for `vectors` float4
/// chains.
func fmaReference(_ id: Int, iterations: UInt32, vectors: Int = fmaChains / 4) -> Float {
    let start = 1.0 + Float(id & 1023) * (1.0 / 1024.0)
    var vector = [Float](repeating: 0, count: 4)
    for chain in 0..<(vectors * 4) {
        var x = start + Float(chain % 4) * 0.125 + Float(chain / 4) * 0.5
        for _ in 0..<iterations { x = fmaB.addingProduct(x, fmaA) }
        vector[chain % 4] += x
    }
    return vector[0] + vector[1] + vector[2] + vector[3]
}

/// A variant of the FMA probe: `vectors` float4 chains and `steps` steps per pass, or the plain form with `plain`. The
/// plain form gets no check, because fast math may fold its steps and change its result.
func fmaVariant(vectors: Int, steps: Int, plain: Bool = false) -> Probe {
    let name = plain ? "probe_plain" : "probe_fma_v\(vectors)_s\(steps)"
    let state = pipeline(name)
    return Probe(
        name: plain ? "plain-v4-s4" : "fma-v\(vectors)-s\(steps)",
        work: Double(fmaInvocations) * Double(fmaIterations) * Double(vectors * 4) * 2, unit: "FLOP", peak: peakFlops,
        encode: { encoder in
            var constants = (fmaIterations, fmaA, fmaB, UInt32(0))
            encoder.setComputePipelineState(state)
            encoder.setBuffer(fmaResults, offset: 0, index: 0)
            encoder.setBytes(&constants, length: 16, index: 1)
            encoder.dispatchThreads(
                MTLSize(width: fmaInvocations, height: 1, depth: 1),
                threadsPerThreadgroup: MTLSize(width: 256, height: 1, depth: 1))
        },
        check: {
            if plain { return nil }
            let results = fmaResults.contents().bindMemory(to: Float.self, capacity: fmaInvocations)
            for id in stride(from: 0, to: fmaInvocations, by: 997) {
                let expected = fmaReference(id, iterations: fmaIterations, vectors: vectors)
                if abs(results[id] - expected) > fmaTolerance * abs(expected) {
                    return "\(name) invocation \(id): \(results[id]) against \(expected) on the CPU"
                }
            }
            return nil
        })
}

// MARK: Texture

let textureState = pipeline("probe_texture")
let textureResults = device.makeBuffer(length: textureGrid.0 * textureGrid.1 * 4, options: .storageModeShared)!
let linearSampler: MTLSamplerState = {
    let descriptor = MTLSamplerDescriptor()
    descriptor.minFilter = .linear
    descriptor.magFilter = .linear
    descriptor.sAddressMode = .repeat
    descriptor.tAddressMode = .repeat
    descriptor.normalizedCoordinates = true
    return device.makeSamplerState(descriptor: descriptor)!
}()

/// The byte of channel `channel` of texel (x, y): a hash from 64 to 255, so that each sample sums to at least 1.
func texelByte(_ x: Int, _ y: Int, _ channel: Int) -> UInt8 {
    var h = UInt32(truncatingIfNeeded: x) &* 0x9E37_79B1 ^ UInt32(truncatingIfNeeded: y) &* 0x85EB_CA77
    h ^= UInt32(channel) &* 0xC2B2_AE3D
    h ^= h >> 15
    h = h &* 0x2C1B_3C6D
    h ^= h >> 12
    return UInt8(64 + (h % 192))
}

/// A private RGBA8 texture of side `side` filled with texelByte, through a shared staging buffer.
func makeTexture(side: Int) -> MTLTexture {
    let descriptor = MTLTextureDescriptor.texture2DDescriptor(
        pixelFormat: .rgba8Unorm, width: side, height: side, mipmapped: false)
    descriptor.storageMode = .private
    descriptor.usage = .shaderRead
    let texture = device.makeTexture(descriptor: descriptor)!
    let staging = device.makeBuffer(length: side * side * 4, options: .storageModeShared)!
    let bytes = staging.contents().bindMemory(to: UInt8.self, capacity: side * side * 4)
    DispatchQueue.concurrentPerform(iterations: side) { y in
        for x in 0..<side { for c in 0..<4 { bytes[(y * side + x) * 4 + c] = texelByte(x, y, c) } }
    }
    let buffer = queue.makeCommandBuffer()!
    let blit = buffer.makeBlitCommandEncoder()!
    blit.copy(
        from: staging, sourceOffset: 0, sourceBytesPerRow: side * 4, sourceBytesPerImage: side * side * 4,
        sourceSize: MTLSize(width: side, height: side, depth: 1), to: texture, destinationSlice: 0,
        destinationLevel: 0, destinationOrigin: MTLOrigin(x: 0, y: 0, z: 0))
    blit.endEncoding()
    buffer.commit()
    buffer.waitUntilCompleted()
    return texture
}

/// The sum of a texel corner's four bilinear taps, each the mean of four texels, on the CPU.
func cornerSum(_ cx: Int, _ cy: Int, side: Int) -> Double {
    var sum = 0.0
    for c in 0..<4 {
        var channel = 0
        for (dx, dy) in [(-1, -1), (0, -1), (-1, 0), (0, 0)] {
            channel += Int(texelByte((cx + dx + side) % side, (cy + dy + side) % side, c))
        }
        sum += Double(channel) / 4.0 / 255.0
    }
    return sum
}

struct TextureCase {
    let name: String
    let side: Int
    let samples: UInt32
    let stride: UInt32
    let stepX: UInt32
    let stepY: UInt32
    let maskX: UInt32
    let shiftY: UInt32

    func corner(_ x: Int, _ y: Int, _ s: Int) -> (Int, Int) {
        let mask = side - 1
        return (
            (x * Int(stride) + (s & Int(maskX)) * Int(stepX)) & mask,
            (y * Int(stride) + (s >> Int(shiftY)) * Int(stepY)) & mask
        )
    }
}

// The cached case walks a 4 KiB texture, 5 texels right and 3 down per sample. The streamed case reads the corners of even
// coordinates of a 16384 x 16384 texture, so that each texel feeds exactly one sample: sample s goes 8192 right when
// s is odd and 512 rows down per two samples.
let cachedCase = TextureCase(
    name: "texture-cached", side: cachedSide, samples: cachedSamples, stride: 1, stepX: 5, stepY: 3, maskX: 0xFFFF_FFFF,
    shiftY: 0)
let streamCase = TextureCase(
    name: "texture-stream", side: streamSide, samples: streamSamples, stride: 2, stepX: UInt32(streamSide / 2),
    stepY: UInt32(2 * textureGrid.1), maskX: 1, shiftY: 1)

func textureProbe(_ textureCase: TextureCase) -> Probe {
    let texture = makeTexture(side: textureCase.side)
    let invocations = textureGrid.0 * textureGrid.1
    return Probe(
        name: textureCase.name, work: Double(invocations) * Double(textureCase.samples), unit: "texel",
        peak: peakTexels,
        encode: { encoder in
            var constants: [UInt32] = [
                textureCase.samples, textureCase.stride, textureCase.stepX, textureCase.stepY, textureCase.maskX,
                textureCase.shiftY, 0, 0,
            ]
            encoder.setComputePipelineState(textureState)
            encoder.setBuffer(textureResults, offset: 0, index: 0)
            encoder.setBytes(&constants, length: 32, index: 1)
            encoder.setTexture(texture, index: 0)
            encoder.setSamplerState(linearSampler, index: 0)
            encoder.dispatchThreads(
                MTLSize(width: textureGrid.0, height: textureGrid.1, depth: 1),
                threadsPerThreadgroup: MTLSize(width: 16, height: 16, depth: 1))
        },
        check: {
            let results = textureResults.contents().bindMemory(to: Float.self, capacity: invocations)
            for id in stride(from: 0, to: invocations, by: 1009) {
                let (x, y) = (id % textureGrid.0, id / textureGrid.0)
                var expected = 0.0
                for s in 0..<Int(textureCase.samples) {
                    let (cx, cy) = textureCase.corner(x, y, s)
                    expected += cornerSum(cx, cy, side: textureCase.side)
                }
                let tolerance = 0.25 + Double(textureCase.samples) * 1e-3
                if abs(Double(results[id]) - expected) > tolerance {
                    return "\(textureCase.name) invocation \(id): \(results[id]) against \(expected) on the CPU"
                }
            }
            return nil
        })
}

// MARK: Bandwidth

/// Element `index` of the bandwidth probe's source.
func element(_ index: UInt32) -> (UInt32, UInt32, UInt32, UInt32) {
    (index, index &* 0x9E37_79B1, index ^ 0xA5A5_A5A5, (index << 7) | (index >> 25))
}

func bandwidthProbe() -> Probe {
    let bytes = bandwidthElements * 16
    let staging = device.makeBuffer(length: bytes, options: .storageModeShared)!
    let words = staging.contents().bindMemory(to: UInt32.self, capacity: bandwidthElements * 4)
    DispatchQueue.concurrentPerform(iterations: 256) { chunk in
        let size = bandwidthElements / 256
        for i in chunk * size..<(chunk + 1) * size {
            let e = element(UInt32(i))
            words[i * 4] = e.0
            words[i * 4 + 1] = e.1
            words[i * 4 + 2] = e.2
            words[i * 4 + 3] = e.3
        }
    }
    let source = device.makeBuffer(length: bytes, options: .storageModePrivate)!
    let copy = queue.makeCommandBuffer()!
    let blit = copy.makeBlitCommandEncoder()!
    blit.copy(from: staging, sourceOffset: 0, to: source, destinationOffset: 0, size: bytes)
    blit.endEncoding()
    copy.commit()
    copy.waitUntilCompleted()
    let results = device.makeBuffer(length: bandwidthInvocations * 4, options: .storageModeShared)!
    let state = pipeline("probe_bandwidth")
    let reads = UInt32(bandwidthElements / bandwidthInvocations)
    return Probe(
        name: "bandwidth", work: Double(bytes), unit: "B", peak: peakBytes,
        encode: { encoder in
            var constants: [UInt32] = [reads, 0, 0, 0]
            encoder.setComputePipelineState(state)
            encoder.setBuffer(results, offset: 0, index: 0)
            encoder.setBytes(&constants, length: 16, index: 1)
            encoder.setBuffer(source, offset: 0, index: 2)
            encoder.dispatchThreads(
                MTLSize(width: bandwidthInvocations, height: 1, depth: 1),
                threadsPerThreadgroup: MTLSize(width: 256, height: 1, depth: 1))
        },
        check: {
            let sums = results.contents().bindMemory(to: UInt32.self, capacity: bandwidthInvocations)
            for id in stride(from: 0, to: bandwidthInvocations, by: 997) {
                var expected: UInt32 = 0
                for r in 0..<reads {
                    let e = element(UInt32(id) &+ r &* UInt32(bandwidthInvocations))
                    expected = expected &+ e.0 &+ e.1 &+ e.2 &+ e.3
                }
                if sums[id] != expected { return "bandwidth invocation \(id): \(sums[id]) against \(expected)" }
            }
            return nil
        })
}

// MARK: Commands

let arguments = CommandLine.arguments
let command = arguments.count > 1 ? arguments[1] : ""
func argument(_ index: Int, _ fallback: Int) -> Int { arguments.count > index ? Int(arguments[index])! : fallback }

/// Prints a marker line and flushes stdout at once, so that the line falls between gpu-clock's samples in the order in
/// which it happened. Swift buffers stdout when it is not a terminal.
func marker(_ line: String) {
    print(line)
    fflush(stdout)
}

/// `count` timed dispatches between the marker lines timed-start and timed-end, from which tools/gpu_clock.py takes
/// gpu-clock's clock over the timed span.
func timedSpan(_ probe: Probe, _ count: Int) -> [Double] {
    marker("timed-start")
    let times = (0..<count).map { _ in timed(probe) }
    marker("timed-end")
    return times
}

/// Warm-up of 5 dispatches, then `count` timed ones.
func measure(_ probe: Probe, count: Int) {
    for _ in 0..<5 { _ = timed(probe) }
    report(probe, timedSpan(probe, count))
}

switch command {
case "fma":
    measure(fmaProbe(iterations: fmaIterations), count: argument(2, 30))
case "fma-rolled":
    measure(fmaProbe(iterations: fmaIterations, rolled: true), count: argument(2, 30))
case "texture-cached":
    measure(textureProbe(cachedCase), count: argument(2, 30))
case "texture-stream":
    measure(textureProbe(streamCase), count: argument(2, 30))
case "bandwidth":
    measure(bandwidthProbe(), count: argument(2, 30))
case "cold":
    // No warm-up: the first dispatch is the first GPU work after the caller's idle wait.
    let probe = fmaProbe(iterations: fmaIterations)
    report(probe, timedSpan(probe, argument(2, 30)), label: "-cold")
case "sustain":
    // Three command buffers in flight keep the GPU busy without a gap between dispatches.
    let probe = fmaProbe(iterations: fmaIterations)
    let seconds = Double(argument(2, 8))
    let start = Date()
    var inFlight: [MTLCommandBuffer] = []
    while Date().timeIntervalSince(start) < seconds {
        let buffer = queue.makeCommandBuffer()!
        let encoder = buffer.makeComputeCommandEncoder()!
        probe.encode(encoder)
        encoder.endEncoding()
        buffer.commit()
        inFlight.append(buffer)
        if inFlight.count >= 3 { inFlight.removeFirst().waitUntilCompleted() }
    }
    inFlight.forEach { $0.waitUntilCompleted() }
    report(probe, timedSpan(probe, argument(3, 30)), label: "-sustained")
case "pulse":
    // A short probe every period, timed alone: its GPU time is the clock-limited time of 2^20 x 64 x 16 FMAs.
    let probe = fmaProbe(iterations: pulseIterations)
    let period = Double(argument(2, 20)) * 1e-3
    let seconds = Double(argument(3, 30))
    let start = Date()
    var next = start
    while Date().timeIntervalSince(start) < seconds {
        let ms = timed(probe)
        print(String(format: "pulse t=%.4f ms=%.4f", Date().timeIntervalSince(start), ms))
        next += period
        let wait = next.timeIntervalSinceNow
        if wait > 0 { Thread.sleep(forTimeInterval: wait) }
    }
    if let failure = probe.check() {
        print("check=FAIL \(failure)")
        exit(1)
    }
    print("check=pass")
case "fma-variants":
    let variants = fmaVariantShapes.map { fmaVariant(vectors: $0.0, steps: $0.1) }
    for probe in variants + [fmaVariant(vectors: 4, steps: 4, plain: true)] { measure(probe, count: argument(2, 30)) }
case "dump-msl":
    print(source)
default:
    FileHandle.standardError.write(
        "usage: probes fma|fma-rolled|texture-cached|texture-stream|bandwidth|cold|fma-variants [n] | sustain <s> [n] | pulse <ms> <s> | dump-msl\n"
            .data(using: .utf8)!)
    exit(2)
}
