using System.Diagnostics;
using System.Security.Cryptography;
using ComputeSharp;
using ComputeSharp.Interop;
using Kintsugi;

var optionSet = args.Where(argument => argument.StartsWith("--", StringComparison.Ordinal)).ToHashSet(StringComparer.OrdinalIgnoreCase);
var outputDirectory = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine(AppContext.BaseDirectory, "harness-output");
Directory.CreateDirectory(outputDirectory);

using var pipeline = KintsugiPipeline.TryCreate();
if (pipeline is null)
{
    Console.WriteLine("Direct3D 12 is unavailable.");
    return 1;
}

if (optionSet.Contains("--golden"))
    return RunGolden(pipeline, outputDirectory);

if (optionSet.Contains("--performance"))
    return RunPerformance(pipeline);

if (optionSet.Contains("--topology-search"))
    return RunTopologySearch(pipeline);

return RunImages(pipeline, outputDirectory);

static int RunGolden(KintsugiPipeline pipeline, string outputDirectory)
{
    const int width = 320;
    const int height = 240;
    var source = CreateTestImage(width, height);
    var output = new int[source.Length];
    var defaultParameters = CreateParameters(KintsugiQuality.Balanced);
    var cases = new (string Name, KintsugiPipeline.Parameters Parameters)[]
    {
        ("balanced-default", defaultParameters),
        ("high-default", CreateParameters(KintsugiQuality.High)),
        ("ultra-default", CreateParameters(KintsugiQuality.Ultra)),
        ("seed-42", defaultParameters with { Seed = 42 }),
        ("growth-50", defaultParameters with { Growth = 0.5f }),
        ("no-gold", defaultParameters with { GoldCoat = 0f }),
        ("rough-gold", defaultParameters with { GoldCoat = 1f, Polish = 0f, Roughness = 0.75f }),
        ("regular-grid", defaultParameters with { Irregularity = 0f }),
    };
    var expected = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["balanced-default"] = "59160E07A93406CE9A7F2D7A7B0C81744F0980CF1EDE1DC291DE1D0E27914333",
        ["high-default"] = "98FDED355C99183D308D3779C954AF1E1CE60F463C0C3DAAABA7A2604DA6D369",
        ["ultra-default"] = "20E5E80502D62BC745A9B439D27195851AD055FD3DD03A45BE2BED276EE88AEF",
        ["seed-42"] = "4B3402129D503816EC14ACB21111BAB8FBECDA106C0D24E878E6AF37B8EF0879",
        ["growth-50"] = "57D03DDF1D6584B6756DDCDAB8D43DEE702D90D8F33206CF732BFB7B192313CD",
        ["no-gold"] = "32A133415648FC329A879DA083FDEABB4D75A1A227667DDCF4C86AB49532F2A0",
        ["rough-gold"] = "E040A6356B10466B192AC460B373007600E14E0B4AC3583A34995BA08CA79758",
        ["regular-grid"] = "E5F56F024822D4DDD6A3F5B004325B9F81892CD6572A70A0479CED87093B1C7C",
    };
    var failed = false;
    foreach (var (name, parameters) in cases)
    {
        pipeline.Process(source, output, width, height, in parameters);
        var hash = HashPixels(output);
        var matches = expected.TryGetValue(name, out var expectedHash) && StringComparer.Ordinal.Equals(hash, expectedHash);
        var topology = GetTopology(pipeline.EdgeLog, pipeline.WorkingWidth);
        Console.WriteLine($"{name}: {hash} {(matches ? "PASS" : "FAIL")} edges={pipeline.WrittenEdgeCount} steps={pipeline.GeneratedSteps} degree={topology.MaximumDegree} cycleRank={topology.CycleRank}");
        WriteBmp(Path.Combine(outputDirectory, $"golden-{name}.bmp"), Composite(source, output), width, height);
        failed |= expected.Count > 0 && !matches;
    }
    return failed ? 1 : 0;
}

