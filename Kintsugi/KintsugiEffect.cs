using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Kintsugi;

[VideoEffect(nameof(Texts.Kintsugi), [VideoEffectCategories.Decoration, VideoEffectCategories.Animation], [nameof(Texts.TagKintsugi), nameof(Texts.TagCrackle), nameof(Texts.TagGold)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class KintsugiEffect : VideoEffectBase
{
    public override string Label => Texts.Kintsugi;

    public KintsugiEffect()
    {
        KintsugiUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Amount), Description = nameof(Texts.AmountDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Growth), Description = nameof(Texts.GrowthDescription), Order = 1, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Growth { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Quality), Description = nameof(Texts.QualityDescription), Order = 2, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public KintsugiQuality Quality { get => _quality; set => Set(ref _quality, value); }
    private KintsugiQuality _quality = KintsugiQuality.High;

    [Display(GroupName = nameof(Texts.FractureGroup), Name = nameof(Texts.Stress), Description = nameof(Texts.StressDescription), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 200)]
    public Animation Stress { get; } = new Animation(100, 0, 200);

    [Display(GroupName = nameof(Texts.FractureGroup), Name = nameof(Texts.Shrinkage), Description = nameof(Texts.ShrinkageDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Shrinkage { get; } = new Animation(60, 0, 100);

    [Display(GroupName = nameof(Texts.FractureGroup), Name = nameof(Texts.Toughness), Description = nameof(Texts.ToughnessDescription), Order = 12, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 1, 100)]
    public Animation Toughness { get; } = new Animation(50, 1, 100);

    [Display(GroupName = nameof(Texts.FractureGroup), Name = nameof(Texts.Relaxation), Description = nameof(Texts.RelaxationDescription), Order = 13, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Relaxation { get; } = new Animation(60, 0, 100);

    [Display(GroupName = nameof(Texts.FractureGroup), Name = nameof(Texts.Propagation), Description = nameof(Texts.PropagationDescription), Order = 14, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Propagation { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.FractureGroup), Name = nameof(Texts.LuminanceInfluence), Description = nameof(Texts.LuminanceInfluenceDescription), Order = 15, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation LuminanceInfluence { get; } = new Animation(70, 0, 100);

    [Display(GroupName = nameof(Texts.FractureGroup), Name = nameof(Texts.Irregularity), Description = nameof(Texts.IrregularityDescription), Order = 16, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Irregularity { get; } = new Animation(60, 0, 100);

    [Display(GroupName = nameof(Texts.FractureGroup), Name = nameof(Texts.Seed), Description = nameof(Texts.SeedDescription), Order = 17, ResourceType = typeof(Texts))]
    [Range(0, int.MaxValue)]
    [DefaultValue(0)]
    [TextBoxSlider("F0", "", 0, 10000)]
    public int Seed
    {
        get => _seed;
        set => Set(ref _seed, Math.Max(value, 0));
    }
    private int _seed;

    [Display(GroupName = nameof(Texts.LayersGroup), Name = nameof(Texts.CrackWidth), Description = nameof(Texts.CrackWidthDescription), Order = 20, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "px", 0.25, 20)]
    public Animation CrackWidth { get; } = new Animation(2.5, 0.05, 20);

    [Display(GroupName = nameof(Texts.LayersGroup), Name = nameof(Texts.ErosionDepth), Description = nameof(Texts.ErosionDepthDescription), Order = 21, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "px", 0, 10)]
    public Animation ErosionDepth { get; } = new Animation(1.5, 0, 10);

    [Display(GroupName = nameof(Texts.LayersGroup), Name = nameof(Texts.LacquerFill), Description = nameof(Texts.LacquerFillDescription), Order = 22, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation LacquerFill { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.LayersGroup), Name = nameof(Texts.GoldCoat), Description = nameof(Texts.GoldCoatDescription), Order = 23, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation GoldCoat { get; } = new Animation(75, 0, 100);

    [Display(GroupName = nameof(Texts.LayersGroup), Name = nameof(Texts.Polish), Description = nameof(Texts.PolishDescription), Order = 24, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Polish { get; } = new Animation(40, 0, 100);

    [Display(GroupName = nameof(Texts.LayersGroup), Name = nameof(Texts.LacquerColor), Description = nameof(Texts.LacquerColorDescription), Order = 25, ResourceType = typeof(Texts))]
    [ColorPicker]
    public Color LacquerColor
    {
        get => _lacquerColor;
        set => Set(ref _lacquerColor, Color.FromRgb(value.R, value.G, value.B));
    }
    private Color _lacquerColor = Color.FromRgb(0x3A, 0x07, 0x05);

    [Display(GroupName = nameof(Texts.LayersGroup), Name = nameof(Texts.GoldColor), Description = nameof(Texts.GoldColorDescription), Order = 26, ResourceType = typeof(Texts))]
    [ColorPicker]
    public Color GoldColor
    {
        get => _goldColor;
        set => Set(ref _goldColor, Color.FromRgb(value.R, value.G, value.B));
    }
    private Color _goldColor = Color.FromRgb(255, 222, 163);

    [Display(GroupName = nameof(Texts.LightingGroup), Name = nameof(Texts.Roughness), Description = nameof(Texts.RoughnessDescription), Order = 30, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Roughness { get; } = new Animation(25, 0, 100);

    [Display(GroupName = nameof(Texts.LightingGroup), Name = nameof(Texts.Isotropy), Description = nameof(Texts.IsotropyDescription), Order = 31, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Isotropy { get; } = new Animation(90, 0, 100);

    [Display(GroupName = nameof(Texts.LightingGroup), Name = nameof(Texts.LightAngle), Description = nameof(Texts.LightAngleDescription), Order = 32, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", -180, 180)]
    public Animation LightAngle { get; } = new Animation(-45, -36000, 36000);

    [Display(GroupName = nameof(Texts.LightingGroup), Name = nameof(Texts.LightElevation), Description = nameof(Texts.LightElevationDescription), Order = 33, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", 1, 89)]
    public Animation LightElevation { get; } = new Animation(45, 0, 90);

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        => new KintsugiEffectProcessor(devices, this);

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Amount, Growth, Stress, Shrinkage, Toughness, Relaxation, Propagation, LuminanceInfluence, Irregularity, CrackWidth, ErosionDepth, LacquerFill, GoldCoat, Polish, Roughness, Isotropy, LightAngle, LightElevation];
}
