using System.Runtime.InteropServices;
using ComputeSharp;
using ComputeSharp.Interop;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace Kintsugi.Tests;

public sealed class KintsugiEffectTests
{
    private static double ValueAt(YukkuriMovieMaker.Commons.Animation animation) => animation.GetValue(0, 1, 30);

    private static KintsugiPipeline.Parameters CreateParameters(
        KintsugiQuality quality = KintsugiQuality.Balanced,
        int seed = 7,
        float stress = 1f,
        float shrinkage = 0.6f,
        float toughness = 0.5f,
        float relaxation = 0.6f,
        float propagation = 0.5f,
        float luminanceInfluence = 0.7f,
        float irregularity = 0.6f,
        float growth = 1f,
        float crackWidth = 2.5f,
        float erosionDepth = 1.5f,
        float lacquerFill = 1f,
        float goldCoat = 0.75f,
        float polish = 0.4f,
        float roughness = 0.25f,
        float isotropy = 0.9f,
        float lightAngleDegrees = -45f,
        float lightElevationDegrees = 45f)
        => new(
            quality,
            seed,
            stress,
            shrinkage,
            toughness,
            relaxation,
            propagation,
            luminanceInfluence,
            irregularity,
            growth,
            crackWidth,
            erosionDepth,
            lacquerFill,
            goldCoat,
            polish,
            0x3A / 255f,
            0x07 / 255f,
            0x05 / 255f,
            1f,
            0xDE / 255f,
            0xA3 / 255f,
            roughness,
            isotropy,
            lightAngleDegrees,
            lightElevationDegrees);

