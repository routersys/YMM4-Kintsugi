using ComputeSharp;

namespace Kintsugi;

internal static class KintsugiMath
{
    public static float SrgbToLinear(float value)
        => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    public static float LinearToSrgb(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
    }

    public static float AbsorptionToScatteringRatio(float infiniteReflectance)
    {
        infiniteReflectance = Math.Clamp(infiniteReflectance, 0f, 1f);
        if (infiniteReflectance <= 0f)
            return float.PositiveInfinity;
        if (infiniteReflectance >= 1f)
            return 0f;
        var complement = 1f - infiniteReflectance;
        return complement * complement / (2f * infiniteReflectance);
    }

    public static (float Reflectance, float Transmittance) KubelkaMunk(float infiniteReflectance, float thickness)
    {
        thickness = Math.Max(thickness, 0f);
        if (thickness <= 0f)
            return (0f, 1f);
        infiniteReflectance = Math.Clamp(infiniteReflectance, 1f / 65536f, 1f);
        var a = 1f + AbsorptionToScatteringRatio(infiniteReflectance);
        var b = MathF.Sqrt(Math.Max(a * a - 1f, 0f));
        if (b <= 1e-7f)
            return (thickness / (1f + thickness), 1f / (1f + thickness));
        var optical = Math.Min(b * thickness, 40f);
        var exponential = MathF.Exp(-2f * optical);
        var denominator = a * (1f - exponential) + b * (1f + exponential);
        return (
            Math.Clamp((1f - exponential) / denominator, 0f, 1f),
            Math.Clamp(2f * b * MathF.Exp(-optical) / denominator, 0f, 1f));
    }

    public static (float Reflectance, float Transmittance) CombineLayers(float r1, float t1, float r2, float t2)
    {
        var denominator = 1f - r1 * r2;
        if (denominator <= 0f)
            return (1f, 0f);
        return (Math.Clamp(r1 + t1 * t1 * r2 / denominator, 0f, 1f), Math.Clamp(t1 * t2 / denominator, 0f, 1f));
    }

    public static float Coat(float height, float thickness) => height + Math.Max(thickness, 0f);
    public static float Erode(float height, float thickness) => height - Math.Max(thickness, 0f);
    public static float Fill(float height, float targetHeight) => Math.Max(height, targetHeight);
    public static float Polish(float height, float maximumHeight) => Math.Min(height, maximumHeight);

    public static float SchlickFresnel(float normalReflectance, float cosine)
    {
        var u = Math.Clamp(cosine, 0f, 1f);
        var complement = 1f - u;
        var square = complement * complement;
        return normalReflectance + (1f - normalReflectance) * square * square * complement;
    }

    public static float SchlickZenith(float roughness, float cosine)
    {
        var r = Math.Clamp(roughness, 0f, 1f);
        var t = Math.Clamp(cosine, 0f, 1f);
        if (r <= 0f)
            return t >= 1f ? 1f : 0f;
        var denominator = 1f + (r - 1f) * t * t;
        return r / Math.Max(denominator * denominator, 1e-12f);
    }

    public static float SchlickAzimuth(float isotropy, float cosine)
    {
        var p = Math.Clamp(isotropy, 0f, 1f);
        var w = Math.Clamp(cosine, 0f, 1f);
        if (p <= 0f)
            return w <= KintsugiSettings.LightConeSine ? KintsugiSettings.AzimuthDiracHeight : 0f;
        var denominator = p * p * (1f - w * w) + w * w;
        return MathF.Sqrt(p / Math.Max(denominator, 1e-12f));
    }

    public static float SchlickGeometry(float roughness, float cosine)
    {
        var r = Math.Clamp(roughness, 0f, 1f);
        var v = Math.Clamp(cosine, 0f, 1f);
        var denominator = r - r * v + v;
        return denominator <= 0f ? 0f : v / denominator;
    }

    public static (float Diffuse, float Microfacet, float Dirac) SchlickWeights(float roughness)
    {
        var r = Math.Clamp(roughness, 0f, 1f);
        var b = 4f * r * (1f - r);
        return r < 0.5f ? (0f, b, 1f - b) : (1f - b, b, 0f);
    }

    public static (float Xx, float Yy, float Xy) TensorPart(float xx, float yy, float xy, bool positive)
    {
        var difference = xx - yy;
        var discriminant = MathF.Sqrt(Math.Max(difference * difference + 4f * xy * xy, 0f));
        var high = 0.5f * (xx + yy + discriminant);
        var low = 0.5f * (xx + yy - discriminant);
        high = positive ? Math.Max(high, 0f) : Math.Min(high, 0f);
        low = positive ? Math.Max(low, 0f) : Math.Min(low, 0f);
        var angle = 0.5f * MathF.Atan2(2f * xy, difference);
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        return (high * c * c + low * s * s, high * s * s + low * c * c, (high - low) * c * s);
    }

    public static (float Value, float X, float Y) MaximumEigenpair(float xx, float yy, float xy)
    {
        var difference = xx - yy;
        var discriminant = MathF.Sqrt(Math.Max(difference * difference + 4f * xy * xy, 0f));
        var value = 0.5f * (xx + yy + discriminant);
        var x = xy;
        var y = value - xx;
        var length = MathF.Sqrt(x * x + y * y);
        if (length <= 1e-7f)
            return xx >= yy ? (value, 1f, 0f) : (value, 0f, 1f);
        return (value, x / length, y / length);
    }

    public static (float Xx, float Yy, float Xy) SeparationTensor(
        ReadOnlySpan<(float X, float Y)> positiveForces,
        ReadOnlySpan<(float X, float Y)> negativeForces)
    {
        static (float Xx, float Yy, float Xy) OuterUnit(float x, float y)
        {
            var length = MathF.Sqrt(x * x + y * y);
            return length <= 1e-8f ? (0f, 0f, 0f) : (x * x / length, y * y / length, x * y / length);
        }

        var positiveX = 0f;
        var positiveY = 0f;
        var positiveXx = 0f;
        var positiveYy = 0f;
        var positiveXy = 0f;
        foreach (var force in positiveForces)
        {
            positiveX += force.X;
            positiveY += force.Y;
            var outer = OuterUnit(force.X, force.Y);
            positiveXx += outer.Xx;
            positiveYy += outer.Yy;
            positiveXy += outer.Xy;
        }
        var negativeX = 0f;
        var negativeY = 0f;
        var negativeXx = 0f;
        var negativeYy = 0f;
        var negativeXy = 0f;
        foreach (var force in negativeForces)
        {
            negativeX += force.X;
            negativeY += force.Y;
            var outer = OuterUnit(force.X, force.Y);
            negativeXx += outer.Xx;
            negativeYy += outer.Yy;
            negativeXy += outer.Xy;
        }
        var positiveResultant = OuterUnit(positiveX, positiveY);
        var negativeResultant = OuterUnit(negativeX, negativeY);
        return (
            0.5f * (-positiveResultant.Xx + positiveXx + negativeResultant.Xx - negativeXx),
            0.5f * (-positiveResultant.Yy + positiveYy + negativeResultant.Yy - negativeYy),
            0.5f * (-positiveResultant.Xy + positiveXy + negativeResultant.Xy - negativeXy));
    }

    public static (float Xx, float Yy, float Xy) PropagationResidual(float eigenvalue, float toughness, float propagation, float nx, float ny)
    {
        var scale = Math.Clamp(propagation, 0f, 1f) * (Math.Max(eigenvalue - Math.Max(toughness, 0f), 0f) + KintsugiSettings.CeramicResidualBias);
        return (scale * nx * nx, scale * ny * ny, scale * nx * ny);
    }

    public static (float Xx, float Yy, float Xy) SymmetricDisplacementGradient(float column0X, float column0Y, float column1X, float column1Y)
        => (column0X - 1f, column1Y - 1f, 0.5f * (column0Y + column1X));

    public static int AlignFloor4(int value) => value >= 0 ? value & ~3 : -(((-value + 3) >> 2) << 2);
    public static int AlignCeiling4(int value) => value >= 0 ? (value + 3) & ~3 : -((-value) & ~3);
}

internal static class KintsugiShaderMath
{
    public static float Hash01(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value * 2.3283064e-10f;
    }

    public static float SrgbToLinear(float value)
        => value <= 0.04045f ? value / 12.92f : Hlsl.Pow((value + 0.055f) / 1.055f, 2.4f);

    public static float LinearToSrgb(float value)
    {
        value = Hlsl.Saturate(value);
        return value <= 0.0031308f ? value * 12.92f : 1.055f * Hlsl.Pow(value, 0.416666667f) - 0.055f;
    }

    public static float CapsuleDistance(float px, float py, float ax, float ay, float bx, float by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var apx = px - ax;
        var apy = py - ay;
        var lengthSquared = abx * abx + aby * aby;
        var t = lengthSquared > 1e-12f ? Hlsl.Saturate((apx * abx + apy * aby) / lengthSquared) : 0f;
        var dx = apx - t * abx;
        var dy = apy - t * aby;
        return Hlsl.Sqrt(dx * dx + dy * dy);
    }

    public static float SmoothMask(float distance, float radius)
    {
        var t = Hlsl.Saturate((distance - radius + KintsugiSettings.RenderAntialiasRadius) / (2f * KintsugiSettings.RenderAntialiasRadius));
        return 1f - t * t * (3f - 2f * t);
    }