static int RunPerformance(KintsugiPipeline pipeline)
{
    const int width = 1280;
    const int height = 720;
    var source = CreateTestImage(width, height);
    var output = new int[source.Length];
    foreach (var quality in new[] { KintsugiQuality.Balanced, KintsugiQuality.High, KintsugiQuality.Ultra })
    {
        var parameters = CreateParameters(quality);
        pipeline.Process(source, output, width, height, in parameters);
        pipeline.Process(source, output, width, height, in parameters);
        var stopwatch = Stopwatch.StartNew();
        const int frames = 5;
        for (var frame = 0; frame < frames; frame++)
            pipeline.Process(source, output, width, height, in parameters);
        stopwatch.Stop();
        Console.WriteLine($"packed cached {quality}: {stopwatch.Elapsed.TotalMilliseconds / frames:F2} ms/frame ({width}x{height}) litPixels={CountLit(output)} edges={pipeline.WrittenEdgeCount} steps={GetGeneratedStepCount(pipeline.EdgeLog)}");
    }

    var device = GraphicsDevice.GetDefault();
    using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
    var sourcePixels = ToBgra32(source);
    sourceTexture.CopyFrom(sourcePixels);
    var cachedParameters = CreateParameters(KintsugiQuality.High);
    pipeline.Simulate(sourceTexture, width, height, in cachedParameters);
    pipeline.WaitForCompletion();
    if (!pipeline.TryGetVisibleBounds(width, height, in cachedParameters, out var rect))
        return 1;
    using var visibleOutput = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, rect.Width, rect.Height);
    pipeline.RenderVisible(sourceTexture, visibleOutput, width, height, rect, in cachedParameters);
    pipeline.WaitForCompletion();
    var visibleStopwatch = Stopwatch.StartNew();
    const int visibleFrames = 20;
    for (var frame = 0; frame < visibleFrames; frame++)
    {
        pipeline.Simulate(sourceTexture, width, height, in cachedParameters);
        pipeline.TryGetVisibleBounds(width, height, in cachedParameters, out rect);
        pipeline.RenderVisible(sourceTexture, visibleOutput, width, height, rect, in cachedParameters);
    }
    pipeline.WaitForCompletion();
    visibleStopwatch.Stop();
    Console.WriteLine($"shared cached High: {visibleStopwatch.Elapsed.TotalMilliseconds / visibleFrames:F2} ms/frame rect={rect.Width}x{rect.Height}@({rect.X},{rect.Y})");

    const int structuralFrames = 3;
    foreach (var quality in new[] { KintsugiQuality.Balanced, KintsugiQuality.High, KintsugiQuality.Ultra })
    {
        var structuralStopwatch = Stopwatch.StartNew();
        for (var frame = 0; frame < structuralFrames; frame++)
        {
            var changingParameters = CreateParameters(quality) with { Seed = frame + 100 };
            pipeline.Simulate(sourceTexture, width, height, in changingParameters);
        }
        pipeline.WaitForCompletion();
        structuralStopwatch.Stop();
        Console.WriteLine($"shared structure {quality}: {structuralStopwatch.Elapsed.TotalMilliseconds / structuralFrames:F2} ms/recompute");
    }
    return 0;
}

