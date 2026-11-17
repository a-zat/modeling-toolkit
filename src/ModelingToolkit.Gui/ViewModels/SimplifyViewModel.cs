using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Meshes;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>A row of the measures: what is measured, before and after.</summary>
public sealed record SimplifyMeasure(string Name, string Before, string After, bool IsBetter);

/// <summary>
/// The simplification of a component, as its window shows it: the settings on the left, the component before or
/// after on the right, as it would be, made again a moment after a setting changes, and the measures of both.
/// </summary>
public sealed partial class SimplifyViewModel : ObservableObject, IDisposable
{
    private readonly Entity _entity;
    private readonly Func<IEditCommand, bool> _apply;
    private readonly Block _before = new("Before");
    private readonly Block _after = new("After");
    private readonly BlockReference _beforeReference;
    private readonly BlockReference _afterReference;
    private CancellationTokenSource? _computing;
    private SimplifiedComponent? _result;
    private bool _applyingPreset;

    public SimplifyViewModel(Entity entity, string name, Func<IEditCommand, bool> apply)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _entity = entity;
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        Name = name;

        // The preview: the component as it is and as it would be, one of them hidden.
        Preview = new Document();
        Preview.Blocks.Add(_before);
        Preview.Blocks.Add(_after);
        _beforeReference = new BlockReference(_before, Transform.Identity) { IsHidden = true };
        _afterReference = new BlockReference(_after, Transform.Identity);
        Preview.Root.Entities.Add(_beforeReference);
        Preview.Root.Entities.Add(_afterReference);
        foreach ((MeshEntity mesh, Transform placement) in SimplifiedComponent.MeshesPlaced(entity))
        {
            _before.Entities.Add(TransformCommands.Duplicate(mesh, placement));
        }
        MeshCount = SimplifiedComponent.MeshesOf(entity).Count;