    public static float LayerHeight(float mask, float core, float erosionDepth, float lacquerFill, float goldCoat, float polish)
    {
        var eroded = -Hlsl.Max(erosionDepth, 0f) * mask;
        var filled = Hlsl.Max(eroded, eroded + Hlsl.Max(erosionDepth, 0f) * mask * Hlsl.Saturate(lacquerFill));
        var coated = filled + Hlsl.Saturate(goldCoat) * core;
        var cap = Hlsl.Max(filled, coated * (1f - Hlsl.Saturate(polish)));
        return Hlsl.Min(coated, cap);
    }

    public static Float2 KubelkaMunk(float infiniteReflectance, float thickness)
    {
        var reflectance = Hlsl.Saturate(infiniteReflectance);
        thickness = Hlsl.Max(thickness, 0f);
        if (thickness <= 0f)
            return new Float2(0f, 1f);
        reflectance = Hlsl.Max(reflectance, 0.0000152587891f);
        var complement = 1f - reflectance;
        var a = 1f + complement * complement / (2f * reflectance);
        var b = Hlsl.Sqrt(Hlsl.Max(a * a - 1f, 0f));
        if (b <= 1e-6f)
            return new Float2(thickness / (1f + thickness), 1f / (1f + thickness));
        var optical = Hlsl.Min(b * thickness, 40f);
        var exponential = Hlsl.Exp(-2f * optical);
        var denominator = a * (1f - exponential) + b * (1f + exponential);
        return new Float2(
            Hlsl.Saturate((1f - exponential) / denominator),
            Hlsl.Saturate(2f * b * Hlsl.Exp(-optical) / denominator));
    }

    public static Float2 CombineLayers(Float2 upper, Float2 lower)
    {
        var denominator = 1f - upper.X * lower.X;
        if (denominator <= 0f)
            return new Float2(1f, 0f);
        return new Float2(
            Hlsl.Saturate(upper.X + upper.Y * upper.Y * lower.X / denominator),
            Hlsl.Saturate(upper.Y * lower.Y / denominator));
    }

    public static float SchlickFresnel(float c, float u)
    {
        var complement = 1f - Hlsl.Saturate(u);
        var square = complement * complement;
        return Hlsl.Saturate(c) + (1f - Hlsl.Saturate(c)) * square * square * complement;
    }

    public static float SchlickZenith(float r, float t)
    {
        r = Hlsl.Saturate(r);
        t = Hlsl.Saturate(t);
        if (r <= 0f)
            return t >= 0.99999f ? 1f : 0f;
        var denominator = 1f + (r - 1f) * t * t;
        return r / Hlsl.Max(denominator * denominator, 1e-8f);
    }

    public static float SchlickAzimuth(float p, float w)
    {
        p = Hlsl.Saturate(p);
        w = Hlsl.Saturate(Hlsl.Abs(w));
        if (p <= 0f)
            return w <= KintsugiSettings.LightConeSine ? KintsugiSettings.AzimuthDiracHeight : 0f;
        var denominator = p * p * (1f - w * w) + w * w;
        return Hlsl.Sqrt(p / Hlsl.Max(denominator, 1e-8f));
    }

    public static float SchlickGeometry(float r, float v)
    {
        r = Hlsl.Saturate(r);
        v = Hlsl.Saturate(v);
        var denominator = r - r * v + v;
        return denominator <= 1e-6f ? 0f : v / denominator;
    }