static int RunImages(KintsugiPipeline pipeline, string outputDirectory)
{
    const int width = 1280;
    const int height = 720;
    var source = CreateTestImage(width, height);
    var output = new int[source.Length];
    var defaultParameters = CreateParameters(KintsugiQuality.High);
    var cases = new (string Name, KintsugiPipeline.Parameters Parameters)[]
    {
        ("default", defaultParameters),
        ("growth025", defaultParameters with { Growth = 0.25f }),
        ("growth050", defaultParameters with { Growth = 0.5f }),
        ("growth075", defaultParameters with { Growth = 0.75f }),
        ("lacquer", defaultParameters with { GoldCoat = 0f }),
        ("gold-full", defaultParameters with { GoldCoat = 1f }),
        ("rough", defaultParameters with { Roughness = 0.75f }),
        ("polished", defaultParameters with { Roughness = 0f, Polish = 1f }),
    };
    WriteBmp(Path.Combine(outputDirectory, "source.bmp"), source, width, height);
    foreach (var (name, parameters) in cases)
    {
        pipeline.Process(source, output, width, height, in parameters);
        var topology = GetTopology(pipeline.EdgeLog, pipeline.WorkingWidth);
        Console.WriteLine($"{name}: {HashPixels(output)} litPixels={CountLit(output)} edges={pipeline.WrittenEdgeCount} steps={pipeline.GeneratedSteps} degree={topology.MaximumDegree} cycleRank={topology.CycleRank}");
        WriteBmp(Path.Combine(outputDirectory, $"{name}.bmp"), Composite(source, output), width, height);
    }

    var device = GraphicsDevice.GetDefault();
    using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
    sourceTexture.CopyFrom(ToBgra32(source));
    pipeline.Simulate(sourceTexture, width, height, in defaultParameters);
    if (pipeline.TryGetVisibleBounds(width, height, in defaultParameters, out var rect))
    {
        using var visibleTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(sourceTexture, visibleTexture, width, height, rect, in defaultParameters);
        var visiblePixels = new Bgra32[rect.Width * rect.Height];
        visibleTexture.CopyTo(visiblePixels);
        var overlay = ToPacked(visiblePixels);
        var composed = CompositeExpanded(source, width, height, overlay, rect);
        WriteBmp(Path.Combine(outputDirectory, "expanded-default.bmp"), composed.Pixels, composed.Width, composed.Height);
        Console.WriteLine($"expanded-default: rect={rect.Width}x{rect.Height}@({rect.X},{rect.Y}) canvas={composed.Width}x{composed.Height}");
    }
    Console.WriteLine($"images written to {outputDirectory}");
    return 0;
}

static int RunTopologySearch(KintsugiPipeline pipeline)
{
    const int width = 64;
    const int height = 64;
    var source = CreateTestImage(width, height);
    var output = new int[source.Length];
    (int Seed, float Relaxation, float Propagation, float Irregularity, int MaximumDegree, int CycleRank)? junction = null;
    (int Seed, float Relaxation, float Propagation, float Irregularity, int MaximumDegree, int CycleRank)? cycle = null;
    foreach (var relaxation in new[] { 0.2f, 0.6f, 1f })
    {
        foreach (var propagation in new[] { 0.25f, 0.75f, 1f })
        {
            foreach (var irregularity in new[] { 0.3f, 0.7f, 1f })
            {
                for (var seed = 0; seed < 128; seed++)
                {
                    var parameters = CreateParameters(KintsugiQuality.Balanced) with
                    {
                        Seed = seed,
                        Stress = 2f,
                        Shrinkage = 1f,
                        Toughness = 0.01f,
                        Relaxation = relaxation,
                        Propagation = propagation,
                        Irregularity = irregularity,
                    };
                    pipeline.Process(source, output, width, height, in parameters);
                    var stats = GetTopology(pipeline.EdgeLog, pipeline.WorkingWidth);
                    if (junction is null && stats.MaximumDegree >= 3)
                    {
                        junction = (seed, relaxation, propagation, irregularity, stats.MaximumDegree, stats.CycleRank);
                        Console.WriteLine($"junction seed={seed} relaxation={relaxation:F2} propagation={propagation:F2} irregularity={irregularity:F2} degree={stats.MaximumDegree} cycleRank={stats.CycleRank}");
                    }
                    if (cycle is null && stats.CycleRank > 0)
                    {
                        cycle = (seed, relaxation, propagation, irregularity, stats.MaximumDegree, stats.CycleRank);
                        Console.WriteLine($"cycle seed={seed} relaxation={relaxation:F2} propagation={propagation:F2} irregularity={irregularity:F2} degree={stats.MaximumDegree} cycleRank={stats.CycleRank}");
                    }
                    if (junction is not null && cycle is not null)
                        return 0;
                }
            }
        }
    }
    Console.WriteLine($"junction={(junction is null ? "not found" : "found")} cycle={(cycle is null ? "not found" : "found")}");
    return junction is not null && cycle is not null ? 0 : 2;
}

