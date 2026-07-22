namespace Kintsugi;

internal static class KintsugiSettings
{
    public const int MaximumTextureSize = 16384;
    public const int MinimumWorkingSize = 8;
    public const int EdgesPerVertex = 3;
    public const int EdgesPerStep = 2;
    public const int ReductionBlockSize = 256;
    public const int ScratchLength = 8;
    public const int ScratchInputChanged = 0;
    public const int ScratchSelectedVertex = 1;
    public const int ScratchStructureActive = 2;
    public const int ScratchWrittenEdgeCount = 3;
    public const int ScratchStoppedStep = 4;
    public const float AlphaThreshold = 1f / 255f;
    public const float TriangleHeight = 0.866025404f;
    public const float MeshJitterRadius = 0.22f;
    public const float RelaxationTimeStep = 0.0125f;
    public const float StressLimit = 8f;
    public const float CeramicResidualBias = 0.25f;
    public const float RenderAntialiasRadius = 1f;
    public const float GoldF0R = 1f;
    public const float GoldF0G = 0.7294989f;
    public const float GoldF0B = 0.3645575f;
    public const float DefaultGoldSrgbR = 1f;
    public const float DefaultGoldSrgbG = 0.870588235f;
    public const float DefaultGoldSrgbB = 0.639215686f;
    public const float LightConeCosine = 0.999961923f;
    public const float LightConeSolidAngle = 0.000239244f;
    public const float LightConeHalfAngle = 0.00872664626f;
    public const float LightConeSine = 0.0087265355f;
    public const float AzimuthDiracHeight = 180f;

    public static QualitySettings GetQuality(KintsugiQuality quality)
        => quality switch
        {
            KintsugiQuality.Balanced => new QualitySettings(128, 256),
            KintsugiQuality.Ultra => new QualitySettings(384, 896),
            _ => new QualitySettings(224, 512),
        };

    public static (int Width, int Height, float ScaleX, float ScaleY) GetWorkingSize(int width, int height, int resolution)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width > MaximumTextureSize)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height > MaximumTextureSize)
            throw new ArgumentOutOfRangeException(nameof(height));

        var latticeWidth = width - 1f;
        var latticeHeight = (height - 1f) / TriangleHeight;
        var latticeLongSide = Math.Max(latticeWidth, latticeHeight);
        var targetIntervals = Math.Min(Math.Max(resolution - 1, 1), Math.Max((int)MathF.Ceiling(latticeLongSide), 1));
        var spacing = latticeLongSide > 0f ? latticeLongSide / targetIntervals : 1f;
        var workingWidth = Math.Max((int)MathF.Ceiling(latticeWidth / spacing) + 1, MinimumWorkingSize);
        var workingHeight = Math.Max((int)MathF.Ceiling(latticeHeight / spacing) + 1, MinimumWorkingSize);
        return (workingWidth, workingHeight, spacing, spacing * TriangleHeight);
    }

    public static float GetSupportRadius(float crackWidth)
        => Math.Max(crackWidth * 0.5f, 0.5f) + RenderAntialiasRadius;

    public static int GetReductionBlockCount(int vertexCount)
        => (vertexCount + ReductionBlockSize - 1) / ReductionBlockSize;

    internal readonly record struct QualitySettings(int WorkingResolution, int MaximumSteps);
}
