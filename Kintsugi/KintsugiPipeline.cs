using System.Runtime.InteropServices;
using ComputeSharp;

namespace Kintsugi;

internal sealed class KintsugiPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private ReadWriteBuffer<int>? _inputCurrent;
    private ReadWriteBuffer<int>? _inputPrevious;
    private ReadWriteBuffer<Float2>? _positions;
    private ReadBackBuffer<Float2>? _positionsReadBack;
    private Float2[] _cachedPositions = [];
    private ReadWriteBuffer<Float2>? _forceCoefficients;
    private ReadWriteBuffer<Float4>? _inverseRest;
    private ReadWriteBuffer<uint>? _validSectors;
    private ReadWriteBuffer<Float4>? _sectorForces;
    private ReadWriteBuffer<Float2>? _relaxationForces;
    private ReadWriteBuffer<Float2>? _fanForces;
    private ReadWriteBuffer<Float4>? _stressA;
    private ReadWriteBuffer<Float4>? _stressB;
    private ReadWriteBuffer<int>? _edgeBirth;
    private ReadWriteBuffer<Float4>? _residual;
    private ReadWriteBuffer<int>? _candidateScores;
    private ReadWriteBuffer<Int2>? _candidateEdges;
    private ReadWriteBuffer<Float4>? _candidateData;
    private ReadWriteBuffer<int>? _blockScores;
    private ReadWriteBuffer<int>? _blockVertices;
    private ReadWriteBuffer<int>? _edgeLog;
    private ReadBackBuffer<int>? _edgeLogReadBack;
    private int[] _cachedEdgeLog = [];
    private float[] _boundsMinX = [];
    private float[] _boundsMinY = [];
    private float[] _boundsMaxX = [];
    private float[] _boundsMaxY = [];
    private StructureKey? _structureKey;
    private int _workingWidth;
    private int _workingHeight;
    private int _maximumSteps;
    private float _scaleX;
    private float _scaleY;
    private float _offsetX;
    private float _offsetY;
    private int _writtenEdgeCount;
    private int _generatedSteps;
    private bool _finishingExhaustedCandidates;
    private bool _hasStructure;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;

    private KintsugiPipeline(GraphicsDevice device)
    {
        _device = device;
        _scratch = device.AllocateReadWriteBuffer<int>(KintsugiSettings.ScratchLength);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(KintsugiSettings.ScratchLength);
    }

    public static KintsugiPipeline? TryCreate()
    {
        try
        {
            return new KintsugiPipeline(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static KintsugiPipeline? TryCreate(GraphicsDevice device)
    {
        try
        {
            return new KintsugiPipeline(device);
        }
        catch
        {
            return null;
        }
    }

    internal int WorkingWidth => _workingWidth;
    internal int WorkingHeight => _workingHeight;
    internal int WrittenEdgeCount => _writtenEdgeCount;
    internal int GeneratedSteps => _generatedSteps;
    internal bool FinishingExhaustedCandidates => _finishingExhaustedCandidates;
    internal ReadOnlySpan<int> EdgeLog => _cachedEdgeLog;
    internal ReadOnlySpan<Float2> Positions => _cachedPositions;
    internal float Scale => _scaleX;
    internal float OffsetX => _offsetX;
    internal float OffsetY => _offsetY;

    internal void WaitForCompletion()
        => _device.For(1, new FillIntShader(_scratch, 0, 0));

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in Parameters parameters)
    {
        var pixelCount = checked(width * height);
        EnsurePackedTextures(width, height);
        var sourceTexture = _packedSource!;
        var outputTexture = _packedOutput!;
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source[..pixelCount]));
        Simulate(sourceTexture, width, height, in parameters);
        RenderVisible(sourceTexture, outputTexture, width, height, new PixelRect(0, 0, width, height), in parameters);
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(destination[..pixelCount]));
    }

    public void Process(ReadWriteTexture2D<Bgra32, Float4> source, ReadWriteTexture2D<Bgra32, Float4> destination, int width, int height, in Parameters parameters)
    {
        Simulate(source, width, height, in parameters);
        RenderVisibleAsync(source, destination, width, height, new PixelRect(0, 0, width, height), in parameters);
    }

    internal void ProcessSharedAndWait(ReadWriteTexture2D<Bgra32, Float4> source, ReadWriteTexture2D<Bgra32, Float4> destination, int width, int height, in Parameters parameters)
    {
        Simulate(source, width, height, in parameters);
        RenderVisible(source, destination, width, height, new PixelRect(0, 0, width, height), in parameters);
    }

    internal bool Simulate(ReadWriteTexture2D<Bgra32, Float4> source, int width, int height, in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        using (ComputeContext context = _device.CreateComputeContext())
        {
            context.For(1, new FillIntShader(_scratch, KintsugiSettings.ScratchLength, 0));
            context.Barrier(_scratch);
            context.For(_workingWidth, _workingHeight, new AnalyzeInputShader(
                source, _inputCurrent!, _inputPrevious!, _positions!, _scratch,
                width, height, _workingWidth, _workingHeight, _scaleX, _offsetX, _offsetY, parameters.Irregularity, parameters.Seed));
            context.Barrier(_inputCurrent!);
            context.Barrier(_positions!);
            context.Barrier(_scratch);
        }
        _scratchReadBack.CopyFrom(_scratch);
        var inputChanged = _scratchReadBack.Span[KintsugiSettings.ScratchInputChanged] != 0;
        var key = new StructureKey(
            width,
            height,
            parameters.Quality,
            parameters.Seed,
            parameters.Stress,
            parameters.Shrinkage,
            parameters.Toughness,
            parameters.Relaxation,
            parameters.Propagation,
            parameters.LuminanceInfluence,
            parameters.Irregularity);
        if (!inputChanged && _structureKey == key)
            return false;

        RecordStructure(in parameters);
        _structureKey = key;
        return true;
    }

    internal bool TryGetVisibleBounds(int width, int height, in Parameters parameters, out PixelRect rect)
    {
        rect = default;
        if (!_hasStructure || _cachedEdgeLog.Length == 0 || (parameters.ErosionDepth <= 0f && (parameters.GoldCoat <= 0f || parameters.Polish >= 1f)))
            return false;
        var visibleSteps = Math.Clamp(parameters.Growth, 0f, 1f) * _generatedSteps;
        if (visibleSteps <= 0f)
            return false;
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var wholeSteps = Math.Min((int)Math.Floor(visibleSteps), _generatedSteps);
        if (wholeSteps > 0)
        {
            var index = wholeSteps - 1;
            minX = _boundsMinX[index];
            minY = _boundsMinY[index];
            maxX = _boundsMaxX[index];
            maxY = _boundsMaxY[index];
        }
        if (wholeSteps < _generatedSteps)
        {
            var fraction = visibleSteps - wholeSteps;
            for (var slotIndex = 0; slotIndex < KintsugiSettings.EdgesPerStep; slotIndex++)
            {
                var record = _cachedEdgeLog[wholeSteps * KintsugiSettings.EdgesPerStep + slotIndex];
                if (record < 0)
                    continue;
                GetSegment(record, fraction, out var ax, out var ay, out var bx, out var by);
                minX = Math.Min(minX, Math.Min(ax, bx));
                minY = Math.Min(minY, Math.Min(ay, by));
                maxX = Math.Max(maxX, Math.Max(ax, bx));
                maxY = Math.Max(maxY, Math.Max(ay, by));
            }
        }
        if (!float.IsFinite(minX))
            return false;
        var radius = KintsugiSettings.GetSupportRadius(parameters.CrackWidth);
        var left = KintsugiMath.AlignFloor4((int)Math.Floor(minX - radius + 0.5f));
        var top = KintsugiMath.AlignFloor4((int)Math.Floor(minY - radius + 0.5f));
        var right = KintsugiMath.AlignCeiling4((int)Math.Ceiling(maxX + radius - 0.5f));
        var bottom = KintsugiMath.AlignCeiling4((int)Math.Ceiling(maxY + radius - 0.5f));
        if (right <= left || bottom <= top)
            return false;
        rect = new PixelRect(left, top, right - left, bottom - top);
        return true;
    }

    internal void RenderVisible(ReadWriteTexture2D<Bgra32, Float4> source, ReadWriteTexture2D<Bgra32, Float4> output, int width, int height, PixelRect rect, in Parameters parameters)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        EnsureGridFor(width, height, parameters.Quality);
        using ComputeContext context = _device.CreateComputeContext();
        RecordRender(in context, source, output, width, height, rect, in parameters);
    }

    internal void RenderVisibleAsync(ReadWriteTexture2D<Bgra32, Float4> source, ReadWriteTexture2D<Bgra32, Float4> output, int width, int height, PixelRect rect, in Parameters parameters)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        EnsureGridFor(width, height, parameters.Quality);
        using ComputeContext context = _device.CreateComputeContext();
        RecordRender(in context, source, output, width, height, rect, in parameters);
        context.Submit();
    }

    private void RecordRender(in ComputeContext context, ReadWriteTexture2D<Bgra32, Float4> source, ReadWriteTexture2D<Bgra32, Float4> output, int width, int height, PixelRect rect, in Parameters parameters)
    {
        var support = KintsugiSettings.GetSupportRadius(parameters.CrackWidth);
        var minimumScale = Math.Max(Math.Min(_scaleX, _scaleY), 1e-6f);
        var scanRadius = Math.Max((int)Math.Ceiling(support / minimumScale) + 2, 2);
        context.For(rect.Width, rect.Height, new RenderKintsugiShader(
            _edgeBirth!, _positions!, source, output, width, height, _workingWidth, _workingHeight, _scaleX, _scaleY, _offsetX, _offsetY, _generatedSteps,
            rect.X, rect.Y, rect.Width, rect.Height, scanRadius,
            parameters.Growth, parameters.CrackWidth, parameters.ErosionDepth, parameters.LacquerFill,
            parameters.GoldCoat, parameters.Polish, parameters.LacquerR, parameters.LacquerG, parameters.LacquerB,
            parameters.GoldR, parameters.GoldG, parameters.GoldB, parameters.Roughness, parameters.Isotropy,
            parameters.LightAngleDegrees, parameters.LightElevationDegrees));
    }

    private void RecordStructure(in Parameters parameters)
    {
        var vertexCount = _workingWidth * _workingHeight;
        var triangleCount = (_workingWidth - 1) * (_workingHeight - 1) * 2;
        var edgeCount = vertexCount * KintsugiSettings.EdgesPerVertex;
        var edgeLogLength = checked((_maximumSteps + edgeCount) * KintsugiSettings.EdgesPerStep);
        var blockCount = KintsugiSettings.GetReductionBlockCount(vertexCount);
        ReadWriteBuffer<Float4> reading;
        ReadWriteBuffer<Float4> writing;
        using (ComputeContext context = _device.CreateComputeContext())
        {
            context.For(1, new InitializeSimulationScratchShader(_scratch));
            context.For(Math.Max(vertexCount, triangleCount), new InitializeGeometryShader(
                _positions!, _forceCoefficients!, _inverseRest!, _workingWidth, _workingHeight, vertexCount, triangleCount));
            context.For(vertexCount, new InitializeMaterialTopologyShader(
                _inputCurrent!, _validSectors!, _workingWidth, _workingHeight));
            context.For(edgeCount, new FillIntShader(_edgeBirth!, edgeCount, 0));
            context.For(edgeLogLength, new FillIntShader(_edgeLog!, edgeLogLength, -1));
            context.For(vertexCount, new FillFloat4Shader(_residual!, vertexCount, new Float4(0f, 0f, 0f, 0f)));
            context.For(triangleCount, new InitializeStressShader(
                _inputCurrent!, _stressA!, _workingWidth, _workingHeight,
                parameters.Stress, parameters.Shrinkage, parameters.LuminanceInfluence, parameters.Irregularity, parameters.Seed));
            context.Barrier(_scratch);
            context.Barrier(_forceCoefficients!);
            context.Barrier(_inverseRest!);
            context.Barrier(_validSectors!);
            context.Barrier(_edgeBirth!);
            context.Barrier(_edgeLog!);
            context.Barrier(_residual!);
            context.Barrier(_stressA!);
            reading = _stressA!;
            writing = _stressB!;
            for (var step = 0; step < _maximumSteps; step++)
            {
                context.For(vertexCount * 6, new ComputeSectorForcesShader(
                    _validSectors!, _forceCoefficients!, reading, _sectorForces!, _relaxationForces!, _scratch, _workingWidth, _workingHeight));
                context.Barrier(_sectorForces!);
                context.Barrier(_relaxationForces!);
                context.For(vertexCount, new FailureCandidateShader(
                    _validSectors!, _positions!, _sectorForces!, _edgeBirth!, _residual!, _scratch, _candidateScores!, _candidateEdges!, _candidateData!,
                    _workingWidth, _workingHeight, parameters.Toughness, 0));
                context.Barrier(_candidateScores!);
                context.Barrier(_candidateEdges!);
                context.Barrier(_candidateData!);
                context.For(blockCount, new ReduceFailureBlocksShader(_candidateScores!, _blockScores!, _blockVertices!, _scratch, vertexCount));
                context.Barrier(_blockScores!);
                context.Barrier(_blockVertices!);
                context.For(1, new ApplyFailureShader(
                    _edgeBirth!, _edgeLog!, _residual!, _candidateScores!, _candidateEdges!, _candidateData!, _blockScores!, _blockVertices!, _scratch,
                    _workingWidth, _workingHeight, step, parameters.Toughness, parameters.Propagation, 0, blockCount));
                context.Barrier(_edgeBirth!);
                context.Barrier(_residual!);
                context.Barrier(_scratch);
                context.For(vertexCount * 6, new ComputeFanForcesShader(
                    _validSectors!, _relaxationForces!, _edgeBirth!, _fanForces!, _scratch, _workingWidth, _workingHeight, step + 1));
                context.Barrier(_fanForces!);
                context.For(triangleCount, new RelaxStressShader(
                    _validSectors!, _fanForces!, _inverseRest!, reading, writing, _scratch, _workingWidth, _workingHeight, parameters.Relaxation, parameters.Shrinkage));
                context.Barrier(writing);
                (reading, writing) = (writing, reading);
            }
        }
        _scratchReadBack.CopyFrom(_scratch);
        var stoppedStep = _scratchReadBack.Span[KintsugiSettings.ScratchStoppedStep];
        var normalSteps = stoppedStep >= 0 ? Math.Min(stoppedStep, _maximumSteps) : _maximumSteps;
        reading = (normalSteps & 1) == 0 ? _stressA! : _stressB!;
        writing = (normalSteps & 1) == 0 ? _stressB! : _stressA!;
        var normalEdgeCount = _scratchReadBack.Span[KintsugiSettings.ScratchWrittenEdgeCount];
        var finishSteps = FinishCrackTips(
            in parameters,
            normalSteps,
            Math.Max(edgeCount - normalEdgeCount, 0),
            vertexCount,
            triangleCount,
            blockCount,
            ref reading,
            ref writing);
        using (ComputeContext context = _device.CreateComputeContext())
            context.For(vertexCount, new CopyIntShader(_inputCurrent!, _inputPrevious!, vertexCount));
        _scratchReadBack.CopyFrom(_scratch);
        var edgeLogReadBack = _edgeLogReadBack!;
        edgeLogReadBack.CopyFrom(_edgeLog!);
        var positionsReadBack = _positionsReadBack!;
        positionsReadBack.CopyFrom(_positions!);
        _generatedSteps = normalSteps + finishSteps;
        _cachedEdgeLog = edgeLogReadBack.Span[..checked(_generatedSteps * KintsugiSettings.EdgesPerStep)].ToArray();
        positionsReadBack.Span.CopyTo(_cachedPositions);
        BuildBoundsPrefix();
        _writtenEdgeCount = _scratchReadBack.Span[KintsugiSettings.ScratchWrittenEdgeCount];
        _hasStructure = _writtenEdgeCount > 0;
    }

    private int FinishCrackTips(
        in Parameters parameters,
        int startStep,
        int maximumSteps,
        int vertexCount,
        int triangleCount,
        int blockCount,
        ref ReadWriteBuffer<Float4> reading,
        ref ReadWriteBuffer<Float4> writing)
    {
        const int batchSize = 32;
        _finishingExhaustedCandidates = false;
        var completed = 0;
        while (completed < maximumSteps)
        {
            var count = Math.Min(batchSize, maximumSteps - completed);
            using (ComputeContext context = _device.CreateComputeContext())
            {
                if (completed == 0)
                {
                    context.For(1, new InitializeSimulationPhaseShader(_scratch));
                    context.Barrier(_scratch);
                }
                for (var localStep = 0; localStep < count; localStep++)
                {
                    var step = startStep + completed + localStep;
                    context.For(vertexCount, new FailureCandidateShader(
                        _validSectors!, _positions!, _sectorForces!, _edgeBirth!, _residual!, _scratch, _candidateScores!, _candidateEdges!, _candidateData!,
                        _workingWidth, _workingHeight, parameters.Toughness, 1));
                    context.Barrier(_candidateScores!);
                    context.Barrier(_candidateEdges!);
                    context.Barrier(_candidateData!);
                    context.For(blockCount, new ReduceFailureBlocksShader(_candidateScores!, _blockScores!, _blockVertices!, _scratch, vertexCount));
                    context.Barrier(_blockScores!);
                    context.Barrier(_blockVertices!);
                    context.For(1, new ApplyFailureShader(
                        _edgeBirth!, _edgeLog!, _residual!, _candidateScores!, _candidateEdges!, _candidateData!, _blockScores!, _blockVertices!, _scratch,
                        _workingWidth, _workingHeight, step, parameters.Toughness, parameters.Propagation, 1, blockCount));
                    context.Barrier(_edgeBirth!);
                    context.Barrier(_residual!);
                    context.Barrier(_scratch);
                }
            }
            _scratchReadBack.CopyFrom(_scratch);
            var stoppedStep = _scratchReadBack.Span[KintsugiSettings.ScratchStoppedStep];
            if (stoppedStep >= startStep)
            {
                completed = Math.Min(stoppedStep - startStep, maximumSteps);
                _finishingExhaustedCandidates = true;
                break;
            }
            completed += count;
        }
        const int replayBatchSize = 256;
        var replayed = 0;
        while (replayed < completed)
        {
            var count = Math.Min(replayBatchSize, completed - replayed);
            using (ComputeContext context = _device.CreateComputeContext())
            {
                if (replayed == 0)
                {
                    context.For(1, new InitializeSimulationPhaseShader(_scratch));
                    context.Barrier(_scratch);
                }
                for (var localStep = 0; localStep < count; localStep++)
                {
                    var step = startStep + replayed + localStep;
                    context.For(vertexCount * 6, new ComputeRelaxationForcesShader(
                        _validSectors!, _forceCoefficients!, reading, _relaxationForces!, _scratch, _workingWidth, _workingHeight));
                    context.Barrier(_relaxationForces!);
                    context.For(vertexCount * 6, new ComputeFanForcesShader(
                        _validSectors!, _relaxationForces!, _edgeBirth!, _fanForces!, _scratch, _workingWidth, _workingHeight, step + 1));
                    context.Barrier(_fanForces!);
                    context.For(triangleCount, new RelaxStressShader(
                        _validSectors!, _fanForces!, _inverseRest!, reading, writing, _scratch, _workingWidth, _workingHeight, parameters.Relaxation, parameters.Shrinkage));
                    context.Barrier(writing);
                    (reading, writing) = (writing, reading);
                }
            }
            replayed += count;
        }
        return completed;
    }

    private void GetSegment(int record, float fraction, out float ax, out float ay, out float bx, out float by)
    {
        var edge = record >> 1;
        var orientation = record & 1;
        var slot = edge % KintsugiSettings.EdgesPerVertex;
        var origin = edge / KintsugiSettings.EdgesPerVertex;
        var y = origin / _workingWidth;
        var parity = y & 1;
        var target = slot == 0 ? origin + 1 : slot == 1 ? origin + _workingWidth + parity : origin + _workingWidth + parity - 1;
        var first = _cachedPositions[origin];
        var second = _cachedPositions[target];
        ax = _offsetX + first.X * _scaleX;
        ay = _offsetY + first.Y * _scaleX;
        bx = _offsetX + second.X * _scaleX;
        by = _offsetY + second.Y * _scaleX;
        if (orientation != 0)
        {
            (ax, bx) = (bx, ax);
            (ay, by) = (by, ay);
        }
        bx = ax + (bx - ax) * fraction;
        by = ay + (by - ay) * fraction;
    }

    private void BuildBoundsPrefix()
    {
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        for (var step = 0; step < _generatedSteps; step++)
        {
            for (var slot = 0; slot < KintsugiSettings.EdgesPerStep; slot++)
            {
                var record = _cachedEdgeLog[step * KintsugiSettings.EdgesPerStep + slot];
                if (record < 0)
                    continue;
                GetSegment(record, 1f, out var ax, out var ay, out var bx, out var by);
                minX = Math.Min(minX, Math.Min(ax, bx));
                minY = Math.Min(minY, Math.Min(ay, by));
                maxX = Math.Max(maxX, Math.Max(ax, bx));
                maxY = Math.Max(maxY, Math.Max(ay, by));
            }
            _boundsMinX[step] = minX;
            _boundsMinY[step] = minY;
            _boundsMaxX[step] = maxX;
            _boundsMaxY[step] = maxY;
        }
    }

    private void EnsureGridFor(int width, int height, KintsugiQuality quality)
    {
        var settings = KintsugiSettings.GetQuality(quality);
        var (workingWidth, workingHeight, scaleX, scaleY) = KintsugiSettings.GetWorkingSize(width, height, settings.WorkingResolution);
        var offsetX = (width - 1f - (workingWidth - 0.5f) * scaleX) * 0.5f;
        var offsetY = (height - 1f - (workingHeight - 1f) * scaleY) * 0.5f;
        if (_workingWidth == workingWidth && _workingHeight == workingHeight && _maximumSteps == settings.MaximumSteps)
        {
            _scaleX = scaleX;
            _scaleY = scaleY;
            _offsetX = offsetX;
            _offsetY = offsetY;
            return;
        }
        DisposeGridBuffers();
        var vertexCount = workingWidth * workingHeight;
        var triangleCount = (workingWidth - 1) * (workingHeight - 1) * 2;
        var edgeCount = vertexCount * KintsugiSettings.EdgesPerVertex;
        var blockCount = KintsugiSettings.GetReductionBlockCount(vertexCount);
        var edgeLogLength = checked((settings.MaximumSteps + edgeCount) * KintsugiSettings.EdgesPerStep);
        _inputCurrent = _device.AllocateReadWriteBuffer<int>(vertexCount);
        _inputPrevious = _device.AllocateReadWriteBuffer<int>(vertexCount);
        _positions = _device.AllocateReadWriteBuffer<Float2>(vertexCount);
        _positionsReadBack = _device.AllocateReadBackBuffer<Float2>(vertexCount);
        _cachedPositions = new Float2[vertexCount];
        _forceCoefficients = _device.AllocateReadWriteBuffer<Float2>(vertexCount * 6);
        _inverseRest = _device.AllocateReadWriteBuffer<Float4>(triangleCount);
        _validSectors = _device.AllocateReadWriteBuffer<uint>(vertexCount);
        _sectorForces = _device.AllocateReadWriteBuffer<Float4>(vertexCount * 6);
        _relaxationForces = _device.AllocateReadWriteBuffer<Float2>(vertexCount * 6);
        _fanForces = _device.AllocateReadWriteBuffer<Float2>(vertexCount * 6);
        _stressA = _device.AllocateReadWriteBuffer<Float4>(triangleCount);
        _stressB = _device.AllocateReadWriteBuffer<Float4>(triangleCount);
        _edgeBirth = _device.AllocateReadWriteBuffer<int>(edgeCount);
        _residual = _device.AllocateReadWriteBuffer<Float4>(vertexCount);
        _candidateScores = _device.AllocateReadWriteBuffer<int>(vertexCount);
        _candidateEdges = _device.AllocateReadWriteBuffer<Int2>(vertexCount);
        _candidateData = _device.AllocateReadWriteBuffer<Float4>(vertexCount);
        _blockScores = _device.AllocateReadWriteBuffer<int>(blockCount);
        _blockVertices = _device.AllocateReadWriteBuffer<int>(blockCount);
        _edgeLog = _device.AllocateReadWriteBuffer<int>(edgeLogLength);
        _edgeLogReadBack = _device.AllocateReadBackBuffer<int>(edgeLogLength);
        _cachedEdgeLog = new int[edgeLogLength];
        var boundsLength = settings.MaximumSteps + edgeCount;
        _boundsMinX = new float[boundsLength];
        _boundsMinY = new float[boundsLength];
        _boundsMaxX = new float[boundsLength];
        _boundsMaxY = new float[boundsLength];
        _workingWidth = workingWidth;
        _workingHeight = workingHeight;
        _maximumSteps = settings.MaximumSteps;
        _scaleX = scaleX;
        _scaleY = scaleY;
        _offsetX = offsetX;
        _offsetY = offsetY;
        _device.For(vertexCount, new FillIntShader(_inputPrevious, vertexCount, -1));
    }

    private void EnsurePackedTextures(int width, int height)
    {
        if (_packedWidth == width && _packedHeight == height)
            return;
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedOutput = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedWidth = width;
        _packedHeight = height;
    }

    private void DisposeGridBuffers()
    {
        _inputCurrent?.Dispose();
        _inputPrevious?.Dispose();
        _positions?.Dispose();
        _positionsReadBack?.Dispose();
        _forceCoefficients?.Dispose();
        _inverseRest?.Dispose();
        _validSectors?.Dispose();
        _sectorForces?.Dispose();
        _relaxationForces?.Dispose();
        _fanForces?.Dispose();
        _stressA?.Dispose();
        _stressB?.Dispose();
        _edgeBirth?.Dispose();
        _residual?.Dispose();
        _candidateScores?.Dispose();
        _candidateEdges?.Dispose();
        _candidateData?.Dispose();
        _blockScores?.Dispose();
        _blockVertices?.Dispose();
        _edgeLog?.Dispose();
        _edgeLogReadBack?.Dispose();
        _inputCurrent = null;
        _inputPrevious = null;
        _positions = null;
        _positionsReadBack = null;
        _cachedPositions = [];
        _forceCoefficients = null;
        _inverseRest = null;
        _validSectors = null;
        _sectorForces = null;
        _relaxationForces = null;
        _fanForces = null;
        _stressA = null;
        _stressB = null;
        _edgeBirth = null;
        _residual = null;
        _candidateScores = null;
        _candidateEdges = null;
        _candidateData = null;
        _blockScores = null;
        _blockVertices = null;
        _edgeLog = null;
        _edgeLogReadBack = null;
        _cachedEdgeLog = [];
        _boundsMinX = [];
        _boundsMinY = [];
        _boundsMaxX = [];
        _boundsMaxY = [];
        _workingWidth = 0;
        _workingHeight = 0;
        _maximumSteps = 0;
        _scaleX = 0f;
        _scaleY = 0f;
        _offsetX = 0f;
        _offsetY = 0f;
        _writtenEdgeCount = 0;
        _generatedSteps = 0;
        _finishingExhaustedCandidates = false;
        _hasStructure = false;
        _structureKey = null;
    }

    public void Dispose()
    {
        DisposeGridBuffers();
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _scratchReadBack.Dispose();
        _scratch.Dispose();
    }

    internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

    private readonly record struct StructureKey(
        int Width,
        int Height,
        KintsugiQuality Quality,
        int Seed,
        float Stress,
        float Shrinkage,
        float Toughness,
        float Relaxation,
        float Propagation,
        float LuminanceInfluence,
        float Irregularity);

    internal readonly record struct Parameters(
        KintsugiQuality Quality,
        int Seed,
        float Stress,
        float Shrinkage,
        float Toughness,
        float Relaxation,
        float Propagation,
        float LuminanceInfluence,
        float Irregularity,
        float Growth,
        float CrackWidth,
        float ErosionDepth,
        float LacquerFill,
        float GoldCoat,
        float Polish,
        float LacquerR,
        float LacquerG,
        float LacquerB,
        float GoldR,
        float GoldG,
        float GoldB,
        float Roughness,
        float Isotropy,
        float LightAngleDegrees,
        float LightElevationDegrees);
}
