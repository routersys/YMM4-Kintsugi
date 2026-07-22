using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace Kintsugi;

internal sealed class KintsugiEffectProcessor : VideoEffectProcessorBase
{
    private readonly IGraphicsDevicesAndContext _devices;
    private readonly KintsugiEffect _item;
    private KintsugiGpuInterop? _interop;
    private KintsugiPipeline? _pipeline;
    private KintsugiCustomEffect? _effect;
    private Crop? _outputCrop;
    private ID2D1Image? _outputCropOutput;
    private AffineTransform2D? _outputTransform;
    private ID2D1Image? _outputTransformOutput;
    private bool _isFirst = true;
    private bool _failed;
    private bool _hasOutput;
    private bool _hasOutputOffset;
    private bool _hasCropRect;
    private Vector2 _outputOffset;
    private Vector4 _cropRect;
    private Parameters _parameters;

    public KintsugiEffectProcessor(IGraphicsDevicesAndContext devices, KintsugiEffect item)
        : base(devices)
    {
        _devices = devices;
        _item = item;
    }

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        if (_failed)
            return effectDescription.DrawDescription;
        try
        {
            return UpdateCore(effectDescription);
        }
        catch
        {
            DisableAfterFailure();
            return effectDescription.DrawDescription;
        }
    }

    private DrawDescription UpdateCore(EffectDescription effectDescription)
    {
        if (IsPassThroughEffect || _effect is null || _outputCrop is null || _outputTransform is null || _outputTransformOutput is null || _interop is null || _pipeline is null || input is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;
        var parameters = new Parameters(
            (float)(_item.Amount.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Growth.GetValue(frame, length, fps) / 100.0),
            _item.Quality,
            (float)(_item.Stress.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Shrinkage.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Toughness.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Relaxation.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Propagation.GetValue(frame, length, fps) / 100.0),
            (float)(_item.LuminanceInfluence.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Irregularity.GetValue(frame, length, fps) / 100.0),
            (float)_item.CrackWidth.GetValue(frame, length, fps),
            (float)_item.ErosionDepth.GetValue(frame, length, fps),
            (float)(_item.LacquerFill.GetValue(frame, length, fps) / 100.0),
            (float)(_item.GoldCoat.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Polish.GetValue(frame, length, fps) / 100.0),
            _item.LacquerColor,
            _item.GoldColor,
            (float)(_item.Roughness.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Isotropy.GetValue(frame, length, fps) / 100.0),
            (float)_item.LightAngle.GetValue(frame, length, fps),
            (float)_item.LightElevation.GetValue(frame, length, fps),
            _item.Seed);
        var amount = ClampFinite(parameters.Amount, 0f, 1f, 1f);
        var growth = ClampFinite(parameters.Growth, 0f, 1f, 1f);
        var pipelineParameters = CreatePipelineParameters(in parameters);

        if (_isFirst || _parameters.Amount != parameters.Amount)
            _effect.Amount = amount;

        if (amount <= 0f || growth <= 0f ||
            (pipelineParameters.ErosionDepth <= 0f && (pipelineParameters.GoldCoat <= 0f || pipelineParameters.Polish >= 1f)))
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var bounds = _devices.DeviceContext.GetImageLocalBounds(input);
        var widthValue = Math.Ceiling((double)bounds.Right - bounds.Left);
        var heightValue = Math.Ceiling((double)bounds.Bottom - bounds.Top);
        if (!double.IsFinite(widthValue) || !double.IsFinite(heightValue) ||
            !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            widthValue <= 0d || heightValue <= 0d ||
            widthValue > KintsugiSettings.MaximumTextureSize || heightValue > KintsugiSettings.MaximumTextureSize ||
            widthValue * heightValue > int.MaxValue)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var width = (int)widthValue;
        var height = (int)heightValue;
        _interop.EnsureSource(width, height);
        _interop.RenderInput(input, new Vortice.RawRectF(bounds.Left, bounds.Top, bounds.Left + width, bounds.Top + height));

        _interop.BeginCompute();
        try
        {
            _pipeline.Simulate(_interop.SourceTexture, width, height, in pipelineParameters);
        }
        finally
        {
            _interop.EndCompute();
        }

        if (!_pipeline.TryGetVisibleBounds(width, height, in pipelineParameters, out var rect) ||
            rect.Width <= 0 || rect.Height <= 0 ||
            rect.Width > KintsugiSettings.MaximumTextureSize || rect.Height > KintsugiSettings.MaximumTextureSize ||
            (long)rect.Width * rect.Height > int.MaxValue)
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        if (!_interop.OutputCovers(rect.Width, rect.Height))
            _outputCrop.SetInput(0, null, true);
        var outputChanged = _interop.EnsureOutput(rect.Width, rect.Height);
        _interop.BeginCompute();
        try
        {
            _pipeline.RenderVisibleAsync(_interop.SourceTexture, _interop.OutputTexture, width, height, rect, in pipelineParameters);
        }
        finally
        {
            _interop.EndCompute();
        }

        if (outputChanged || !_hasOutput)
        {
            _outputCrop.SetInput(0, _interop.OutputBitmap, true);
            _effect.SetInput(1, _outputTransformOutput, true);
        }
        var cropRect = new Vector4(0f, 0f, rect.Width, rect.Height);
        if (!_hasCropRect || _cropRect != cropRect)
        {
            _outputCrop.Rectangle = cropRect;
            _cropRect = cropRect;
            _hasCropRect = true;
        }
        var outputOffset = new Vector2(bounds.Left + rect.X, bounds.Top + rect.Y);
        if (!_hasOutputOffset || _outputOffset != outputOffset)
        {
            _outputTransform.TransformMatrix = Matrix3x2.CreateTranslation(outputOffset);
            _outputOffset = outputOffset;
            _hasOutputOffset = true;
        }
        _hasOutput = true;
        _parameters = parameters;
        _isFirst = false;
        return effectDescription.DrawDescription;
    }

    private void DisableAfterFailure()
    {
        _failed = true;
        _isFirst = true;
        _hasOutput = false;
        try
        {
            if (_effect is not null)
            {
                _effect.Amount = 0f;
                _effect.SetInput(1, input, true);
            }
        }
        catch
        {
        }
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
    {
        var interop = KintsugiGpuInterop.TryCreate(devices);
        if (interop is null)
            return null;
        var pipeline = KintsugiPipeline.TryCreate(interop.Device);
        if (pipeline is null)
        {
            interop.Dispose();
            return null;
        }

        KintsugiCustomEffect? effect = null;
        Crop? outputCrop = null;
        ID2D1Image? outputCropOutput = null;
        AffineTransform2D? outputTransform = null;
        ID2D1Image? outputTransformOutput = null;
        ID2D1Image? output = null;
        try
        {
            effect = new KintsugiCustomEffect(devices);
            if (!effect.IsEnabled)
            {
                effect.Dispose();
                pipeline.Dispose();
                interop.Dispose();
                return null;
            }
            outputCrop = new Crop(devices.DeviceContext);
            outputCropOutput = outputCrop.Output;
            outputTransform = new AffineTransform2D(devices.DeviceContext)
            {
                BorderMode = BorderMode.Hard,
            };
            outputTransform.SetInput(0, outputCropOutput, true);
            outputTransformOutput = outputTransform.Output;
            output = effect.Output;
            _interop = interop;
            _pipeline = pipeline;
            _effect = effect;
            _outputCrop = outputCrop;
            _outputCropOutput = outputCropOutput;
            _outputTransform = outputTransform;
            _outputTransformOutput = outputTransformOutput;
            disposer.Collect(effect);
            disposer.Collect(outputCrop);
            disposer.Collect(outputCropOutput);
            disposer.Collect(outputTransform);
            disposer.Collect(outputTransformOutput);
            disposer.Collect(output);
            return output;
        }
        catch
        {
            output?.Dispose();
            outputTransformOutput?.Dispose();
            outputTransform?.Dispose();
            outputCropOutput?.Dispose();
            outputCrop?.Dispose();
            effect?.Dispose();
            pipeline.Dispose();
            interop.Dispose();
            throw;
        }
    }

    protected override void setInput(ID2D1Image? inputImage)
    {
        _effect?.SetInput(0, inputImage, true);
        if (!_hasOutput)
            _effect?.SetInput(1, inputImage, true);
    }

    protected override void ClearEffectChain()
    {
        _effect?.SetInput(0, null, true);
        _effect?.SetInput(1, null, true);
        _outputCrop?.SetInput(0, null, true);
        _isFirst = true;
        _hasOutput = false;
        _hasOutputOffset = false;
        _hasCropRect = false;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                ClearEffectChain();
                _interop?.WaitForIdle();
                _pipeline?.Dispose();
                _pipeline = null;
                _interop?.Dispose();
                _interop = null;
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    private static KintsugiPipeline.Parameters CreatePipelineParameters(in Parameters parameters)
    {
        var quality = Enum.IsDefined(parameters.Quality) ? parameters.Quality : KintsugiQuality.High;
        return new KintsugiPipeline.Parameters(
            quality,
            Math.Max(parameters.Seed, 0),
            ClampFinite(parameters.Stress, 0f, 2f, 1f),
            ClampFinite(parameters.Shrinkage, 0f, 1f, 0.6f),
            ClampFinite(parameters.Toughness, 0.01f, 1f, 0.5f),
            ClampFinite(parameters.Relaxation, 0f, 1f, 0.6f),
            ClampFinite(parameters.Propagation, 0f, 1f, 0.5f),
            ClampFinite(parameters.LuminanceInfluence, 0f, 1f, 0.7f),
            ClampFinite(parameters.Irregularity, 0f, 1f, 0.6f),
            ClampFinite(parameters.Growth, 0f, 1f, 1f),
            ClampFinite(parameters.CrackWidth, 0.05f, 20f, 2.5f),
            ClampFinite(parameters.ErosionDepth, 0f, 10f, 1.5f),
            ClampFinite(parameters.LacquerFill, 0f, 1f, 1f),
            ClampFinite(parameters.GoldCoat, 0f, 1f, 0.75f),
            ClampFinite(parameters.Polish, 0f, 1f, 0.4f),
            parameters.LacquerColor.R / 255f,
            parameters.LacquerColor.G / 255f,
            parameters.LacquerColor.B / 255f,
            parameters.GoldColor.R / 255f,
            parameters.GoldColor.G / 255f,
            parameters.GoldColor.B / 255f,
            ClampFinite(parameters.Roughness, 0f, 1f, 0.25f),
            ClampFinite(parameters.Isotropy, 0f, 1f, 0.9f),
            NormalizeAngle(parameters.LightAngle),
            ClampFinite(parameters.LightElevation, 1f, 89f, 45f));
    }

    private static float ClampFinite(float value, float minimum, float maximum, float fallback)
        => float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static float NormalizeAngle(float value)
    {
        if (!float.IsFinite(value))
            return -45f;
        var angle = value % 360f;
        if (angle >= 180f)
            angle -= 360f;
        else if (angle < -180f)
            angle += 360f;
        return angle;
    }

    private readonly record struct Parameters(
        float Amount,
        float Growth,
        KintsugiQuality Quality,
        float Stress,
        float Shrinkage,
        float Toughness,
        float Relaxation,
        float Propagation,
        float LuminanceInfluence,
        float Irregularity,
        float CrackWidth,
        float ErosionDepth,
        float LacquerFill,
        float GoldCoat,
        float Polish,
        System.Windows.Media.Color LacquerColor,
        System.Windows.Media.Color GoldColor,
        float Roughness,
        float Isotropy,
        float LightAngle,
        float LightElevation,
        int Seed);
}