    public static Float3 SchlickWeights(float r)
    {
        r = Hlsl.Saturate(r);
        var b = 4f * r * (1f - r);
        return r < 0.5f ? new Float3(0f, b, 1f - b) : new Float3(1f - b, b, 0f);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FillIntShader(ReadWriteBuffer<int> buffer, int length, int value) : IComputeShader
{
    private readonly ReadWriteBuffer<int> buffer = buffer;
    private readonly int length = length;
    private readonly int value = value;

    public void Execute()
    {
        if (ThreadIds.X < length)
            buffer[ThreadIds.X] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FillFloat4Shader(ReadWriteBuffer<Float4> buffer, int length, Float4 value) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> buffer = buffer;
    private readonly int length = length;
    private readonly Float4 value = value;

    public void Execute()
    {
        if (ThreadIds.X < length)
            buffer[ThreadIds.X] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitializeSimulationScratchShader(ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        for (var i = 0; i < KintsugiSettings.ScratchLength; i++)
            scratch[i] = 0;
        scratch[KintsugiSettings.ScratchSelectedVertex] = -1;
        scratch[KintsugiSettings.ScratchStructureActive] = 1;
        scratch[KintsugiSettings.ScratchStoppedStep] = -1;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitializeSimulationPhaseShader(ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[KintsugiSettings.ScratchSelectedVertex] = -1;
        scratch[KintsugiSettings.ScratchStructureActive] = 1;
        scratch[KintsugiSettings.ScratchStoppedStep] = -1;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct AnalyzeInputShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<int> currentInput,
    ReadWriteBuffer<int> previousInput,
    ReadWriteBuffer<Float2> positions,
    ReadWriteBuffer<int> scratch,
    int sourceWidth,
    int sourceHeight,
    int workingWidth,
    int workingHeight,
    float scaleX,
    float offsetX,
    float offsetY,
    float irregularity,
    int seed) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<int> currentInput = currentInput;
    private readonly ReadWriteBuffer<int> previousInput = previousInput;
    private readonly ReadWriteBuffer<Float2> positions = positions;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float scaleX = scaleX;
    private readonly float offsetX = offsetX;
    private readonly float offsetY = offsetY;
    private readonly float irregularity = irregularity;
    private readonly int seed = seed;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;
        var index = y * workingWidth + x;
        var hashSeed = (uint)index * 0x9E3779B9u ^ (uint)seed * 0x85EBCA6Bu;
        var angle = KintsugiShaderMath.Hash01(hashSeed) * 6.28318530718f;
        var radius = KintsugiSettings.MeshJitterRadius * Hlsl.Saturate(irregularity) * Hlsl.Sqrt(KintsugiShaderMath.Hash01(hashSeed ^ 0xC2B2AE35u));
        var position = new Float2(
            x + (y & 1) * 0.5f + radius * Hlsl.Cos(angle),
            y * KintsugiSettings.TriangleHeight + radius * Hlsl.Sin(angle));
        positions[index] = position;
        var px = offsetX + position.X * scaleX;
        var py = offsetY + position.Y * scaleX;
        var inside = px >= 0f && py >= 0f && px <= sourceWidth - 1f && py <= sourceHeight - 1f;
        var sx = Hlsl.Clamp((int)(px + 0.5f), 0, sourceWidth - 1);
        var sy = Hlsl.Clamp((int)(py + 0.5f), 0, sourceHeight - 1);
        var color = inside ? source[new Int2(sx, sy)] : new Float4(0f, 0f, 0f, 0f);
        var valid = inside && color.W > KintsugiSettings.AlphaThreshold ? 1 : 0;
        var inverseAlpha = valid != 0 ? 1f / Hlsl.Max(color.W, 1f / 255f) : 0f;
        var luma = Hlsl.Saturate((color.X * 0.2126f + color.Y * 0.7152f + color.Z * 0.0722f) * inverseAlpha);
        var quantized = (int)(luma * 255f + 0.5f) | valid << 8;
        currentInput[index] = quantized;
        if (previousInput[index] != quantized)
            Hlsl.InterlockedOr(ref scratch[KintsugiSettings.ScratchInputChanged], 1);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CopyIntShader(ReadWriteBuffer<int> source, ReadWriteBuffer<int> destination, int length) : IComputeShader
{
    private readonly ReadWriteBuffer<int> source = source;
    private readonly ReadWriteBuffer<int> destination = destination;
    private readonly int length = length;

    public void Execute()
    {
        if (ThreadIds.X < length)
            destination[ThreadIds.X] = source[ThreadIds.X];
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitializeGeometryShader(
    ReadWriteBuffer<Float2> positions,
    ReadWriteBuffer<Float2> forceCoefficients,
    ReadWriteBuffer<Float4> inverseRest,
    int workingWidth,
    int workingHeight,
    int vertexCount,
    int triangleCount) : IComputeShader
{
    private readonly ReadWriteBuffer<Float2> positions = positions;
    private readonly ReadWriteBuffer<Float2> forceCoefficients = forceCoefficients;
    private readonly ReadWriteBuffer<Float4> inverseRest = inverseRest;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int vertexCount = vertexCount;
    private readonly int triangleCount = triangleCount;

    private int TriangleIndex(int x, int y, int sector)
    {
        var cx = x;
        var cy = y;
        var kind = 0;
        if (sector == 1)
        {
            cx -= (y & 1) == 0 ? 1 : 0;
            kind = 1;
        }
        else if (sector == 2)
            cx--;
        else if (sector == 3)
        {
            cx--;
            cy--;
            kind = 1;
        }
        else if (sector == 4)
        {
            cx -= (y & 1) == 0 ? 1 : 0;
            cy--;
        }
        else if (sector == 5)
        {
            cy--;
            kind = 1;
        }
        if (cx < 0 || cy < 0 || cx >= workingWidth - 1 || cy >= workingHeight - 1)
            return -1;
        return (cy * (workingWidth - 1) + cx) * 2 + kind;
    }

    private void TriangleVertices(int triangle, out int v0, out int v1, out int v2)
    {
        var cell = triangle >> 1;
        var cx = cell % (workingWidth - 1);
        var cy = cell / (workingWidth - 1);
        var index = cy * workingWidth + cx;
        var kind = triangle & 1;
        var odd = cy & 1;
        v0 = kind == 0 || odd != 0 ? index : index + 1;
        v1 = kind == 0 ? index + 1 : index + workingWidth + 1;
        v2 = kind == 0 ? (odd == 0 ? index + workingWidth : index + workingWidth + 1) : index + workingWidth;
    }

    private Float2 ForceCoefficient(int vertex, int triangle)
    {
        TriangleVertices(triangle, out var v0, out var v1, out var v2);
        var p0 = positions[v0];
        var p1 = positions[v1];
        var p2 = positions[v2];
        var cross = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
        if (Hlsl.Abs(cross) <= 1e-6f)
            return new Float2(0f, 0f);
        var factor = cross >= 0f ? -0.5f : 0.5f;
        if (vertex == v0)
            return factor * new Float2(p1.Y - p2.Y, p2.X - p1.X);
        if (vertex == v1)
            return factor * new Float2(p2.Y - p0.Y, p0.X - p2.X);
        return factor * new Float2(p0.Y - p1.Y, p1.X - p0.X);
    }

    public void Execute()
    {
        var index = ThreadIds.X;
        if (index < vertexCount)
        {
            var x = index % workingWidth;
            var y = index / workingWidth;
            for (var sector = 0; sector < 6; sector++)
            {
                var triangle = TriangleIndex(x, y, sector);
                forceCoefficients[index * 6 + sector] = triangle >= 0
                    ? ForceCoefficient(index, triangle)
                    : new Float2(0f, 0f);
            }
        }
        if (index >= triangleCount)
            return;
        TriangleVertices(index, out var v0, out var v1, out var v2);
        var p0 = positions[v0];
        var first = positions[v1] - p0;
        var second = positions[v2] - p0;
        var determinant = first.X * second.Y - first.Y * second.X;
        if (Hlsl.Abs(determinant) <= 1e-6f)
        {
            inverseRest[index] = new Float4(0f, 0f, 0f, 0f);
            return;
        }
        inverseRest[index] = new Float4(
            second.Y / determinant,
            -second.X / determinant,
            -first.Y / determinant,
            first.X / determinant);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitializeMaterialTopologyShader(
    ReadWriteBuffer<int> input,
    ReadWriteBuffer<uint> validSectors,
    int workingWidth,
    int workingHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<int> input = input;
    private readonly ReadWriteBuffer<uint> validSectors = validSectors;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;

    private int TriangleIndex(int x, int y, int sector)
    {
        var cx = x;
        var cy = y;
        var kind = 0;
        if (sector == 1)
        {
            cx -= (y & 1) == 0 ? 1 : 0;
            kind = 1;
        }
        else if (sector == 2)
            cx--;
        else if (sector == 3)
        {
            cx--;
            cy--;
            kind = 1;
        }
        else if (sector == 4)
        {
            cx -= (y & 1) == 0 ? 1 : 0;
            cy--;
        }
        else if (sector == 5)
        {
            cy--;
            kind = 1;
        }
        if (cx < 0 || cy < 0 || cx >= workingWidth - 1 || cy >= workingHeight - 1)
            return -1;
        return (cy * (workingWidth - 1) + cx) * 2 + kind;
    }

    private bool IsSectorValid(int x, int y, int sector)
    {
        var triangle = TriangleIndex(x, y, sector);
        if (triangle < 0)
            return false;
        var cell = triangle >> 1;
        var cx = cell % (workingWidth - 1);
        var cy = cell / (workingWidth - 1);
        var kind = triangle & 1;
        var i = cy * workingWidth + cx;
        var a = input[i];
        var b = input[i + 1];
        var c = input[i + workingWidth];
        var d = input[i + workingWidth + 1];
        var odd = cy & 1;
        var p0 = kind == 0 || odd != 0 ? a : b;
        var p1 = kind == 0 ? b : d;
        var p2 = kind == 0 ? (odd == 0 ? c : d) : c;
        return (p0 & 256) != 0 && (p1 & 256) != 0 && (p2 & 256) != 0;
    }

    public void Execute()
    {
        var vertex = ThreadIds.X;
        if (vertex >= workingWidth * workingHeight)
            return;
        var x = vertex % workingWidth;
        var y = vertex / workingWidth;
        var mask = 0u;
        for (var sector = 0; sector < 6; sector++)
            if (IsSectorValid(x, y, sector))
                mask |= 1u << sector;
        validSectors[vertex] = mask;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitializeStressShader(
    ReadWriteBuffer<int> input,
    ReadWriteBuffer<Float4> stress,
    int workingWidth,
    int workingHeight,
    float stressAmount,
    float shrinkage,
    float luminanceInfluence,
    float irregularity,
    int seed) : IComputeShader
{
    private readonly ReadWriteBuffer<int> input = input;
    private readonly ReadWriteBuffer<Float4> stress = stress;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float stressAmount = stressAmount;
    private readonly float shrinkage = shrinkage;
    private readonly float luminanceInfluence = luminanceInfluence;
    private readonly float irregularity = irregularity;
    private readonly int seed = seed;

    public void Execute()
    {
        var triangle = ThreadIds.X;
        var cellWidth = workingWidth - 1;
        var cellHeight = workingHeight - 1;
        var triangleCount = cellWidth * cellHeight * 2;
        if (triangle >= triangleCount)
            return;
        var cell = triangle >> 1;
        var cx = cell % cellWidth;
        var cy = cell / cellWidth;
        var index = cy * workingWidth + cx;
        var a = input[index];
        var b = input[index + 1];
        var c = input[index + workingWidth];
        var d = input[index + workingWidth + 1];
        var kind = triangle & 1;
        var odd = cy & 1;
        var p0 = kind == 0 || odd != 0 ? a : b;
        var p1 = kind == 0 ? b : d;
        var p2 = kind == 0 ? (odd == 0 ? c : d) : c;
        if ((p0 & 256) == 0 || (p1 & 256) == 0 || (p2 & 256) == 0)
        {
            stress[triangle] = new Float4(0f, 0f, 0f, 0f);
            return;
        }
        var luma = ((p0 & 255) + (p1 & 255) + (p2 & 255)) * (1f / 765f);
        var mapped = Hlsl.Lerp(1f, 1f - luma, Hlsl.Saturate(luminanceInfluence));
        var magnitude = Hlsl.Max(stressAmount, 0f) * mapped + Hlsl.Max(shrinkage, 0f);
        var hash = KintsugiShaderMath.Hash01((uint)triangle * 0x9E3779B9u ^ (uint)seed * 0x85EBCA6Bu);
        var angle = hash * 6.28318530718f;
        var dx = Hlsl.Cos(angle);
        var dy = Hlsl.Sin(angle);
        var anisotropy = Hlsl.Saturate(irregularity) * magnitude;
        stress[triangle] = new Float4(
            magnitude + anisotropy * (2f * dx * dx - 1f),
            magnitude + anisotropy * (2f * dy * dy - 1f),
            anisotropy * 2f * dx * dy,
            0f);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ComputeSectorForcesShader(
    ReadWriteBuffer<uint> validSectors,
    ReadWriteBuffer<Float2> forceCoefficients,
    ReadWriteBuffer<Float4> stress,
    ReadWriteBuffer<Float4> sectorForces,
    ReadWriteBuffer<Float2> relaxationForces,
    ReadWriteBuffer<int> scratch,
    int workingWidth,
    int workingHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> validSectors = validSectors;
    private readonly ReadWriteBuffer<Float2> forceCoefficients = forceCoefficients;
    private readonly ReadWriteBuffer<Float4> stress = stress;
    private readonly ReadWriteBuffer<Float4> sectorForces = sectorForces;
    private readonly ReadWriteBuffer<Float2> relaxationForces = relaxationForces;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;

    private int TriangleIndex(int x, int y, int sector)
    {
        var cx = x;
        var cy = y;
        var kind = 0;
        if (sector == 1)
        {
            cx -= (y & 1) == 0 ? 1 : 0;
            kind = 1;
        }
        else if (sector == 2)
            cx--;
        else if (sector == 3)
        {
            cx--;
            cy--;
            kind = 1;
        }
        else if (sector == 4)
        {
            cx -= (y & 1) == 0 ? 1 : 0;
            cy--;
        }
        else if (sector == 5)
        {
            cy--;
            kind = 1;
        }
        if (cx < 0 || cy < 0 || cx >= workingWidth - 1 || cy >= workingHeight - 1)
            return -1;
        return (cy * (workingWidth - 1) + cx) * 2 + kind;
    }

    public void Execute()
    {
        var index = ThreadIds.X;
        var vertex = index / 6;
        if (vertex >= workingWidth * workingHeight)
            return;
        if (scratch[KintsugiSettings.ScratchStructureActive] == 0)
            return;
        var sector = index - vertex * 6;
        var x = vertex % workingWidth;
        var y = vertex / workingWidth;
        if ((validSectors[vertex] & (1u << sector)) == 0u)
        {
            sectorForces[index] = new Float4(0f, 0f, 0f, 0f);
            relaxationForces[index] = new Float2(0f, 0f);
            return;
        }
        var triangle = TriangleIndex(x, y, sector);
        var tensor = stress[triangle];
        var difference = tensor.X - tensor.Y;
        var discriminant = Hlsl.Sqrt(Hlsl.Max(difference * difference + 4f * tensor.Z * tensor.Z, 0f));
        var high = 0.5f * (tensor.X + tensor.Y + discriminant);
        var low = 0.5f * (tensor.X + tensor.Y - discriminant);
        var coefficient = forceCoefficients[index];
        relaxationForces[index] = new Float2(
            tensor.X * coefficient.X + tensor.Z * coefficient.Y,
            tensor.Z * coefficient.X + tensor.Y * coefficient.Y);
        var positiveHigh = Hlsl.Max(high, 0f);
        var positiveLow = Hlsl.Max(low, 0f);
        var positiveAverage = 0.5f * (positiveHigh + positiveLow);
        var positiveScale = discriminant > 1e-8f ? (positiveHigh - positiveLow) / discriminant : 0f;
        var positiveXx = positiveAverage + 0.5f * positiveScale * difference;
        var positiveYy = positiveAverage - 0.5f * positiveScale * difference;
        var positiveXy = positiveScale * tensor.Z;
        var positiveForce = new Float2(
            positiveXx * coefficient.X + positiveXy * coefficient.Y,
            positiveXy * coefficient.X + positiveYy * coefficient.Y);
        var negativeHigh = Hlsl.Min(high, 0f);
        var negativeLow = Hlsl.Min(low, 0f);
        var negativeAverage = 0.5f * (negativeHigh + negativeLow);
        var negativeScale = discriminant > 1e-8f ? (negativeHigh - negativeLow) / discriminant : 0f;
        var negativeXx = negativeAverage + 0.5f * negativeScale * difference;
        var negativeYy = negativeAverage - 0.5f * negativeScale * difference;
        var negativeXy = negativeScale * tensor.Z;
        var negativeForce = new Float2(
            negativeXx * coefficient.X + negativeXy * coefficient.Y,
            negativeXy * coefficient.X + negativeYy * coefficient.Y);
        sectorForces[index] = new Float4(positiveForce.X, positiveForce.Y, negativeForce.X, negativeForce.Y);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ComputeRelaxationForcesShader(
    ReadWriteBuffer<uint> validSectors,
    ReadWriteBuffer<Float2> forceCoefficients,
    ReadWriteBuffer<Float4> stress,
    ReadWriteBuffer<Float2> relaxationForces,
    ReadWriteBuffer<int> scratch,
    int workingWidth,
    int workingHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> validSectors = validSectors;
    private readonly ReadWriteBuffer<Float2> forceCoefficients = forceCoefficients;
    private readonly ReadWriteBuffer<Float4> stress = stress;
    private readonly ReadWriteBuffer<Float2> relaxationForces = relaxationForces;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;

    private int TriangleIndex(int x, int y, int sector)
    {
        var cx = x;
        var cy = y;
        var kind = 0;
        if (sector == 1)
        {
            cx -= (y & 1) == 0 ? 1 : 0;
            kind = 1;
        }
        else if (sector == 2)
            cx--;
        else if (sector == 3)
        {
            cx--;
            cy--;
            kind = 1;
        }
        else if (sector == 4)
        {
            cx -= (y & 1) == 0 ? 1 : 0;
            cy--;
        }
        else if (sector == 5)
        {
            cy--;
            kind = 1;
        }
        return (cy * (workingWidth - 1) + cx) * 2 + kind;
    }

    public void Execute()
    {
        var index = ThreadIds.X;
        var vertex = index / 6;
        if (vertex >= workingWidth * workingHeight)
            return;
        if (scratch[KintsugiSettings.ScratchStructureActive] == 0)
            return;
        var sector = index - vertex * 6;
        if ((validSectors[vertex] & (1u << sector)) == 0u)
        {
            relaxationForces[index] = new Float2(0f, 0f);
            return;
        }
        var x = vertex % workingWidth;
        var y = vertex / workingWidth;
        var tensor = stress[TriangleIndex(x, y, sector)];
        var coefficient = forceCoefficients[index];
        relaxationForces[index] = new Float2(
            tensor.X * coefficient.X + tensor.Z * coefficient.Y,
            tensor.Z * coefficient.X + tensor.Y * coefficient.Y);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FailureCandidateShader(
    ReadWriteBuffer<uint> validSectors,
    ReadWriteBuffer<Float2> positions,
    ReadWriteBuffer<Float4> sectorForces,
    ReadWriteBuffer<int> edgeBirth,
    ReadWriteBuffer<Float4> residual,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<int> candidateScores,
    ReadWriteBuffer<Int2> candidateEdges,
    ReadWriteBuffer<Float4> candidateData,
    int workingWidth,
    int workingHeight,
    float toughness,
    int finishing) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> validSectors = validSectors;
    private readonly ReadWriteBuffer<Float2> positions = positions;
    private readonly ReadWriteBuffer<Float4> sectorForces = sectorForces;
    private readonly ReadWriteBuffer<int> edgeBirth = edgeBirth;
    private readonly ReadWriteBuffer<Float4> residual = residual;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<int> candidateScores = candidateScores;
    private readonly ReadWriteBuffer<Int2> candidateEdges = candidateEdges;
    private readonly ReadWriteBuffer<Float4> candidateData = candidateData;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float toughness = toughness;
    private readonly int finishing = finishing;

    private bool IsSectorValid(int x, int y, int sector)
        => (validSectors[y * workingWidth + x] & (1u << sector)) != 0u;

    private void SectorForces(int x, int y, int sector, out Float2 positiveForce, out Float2 negativeForce)
    {
        var forces = sectorForces[(y * workingWidth + x) * 6 + sector];
        positiveForce = new Float2(forces.X, forces.Y);
        negativeForce = new Float2(forces.Z, forces.W);
    }

    private Float3 OuterUnit(Float2 force)
    {
        var length = Hlsl.Sqrt(force.X * force.X + force.Y * force.Y);
        if (length <= 1e-8f)
            return new Float3(0f, 0f, 0f);
        var inverse = 1f / length;
        return new Float3(force.X * force.X * inverse, force.Y * force.Y * inverse, force.X * force.Y * inverse);
    }

    private Float3 Separation(int x, int y)
    {
        var positiveSum = new Float2(0f, 0f);
        var negativeSum = new Float2(0f, 0f);
        var positiveOuter = new Float3(0f, 0f, 0f);
        var negativeOuter = new Float3(0f, 0f, 0f);
        for (var sector = 0; sector < 6; sector++)
        {
            SectorForces(x, y, sector, out var positiveForce, out var negativeForce);
            positiveSum += positiveForce;
            negativeSum += negativeForce;
            positiveOuter += OuterUnit(positiveForce);
            negativeOuter += OuterUnit(negativeForce);
        }
        var positiveResultant = OuterUnit(positiveSum);
        var negativeResultant = OuterUnit(negativeSum);
        return 0.5f * (-positiveResultant + positiveOuter + negativeResultant - negativeOuter);
    }

    private int EdgeRecord(int x, int y, int direction)
    {
        var vertexWidth = workingWidth;
        var parity = y & 1;
        var orientation = 0;
        var slot = 0;
        if (direction == 1)
            slot = 1;
        else if (direction == 2)
            slot = 2;
        else if (direction == 3)
        {
            x--;
            orientation = 1;
        }
        else if (direction == 4)
        {
            x += parity - 1;
            y--;
            slot = 1;
            orientation = 1;
        }
        else if (direction == 5)
        {
            x += parity;
            y--;
            slot = 2;
            orientation = 1;
        }
        var edge = (y * vertexWidth + x) * KintsugiSettings.EdgesPerVertex + slot;
        return edge * 2 + orientation;
    }

    private bool IsDirectionValid(int x, int y, int direction)
    {
        var tx = x;
        var ty = y;
        var parity = y & 1;
        if (direction == 0)
            tx++;
        else if (direction == 1)
        {
            tx += parity;
            ty++;
        }
        else if (direction == 2)
        {
            tx += parity - 1;
            ty++;
        }
        else if (direction == 3)
            tx--;
        else if (direction == 4)
        {
            tx += parity - 1;
            ty--;
        }
        else
        {
            tx += parity;
            ty--;
        }
        if (tx < 0 || ty < 0 || tx >= workingWidth || ty >= workingHeight)
            return false;
        var previousSector = direction == 0 ? 5 : direction - 1;
        return IsSectorValid(x, y, direction) && IsSectorValid(x, y, previousSector);
    }

    private bool IsBroken(int x, int y, int direction)
    {
        if (!IsDirectionValid(x, y, direction))
            return false;
        return edgeBirth[EdgeRecord(x, y, direction) >> 1] != 0;
    }

    private Float2 Direction(int x, int y, int direction)
    {
        var tx = x;
        var ty = y;
        var parity = y & 1;
        if (direction == 0)
            tx++;
        else if (direction == 1)
        {
            tx += parity;
            ty++;
        }
        else if (direction == 2)
        {
            tx += parity - 1;
            ty++;
        }
        else if (direction == 3)
            tx--;
        else if (direction == 4)
        {
            tx += parity - 1;
            ty--;
        }
        else
        {
            tx += parity;
            ty--;
        }
        var vector = positions[ty * workingWidth + tx] - positions[y * workingWidth + x];
        var length = Hlsl.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        return vector / Hlsl.Max(length, 1e-8f);
    }

    private int Degree(int x, int y, out int backDirection)
    {
        var degree = 0;
        backDirection = -1;
        for (var direction = 0; direction < 6; direction++)
        {
            if (IsBroken(x, y, direction))
            {
                degree++;
                backDirection = direction;
            }
        }
        return degree;
    }

    private bool IsBoundaryVertex(int x, int y)
    {
        for (var direction = 0; direction < 6; direction++)
            if (!IsDirectionValid(x, y, direction))
                return true;
        return false;
    }

    private bool IsReflexIntersectionTip(int x, int y)
    {
        var firstDirection = -1;
        for (var direction = 0; direction < 6; direction++)
        {
            if (!IsBroken(x, y, direction))
                continue;
            if (firstDirection < 0)
            {
                firstDirection = direction;
                continue;
            }
            var first = Direction(x, y, firstDirection);
            var second = Direction(x, y, direction);
            return first.X * second.X + first.Y * second.Y > 1e-6f;
        }
        return false;
    }

    private bool IsMaterialVertex(int x, int y)
    {
        for (var sector = 0; sector < 6; sector++)
            if (IsSectorValid(x, y, sector))
                return true;
        return false;
    }

    public void Execute()
    {
        var vertex = ThreadIds.X;
        var vertexWidth = workingWidth;
        var vertexCount = vertexWidth * workingHeight;
        if (vertex >= vertexCount)
            return;
        if (scratch[KintsugiSettings.ScratchStructureActive] == 0)
            return;
        candidateScores[vertex] = 0;
        candidateEdges[vertex] = new Int2(-1, -1);
        candidateData[vertex] = new Float4(0f, 0f, 0f, 0f);
        var x = vertex % vertexWidth;
        var y = vertex / vertexWidth;
        if (!IsMaterialVertex(x, y))
            return;

        var propagated = residual[vertex];
        var backDirection = -1;
        var degree = Degree(x, y, out backDirection);
        Float3 tensor;
        if (finishing != 0)
        {
            if ((degree != 1 && (degree != 2 || !IsReflexIntersectionTip(x, y))) || IsBoundaryVertex(x, y))
                return;
            tensor = new Float3(propagated.X, propagated.Y, propagated.Z);
        }
        else
        {
            if (degree > 1 || (degree == 1 && IsBoundaryVertex(x, y)))
                return;
            tensor = Separation(x, y) + new Float3(propagated.X, propagated.Y, propagated.Z);
        }
        var difference = tensor.X - tensor.Y;
        var discriminant = Hlsl.Sqrt(Hlsl.Max(difference * difference + 4f * tensor.Z * tensor.Z, 0f));
        var eigenvalue = 0.5f * (tensor.X + tensor.Y + discriminant);
        var threshold = finishing != 0 ? 1e-8f : Hlsl.Max(toughness, 1e-6f);
        if (eigenvalue <= threshold)
            return;
        var nx = tensor.Z;
        var ny = eigenvalue - tensor.X;
        var eigenLength = Hlsl.Sqrt(nx * nx + ny * ny);
        if (eigenLength <= 1e-7f)
        {
            nx = tensor.X >= tensor.Y ? 1f : 0f;
            ny = tensor.X >= tensor.Y ? 0f : 1f;
        }
        else
        {
            nx /= eigenLength;
            ny /= eigenLength;
        }
        var tangent = new Float2(-ny, nx);

        var firstDirection = -1;
        var secondDirection = -1;
        var firstScore = 0f;
        var secondScore = 0f;
        if (degree == 0)
        {
            for (var direction = 0; direction < 6; direction++)
            {
                if (!IsDirectionValid(x, y, direction) || IsBroken(x, y, direction))
                    continue;
                var vector = Direction(x, y, direction);
                var alignment = vector.X * tangent.X + vector.Y * tangent.Y;
                if (alignment > firstScore)
                {
                    firstScore = alignment;
                    firstDirection = direction;
                }
                var oppositeAlignment = -alignment;
                if (oppositeAlignment > secondScore)
                {
                    secondScore = oppositeAlignment;
                    secondDirection = direction;
                }
            }
            if (firstDirection == secondDirection)
                secondDirection = -1;
        }
        else if (degree == 1)
        {
            var back = Direction(x, y, backDirection);
            var continuation = -back;
            if (tangent.X * continuation.X + tangent.Y * continuation.Y < 0f)
                tangent = -tangent;
            for (var direction = 0; direction < 6; direction++)
            {
                if (direction == backDirection || !IsDirectionValid(x, y, direction) || IsBroken(x, y, direction))
                    continue;
                var vector = Direction(x, y, direction);
                var alignment = vector.X * tangent.X + vector.Y * tangent.Y;
                if (alignment > firstScore)
                {
                    firstScore = alignment;
                    firstDirection = direction;
                }
            }
        }
        else
        {
            firstScore = -1f;
            for (var direction = 0; direction < 6; direction++)
            {
                if (!IsDirectionValid(x, y, direction) || IsBroken(x, y, direction))
                    continue;
                var vector = Direction(x, y, direction);
                var alignment = Hlsl.Abs(vector.X * tangent.X + vector.Y * tangent.Y);
                if (alignment > firstScore)
                {
                    firstScore = alignment;
                    firstDirection = direction;
                }
            }
        }
        if (firstDirection < 0)
            return;
        candidateScores[vertex] = Hlsl.AsInt(eigenvalue - (finishing != 0 ? 0f : Hlsl.Max(toughness, 0f)));
        candidateEdges[vertex] = new Int2(EdgeRecord(x, y, firstDirection), secondDirection >= 0 ? EdgeRecord(x, y, secondDirection) : -1);
        candidateData[vertex] = new Float4(nx, ny, eigenvalue, degree);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ReduceFailureBlocksShader(
    ReadWriteBuffer<int> candidateScores,
    ReadWriteBuffer<int> blockScores,
    ReadWriteBuffer<int> blockVertices,
    ReadWriteBuffer<int> scratch,
    int vertexCount) : IComputeShader
{
    private readonly ReadWriteBuffer<int> candidateScores = candidateScores;
    private readonly ReadWriteBuffer<int> blockScores = blockScores;
    private readonly ReadWriteBuffer<int> blockVertices = blockVertices;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int vertexCount = vertexCount;

    public void Execute()
    {
        var block = ThreadIds.X;
        var start = block * KintsugiSettings.ReductionBlockSize;
        if (start >= vertexCount)
            return;
        if (scratch[KintsugiSettings.ScratchStructureActive] == 0)
        {
            blockScores[block] = 0;
            blockVertices[block] = -1;
            return;
        }
        var end = Hlsl.Min(start + KintsugiSettings.ReductionBlockSize, vertexCount);
        var bestScore = 0;
        var bestVertex = -1;
        for (var vertex = start; vertex < end; vertex++)
        {
            var score = candidateScores[vertex];
            if (score > bestScore || (score == bestScore && score > 0 && (bestVertex < 0 || vertex < bestVertex)))
            {
                bestScore = score;
                bestVertex = vertex;
            }
        }
        blockScores[block] = bestScore;
        blockVertices[block] = bestVertex;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ApplyFailureShader(
    ReadWriteBuffer<int> edgeBirth,
    ReadWriteBuffer<int> edgeLog,
    ReadWriteBuffer<Float4> residual,
    ReadWriteBuffer<int> candidateScores,
    ReadWriteBuffer<Int2> candidateEdges,
    ReadWriteBuffer<Float4> candidateData,
    ReadWriteBuffer<int> blockScores,
    ReadWriteBuffer<int> blockVertices,
    ReadWriteBuffer<int> scratch,
    int workingWidth,
    int workingHeight,
    int step,
    float toughness,
    float propagation,
    int finishing,
    int blockCount) : IComputeShader
{
    private readonly ReadWriteBuffer<int> edgeBirth = edgeBirth;
    private readonly ReadWriteBuffer<int> edgeLog = edgeLog;
    private readonly ReadWriteBuffer<Float4> residual = residual;
    private readonly ReadWriteBuffer<int> candidateScores = candidateScores;
    private readonly ReadWriteBuffer<Int2> candidateEdges = candidateEdges;
    private readonly ReadWriteBuffer<Float4> candidateData = candidateData;
    private readonly ReadWriteBuffer<int> blockScores = blockScores;
    private readonly ReadWriteBuffer<int> blockVertices = blockVertices;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int step = step;
    private readonly float toughness = toughness;
    private readonly float propagation = propagation;
    private readonly int finishing = finishing;
    private readonly int blockCount = blockCount;

    private int TargetVertex(int record)
    {
        var edge = record >> 1;
        var orientation = record & 1;
        var slot = edge % KintsugiSettings.EdgesPerVertex;
        var origin = edge / KintsugiSettings.EdgesPerVertex;
        var vertexWidth = workingWidth;
        var y = origin / vertexWidth;
        var parity = y & 1;
        var target = origin;
        if (slot == 0)
            target = origin + 1;
        else if (slot == 1)
            target = origin + vertexWidth + parity;
        else
            target = origin + vertexWidth + parity - 1;
        return orientation == 0 ? target : origin;
    }

    private int Degree(int vertex)
    {
        var vertexWidth = workingWidth;
        var x = vertex % vertexWidth;
        var y = vertex / vertexWidth;
        var parity = y & 1;
        var degree = 0;
        if (x < workingWidth - 1 && edgeBirth[(vertex * 3)] != 0)
            degree++;
        var downRightX = x + parity;
        if (y < workingHeight - 1 && downRightX >= 0 && downRightX < workingWidth && edgeBirth[vertex * 3 + 1] != 0)
            degree++;
        var downLeftX = x + parity - 1;
        if (y < workingHeight - 1 && downLeftX >= 0 && downLeftX < workingWidth && edgeBirth[vertex * 3 + 2] != 0)
            degree++;
        if (x > 0 && edgeBirth[(vertex - 1) * 3] != 0)
            degree++;
        var upLeftX = x + parity - 1;
        if (y > 0 && upLeftX >= 0 && upLeftX < workingWidth && edgeBirth[((y - 1) * vertexWidth + upLeftX) * 3 + 1] != 0)
            degree++;
        var upRightX = x + parity;
        if (y > 0 && upRightX >= 0 && upRightX < workingWidth && edgeBirth[((y - 1) * vertexWidth + upRightX) * 3 + 2] != 0)
            degree++;
        return degree;
    }

    private void BreakEdge(int record, int slot, Float4 data, float residualScale)
    {
        if (record < 0)
            return;
        var edge = record >> 1;
        if (edgeBirth[edge] != 0)
            return;
        var target = TargetVertex(record);
        var targetDegree = Degree(target);
        edgeBirth[edge] = ((step + 1) << 1) | (record & 1);
        edgeLog[step * KintsugiSettings.EdgesPerStep + slot] = record;
        Hlsl.InterlockedAdd(ref scratch[KintsugiSettings.ScratchWrittenEdgeCount], 1);
        if (targetDegree <= 1 && residualScale > 0f)
        {
            var nx = data.X;
            var ny = data.Y;
            residual[target] += new Float4(residualScale * nx * nx, residualScale * ny * ny, residualScale * nx * ny, 0f);
        }
    }

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        if (scratch[KintsugiSettings.ScratchStructureActive] == 0)
            return;
        var bestScore = 0;
        var vertex = -1;
        for (var block = 0; block < blockCount; block++)
        {
            var score = blockScores[block];
            var candidate = blockVertices[block];
            if (score > bestScore || (score == bestScore && score > 0 && (vertex < 0 || candidate < vertex)))
            {
                bestScore = score;
                vertex = candidate;
            }
        }
        scratch[KintsugiSettings.ScratchSelectedVertex] = vertex;
        if (vertex < 0)
        {
            scratch[KintsugiSettings.ScratchStructureActive] = 0;
            scratch[KintsugiSettings.ScratchStoppedStep] = step;
            return;
        }
        var data = candidateData[vertex];
        var edges = candidateEdges[vertex];
        var residualScale = finishing != 0
            ? data.Z
            : Hlsl.Saturate(propagation) * (Hlsl.Max(data.Z - Hlsl.Max(toughness, 0f), 0f) + KintsugiSettings.CeramicResidualBias);
        residual[vertex] = new Float4(0f, 0f, 0f, 0f);
        BreakEdge(edges.X, 0, data, residualScale);
        BreakEdge(edges.Y, 1, data, residualScale);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ComputeFanForcesShader(
    ReadWriteBuffer<uint> validSectors,
    ReadWriteBuffer<Float2> relaxationForces,
    ReadWriteBuffer<int> edgeBirth,
    ReadWriteBuffer<Float2> fanForces,
    ReadWriteBuffer<int> scratch,
    int workingWidth,
    int workingHeight,
    int maximumBirth) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> validSectors = validSectors;
    private readonly ReadWriteBuffer<Float2> relaxationForces = relaxationForces;
    private readonly ReadWriteBuffer<int> edgeBirth = edgeBirth;
    private readonly ReadWriteBuffer<Float2> fanForces = fanForces;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int maximumBirth = maximumBirth;

    private bool IsSectorValid(int x, int y, int sector)
        => (validSectors[y * workingWidth + x] & (1u << sector)) != 0u;

    private int EdgeIndex(int x, int y, int direction)
    {
        var parity = y & 1;
        var slot = 0;
        if (direction == 1)
            slot = 1;
        else if (direction == 2)
            slot = 2;
        else if (direction == 3)
            x--;
        else if (direction == 4)
        {
            x += parity - 1;
            y--;
            slot = 1;
        }
        else if (direction == 5)
        {
            x += parity;
            y--;
            slot = 2;
        }
        return (y * workingWidth + x) * KintsugiSettings.EdgesPerVertex + slot;
    }

    private bool IsDirectionValid(int x, int y, int direction)
    {
        var tx = x;
        var ty = y;
        var parity = y & 1;
        if (direction == 0)
            tx++;
        else if (direction == 1)
        {
            tx += parity;
            ty++;
        }
        else if (direction == 2)
        {
            tx += parity - 1;
            ty++;
        }
        else if (direction == 3)
            tx--;
        else if (direction == 4)
        {
            tx += parity - 1;
            ty--;
        }
        else
        {
            tx += parity;
            ty--;
        }
        return tx >= 0 && ty >= 0 && tx < workingWidth && ty < workingHeight;
    }

    private bool IsBroken(int x, int y, int direction)
    {
        if (!IsDirectionValid(x, y, direction))
            return false;
        var value = edgeBirth[EdgeIndex(x, y, direction)];
        return value != 0 && (value >> 1) <= maximumBirth;
    }

    public void Execute()
    {
        var index = ThreadIds.X;
        var vertex = index / 6;
        if (vertex >= workingWidth * workingHeight)
            return;
        if (scratch[KintsugiSettings.ScratchStructureActive] == 0)
            return;
        var target = index - vertex * 6;
        var x = vertex % workingWidth;
        var y = vertex / workingWidth;
        if (!IsSectorValid(x, y, target))
        {
            fanForces[index] = new Float2(0f, 0f);
            return;
        }
        var mask = 1u << target;
        var included = 1;
        var current = target;
        for (var count = 1; count < 6; count++)
        {
            var next = current + 1;
            if (next == 6)
                next = 0;
            if (!IsSectorValid(x, y, next) || IsBroken(x, y, next))
                break;
            mask |= 1u << next;
            included++;
            current = next;
        }
        current = target;
        for (var count = 1; count < 6 && included < 6; count++)
        {
            if (IsBroken(x, y, current))
                break;
            var previous = current - 1;
            if (previous < 0)
                previous = 5;
            if (!IsSectorValid(x, y, previous))
                break;
            mask |= 1u << previous;
            included++;
            current = previous;
        }
        var force = new Float2(0f, 0f);
        for (var sector = 0; sector < 6; sector++)
            if ((mask & (1u << sector)) != 0u)
                force += relaxationForces[vertex * 6 + sector];
        fanForces[index] = force;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RelaxStressShader(
    ReadWriteBuffer<uint> validSectors,
    ReadWriteBuffer<Float2> fanForces,
    ReadWriteBuffer<Float4> inverseRest,
    ReadWriteBuffer<Float4> sourceStress,
    ReadWriteBuffer<Float4> destinationStress,
    ReadWriteBuffer<int> scratch,
    int workingWidth,
    int workingHeight,
    float relaxation,
    float shrinkage) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> validSectors = validSectors;
    private readonly ReadWriteBuffer<Float2> fanForces = fanForces;
    private readonly ReadWriteBuffer<Float4> inverseRest = inverseRest;
    private readonly ReadWriteBuffer<Float4> sourceStress = sourceStress;
    private readonly ReadWriteBuffer<Float4> destinationStress = destinationStress;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float relaxation = relaxation;
    private readonly float shrinkage = shrinkage;

    private bool IsSectorValid(int x, int y, int sector)
        => (validSectors[y * workingWidth + x] & (1u << sector)) != 0u;

    private void TriangleVertices(int triangle, out int v0, out int v1, out int v2)
    {
        var cell = triangle >> 1;
        var cx = cell % (workingWidth - 1);
        var cy = cell / (workingWidth - 1);
        var index = cy * workingWidth + cx;
        var kind = triangle & 1;
        var odd = cy & 1;
        v0 = kind == 0 || odd != 0 ? index : index + 1;
        v1 = kind == 0 ? index + 1 : index + workingWidth + 1;
        v2 = kind == 0 ? (odd == 0 ? index + workingWidth : index + workingWidth + 1) : index + workingWidth;
    }

    private Float4 ClampTensor(Float4 tensor)
    {
        var difference = tensor.X - tensor.Y;
        var discriminant = Hlsl.Sqrt(Hlsl.Max(difference * difference + 4f * tensor.Z * tensor.Z, 0f));
        var high = 0.5f * (tensor.X + tensor.Y + discriminant);
        var low = 0.5f * (tensor.X + tensor.Y - discriminant);
        var clampedHigh = Hlsl.Clamp(high, -KintsugiSettings.StressLimit, KintsugiSettings.StressLimit);
        var clampedLow = Hlsl.Clamp(low, -KintsugiSettings.StressLimit, KintsugiSettings.StressLimit);
        if (clampedHigh == high && clampedLow == low)
            return tensor;
        var average = 0.5f * (clampedHigh + clampedLow);
        var scale = discriminant > 1e-8f ? (clampedHigh - clampedLow) / discriminant : 0f;
        return new Float4(
            average + 0.5f * scale * difference,
            average - 0.5f * scale * difference,
            scale * tensor.Z,
            0f);
    }

    public void Execute()
    {
        var triangle = ThreadIds.X;
        var cellWidth = workingWidth - 1;
        var triangleCount = cellWidth * (workingHeight - 1) * 2;
        if (triangle >= triangleCount)
            return;
        if (scratch[KintsugiSettings.ScratchStructureActive] == 0)
            return;
        var kind = triangle & 1;
        var target0 = kind == 0 ? 0 : 1;
        var target1 = kind == 0 ? 2 : 3;
        var target2 = kind == 0 ? 4 : 5;
        TriangleVertices(triangle, out var v0, out var v1, out var v2);
        var x0 = v0 % workingWidth;
        var y0 = v0 / workingWidth;
        var x1 = v1 % workingWidth;
        var y1 = v1 / workingWidth;
        var x2 = v2 % workingWidth;
        var y2 = v2 / workingWidth;
        if (!IsSectorValid(x0, y0, target0))
        {
            destinationStress[triangle] = new Float4(0f, 0f, 0f, 0f);
            return;
        }
        var timeStep = Hlsl.Saturate(relaxation) * KintsugiSettings.RelaxationTimeStep;
        var u0 = fanForces[v0 * 6 + target0] * timeStep;
        var u1 = fanForces[v1 * 6 + target1] * timeStep;
        var u2 = fanForces[v2 * 6 + target2] * timeStep;
        var inverse = inverseRest[triangle];
        if (inverse.X == 0f && inverse.Y == 0f && inverse.Z == 0f && inverse.W == 0f)
        {
            destinationStress[triangle] = sourceStress[triangle];
            return;
        }
        var delta1 = u1 - u0;
        var delta2 = u2 - u0;
        var column0 = delta1 * inverse.X + delta2 * inverse.Z;
        var column1 = delta1 * inverse.Y + delta2 * inverse.W;
        var exx = column0.X;
        var eyy = column1.Y;
        var exy = 0.5f * (column0.Y + column1.X);
        var source = sourceStress[triangle];
        var drive = Hlsl.Max(shrinkage, 0f) * KintsugiSettings.RelaxationTimeStep * 0.01f;
        destinationStress[triangle] = ClampTensor(new Float4(
            source.X + exx + drive,
            source.Y + eyy + drive,
            source.Z + exy,
            0f));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RenderKintsugiShader(
    ReadWriteBuffer<int> edgeBirth,
    ReadWriteBuffer<Float2> positions,
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int sourceWidth,
    int sourceHeight,
    int workingWidth,
    int workingHeight,
    float scaleX,
    float scaleY,
    float offsetX,
    float offsetY,
    int maximumSteps,
    int rectX,
    int rectY,
    int rectWidth,
    int rectHeight,
    int scanRadius,
    float growth,
    float crackWidth,
    float erosionDepth,
    float lacquerFill,
    float goldCoat,
    float polish,
    float lacquerR,
    float lacquerG,
    float lacquerB,
    float goldR,
    float goldG,
    float goldB,
    float roughness,
    float isotropy,
    float lightAngle,
    float lightElevation) : IComputeShader
{
    private readonly ReadWriteBuffer<int> edgeBirth = edgeBirth;
    private readonly ReadWriteBuffer<Float2> positions = positions;
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float scaleX = scaleX;
    private readonly float scaleY = scaleY;
    private readonly float offsetX = offsetX;
    private readonly float offsetY = offsetY;
    private readonly int maximumSteps = maximumSteps;
    private readonly int rectX = rectX;
    private readonly int rectY = rectY;
    private readonly int rectWidth = rectWidth;
    private readonly int rectHeight = rectHeight;
    private readonly int scanRadius = scanRadius;
    private readonly float growth = growth;
    private readonly float crackWidth = crackWidth;
    private readonly float erosionDepth = erosionDepth;
    private readonly float lacquerFill = lacquerFill;
    private readonly float goldCoat = goldCoat;
    private readonly float polish = polish;
    private readonly float lacquerR = lacquerR;
    private readonly float lacquerG = lacquerG;
    private readonly float lacquerB = lacquerB;
    private readonly float goldR = goldR;
    private readonly float goldG = goldG;
    private readonly float goldB = goldB;
    private readonly float roughness = roughness;
    private readonly float isotropy = isotropy;
    private readonly float lightAngle = lightAngle;
    private readonly float lightElevation = lightElevation;

    private Float3 GoldF0()
    {
        var defaultR = KintsugiShaderMath.SrgbToLinear(KintsugiSettings.DefaultGoldSrgbR);
        var defaultG = KintsugiShaderMath.SrgbToLinear(KintsugiSettings.DefaultGoldSrgbG);
        var defaultB = KintsugiShaderMath.SrgbToLinear(KintsugiSettings.DefaultGoldSrgbB);
        return new Float3(
            Hlsl.Saturate(KintsugiSettings.GoldF0R * KintsugiShaderMath.SrgbToLinear(goldR) / Hlsl.Max(defaultR, 1e-6f)),
            Hlsl.Saturate(KintsugiSettings.GoldF0G * KintsugiShaderMath.SrgbToLinear(goldG) / Hlsl.Max(defaultG, 1e-6f)),
            Hlsl.Saturate(KintsugiSettings.GoldF0B * KintsugiShaderMath.SrgbToLinear(goldB) / Hlsl.Max(defaultB, 1e-6f)));
    }

    private Float3 LayerReflectance(float mask, out Float3 transmittance)
    {
        var width = Hlsl.Max(crackWidth, 1f);
        var lacquerThickness = Hlsl.Max(erosionDepth, 0f) * mask * Hlsl.Saturate(lacquerFill) / width;
        var lr = KintsugiShaderMath.KubelkaMunk(KintsugiShaderMath.SrgbToLinear(lacquerR), lacquerThickness);
        var lg = KintsugiShaderMath.KubelkaMunk(KintsugiShaderMath.SrgbToLinear(lacquerG), lacquerThickness);
        var lb = KintsugiShaderMath.KubelkaMunk(KintsugiShaderMath.SrgbToLinear(lacquerB), lacquerThickness);
        transmittance = new Float3(lr.Y, lg.Y, lb.Y);
        return new Float3(lr.X, lg.X, lb.X);
    }

    public void Execute()
    {
        if (ThreadIds.X >= rectWidth || ThreadIds.Y >= rectHeight)
            return;
        var px = rectX + ThreadIds.X + 0.5f;
        var py = rectY + ThreadIds.Y + 0.5f;
        var safeScaleX = Hlsl.Max(scaleX, 1e-6f);
        var safeScaleY = Hlsl.Max(scaleY, 1e-6f);
        var centerX = (int)Hlsl.Floor((px - offsetX) / safeScaleX);
        var centerY = (int)Hlsl.Floor((py - offsetY) / safeScaleY);
        var minX = Hlsl.Max(centerX - scanRadius, 0);
        var maxX = Hlsl.Min(centerX + scanRadius, workingWidth - 1);
        var minY = Hlsl.Max(centerY - scanRadius, 0);
        var maxY = Hlsl.Min(centerY + scanRadius, workingHeight - 1);
        var visibleSteps = Hlsl.Saturate(growth) * maximumSteps;
        var nearest = 1e+20f;
        var nearestAx = 0f;
        var nearestAy = 0f;
        var nearestBx = 0f;
        var nearestBy = 0f;
        for (var oy = minY; oy <= maxY; oy++)
        {
            for (var ox = minX; ox <= maxX; ox++)
            {
                var origin = oy * workingWidth + ox;
                var parity = oy & 1;
                for (var slot = 0; slot < 3; slot++)
                {
                    var targetX = slot == 0 ? ox + 1 : slot == 1 ? ox + parity : ox + parity - 1;
                    var targetY = slot == 0 ? oy : oy + 1;
                    if (targetX < 0 || targetX >= workingWidth || targetY >= workingHeight)
                        continue;
                    var entry = edgeBirth[origin * 3 + slot];
                    if (entry == 0)
                        continue;
                    var step = (entry >> 1) - 1;
                    var fraction = Hlsl.Saturate(visibleSteps - step);
                    if (fraction <= 0f)
                        continue;
                    var firstPosition = positions[origin];
                    var secondPosition = positions[targetY * workingWidth + targetX];
                    var ax = offsetX + firstPosition.X * scaleX;
                    var ay = offsetY + firstPosition.Y * scaleX;
                    var bx = offsetX + secondPosition.X * scaleX;
                    var by = offsetY + secondPosition.Y * scaleX;
                    if ((entry & 1) != 0)
                    {
                        var swapX = ax;
                        var swapY = ay;
                        ax = bx;
                        ay = by;
                        bx = swapX;
                        by = swapY;
                    }
                    bx = ax + (bx - ax) * fraction;
                    by = ay + (by - ay) * fraction;
                    var distance = KintsugiShaderMath.CapsuleDistance(px, py, ax, ay, bx, by);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        nearestAx = ax;
                        nearestAy = ay;
                        nearestBx = bx;
                        nearestBy = by;
                    }
                }
            }
        }
        var radius = Hlsl.Max(crackWidth * 0.5f, 0.5f);
        if (nearest > radius + KintsugiSettings.RenderAntialiasRadius)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }
        var mask = KintsugiShaderMath.SmoothMask(nearest, radius);
        var core = mask * mask;
        var tangentX = nearestBx - nearestAx;
        var tangentY = nearestBy - nearestAy;
        var tangentLength = Hlsl.Sqrt(tangentX * tangentX + tangentY * tangentY);
        if (tangentLength > 1e-6f)
        {
            tangentX /= tangentLength;
            tangentY /= tangentLength;
        }
        else
        {
            tangentX = 1f;
            tangentY = 0f;
        }
        var segmentX = nearestBx - nearestAx;
        var segmentY = nearestBy - nearestAy;
        var segmentLengthSquared = segmentX * segmentX + segmentY * segmentY;
        var projection = segmentLengthSquared > 1e-8f ? Hlsl.Saturate(((px - nearestAx) * segmentX + (py - nearestAy) * segmentY) / segmentLengthSquared) : 0f;
        var closestX = nearestAx + projection * segmentX;
        var closestY = nearestAy + projection * segmentY;
        var radialX = px - closestX;
        var radialY = py - closestY;
        var radialLength = Hlsl.Sqrt(radialX * radialX + radialY * radialY);
        if (radialLength > 1e-6f)
        {
            radialX /= radialLength;
            radialY /= radialLength;
        }
        var distanceMinus = Hlsl.Max(nearest - 0.5f, 0f);
        var distancePlus = nearest + 0.5f;
        var maskMinus = KintsugiShaderMath.SmoothMask(distanceMinus, radius);
        var maskPlus = KintsugiShaderMath.SmoothMask(distancePlus, radius);
        var heightMinus = KintsugiShaderMath.LayerHeight(maskMinus, maskMinus * maskMinus, erosionDepth, lacquerFill, goldCoat, polish);
        var heightPlus = KintsugiShaderMath.LayerHeight(maskPlus, maskPlus * maskPlus, erosionDepth, lacquerFill, goldCoat, polish);
        var slope = heightPlus - heightMinus;
        var normal = Hlsl.Normalize(new Float3(-slope * radialX, -slope * radialY, 1f));
        var angle = lightAngle * 0.01745329252f;
        var elevation = Hlsl.Clamp(lightElevation, 0.001f, 89.999f) * 0.01745329252f;
        var horizontal = Hlsl.Cos(elevation);
        var light = new Float3(horizontal * Hlsl.Cos(angle), horizontal * Hlsl.Sin(angle), Hlsl.Sin(elevation));
        var view = new Float3(0f, 0f, 1f);
        var halfVector = Hlsl.Normalize(light + view);
        var v = Hlsl.Max(Hlsl.Dot(normal, view), 0f);
        var vp = Hlsl.Max(Hlsl.Dot(normal, light), 0f);
        var t = Hlsl.Max(Hlsl.Dot(normal, halfVector), 0f);
        var baseTangent = new Float3(tangentX, tangentY, 0f);
        var projectedTangent = baseTangent - normal * Hlsl.Dot(normal, baseTangent);
        var projectedTangentLength = Hlsl.Length(projectedTangent);
        if (projectedTangentLength > 1e-6f)
            projectedTangent /= projectedTangentLength;
        else
            projectedTangent = new Float3(1f, 0f, 0f);
        var projectedHalf = halfVector - normal * Hlsl.Dot(normal, halfVector);
        var projectedHalfLength = Hlsl.Length(projectedHalf);
        var w = projectedHalfLength > 1e-6f ? Hlsl.Abs(Hlsl.Dot(projectedHalf / projectedHalfLength, projectedTangent)) : 0f;
        var r = Hlsl.Saturate(roughness);
        var p = Hlsl.Saturate(isotropy);
        var z = KintsugiShaderMath.SchlickZenith(r, t);
        var azimuth = KintsugiShaderMath.SchlickAzimuth(p, w);
        var geometry = KintsugiShaderMath.SchlickGeometry(r, v) * KintsugiShaderMath.SchlickGeometry(r, vp);
        var directionalB = geometry * z * azimuth + 1f - geometry;
        var weights = KintsugiShaderMath.SchlickWeights(r);
        var denominator = Hlsl.Max(4f * 3.14159265359f * v * vp, 1e-6f);
        var directional = weights.X * 0.31830988618f + weights.Y * directionalB / denominator;
        var reflectedView = normal * (2f * Hlsl.Dot(normal, view)) - view;
        if (weights.Z > 0f && Hlsl.Dot(light, reflectedView) >= KintsugiSettings.LightConeCosine && v > 0f && vp > 0f)
            directional += weights.Z / Hlsl.Max(v * vp * KintsugiSettings.LightConeSolidAngle, 1e-6f);
        var conductor = GoldF0();
        var fresnel = new Float3(
            KintsugiShaderMath.SchlickFresnel(conductor.X, Hlsl.Dot(light, halfVector)),
            KintsugiShaderMath.SchlickFresnel(conductor.Y, Hlsl.Dot(light, halfVector)),
            KintsugiShaderMath.SchlickFresnel(conductor.Z, Hlsl.Dot(light, halfVector)));
        var gold = Hlsl.Saturate(fresnel * directional * vp);
        var lacquer = LayerReflectance(mask, out var lacquerTransmittance);
        var baseLinear = new Float3(0f, 0f, 0f);
        var basePremultiplied = new Float3(0f, 0f, 0f);
        var baseAlpha = 0f;
        if (px >= 0f && py >= 0f && px < sourceWidth && py < sourceHeight)
        {
            var sourcePixel = source[new Int2(Hlsl.Clamp((int)px, 0, sourceWidth - 1), Hlsl.Clamp((int)py, 0, sourceHeight - 1))];
            baseAlpha = Hlsl.Saturate(sourcePixel.W);
            basePremultiplied = new Float3(sourcePixel.X, sourcePixel.Y, sourcePixel.Z);
            var inverseAlpha = sourcePixel.W > 1e-6f ? 1f / sourcePixel.W : 0f;
            baseLinear = new Float3(
                KintsugiShaderMath.SrgbToLinear(Hlsl.Saturate(sourcePixel.X * inverseAlpha)),
                KintsugiShaderMath.SrgbToLinear(Hlsl.Saturate(sourcePixel.Y * inverseAlpha)),
                KintsugiShaderMath.SrgbToLinear(Hlsl.Saturate(sourcePixel.Z * inverseAlpha)));
        }
        var erosionCoverage = Hlsl.Saturate(Hlsl.Max(erosionDepth, 0f) * mask / Hlsl.Max(crackWidth, 1f));
        var ceramicDiffuse = Hlsl.Saturate(vp / Hlsl.Max(light.Z, 1e-6f));
        var grooveShade = erosionCoverage * (1f - ceramicDiffuse);
        var shadedBase = baseLinear * (1f - grooveShade);
        var combinedR = KintsugiShaderMath.CombineLayers(new Float2(lacquer.X, lacquerTransmittance.X), new Float2(shadedBase.X, 0f));
        var combinedG = KintsugiShaderMath.CombineLayers(new Float2(lacquer.Y, lacquerTransmittance.Y), new Float2(shadedBase.Y, 0f));
        var combinedB = KintsugiShaderMath.CombineLayers(new Float2(lacquer.Z, lacquerTransmittance.Z), new Float2(shadedBase.Z, 0f));
        lacquer = new Float3(combinedR.X, combinedG.X, combinedB.X);
        var filledHeight = -Hlsl.Max(erosionDepth, 0f) * mask * (1f - Hlsl.Saturate(lacquerFill));
        var polishedHeight = KintsugiShaderMath.LayerHeight(mask, core, erosionDepth, lacquerFill, goldCoat, polish);
        var goldExposure = Hlsl.Saturate(Hlsl.Max(polishedHeight - filledHeight, 0f));
        var linear = Hlsl.Lerp(lacquer, gold, goldExposure);
        var lacquerOpacity = Hlsl.Saturate(1f - (lacquerTransmittance.X + lacquerTransmittance.Y + lacquerTransmittance.Z) * 0.333333333f);
        var materialOpacity = Hlsl.Saturate(goldExposure + lacquerOpacity * (1f - goldExposure));
        var erosionOpacity = baseAlpha * grooveShade;
        var opacity = Hlsl.Saturate(materialOpacity + erosionOpacity * (1f - materialOpacity));
        var composedAlpha = opacity + baseAlpha * (1f - opacity);
        var srgb = new Float3(
            KintsugiShaderMath.LinearToSrgb(linear.X),
            KintsugiShaderMath.LinearToSrgb(linear.Y),
            KintsugiShaderMath.LinearToSrgb(linear.Z));
        var overlay = Hlsl.Clamp(srgb * composedAlpha - basePremultiplied * (1f - opacity), 0f, opacity);
        output[ThreadIds.XY] = new Float4(overlay.X, overlay.Y, overlay.Z, opacity);
    }
}