    [Fact]
    public void DefaultParameterValuesMatchSpecification()
    {
        var effect = new KintsugiEffect();

        Assert.Equal(100d, ValueAt(effect.Amount), 6);
        Assert.Equal(100d, ValueAt(effect.Growth), 6);
        Assert.Equal(KintsugiQuality.High, effect.Quality);
        Assert.Equal(100d, ValueAt(effect.Stress), 6);
        Assert.Equal(60d, ValueAt(effect.Shrinkage), 6);
        Assert.Equal(50d, ValueAt(effect.Toughness), 6);
        Assert.Equal(60d, ValueAt(effect.Relaxation), 6);
        Assert.Equal(50d, ValueAt(effect.Propagation), 6);
        Assert.Equal(70d, ValueAt(effect.LuminanceInfluence), 6);
        Assert.Equal(60d, ValueAt(effect.Irregularity), 6);
        Assert.Equal(0, effect.Seed);
        Assert.Equal(2.5d, ValueAt(effect.CrackWidth), 6);
        Assert.Equal(1.5d, ValueAt(effect.ErosionDepth), 6);
        Assert.Equal(100d, ValueAt(effect.LacquerFill), 6);
        Assert.Equal(75d, ValueAt(effect.GoldCoat), 6);
        Assert.Equal(40d, ValueAt(effect.Polish), 6);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x3A, 0x07, 0x05), effect.LacquerColor);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0xFF, 0xDE, 0xA3), effect.GoldColor);
        Assert.Equal(25d, ValueAt(effect.Roughness), 6);
        Assert.Equal(90d, ValueAt(effect.Isotropy), 6);
        Assert.Equal(-45d, ValueAt(effect.LightAngle), 6);
        Assert.Equal(45d, ValueAt(effect.LightElevation), 6);
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1234, 1234)]
    public void SeedClampsNegativeInputToZero(int input, int expected)
    {
        var effect = new KintsugiEffect { Seed = input };

        Assert.Equal(expected, effect.Seed);
    }

    [Fact]
    public void ColorPropertiesDiscardInputAlpha()
    {
        var effect = new KintsugiEffect
        {
            LacquerColor = System.Windows.Media.Color.FromArgb(1, 2, 3, 4),
            GoldColor = System.Windows.Media.Color.FromArgb(5, 6, 7, 8),
        };

        Assert.Equal(System.Windows.Media.Color.FromRgb(2, 3, 4), effect.LacquerColor);
        Assert.Equal(System.Windows.Media.Color.FromRgb(6, 7, 8), effect.GoldColor);
    }

    [Fact]
    public void CreateExoVideoFiltersReturnsEmpty()
    {
        var effect = new KintsugiEffect();

        Assert.Empty(effect.CreateExoVideoFilters(0, null!));
    }

    [Theory]
    [InlineData(KintsugiQuality.Balanced, 128, 256)]
    [InlineData(KintsugiQuality.High, 224, 512)]
    [InlineData(KintsugiQuality.Ultra, 384, 896)]
    public void QualitySettingsMatchSpecification(KintsugiQuality quality, int resolution, int maximumSteps)
    {
        var settings = KintsugiSettings.GetQuality(quality);

        Assert.Equal(resolution, settings.WorkingResolution);
        Assert.Equal(maximumSteps, settings.MaximumSteps);
    }

    [Theory]
    [InlineData(1920, 1080, 224)]
    [InlineData(1080, 1920, 224)]
    [InlineData(8, 8, 224)]
    [InlineData(4096, 16, 128)]
    [InlineData(1, 1, 384)]
    public void WorkingSizeCoversTheImageWithEquilateralSpacing(int width, int height, int resolution)
    {
        var (workingWidth, workingHeight, scaleX, scaleY) = KintsugiSettings.GetWorkingSize(width, height, resolution);

        Assert.True(workingWidth >= KintsugiSettings.MinimumWorkingSize);
        Assert.True(workingHeight >= KintsugiSettings.MinimumWorkingSize);
        Assert.True(scaleX > 0f);
        Assert.True(scaleY > 0f);
        Assert.Equal(scaleX * KintsugiSettings.TriangleHeight, scaleY, 6);
        Assert.True((workingWidth - 0.5f) * scaleX >= width - 1f);
        Assert.True((workingHeight - 1f) * scaleY >= height - 1f);
        Assert.True(Math.Max(workingWidth, workingHeight) <= Math.Max(resolution + 1, KintsugiSettings.MinimumWorkingSize));
    }

    [Fact]
    public void WorkingSizeReportsTheDimensionThatExceedsTheTextureLimit()
    {
        var widthException = Assert.Throws<ArgumentOutOfRangeException>(() => KintsugiSettings.GetWorkingSize(KintsugiSettings.MaximumTextureSize + 1, 1, 128));
        var heightException = Assert.Throws<ArgumentOutOfRangeException>(() => KintsugiSettings.GetWorkingSize(1, KintsugiSettings.MaximumTextureSize + 1, 128));

        Assert.Equal("width", widthException.ParamName);
        Assert.Equal("height", heightException.ParamName);
    }

    [Theory]
    [InlineData(0f, 1.5f)]
    [InlineData(0.05f, 1.5f)]
    [InlineData(2.5f, 2.25f)]
    [InlineData(20f, 11f)]
    public void SupportRadiusCoversHalfWidthAndAntialiasing(float crackWidth, float expected)
    {
        Assert.Equal(expected, KintsugiSettings.GetSupportRadius(crackWidth), 6);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(256, 1)]
    [InlineData(257, 2)]
    [InlineData(512, 2)]
    public void ReductionBlockCountRoundsUp(int vertexCount, int expected)
    {
        Assert.Equal(expected, KintsugiSettings.GetReductionBlockCount(vertexCount));
    }

    [Theory]
    [InlineData(-9, -12, -8)]
    [InlineData(-8, -8, -8)]
    [InlineData(-7, -8, -4)]
    [InlineData(-1, -4, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 4)]
    [InlineData(3, 0, 4)]
    [InlineData(4, 4, 4)]
    [InlineData(5, 4, 8)]
    public void FourPixelAlignmentHandlesNegativeCoordinates(int value, int floor, int ceiling)
    {
        Assert.Equal(floor, KintsugiMath.AlignFloor4(value));
        Assert.Equal(ceiling, KintsugiMath.AlignCeiling4(value));
        Assert.Equal(0, floor & 3);
        Assert.Equal(0, ceiling & 3);
        Assert.True(floor <= value);
        Assert.True(ceiling >= value);
    }

    [Theory]
    [InlineData(-9, -1, -12, 0)]
    [InlineData(-1, 1, -4, 4)]
    [InlineData(1, 5, 0, 8)]
    public void FourPixelAlignedRectangleIsTheSmallestContainingRectangle(int minimum, int maximum, int expectedMinimum, int expectedMaximum)
    {
        var alignedMinimum = KintsugiMath.AlignFloor4(minimum);
        var alignedMaximum = KintsugiMath.AlignCeiling4(maximum);

        Assert.Equal(expectedMinimum, alignedMinimum);
        Assert.Equal(expectedMaximum, alignedMaximum);
        Assert.Equal(0, alignedMaximum - alignedMinimum & 3);
        Assert.True(alignedMinimum + 4 > minimum || alignedMinimum == minimum);
        Assert.True(alignedMaximum - 4 < maximum || alignedMaximum == maximum);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.04045, 0.00313080495)]
    [InlineData(0.5, 0.21404114048)]
    [InlineData(1, 1)]
    public void SrgbTransferMatchesIecCurve(double encoded, double linear)
    {
        Assert.Equal(linear, KintsugiMath.SrgbToLinear((float)encoded), 6);
        Assert.Equal(encoded, KintsugiMath.LinearToSrgb((float)linear), 6);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0.5, 0.25)]
    [InlineData(0.21404114048, 1.44302008358)]
    [InlineData(0.0000152587890625, 32767.0000076294)]
    public void KubelkaMunkInverseMapsInfiniteReflectanceToAbsorptionRatio(double reflectance, double expected)
    {
        Assert.Equal(expected, KintsugiMath.AbsorptionToScatteringRatio((float)reflectance), 4);
    }

    [Theory]
    [InlineData(0.5, 0, 0, 1)]
    [InlineData(0.5, 1, 0.41138290319, 0.37520479082)]
    [InlineData(1, 1, 0.5, 0.5)]
    [InlineData(0.0000152587890625, 1, 0.0000152587890625, 0)]
    public void KubelkaMunkLayerMatchesClosedForm(double infiniteReflectance, double thickness, double reflectance, double transmittance)
    {
        var actual = KintsugiMath.KubelkaMunk((float)infiniteReflectance, (float)thickness);

        Assert.Equal(reflectance, actual.Reflectance, 5);
        Assert.Equal(transmittance, actual.Transmittance, 5);
        Assert.InRange(actual.Reflectance, 0f, 1f);
        Assert.InRange(actual.Transmittance, 0f, 1f);
    }

    [Fact]
    public void KubelkaMunkLayerCompositionMatchesAddingEquations()
    {
        var actual = KintsugiMath.CombineLayers(0.2f, 0.7f, 0.3f, 0.6f);

        Assert.Equal(0.356382978723, actual.Reflectance, 6);
        Assert.Equal(0.446808510638, actual.Transmittance, 6);
    }

    [Fact]
    public void LayerOperatorsFollowAbsoluteHeightDefinitions()
    {
        Assert.Equal(3f, KintsugiMath.Coat(1f, 2f));
        Assert.Equal(-1f, KintsugiMath.Erode(1f, 2f));
        Assert.Equal(3f, KintsugiMath.Fill(1f, 3f));
        Assert.Equal(1f, KintsugiMath.Fill(1f, -3f));
        Assert.Equal(1f, KintsugiMath.Polish(3f, 1f));
        Assert.Equal(-1f, KintsugiMath.Polish(-1f, 1f));
    }

    [Fact]
    public void SchlickFresnelMatchesFifthPowerApproximation()
    {
        Assert.Equal(0.4f, KintsugiMath.SchlickFresnel(0.4f, 1f), 6);
        Assert.Equal(0.41875f, KintsugiMath.SchlickFresnel(0.4f, 0.5f), 6);
        Assert.Equal(1f, KintsugiMath.SchlickFresnel(0.4f, 0f), 6);
        Assert.Equal(1f, KintsugiMath.SchlickFresnel(1f, 0.5f), 6);
    }

    [Fact]
    public void MeasuredGoldFresnelMatchesDefaultNormalReflectance()
    {
        Assert.Equal(1f, KintsugiSettings.GoldF0R);
        Assert.Equal(0.7294989f, KintsugiSettings.GoldF0G);
        Assert.Equal(0.3645575f, KintsugiSettings.GoldF0B);
        Assert.Equal(1f, KintsugiSettings.DefaultGoldSrgbR);
        Assert.Equal(0.870588235f, KintsugiSettings.DefaultGoldSrgbG);
        Assert.Equal(0.639215686f, KintsugiSettings.DefaultGoldSrgbB);
        Assert.Equal(1f, KintsugiMath.SchlickFresnel(1f, 0.5f), 6);
        Assert.Equal(0.737952045924, KintsugiMath.SchlickFresnel(0.729498886115f, 0.5f), 6);
        Assert.Equal(0.384415099476, KintsugiMath.SchlickFresnel(0.364557522040f, 0.5f), 6);
    }

    [Fact]
    public void FiniteAreaLightConstantsMatchConeSolidAngle()
    {
        var expected = 2d * Math.PI * (1d - KintsugiSettings.LightConeCosine);

        Assert.Equal(KintsugiSettings.LightConeSolidAngle, expected, 6);
        Assert.InRange(KintsugiSettings.LightConeCosine, 0f, 1f);
        Assert.True(KintsugiSettings.LightConeSolidAngle > 0f);
    }

    [Fact]
    public void SchlickDistributionAndGeometryMatchPublishedEquations()
    {
        Assert.Equal(0.378698224852, KintsugiMath.SchlickZenith(0.25f, 0.5f), 6);
        Assert.Equal(0.917662935482, KintsugiMath.SchlickAzimuth(0.25f, 0.5f), 6);
        Assert.Equal(0.8f, KintsugiMath.SchlickGeometry(0.25f, 0.5f), 6);
    }

    [Fact]
    public void SchlickDiracLimitsAreExplicit()
    {
        Assert.Equal(0f, KintsugiMath.SchlickZenith(0f, 0.5f));
        Assert.Equal(1f, KintsugiMath.SchlickZenith(0f, 1f));
        Assert.Equal(KintsugiSettings.AzimuthDiracHeight, KintsugiMath.SchlickAzimuth(0f, 0f));
        Assert.Equal(0f, KintsugiMath.SchlickAzimuth(0f, 0.5f));
        Assert.Equal(1f, KintsugiMath.SchlickGeometry(0f, 0.5f));
        Assert.Equal(0f, KintsugiMath.SchlickGeometry(0f, 0f));
    }

    [Theory]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0.25, 0, 0.75, 0.25)]
    [InlineData(0.5, 0, 1, 0)]
    [InlineData(0.75, 0.25, 0.75, 0)]
    [InlineData(1, 1, 0, 0)]
    public void SchlickWeightsPartitionUnity(double roughness, double diffuse, double microfacet, double dirac)
    {
        var weights = KintsugiMath.SchlickWeights((float)roughness);

        Assert.Equal(diffuse, weights.Diffuse, 6);
        Assert.Equal(microfacet, weights.Microfacet, 6);
        Assert.Equal(dirac, weights.Dirac, 6);
        Assert.Equal(1f, weights.Diffuse + weights.Microfacet + weights.Dirac, 6);
    }

    [Fact]
    public void TensorPartSeparatesPositiveAndNegativePrincipalStress()
    {
        var positive = KintsugiMath.TensorPart(0f, 0f, 1f, true);
        var negative = KintsugiMath.TensorPart(0f, 0f, 1f, false);

        Assert.Equal(0.5f, positive.Xx, 6);
        Assert.Equal(0.5f, positive.Yy, 6);
        Assert.Equal(0.5f, positive.Xy, 6);
        Assert.Equal(-0.5f, negative.Xx, 6);
        Assert.Equal(-0.5f, negative.Yy, 6);
        Assert.Equal(0.5f, negative.Xy, 6);
        Assert.Equal(0f, positive.Xx + negative.Xx, 6);
        Assert.Equal(0f, positive.Yy + negative.Yy, 6);
        Assert.Equal(1f, positive.Xy + negative.Xy, 6);
    }

    [Fact]
    public void MaximumEigenpairSatisfiesTheSymmetricEigenproblem()
    {
        var eigenpair = KintsugiMath.MaximumEigenpair(2f, 2f, 1f);

        Assert.Equal(3f, eigenpair.Value, 6);
        Assert.Equal(1f, eigenpair.X * eigenpair.X + eigenpair.Y * eigenpair.Y, 6);
        Assert.Equal(eigenpair.Value * eigenpair.X, 2f * eigenpair.X + eigenpair.Y, 6);
        Assert.Equal(eigenpair.Value * eigenpair.Y, eigenpair.X + 2f * eigenpair.Y, 6);
        Assert.True(eigenpair.X > 0f);
        Assert.True(eigenpair.Y > 0f);
    }

    [Fact]
    public void SeparationTensorCancelsResultantAndRetainsOpposedTension()
    {
        (float X, float Y)[] opposed = [(1f, 0f), (-1f, 0f)];
        (float X, float Y)[] aligned = [(1f, 0f), (1f, 0f)];
        var opposedTensor = KintsugiMath.SeparationTensor(opposed, []);
        var alignedTensor = KintsugiMath.SeparationTensor(aligned, []);
        var compressionTensor = KintsugiMath.SeparationTensor([], opposed);

        Assert.Equal((1f, 0f, 0f), opposedTensor);
        Assert.Equal((0f, 0f, 0f), alignedTensor);
        Assert.Equal((-1f, 0f, 0f), compressionTensor);
    }

    [Fact]
    public void EquilateralLatticeKeepsIsotropicStressSeparationIsotropic()
    {
        var forces = new (float X, float Y)[6];
        for (var sector = 0; sector < forces.Length; sector++)
        {
            var angle = (sector + 0.5f) * MathF.PI / 3f;
            forces[sector] = (0.5f * MathF.Cos(angle), 0.5f * MathF.Sin(angle));
        }

        var tensor = KintsugiMath.SeparationTensor(forces, []);

        Assert.Equal(0.75f, tensor.Xx, 5);
        Assert.Equal(0.75f, tensor.Yy, 5);
        Assert.Equal(0f, tensor.Xy, 5);
    }

    [Fact]
    public void PropagationResidualMatchesTipStressTensor()
    {
        var residual = KintsugiMath.PropagationResidual(1.2f, 0.5f, 0.25f, 0.6f, 0.8f);

        Assert.Equal(0.25f, KintsugiSettings.CeramicResidualBias);
        Assert.Equal(0.0855f, residual.Xx, 6);
        Assert.Equal(0.152f, residual.Yy, 6);
        Assert.Equal(0.114f, residual.Xy, 6);
        Assert.Equal((0f, 0f, 0f), KintsugiMath.PropagationResidual(1.2f, 0.5f, 0f, 0.6f, 0.8f));
        Assert.Equal(KintsugiMath.PropagationResidual(1.2f, 0.5f, 1f, 0.6f, 0.8f), KintsugiMath.PropagationResidual(1.2f, 0.5f, 5f, 0.6f, 0.8f));
    }

    [Fact]
    public void SymmetricDisplacementGradientMatchesLinearElasticStrain()
    {
        var strain = KintsugiMath.SymmetricDisplacementGradient(1.2f, 0.4f, -0.2f, 0.7f);
        var rotation = KintsugiMath.SymmetricDisplacementGradient(1f, 2f, -2f, 1f);

        Assert.Equal(0.2f, strain.Xx, 6);
        Assert.Equal(-0.3f, strain.Yy, 6);
        Assert.Equal(0.1f, strain.Xy, 6);
        Assert.Equal((0f, 0f, 0f), rotation);
    }

    [Fact]
    public void PaperMathExtremeInputsStayFiniteWhereTheLimitIsFinite()
    {
        var negativeThickness = KintsugiMath.KubelkaMunk(0.5f, float.NegativeInfinity);
        var infiniteThickness = KintsugiMath.KubelkaMunk(0.5f, float.PositiveInfinity);
        var singularLayers = KintsugiMath.CombineLayers(1f, 1f, 1f, 1f);

        Assert.Equal((0f, 1f), negativeThickness);
        Assert.True(float.IsFinite(infiniteThickness.Reflectance));
        Assert.True(float.IsFinite(infiniteThickness.Transmittance));
        Assert.Equal(0.5f, infiniteThickness.Reflectance, 5);
        Assert.Equal(0f, infiniteThickness.Transmittance, 5);
        Assert.Equal((1f, 0f), singularLayers);
        Assert.Equal(0f, KintsugiMath.SchlickZenith(float.NegativeInfinity, 0.5f));
        Assert.Equal(1f, KintsugiMath.SchlickGeometry(float.PositiveInfinity, 1f));
    }

    [Fact]
    public void TransparentInputYieldsTransparentOutput()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = new int[width * height];
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
        Assert.Equal(0, pipeline.WrittenEdgeCount);
        Assert.False(pipeline.TryGetVisibleBounds(width, height, in parameters, out _));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 64)]
    [InlineData(64, 1)]
    public void DegenerateImageAreaDoesNotGenerateFracture(int width, int height)
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        var source = new int[width * height];
        Array.Fill(source, unchecked((int)0xFFC09070));
        var destination = new int[source.Length];
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.Equal(0, pipeline.WrittenEdgeCount);
        Assert.False(pipeline.TryGetVisibleBounds(width, height, in parameters, out _));
        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void GrowthZeroYieldsTransparentOutputWithoutDiscardingStructure()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = CreateCeramicSource(width, height);
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters(growth: 0f);

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.True(pipeline.WrittenEdgeCount > 0);
        Assert.All(destination, pixel => Assert.Equal(0, pixel));
        Assert.False(pipeline.TryGetVisibleBounds(width, height, in parameters, out _));
    }

    [Fact]
    public void DefaultParametersProduceCrackLayers()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 72;
        var source = CreateCeramicSource(width, height);
        var destination = new int[source.Length];
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.True(pipeline.WrittenEdgeCount > 0);
        Assert.True(CountLitPixels(destination) > 0);
    }

    [Fact]
    public void GpuPipelineIsDeterministic()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 72;
        var source = CreateCeramicSource(width, height);
        var first = new int[source.Length];
        var second = new int[source.Length];
        var parameters = CreateParameters(irregularity: 0.8f, seed: 42);

        pipeline.Process(source, first, width, height, in parameters);
        var firstLog = pipeline.EdgeLog.ToArray();
        pipeline.Process(source, second, width, height, in parameters);

        Assert.Equal(first, second);
        Assert.Equal(firstLog, pipeline.EdgeLog.ToArray());
    }

    [Fact]
    public void DifferentSeedsProduceDifferentCrackNetworks()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 72;
        var source = CreateCeramicSource(width, height);
        var first = new int[source.Length];
        var second = new int[source.Length];
        var firstParameters = CreateParameters(irregularity: 1f, seed: 1);
        var secondParameters = firstParameters with { Seed = 2 };

        pipeline.Process(source, first, width, height, in firstParameters);
        var firstLog = pipeline.EdgeLog.ToArray();
        pipeline.Process(source, second, width, height, in secondParameters);

        Assert.NotEqual(firstLog, pipeline.EdgeLog.ToArray());
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GrowthRevealsASequentialPrefix()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 72;
        var source = CreateCeramicSource(width, height);
        var partial = new int[source.Length];
        var full = new int[source.Length];
        var partialParameters = CreateParameters(growth: 0.25f);
        var fullParameters = partialParameters with { Growth = 1f };

        pipeline.Process(source, partial, width, height, in partialParameters);
        var log = pipeline.EdgeLog.ToArray();
        pipeline.Process(source, full, width, height, in fullParameters);

        Assert.Equal(log, pipeline.EdgeLog.ToArray());
        Assert.True(CountLitPixels(partial) > 0);
        Assert.True(CountLitPixels(full) > CountLitPixels(partial));
    }

    [Fact]
    public void OutputAlphaStaysPremultipliedAndBounded()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 72;
        var source = CreateCeramicSource(width, height);
        var destination = new int[source.Length];
        var parameters = CreateParameters(crackWidth: 20f, erosionDepth: 10f, goldCoat: 1f, polish: 1f, roughness: 0f);

        pipeline.Process(source, destination, width, height, in parameters);

        foreach (var pixel in destination)
        {
            var alpha = (pixel >> 24) & 255;
            Assert.InRange((pixel >> 16) & 255, 0, alpha);
            Assert.InRange((pixel >> 8) & 255, 0, alpha);
            Assert.InRange(pixel & 255, 0, alpha);
        }
    }

    [Fact]
    public void EdgeLogRepresentsUniqueSequentialFractureEvents()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 80;
        const int height = 64;
        var source = CreateCeramicSource(width, height);
        var destination = new int[source.Length];
        var parameters = CreateParameters(toughness: 0.2f, irregularity: 0.8f);
        pipeline.Process(source, destination, width, height, in parameters);

        var log = pipeline.EdgeLog;
        var maximumSteps = KintsugiSettings.GetQuality(parameters.Quality).MaximumSteps;
        Assert.Equal(pipeline.GeneratedSteps * KintsugiSettings.EdgesPerStep, log.Length);
        Assert.InRange(pipeline.GeneratedSteps, 1, maximumSteps + pipeline.WorkingWidth * pipeline.WorkingHeight * KintsugiSettings.EdgesPerVertex);
        Assert.True(pipeline.WrittenEdgeCount > 0);
        var loggedEdgeCount = 0;
        foreach (var record in log)
            loggedEdgeCount += record >= 0 ? 1 : 0;
        Assert.Equal(pipeline.WrittenEdgeCount, loggedEdgeCount);
        Assert.True(pipeline.FinishingExhaustedCandidates);
        var uniqueEdges = new HashSet<int>();
        var reachedEnd = false;
        for (var step = 0; step < pipeline.GeneratedSteps; step++)
        {
            var first = log[step * KintsugiSettings.EdgesPerStep];
            var second = log[step * KintsugiSettings.EdgesPerStep + 1];
            if (first < 0)
            {
                Assert.Equal(-1, second);
                reachedEnd = true;
                continue;
            }
            Assert.False(reachedEnd);
            AssertValidEdgeRecord(first, pipeline.WorkingWidth, pipeline.WorkingHeight);
            Assert.True(uniqueEdges.Add(first >> 1));
            if (second >= 0)
            {
                AssertValidEdgeRecord(second, pipeline.WorkingWidth, pipeline.WorkingHeight);
                Assert.True(uniqueEdges.Add(second >> 1));
                Assert.Equal(GetDirectedStart(first, pipeline.WorkingWidth), GetDirectedStart(second, pipeline.WorkingWidth));
            }
        }
    }

    [Fact]
    public void FractureLogContainsOnlyEdgesWithTwoMaterialTriangles()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 72;
        var source = new int[width * height];
        Array.Fill(source, unchecked((int)0xFFC09070));
        var destination = new int[source.Length];
        var parameters = CreateParameters();
        pipeline.Process(source, destination, width, height, in parameters);

        var material = new bool[pipeline.Positions.Length];
        for (var vertex = 0; vertex < material.Length; vertex++)
        {
            var position = pipeline.Positions[vertex];
            var px = pipeline.OffsetX + position.X * pipeline.Scale;
            var py = pipeline.OffsetY + position.Y * pipeline.Scale;
            material[vertex] = px >= 0f && py >= 0f && px <= width - 1f && py <= height - 1f;
        }
        var incidence = new Dictionary<long, int>();
        for (var y = 0; y < pipeline.WorkingHeight - 1; y++)
        {
            for (var x = 0; x < pipeline.WorkingWidth - 1; x++)
            {
                var a = y * pipeline.WorkingWidth + x;
                var b = a + 1;
                var c = a + pipeline.WorkingWidth;
                var d = c + 1;
                if ((y & 1) == 0)
                {
                    AddMaterialTriangle(a, b, c, material, incidence);
                    AddMaterialTriangle(b, d, c, material, incidence);
                }
                else
                {
                    AddMaterialTriangle(a, b, d, material, incidence);
                    AddMaterialTriangle(a, d, c, material, incidence);
                }
            }
        }

        foreach (var record in pipeline.EdgeLog)
        {
            if (record < 0)
                continue;
            var first = GetDirectedStart(record & ~1, pipeline.WorkingWidth);
            var second = GetDirectedEnd(record & ~1, pipeline.WorkingWidth);
            Assert.Equal(2, incidence[EdgeKey(first, second)]);
        }
    }

    [Fact]
    public void MaximumIrregularityKeepsEveryTrianglePositivelyOriented()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 72;
        var device = GraphicsDevice.GetDefault();
        using var source = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var pixels = new int[width * height];
        Array.Fill(pixels, unchecked((int)0xFFC09070));
        source.CopyFrom(ToBgra32(pixels));
        var parameters = CreateParameters(irregularity: 1f);
        pipeline.Simulate(source, width, height, in parameters);

        for (var y = 0; y < pipeline.WorkingHeight - 1; y++)
        {
            for (var x = 0; x < pipeline.WorkingWidth - 1; x++)
            {
                var a = y * pipeline.WorkingWidth + x;
                var b = a + 1;
                var c = a + pipeline.WorkingWidth;
                var d = c + 1;
                if ((y & 1) == 0)
                {
                    Assert.True(SignedArea(pipeline.Positions[a], pipeline.Positions[b], pipeline.Positions[c]) > 0f);
                    Assert.True(SignedArea(pipeline.Positions[b], pipeline.Positions[d], pipeline.Positions[c]) > 0f);
                }
                else
                {
                    Assert.True(SignedArea(pipeline.Positions[a], pipeline.Positions[b], pipeline.Positions[d]) > 0f);
                    Assert.True(SignedArea(pipeline.Positions[a], pipeline.Positions[d], pipeline.Positions[c]) > 0f);
                }
            }
        }
    }

    [Fact]
    public void CrackTipFinishingContinuesBeyondTheNormalStepBudgetAndTerminates()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 320;
        const int height = 240;
        var source = CreateCeramicSource(width, height);
        var destination = new int[source.Length];
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.True(pipeline.GeneratedSteps > KintsugiSettings.GetQuality(parameters.Quality).MaximumSteps);
        Assert.True(pipeline.FinishingExhaustedCandidates);
        Assert.Equal(pipeline.GeneratedSteps * KintsugiSettings.EdgesPerStep, pipeline.EdgeLog.Length);
    }

    [Fact]
    public void LowToughnessNetworkContainsTJunctionsAndClosedCycles()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = CreateTopologySource(width, height);
        var destination = new int[source.Length];
        var parameters = CreateParameters(
            seed: 0,
            stress: 2f,
            shrinkage: 1f,
            toughness: 0.01f,
            relaxation: 0.2f,
            propagation: 0.25f,
            irregularity: 0.3f);

        pipeline.Process(source, destination, width, height, in parameters);
        var topology = CalculateTopology(pipeline.EdgeLog, pipeline.WorkingWidth);

        Assert.True(topology.MaximumDegree >= 3);
        Assert.True(topology.CycleRank > 0);
    }

    [Fact]
    public void SharedTexturePipelineMatchesPackedBufferPipeline()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 80;
        const int height = 64;
        var source = CreateCeramicSource(width, height);
        var expected = new int[source.Length];
        var parameters = CreateParameters(seed: 11);
        pipeline.Process(source, expected, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        sourceTexture.CopyFrom(ToBgra32(source));
        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, width, height, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(unchecked((uint)expected[index]), result[index].PackedValue);
    }

    [Fact]
    public void AsynchronousVisibleRenderMatchesTheSynchronousRender()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 80;
        const int height = 64;
        var source = CreateCeramicSource(width, height);
        var parameters = CreateParameters(seed: 13);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        using var synchronousTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        using var asynchronousTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        sourceTexture.CopyFrom(ToBgra32(source));
        pipeline.Simulate(sourceTexture, width, height, in parameters);
        var rect = new KintsugiPipeline.PixelRect(0, 0, width, height);

        pipeline.RenderVisible(sourceTexture, synchronousTexture, width, height, rect, in parameters);
        pipeline.RenderVisibleAsync(sourceTexture, asynchronousTexture, width, height, rect, in parameters);
        pipeline.WaitForCompletion();

        var synchronous = new Bgra32[source.Length];
        var asynchronous = new Bgra32[source.Length];
        synchronousTexture.CopyTo(synchronous);
        asynchronousTexture.CopyTo(asynchronous);
        Assert.Equal(synchronous.Select(pixel => pixel.PackedValue), asynchronous.Select(pixel => pixel.PackedValue));
    }

    [Fact]
    public void PackedPipelineDoesNotAllocateManagedMemoryAfterWarmup()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 48;
        const int height = 40;
        var source = CreateCeramicSource(width, height);
        var destination = new int[source.Length];
        var parameters = CreateParameters();
        pipeline.Process(source, destination, width, height, in parameters);
        pipeline.Process(source, destination, width, height, in parameters);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        pipeline.Process(source, destination, width, height, in parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SubmittedSharedTexturePipelineDoesNotAllocateManagedMemoryAfterWarmup()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        var device = GraphicsDevice.GetDefault();
        if (!device.IsHardwareAccelerated)
        {
            Assert.Skip("A hardware GPU is required to keep the submission queue drained.");
            return;
        }

        const int width = 48;
        const int height = 40;
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        source.CopyFrom(ToBgra32(CreateCeramicSource(width, height)));
        var parameters = CreateParameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, width, height, in parameters);
        pipeline.WaitForCompletion();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        pipeline.Process(source, destination, width, height, in parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        pipeline.WaitForCompletion();

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void VisibleBoundsAreTheMinimumFourPixelAlignedSupportRectangle()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 72;
        var source = CreateCeramicSource(width, height);
        var full = new int[source.Length];
        var parameters = CreateParameters(crackWidth: 5.5f, growth: 0.63f);
        pipeline.Process(source, full, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        sourceTexture.CopyFrom(ToBgra32(source));
        Assert.False(pipeline.Simulate(sourceTexture, width, height, in parameters));
        Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect));
        Assert.Equal(CalculateExpectedBounds(pipeline, width, height, in parameters), rect);
        Assert.Equal(0, rect.X & 3);
        Assert.Equal(0, rect.Y & 3);
        Assert.Equal(0, rect.Width & 3);
        Assert.Equal(0, rect.Height & 3);

        using var visibleTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(sourceTexture, visibleTexture, width, height, rect, in parameters);
        var visible = new Bgra32[rect.Width * rect.Height];
        visibleTexture.CopyTo(visible);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var visibleX = x - rect.X;
                var visibleY = y - rect.Y;
                if (visibleX >= 0 && visibleX < rect.Width && visibleY >= 0 && visibleY < rect.Height)
                    Assert.Equal(unchecked((uint)full[y * width + x]), visible[visibleY * rect.Width + visibleX].PackedValue);
                else
                    Assert.Equal(0, full[y * width + x]);
            }
        }
    }

    [Fact]
    public void RenderParametersDoNotInvalidateTheFractureStructure()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 48;
        const int height = 40;
        var device = GraphicsDevice.GetDefault();
        using var source = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        source.CopyFrom(ToBgra32(CreateCeramicSource(width, height)));
        var current = CreateParameters();
        Assert.True(pipeline.Simulate(source, width, height, in current));
        var changes = new Func<KintsugiPipeline.Parameters, KintsugiPipeline.Parameters>[]
        {
            value => value with { Growth = 0.5f },
            value => value with { CrackWidth = 8f },
            value => value with { ErosionDepth = 4f },
            value => value with { LacquerFill = 0.4f },
            value => value with { GoldCoat = 0.2f },
            value => value with { Polish = 0.8f },
            value => value with { LacquerR = 0.8f, LacquerG = 0.2f, LacquerB = 0.1f },
            value => value with { GoldR = 0.8f, GoldG = 0.7f, GoldB = 0.5f },
            value => value with { Roughness = 0.8f },
            value => value with { Isotropy = 0.2f },
            value => value with { LightAngleDegrees = 120f },
            value => value with { LightElevationDegrees = 20f },
        };
        foreach (var change in changes)
        {
            current = change(current);
            Assert.False(pipeline.Simulate(source, width, height, in current));
        }
    }

    [Fact]
    public void EveryStructuralParameterInvalidatesOnlyWhenItsValueChanges()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 32;
        const int height = 24;
        var device = GraphicsDevice.GetDefault();
        using var source = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        source.CopyFrom(ToBgra32(CreateCeramicSource(width, height)));
        var current = CreateParameters();
        Assert.True(pipeline.Simulate(source, width, height, in current));
        var changes = new Func<KintsugiPipeline.Parameters, KintsugiPipeline.Parameters>[]
        {
            value => value with { Quality = KintsugiQuality.High },
            value => value with { Seed = 8 },
            value => value with { Stress = 1.2f },
            value => value with { Shrinkage = 0.7f },
            value => value with { Toughness = 0.4f },
            value => value with { Relaxation = 0.7f },
            value => value with { Propagation = 0.7f },
            value => value with { LuminanceInfluence = 0.6f },
            value => value with { Irregularity = 0.5f },
        };
        foreach (var change in changes)
        {
            current = change(current);
            Assert.True(pipeline.Simulate(source, width, height, in current));
            Assert.False(pipeline.Simulate(source, width, height, in current));
        }
    }

    [Fact]
    public void InputLuminanceAndAlphaChangesInvalidateTheStructureExactly()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 48;
        const int height = 40;
        var device = GraphicsDevice.GetDefault();
        using var source = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var pixels = ToBgra32(CreateCeramicSource(width, height));
        source.CopyFrom(pixels);
        var parameters = CreateParameters();
        Assert.True(pipeline.Simulate(source, width, height, in parameters));
        Assert.False(pipeline.Simulate(source, width, height, in parameters));

        pixels[width / 2 + height / 2 * width].PackedValue = 0xFFFFFFFFu;
        source.CopyFrom(pixels);
        Assert.True(pipeline.Simulate(source, width, height, in parameters));
        Assert.False(pipeline.Simulate(source, width, height, in parameters));

        var alphaSample = pipeline.Positions
            .ToArray()
            .Select(position => (
                Px: pipeline.OffsetX + position.X * pipeline.Scale,
                Py: pipeline.OffsetY + position.Y * pipeline.Scale))
            .Where(point => point.Px >= 0f && point.Py >= 0f && point.Px <= width - 1f && point.Py <= height - 1f)
            .Select(point => (X: (int)(point.Px + 0.5f), Y: (int)(point.Py + 0.5f)))
            .First(point => point.X != width / 2 || point.Y != height / 2);
        pixels[alphaSample.Y * width + alphaSample.X].PackedValue = 0u;
        source.CopyFrom(pixels);
        Assert.True(pipeline.Simulate(source, width, height, in parameters));
        Assert.False(pipeline.Simulate(source, width, height, in parameters));
    }

    [Fact]
    public void EqualQuantizedLuminancePreservesStructureButUpdatesBaseLayerColor()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 48;
        const int neutral = unchecked((int)0xFF808080);
        const int chromatic = unchecked((int)0xFFB27180);
        Assert.Equal(QuantizedLuminance(neutral), QuantizedLuminance(chromatic));
        var firstSource = new int[width * height];
        var secondSource = new int[width * height];
        Array.Fill(firstSource, neutral);
        Array.Fill(secondSource, chromatic);
        var device = GraphicsDevice.GetDefault();
        using var source = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var parameters = CreateParameters(crackWidth: 5f, erosionDepth: 0.5f, lacquerFill: 0.5f, goldCoat: 0f);
        source.CopyFrom(ToBgra32(firstSource));
        Assert.True(pipeline.Simulate(source, width, height, in parameters));
        pipeline.RenderVisible(source, output, width, height, new KintsugiPipeline.PixelRect(0, 0, width, height), in parameters);
        var first = new Bgra32[firstSource.Length];
        output.CopyTo(first);

        source.CopyFrom(ToBgra32(secondSource));
        Assert.False(pipeline.Simulate(source, width, height, in parameters));
        pipeline.RenderVisible(source, output, width, height, new KintsugiPipeline.PixelRect(0, 0, width, height), in parameters);
        var second = new Bgra32[secondSource.Length];
        output.CopyTo(second);

        Assert.False(first.Select(pixel => pixel.PackedValue).SequenceEqual(second.Select(pixel => pixel.PackedValue)));
    }

    [Fact]
    public void LayerRenderingDoesNotCreateOpaqueBlackHaloOutsideMaterial()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 72;
        const int left = 24;
        const int top = 18;
        const int right = 72;
        const int bottom = 54;
        var pixels = new int[width * height];
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
                pixels[y * width + x] = unchecked((int)0xFFC09070);
        }
        var device = GraphicsDevice.GetDefault();
        using var source = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        source.CopyFrom(ToBgra32(pixels));
        var parameters = CreateParameters(stress: 2f, shrinkage: 1f, toughness: 0.2f, crackWidth: 20f, goldCoat: 1f, polish: 0f);
        Assert.True(pipeline.Simulate(source, width, height, in parameters));
        Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect));
        using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(source, output, width, height, rect, in parameters);
        var overlay = new Bgra32[rect.Width * rect.Height];
        output.CopyTo(overlay);

        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                var globalX = rect.X + x;
                var globalY = rect.Y + y;
                if (globalX >= left && globalX < right && globalY >= top && globalY < bottom)
                    continue;
                var packed = overlay[y * rect.Width + x].PackedValue;
                var alpha = (int)(packed >> 24);
                if (alpha <= 0)
                    continue;
                var red = (int)(packed >> 16 & 255);
                var green = (int)(packed >> 8 & 255);
                var blue = (int)(packed & 255);
                Assert.False(alpha == 255 && red == 0 && green == 0 && blue == 0);
            }
        }
        Assert.Contains(overlay, pixel => pixel.PackedValue >> 24 > 0);
    }

    [Fact]
    public void ZeroLayersProduceNoOverlayOnSemitransparentMaterial()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 48;
        var source = new int[width * height];
        Array.Fill(source, unchecked((int)0x80604030));
        var overlay = new int[source.Length];
        Array.Fill(overlay, -1);
        var parameters = CreateParameters(erosionDepth: 0f, lacquerFill: 0f, goldCoat: 0f);

        pipeline.Process(source, overlay, width, height, in parameters);

        Assert.True(pipeline.WrittenEdgeCount > 0);
        Assert.False(pipeline.TryGetVisibleBounds(width, height, in parameters, out _));
        foreach (var pixel in overlay)
        {
            Assert.Equal(0, pixel);
            Assert.Equal(128, CompositeAlpha(128, (pixel >> 24) & 255));
        }
    }

    [Fact]
    public void ErosionWithoutLacquerOrGoldProducesAVisibleBoundedGroove()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 72;
        var source = new int[width * height];
        Array.Fill(source, unchecked((int)0xFFC09070));
        var overlay = new int[source.Length];
        var parameters = CreateParameters(
            crackWidth: 8f,
            erosionDepth: 6f,
            lacquerFill: 0f,
            goldCoat: 0f,
            lightAngleDegrees: 0f,
            lightElevationDegrees: 20f);

        pipeline.Process(source, overlay, width, height, in parameters);

        Assert.True(CountLitPixels(overlay) > 0);
        foreach (var pixel in overlay)
        {
            var alpha = (pixel >> 24) & 255;
            Assert.InRange((pixel >> 16) & 255, 0, alpha);
            Assert.InRange((pixel >> 8) & 255, 0, alpha);
            Assert.InRange(pixel & 255, 0, alpha);
        }
    }

    [Fact]
    public void SemitransparentLayerOverlayCompositesToBoundedNondecreasingAlpha()
    {
        using var pipeline = KintsugiPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 48;
        const int sourcePixel = unchecked((int)0x80604030);
        const int sourceAlpha = 128;
        var source = new int[width * height];
        Array.Fill(source, sourcePixel);
        var overlay = new int[source.Length];
        var parameters = CreateParameters(crackWidth: 6f, erosionDepth: 2f, lacquerFill: 1f, goldCoat: 1f, polish: 0f);

        pipeline.Process(source, overlay, width, height, in parameters);

        Assert.True(CountLitPixels(overlay) > 0);
        foreach (var pixel in overlay)
        {
            var overlayAlpha = (pixel >> 24) & 255;
            Assert.InRange((pixel >> 16) & 255, 0, overlayAlpha);
            Assert.InRange((pixel >> 8) & 255, 0, overlayAlpha);
            Assert.InRange(pixel & 255, 0, overlayAlpha);
            var compositedAlpha = overlayAlpha + sourceAlpha * (255 - overlayAlpha) / 255;
            Assert.InRange(compositedAlpha, sourceAlpha, 255);
            var normalizedAlpha = overlayAlpha / 255f + sourceAlpha / 255f * (1f - overlayAlpha / 255f);
            Assert.InRange(MathF.Abs(compositedAlpha / 255f - normalizedAlpha), 0f, 1f / 255f);
        }
    }

    [Fact]
    public void Direct2DInteropProducesPremultipliedKintsugiOverlay()
    {
        using var devices = new GraphicsDevices();
        using var graphicsContext = devices.CreateContext();
        using var interop = KintsugiGpuInterop.TryCreate(graphicsContext);
        if (interop is null)
        {
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            return;
        }

        using var pipeline = KintsugiPipeline.TryCreate(interop.Device);
        Assert.NotNull(pipeline);

        const int width = 80;
        const int height = 64;
        var pixels = CreateCeramicSource(width, height);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        using var inputBitmap = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.None));
        try
        {
            inputBitmap.CopyFromMemory(handle.AddrOfPinnedObject(), width * sizeof(int));
        }
        finally
        {
            handle.Free();
        }

        Assert.True(interop.EnsureResources(width, height));
        var bounds = new RawRectF(0f, 0f, width, height);
        var parameters = CreateParameters();
        for (var iteration = 0; iteration < 2; iteration++)
        {
            interop.RenderInput(inputBitmap, bounds);
            interop.BeginCompute();
            try
            {
                pipeline!.Process(interop.SourceTexture, interop.OutputTexture, width, height, in parameters);
            }
            finally
            {
                interop.EndCompute();
            }
        }
        interop.WaitForIdle();

        using var staging = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        staging.CopyFromBitmap(interop.OutputBitmap);
        var mapped = staging.Map(MapOptions.Read);
        try
        {
            var lit = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var actual = Marshal.ReadInt32(mapped.Bits + (nint)(y * mapped.Pitch + x * sizeof(int)));
                    var alpha = (actual >> 24) & 255;
                    Assert.InRange((actual >> 16) & 255, 0, alpha);
                    Assert.InRange((actual >> 8) & 255, 0, alpha);
                    Assert.InRange(actual & 255, 0, alpha);
                    lit += alpha > 0 ? 1 : 0;
                }
            }
            Assert.True(lit > 0);
        }
        finally
        {
            staging.Unmap();
        }
    }

    [Fact]
    public void InvalidWorkingDimensionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => KintsugiSettings.GetWorkingSize(0, 1, 128));
        Assert.Throws<ArgumentOutOfRangeException>(() => KintsugiSettings.GetWorkingSize(1, 0, 128));
        Assert.Throws<ArgumentOutOfRangeException>(() => KintsugiSettings.GetWorkingSize(KintsugiSettings.MaximumTextureSize + 1, 1, 128));
        Assert.Throws<ArgumentOutOfRangeException>(() => KintsugiSettings.GetWorkingSize(1, KintsugiSettings.MaximumTextureSize + 1, 128));
    }

    private static KintsugiPipeline.PixelRect CalculateExpectedBounds(KintsugiPipeline pipeline, int width, int height, in KintsugiPipeline.Parameters parameters)
    {
        var generatedSteps = pipeline.GeneratedSteps;
        var visibleSteps = Math.Clamp(parameters.Growth, 0f, 1f) * generatedSteps;
        var lastStep = Math.Min((int)Math.Ceiling(visibleSteps), generatedSteps);
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        for (var step = 0; step < lastStep; step++)
        {
            var fraction = Math.Clamp(visibleSteps - step, 0f, 1f);
            for (var slotIndex = 0; slotIndex < KintsugiSettings.EdgesPerStep; slotIndex++)
            {
                var record = pipeline.EdgeLog[step * KintsugiSettings.EdgesPerStep + slotIndex];
                if (record < 0)
                    continue;
                GetSegment(record, pipeline.WorkingWidth, pipeline.Positions, pipeline.Scale, pipeline.OffsetX, pipeline.OffsetY, fraction, out var ax, out var ay, out var bx, out var by);
                minX = Math.Min(minX, Math.Min(ax, bx));
                minY = Math.Min(minY, Math.Min(ay, by));
                maxX = Math.Max(maxX, Math.Max(ax, bx));
                maxY = Math.Max(maxY, Math.Max(ay, by));
            }
        }
        var radius = KintsugiSettings.GetSupportRadius(parameters.CrackWidth);
        var left = KintsugiMath.AlignFloor4((int)Math.Floor(minX - radius + 0.5f));
        var top = KintsugiMath.AlignFloor4((int)Math.Floor(minY - radius + 0.5f));
        var right = KintsugiMath.AlignCeiling4((int)Math.Ceiling(maxX + radius - 0.5f));
        var bottom = KintsugiMath.AlignCeiling4((int)Math.Ceiling(maxY + radius - 0.5f));
        return new KintsugiPipeline.PixelRect(left, top, right - left, bottom - top);
    }

    private static void GetSegment(int record, int workingWidth, ReadOnlySpan<Float2> positions, float scale, float offsetX, float offsetY, float fraction, out float ax, out float ay, out float bx, out float by)
    {
        var edge = record >> 1;
        var orientation = record & 1;
        var slot = edge % KintsugiSettings.EdgesPerVertex;
        var origin = edge / KintsugiSettings.EdgesPerVertex;
        var y = origin / workingWidth;
        var parity = y & 1;
        var target = slot == 0 ? origin + 1 : slot == 1 ? origin + workingWidth + parity : origin + workingWidth + parity - 1;
        var first = positions[origin];
        var second = positions[target];
        ax = offsetX + first.X * scale;
        ay = offsetY + first.Y * scale;
        bx = offsetX + second.X * scale;
        by = offsetY + second.Y * scale;
        if (orientation != 0)
        {
            (ax, bx) = (bx, ax);
            (ay, by) = (by, ay);
        }
        bx = ax + (bx - ax) * fraction;
        by = ay + (by - ay) * fraction;
    }

    private static void AssertValidEdgeRecord(int record, int workingWidth, int workingHeight)
    {
        Assert.True(record >= 0);
        var edge = record >> 1;
        Assert.InRange(edge, 0, workingWidth * workingHeight * KintsugiSettings.EdgesPerVertex - 1);
        var slot = edge % KintsugiSettings.EdgesPerVertex;
        var origin = edge / KintsugiSettings.EdgesPerVertex;
        var x = origin % workingWidth;
        var y = origin / workingWidth;
        if (slot == 0)
            Assert.True(x < workingWidth - 1);
        else
        {
            Assert.True(y < workingHeight - 1);
            var targetX = slot == 1 ? x + (y & 1) : x + (y & 1) - 1;
            Assert.InRange(targetX, 0, workingWidth - 1);
        }
        var start = GetDirectedStart(record, workingWidth);
        var end = GetDirectedEnd(record, workingWidth);
        Assert.InRange(start, 0, workingWidth * workingHeight - 1);
        Assert.InRange(end, 0, workingWidth * workingHeight - 1);
        Assert.NotEqual(start, end);
    }

    private static int GetDirectedStart(int record, int workingWidth)
    {
        var edge = record >> 1;
        var slot = edge % KintsugiSettings.EdgesPerVertex;
        var origin = edge / KintsugiSettings.EdgesPerVertex;
        var parity = origin / workingWidth & 1;
        var target = slot == 0 ? origin + 1 : slot == 1 ? origin + workingWidth + parity : origin + workingWidth + parity - 1;
        return (record & 1) == 0 ? origin : target;
    }

    private static int GetDirectedEnd(int record, int workingWidth)
    {
        var edge = record >> 1;
        var slot = edge % KintsugiSettings.EdgesPerVertex;
        var origin = edge / KintsugiSettings.EdgesPerVertex;
        var parity = origin / workingWidth & 1;
        var target = slot == 0 ? origin + 1 : slot == 1 ? origin + workingWidth + parity : origin + workingWidth + parity - 1;
        return (record & 1) == 0 ? target : origin;
    }

    private static void AddMaterialTriangle(int first, int second, int third, bool[] material, Dictionary<long, int> incidence)
    {
        if (!material[first] || !material[second] || !material[third])
            return;
        IncrementEdge(first, second, incidence);
        IncrementEdge(second, third, incidence);
        IncrementEdge(third, first, incidence);
    }

    private static void IncrementEdge(int first, int second, Dictionary<long, int> incidence)
    {
        var key = EdgeKey(first, second);
        incidence[key] = incidence.GetValueOrDefault(key) + 1;
    }

    private static long EdgeKey(int first, int second)
        => (long)Math.Min(first, second) << 32 | (uint)Math.Max(first, second);

    private static float SignedArea(Float2 first, Float2 second, Float2 third)
        => (second.X - first.X) * (third.Y - first.Y) - (second.Y - first.Y) * (third.X - first.X);

    private static int[] CreateCeramicSource(int width, int height)
    {
        var source = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var variation = (x * 17 + y * 31 + x * y % 47) % 96;
                var red = 128 + variation;
                var green = 96 + variation * 3 / 4;
                var blue = 72 + variation / 2;
                source[y * width + x] = unchecked((int)0xFF000000) | red << 16 | green << 8 | blue;
            }
        }
        return source;
    }

    private static int[] CreateTopologySource(int width, int height)
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

    private static (int MaximumDegree, int CycleRank) CalculateTopology(ReadOnlySpan<int> edgeLog, int workingWidth)
    {
        var adjacency = new Dictionary<int, List<int>>();
        var edges = new HashSet<int>();
        foreach (var record in edgeLog)
        {
            if (record < 0 || !edges.Add(record >> 1))
                continue;
            var first = GetDirectedStart(record & ~1, workingWidth);
            var second = GetDirectedEnd(record & ~1, workingWidth);
            if (!adjacency.TryGetValue(first, out var firstNeighbors))
                adjacency[first] = firstNeighbors = [];
            if (!adjacency.TryGetValue(second, out var secondNeighbors))
                adjacency[second] = secondNeighbors = [];
            firstNeighbors.Add(second);
            secondNeighbors.Add(first);
        }
        var visited = new HashSet<int>();
        var stack = new Stack<int>();
        var components = 0;
        var maximumDegree = 0;
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

    private static Bgra32[] ToBgra32(int[] pixels)
    {
        var result = new Bgra32[pixels.Length];
        for (var index = 0; index < pixels.Length; index++)
            result[index].PackedValue = unchecked((uint)pixels[index]);
        return result;
    }

    private static int CountLitPixels(int[] pixels)
    {
        var count = 0;
        foreach (var pixel in pixels)
            count += ((pixel >> 24) & 255) > 0 ? 1 : 0;
        return count;
    }

    private static int QuantizedLuminance(int pixel)
    {
        var red = (pixel >> 16) & 255;
        var green = (pixel >> 8) & 255;
        var blue = pixel & 255;
        return (int)(red * 0.2126f + green * 0.7152f + blue * 0.0722f + 0.5f);
    }

    private static int CompositeAlpha(int sourceAlpha, int overlayAlpha)
        => overlayAlpha + sourceAlpha * (255 - overlayAlpha) / 255;
}
