// Tool code (tools/README.md, "GPU calibration"). It never ships. It compiles a Metal source file with Metal's default
// compile options, fast math as MoltenVK's default, and serializes each kernel alone into a binary archive, which holds
// this GPU's native code. Apple ships no disassembler for that code (metal-objdump has no instruction printer for AGX),
// so count.sh compares the sizes of the native code of the kernels.
//   xcrun swiftc -O -o captures/gpu-calibration/bin/kernel-code tools/gpu-calibration/kernel-code.swift -framework Metal
//   kernel-code <source.metal> <out folder>     writes <out folder>/<kernel>.bin for each kernel

import Foundation
import Metal

let arguments = CommandLine.arguments
guard arguments.count == 3, let device = MTLCreateSystemDefaultDevice() else {
    FileHandle.standardError.write("usage: kernel-code <source.metal> <out folder>\n".data(using: .utf8)!)
    exit(2)
}
let source = try String(contentsOfFile: arguments[1], encoding: .utf8)
let library = try device.makeLibrary(source: source, options: MTLCompileOptions())
try FileManager.default.createDirectory(atPath: arguments[2], withIntermediateDirectories: true)
for name in library.functionNames.sorted() {
    let archive = try device.makeBinaryArchive(descriptor: MTLBinaryArchiveDescriptor())
    let pipeline = MTLComputePipelineDescriptor()
    pipeline.computeFunction = library.makeFunction(name: name)
    try archive.addComputePipelineFunctions(descriptor: pipeline)
    try archive.serialize(to: URL(fileURLWithPath: "\(arguments[2])/\(name).bin"))
    print("\(arguments[2])/\(name).bin")
}