        SelectedPreset = Presets[0];
        PropertyChanged += OnSettingChanged;
        Recompute();
    }

    /// <summary>The name of the component.</summary>
    public string Name { get; }

    public int MeshCount { get; }

    /// <summary>What the meshes simplified are, as the foot of the window tells.</summary>
    public string Scope => MeshCount == 1
        ? "1 mesh; where it is placed elsewhere too, it changes there as well."
        : $"{MeshCount} meshes in the component; where its blocks are placed elsewhere too, they change there as well.";

    /// <summary>Raised when the first result is shown, for the preview to frame it.</summary>
    public event EventHandler? FirstShown;

    /// <summary>The document the preview shows.</summary>
    public Document Preview { get; }

    public static IReadOnlyList<SimplifyPreset> AllPresets => SimplifyPreset.All;

    /// <summary>The presets, as the list of the window shows them.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Bound by the window")]
    public IReadOnlyList<SimplifyPreset> Presets => AllPresets;

    [ObservableProperty]
    public partial SimplifyPreset? SelectedPreset { get; set; }

    partial void OnSelectedPresetChanged(SimplifyPreset? value)
    {
        if (value is null)
        {
            return;
        }
        _applyingPreset = true;
        try
        {
            SimplifyOptions o = value.Options;
            WeldTolerance = o.WeldTolerance;
            RemoveDegenerate = o.RemoveDegenerate;
            MinPieceSize = o.MinPieceSize;
            SmoothIterations = o.SmoothIterations;
            SmoothMaxMove = o.SmoothMaxMove;
            SmoothKeepAngle = o.SmoothKeepAngle;
            KeepPercent = o.KeepPercent;
            MaxError = o.MaxError;
            KeepBorders = o.KeepBorders;
            KeepTextureSeams = o.KeepTextureSeams;
            RecomputeNormals = o.Normals == NormalsMode.Recompute;
            CreaseAngle = o.CreaseAngle;
            SoftenAngle = o.SoftenAngle;
            MergeByMaterial = o.MergeByMaterial;
        }
        finally
        {
            _applyingPreset = false;
        }
        Recompute();
    }

    [ObservableProperty] public partial double WeldTolerance { get; set; }
    [ObservableProperty] public partial bool RemoveDegenerate { get; set; }
    [ObservableProperty] public partial double MinPieceSize { get; set; }
    [ObservableProperty] public partial int SmoothIterations { get; set; }
    [ObservableProperty] public partial double SmoothMaxMove { get; set; }
    [ObservableProperty] public partial double SmoothKeepAngle { get; set; }
    [ObservableProperty] public partial double KeepPercent { get; set; }
    [ObservableProperty] public partial double MaxError { get; set; }
    [ObservableProperty] public partial bool KeepBorders { get; set; }
    [ObservableProperty] public partial bool KeepTextureSeams { get; set; }
    [ObservableProperty] public partial bool RecomputeNormals { get; set; }
    [ObservableProperty] public partial double CreaseAngle { get; set; }
    [ObservableProperty] public partial double SoftenAngle { get; set; }
    [ObservableProperty] public partial bool MergeByMaterial { get; set; }

    /// <summary>The settings, as the fields give them.</summary>
    public SimplifyOptions Options => new()
    {
        WeldTolerance = WeldTolerance,
        RemoveDegenerate = RemoveDegenerate,
        MinPieceSize = MinPieceSize,
        SmoothIterations = SmoothIterations,
        SmoothMaxMove = SmoothMaxMove,
        SmoothKeepAngle = SmoothKeepAngle,
        KeepPercent = KeepPercent,
        MaxError = MaxError,
        KeepBorders = KeepBorders,
        KeepTextureSeams = KeepTextureSeams,
        Normals = RecomputeNormals ? NormalsMode.Recompute : NormalsMode.Keep,
        CreaseAngle = CreaseAngle,
        SoftenAngle = SoftenAngle,
        MergeByMaterial = MergeByMaterial,
    };

    /// <summary>Whether the preview shows the component simplified, rather than as it is.</summary>
    [ObservableProperty]
    public partial bool ShowsResult { get; set; } = true;

    partial void OnShowsResultChanged(bool value)
    {
        _beforeReference.IsHidden = value;
        _afterReference.IsHidden = !value;
    }

    [RelayCommand]
    private void ShowBefore() => ShowsResult = false;

    [RelayCommand]
    private void ShowAfter() => ShowsResult = true;

    [ObservableProperty]
    public partial bool IsComputing { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<SimplifyMeasure> Measures { get; private set; } = [];

    [ObservableProperty]
    public partial string Deviation { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool HasResult { get; private set; }

    /// <summary>Raised when the window should close: after applying, or cancelling.</summary>
    public event EventHandler? CloseRequested;

    private static readonly string[] Settings =
    [
        nameof(WeldTolerance), nameof(RemoveDegenerate), nameof(MinPieceSize), nameof(SmoothIterations),
        nameof(SmoothMaxMove), nameof(SmoothKeepAngle), nameof(KeepPercent), nameof(MaxError), nameof(KeepBorders), nameof(KeepTextureSeams),
        nameof(RecomputeNormals), nameof(CreaseAngle), nameof(SoftenAngle), nameof(MergeByMaterial),
    ];

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applyingPreset || !Settings.Contains(e.PropertyName))
        {
            return;
        }
        if (SelectedPreset is not null && SelectedPreset.Options != Options)
        {
            _applyingPreset = true;
            SelectedPreset = null; // edited: none of the presets
            _applyingPreset = false;
        }
        Recompute();
    }

    // Simplifies again, a moment after the last change, away from the user interface; a newer change cancels it.
    private async void Recompute()
    {
        _computing?.Cancel();
        _computing?.Dispose();
        var cancellation = new CancellationTokenSource();
        _computing = cancellation;
        IsComputing = true;
        HasResult = false;
        SimplifyOptions options = Options;
        try
        {
            await Task.Delay(250, cancellation.Token);
            var watch = Stopwatch.StartNew();
            SimplifiedComponent result = await Task.Run(
                () => SimplifiedComponent.Of(_entity, options, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }
            Show(result, watch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            // A newer change came.
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Status = $"It could not be simplified: {exception.Message}";
            IsComputing = false;
        }
    }

    private void Show(SimplifiedComponent result, TimeSpan took)
    {
        bool first = _result is null;
        _result = result;
        _after.Entities.Clear();
        foreach ((MeshEntity mesh, Transform placement) in result.Placed(_entity, simplified: true))
        {
            _after.Entities.Add(TransformCommands.Duplicate(mesh, placement));
        }
        CultureInfo culture = CultureInfo.CurrentCulture;
        string Count(int value) => value.ToString("N0", culture);
        MeshMeasures before = result.Before, after = result.After;
        Measures =
        [
            new("Triangles", Count(before.Triangles), Count(after.Triangles), after.Triangles < before.Triangles),
            new("Vertices", Count(before.Vertices), Count(after.Vertices), after.Vertices < before.Vertices),
            new("Pieces", Count(before.Pieces), Count(after.Pieces), after.Pieces < before.Pieces),
            new("Edges drawn", Count(before.Edges), Count(after.Edges), after.Edges < before.Edges),
            new("Meshes", Count(result.Meshes.Count), Count(_after.Entities.Count), _after.Entities.Count < result.Meshes.Count),
        ];
        Deviation = string.Create(culture,
            $"Moved at most {result.MaxDeviation:0.0} mm, on average {result.MeanDeviation:0.00} mm");
        double share = before.Triangles == 0 ? 0 : 100.0 * after.Triangles / before.Triangles;
        Status = string.Create(culture, $"{share:0}% of the triangles · made in {took.TotalMilliseconds:0} ms");
        IsComputing = false;
        HasResult = true;
        if (first)
        {
            FirstShown?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void Apply()
    {
        if (_result is { } result && _apply(result.Command()))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _computing?.Cancel();
        _computing?.Dispose();
        _computing = null;
    }
}