static (int MaximumDegree, int CycleRank) GetTopology(ReadOnlySpan<int> edgeLog, int workingWidth)
{
    var adjacency = new Dictionary<int, List<int>>();
    var edges = new HashSet<int>();
    foreach (var record in edgeLog)
    {
        if (record < 0 || !edges.Add(record >> 1))
            continue;
        var edge = record >> 1;
        var slot = edge % KintsugiSettings.EdgesPerVertex;
        var first = edge / KintsugiSettings.EdgesPerVertex;
        var parity = first / workingWidth & 1;
        var second = slot == 0 ? first + 1 : slot == 1 ? first + workingWidth + parity : first + workingWidth + parity - 1;
        if (!adjacency.TryGetValue(first, out var firstNeighbors))
            adjacency[first] = firstNeighbors = [];
        if (!adjacency.TryGetValue(second, out var secondNeighbors))
            adjacency[second] = secondNeighbors = [];
        firstNeighbors.Add(second);
        secondNeighbors.Add(first);
    }
    var visited = new HashSet<int>();
    var components = 0;
    var maximumDegree = 0;
    var stack = new Stack<int>();
    foreach (var pair in adjacency)
    {
        maximumDegree = Math.Max(maximumDegree, pair.Value.Count);
        if (!visited.Add(pair.Key))
            continue;
        components++;
        stack.Push(pair.Key);
        while (stack.TryPop(out var vertex))
        {
            foreach (var neighbor in adjacency[vertex])
            {
                if (visited.Add(neighbor))
                    stack.Push(neighbor);
            }
        }
    }
    return (maximumDegree, edges.Count - adjacency.Count + components);
}

static int GetGeneratedStepCount(ReadOnlySpan<int> edgeLog)
{
    for (var step = edgeLog.Length / KintsugiSettings.EdgesPerStep - 1; step >= 0; step--)
    {
        for (var slot = 0; slot < KintsugiSettings.EdgesPerStep; slot++)
        {
            if (edgeLog[step * KintsugiSettings.EdgesPerStep + slot] >= 0)
                return step + 1;
        }
    }
    return 0;
}

static KintsugiPipeline.Parameters CreateParameters(KintsugiQuality quality)
    => new(
        quality,
        7,
        1f,
        0.6f,
        0.5f,
        0.6f,
        0.5f,
        0.7f,
        0.6f,
        1f,
        2.5f,
        1.5f,
        1f,
        0.75f,
        0.4f,
        0x3A / 255f,
        0x07 / 255f,
        0x05 / 255f,
        1f,
        0xDE / 255f,
        0xA3 / 255f,
        0.25f,
        0.9f,
        -45f,
        45f);

static int[] CreateTestImage(int width, int height)
{
    var pixels = new int[width * height];
    var centerX = (width - 1) * 0.5;
    var centerY = (height - 1) * 0.5;
    var radiusX = width * 0.43;
    var radiusY = height * 0.43;
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var nx = (x - centerX) / radiusX;
            var ny = (y - centerY) / radiusY;
            var radius = nx * nx + ny * ny;
            if (radius > 1d)
                continue;
            var illumination = Math.Sqrt(Math.Max(1d - radius, 0d));
            var pattern = Math.Sin(x * 0.021d) * Math.Cos(y * 0.027d) * 13d;
            var red = Math.Clamp((int)(154d + illumination * 78d + pattern), 0, 255);
            var green = Math.Clamp((int)(92d + illumination * 98d + pattern * 0.7d), 0, 255);
            var blue = Math.Clamp((int)(63d + illumination * 112d + pattern * 0.5d), 0, 255);
            pixels[y * width + x] = unchecked((int)0xFF000000) | red << 16 | green << 8 | blue;
        }
    }
    return pixels;
}

static Bgra32[] ToBgra32(int[] pixels)
{
    var result = new Bgra32[pixels.Length];
    for (var index = 0; index < pixels.Length; index++)
        result[index].PackedValue = unchecked((uint)pixels[index]);
    return result;
}

static int[] ToPacked(Bgra32[] pixels)
{
    var result = new int[pixels.Length];
    for (var index = 0; index < pixels.Length; index++)
        result[index] = unchecked((int)pixels[index].PackedValue);
    return result;
}

static string HashPixels(int[] pixels)
{
    var bytes = new byte[pixels.Length * sizeof(int)];
    Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
    return Convert.ToHexString(SHA256.HashData(bytes));
}

static int CountLit(int[] pixels)
{
    var count = 0;
    foreach (var pixel in pixels)
    {
        if (((pixel >> 24) & 255) > 0)
            count++;
    }
    return count;
}

static int[] Composite(int[] source, int[] overlay)
{
    var result = new int[source.Length];
    for (var index = 0; index < source.Length; index++)
        result[index] = Over(source[index], overlay[index]);
    return result;
}

static (int[] Pixels, int Width, int Height) CompositeExpanded(int[] source, int sourceWidth, int sourceHeight, int[] overlay, KintsugiPipeline.PixelRect rect)
{
    var left = Math.Min(0, rect.X);
    var top = Math.Min(0, rect.Y);
    var right = Math.Max(sourceWidth, rect.X + rect.Width);
    var bottom = Math.Max(sourceHeight, rect.Y + rect.Height);
    var width = right - left;
    var height = bottom - top;
    var result = new int[width * height];
    for (var y = 0; y < sourceHeight; y++)
    {
        for (var x = 0; x < sourceWidth; x++)
            result[(y - top) * width + x - left] = source[y * sourceWidth + x];
    }
    for (var y = 0; y < rect.Height; y++)
    {
        for (var x = 0; x < rect.Width; x++)
        {
            var index = (rect.Y + y - top) * width + rect.X + x - left;
            result[index] = Over(result[index], overlay[y * rect.Width + x]);
        }
    }
    return (result, width, height);
}

static int Over(int source, int overlay)
{
    var sourceAlpha = (source >> 24) & 255;
    var overlayAlpha = (overlay >> 24) & 255;
    var inverse = 255 - overlayAlpha;
    var alpha = Math.Min(overlayAlpha + sourceAlpha * inverse / 255, 255);
    var red = Math.Min(((overlay >> 16) & 255) + ((source >> 16) & 255) * inverse / 255, alpha);
    var green = Math.Min(((overlay >> 8) & 255) + ((source >> 8) & 255) * inverse / 255, alpha);
    var blue = Math.Min((overlay & 255) + (source & 255) * inverse / 255, alpha);
    return alpha << 24 | red << 16 | green << 8 | blue;
}

static void WriteBmp(string path, int[] pixels, int width, int height)
{
    var stride = width * 3;
    var padding = (4 - stride % 4) % 4;
    var dataSize = (stride + padding) * height;
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var writer = new BinaryWriter(stream);
    writer.Write((byte)'B');
    writer.Write((byte)'M');
    writer.Write(54 + dataSize);
    writer.Write(0);
    writer.Write(54);
    writer.Write(40);
    writer.Write(width);
    writer.Write(height);
    writer.Write((short)1);
    writer.Write((short)24);
    writer.Write(0);
    writer.Write(dataSize);
    writer.Write(2835);
    writer.Write(2835);
    writer.Write(0);
    writer.Write(0);
    var pad = new byte[padding];
    for (var y = height - 1; y >= 0; y--)
    {
        for (var x = 0; x < width; x++)
        {
            var pixel = pixels[y * width + x];
            writer.Write((byte)(pixel & 255));
            writer.Write((byte)((pixel >> 8) & 255));
            writer.Write((byte)((pixel >> 16) & 255));
        }
        writer.Write(pad);
    }
}
