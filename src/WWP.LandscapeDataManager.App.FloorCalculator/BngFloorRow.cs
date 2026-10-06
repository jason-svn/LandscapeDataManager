using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.FloorCalculator;

/// <summary>
/// A habitat suggestion in the BNG tab's habitat search box, shown by its full metric description.
/// A class rather than a positional record: XAML's generated type info can't handle init-only setters.
/// </summary>
public sealed class BngHabitatOption(BngHabitat habitat)
{
    public BngHabitat Habitat { get; } = habitat;

    public string Display => Habitat.Description;
}

/// <summary>
/// One Floor on the BNG tab. Its Revit phases decide which metric sheet it is a row of (see
/// <see cref="BngPhaseRoles"/>): an Existing-phase floor is a baseline A-1 row (retained, or lost
/// when demolished — and, flagged Enhanced, also an A-3 row), a later-phase floor an A-2 row. The
/// row recalculates against the metric whenever an input changes, so it reads like the workbook's
/// own row before anything is written to Revit.
/// </summary>
public sealed class BngFloorRow : INotifyPropertyChanged
{
    private readonly BngMetricCatalog _catalog;
    private readonly string _phaseRole;
    private bool _enhanced;
    private bool _unique;
    private bool _storedUnique;
    private BngHabitat? _habitat;
    private string _habitatQuery = string.Empty;
    private string? _condition;
    private BngHabitat? _baselineHabitat;
    private string _baselineQuery = string.Empty;
    private string? _baselineCondition;
    private string? _irreplaceable;
    private string? _strategicSignificance;
    private int _yearOffset;
    private bool _isAutoMatched;
    private string? _writeError;
    private string? _storedSignature;
    private string? _storedStatus;
    private string? _lastUpdated;

    public BngFloorRow(BngFloorItem item, BngMetricCatalog catalog)
    {
        _catalog = catalog;
        UniqueId = item.UniqueId;
        FamilyName = item.FamilyName;
        TypeName = item.TypeName;
        TypeId = item.TypeId;
        AreaSquareMeters = item.AreaSquareMeters;
        ParametersBound = item.ParametersBound;
        TypeHabitatMapping = item.TypeHabitatMapping;
        PhaseCreated = item.PhaseCreated;
        PhaseDemolished = item.PhaseDemolished;
        _phaseRole = item.PhaseRole;
        _enhanced = item.Enhanced && item.PhaseRole == BngPhaseRoles.Retained;
        UniqueSupported = item.UniqueSupported;
        _unique = _storedUnique = item.Unique && item.UniqueSupported;
        HadStoredInputs =
            !string.IsNullOrWhiteSpace(item.ProposedHabitat) || !string.IsNullOrWhiteSpace(item.Condition) ||
            !string.IsNullOrWhiteSpace(item.BaselineHabitat) || !string.IsNullOrWhiteSpace(item.BaselineCondition) ||
            !string.IsNullOrWhiteSpace(item.StrategicSignificance) || item.YearOffset != 0;
        _storedSignature = item.StoredInputSignature;
        _storedStatus = item.StoredStatus;
        _lastUpdated = item.StoredLastUpdated;
        _condition = NullIfBlank(item.Condition);
        _baselineCondition = NullIfBlank(item.BaselineCondition);
        _irreplaceable = NullIfBlank(item.Irreplaceable);
        _strategicSignificance = catalog.FindStrategicSignificance(item.StrategicSignificance)?.Description ?? item.StrategicSignificance;
        _yearOffset = item.YearOffset;
        SetHabitatCore(catalog.FindHabitat(item.ProposedHabitat), item.ProposedHabitat);
        SetBaselineHabitatCore(catalog.FindHabitat(item.BaselineHabitat), item.BaselineHabitat);
        Result = BngFloorCalculator.Calculate(BuildInput(), catalog);
        Status = EvaluateStatus();
    }

    /// <summary>Raised whenever <see cref="Status"/> changes, so the tab can refresh its status card counts.</summary>
    public event EventHandler? StatusChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string UniqueId { get; }
    public string FamilyName { get; }
    public string TypeName { get; }
    public long TypeId { get; }
    public double AreaSquareMeters { get; }
    public bool ParametersBound { get; }
    public string? TypeHabitatMapping { get; set; }
    public string? PhaseCreated { get; }
    public string? PhaseDemolished { get; }

    public string DisplayName => $"{FamilyName} : {TypeName}";
    public string AreaDisplay => $"{AreaSquareMeters:N1} m²";
    public double AreaHectares => AreaSquareMeters / BngFloorStatusEvaluator.SquareMetresPerHectare;

    // ---- Role (from phases) ----

    /// <summary>The floor's metric role: Created, Retained, Enhanced, Lost or Excluded.</summary>
    public string Role => _phaseRole == BngPhaseRoles.Retained && _enhanced ? BngPhaseRoles.Enhanced : _phaseRole;

    /// <summary>On site at baseline (an A-1 row): retained, enhanced or lost.</summary>
    public bool IsBaselineRow => Role is BngPhaseRoles.Retained or BngPhaseRoles.Enhanced or BngPhaseRoles.Lost;

    /// <summary>Needs a proposed habitat: new (A-2) or enhanced (A-3).</summary>
    public bool UsesProposedHabitat => Role is BngPhaseRoles.Created or BngPhaseRoles.Enhanced;

    /// <summary>Only a retained Existing-phase floor can be flagged Enhanced.</summary>
    public bool CanEnhance => _phaseRole == BngPhaseRoles.Retained;

    public Visibility EnhanceVisibility => CanEnhance ? Visibility.Visible : Visibility.Collapsed;

    public string RoleDisplay => Role switch
    {
        BngPhaseRoles.Created => "New (A-2)",
        BngPhaseRoles.Retained => "Retained (A-1)",
        BngPhaseRoles.Enhanced => "Enhanced (A-1 + A-3)",
        BngPhaseRoles.Lost => "Lost (A-1)",
        _ => "Excluded"
    };

    public string RoleDetails => PhaseDemolished is null
        ? $"Phase created: {PhaseCreated ?? "—"}"
        : $"Phase created: {PhaseCreated ?? "—"} · demolished: {PhaseDemolished}";

    public SolidColorBrush RoleBrush => Role switch
    {
        BngPhaseRoles.Created => new SolidColorBrush(Colors.SeaGreen),
        BngPhaseRoles.Enhanced => new SolidColorBrush(Colors.Teal),
        BngPhaseRoles.Retained => new SolidColorBrush(Colors.SteelBlue),
        BngPhaseRoles.Lost => new SolidColorBrush(Colors.Firebrick),
        _ => new SolidColorBrush(Colors.Gray)
    };

    /// <summary>The Enhanced flag (<c>!_S_PLT_BNGInput_Enhanced_YesNo</c>) — switches a retained floor onto sheet A-3.</summary>
    public bool Enhanced
    {
        get => _enhanced;
        set
        {
            if (!CanEnhance || _enhanced == value)
            {
                return;
            }

            _enhanced = value;
            OnPropertyChanged();
            OnRoleChanged();
            Recalculate();
        }
    }

    // ---- Linking floors of the same type ----

    /// <summary>
    /// The inputs that floors linked by <see cref="LinkKey"/> share. Changing any of these on a
    /// linked row is copied to the rest of its group (see BngTab).
    /// </summary>
    public static readonly IReadOnlySet<string> LinkedInputProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(Habitat), nameof(Condition), nameof(BaselineHabitat), nameof(BaselineCondition),
        nameof(IrreplaceableSelection), nameof(StrategicSignificance), nameof(YearOffset), nameof(IsAutoMatched)
    };

    /// <summary>True when Revit already held BNG inputs for this floor when it was loaded.</summary>
    public bool HadStoredInputs { get; }

    /// <summary>False until "Import Shared Parameter" binds <c>!_S_PLT_BNGInput_Unique_YesNo</c> in this project.</summary>
    public bool UniqueSupported { get; }

    public string UniqueToolTip => UniqueSupported
        ? "Unique: this floor keeps its own BNG inputs. Untick to share inputs with the other floors of this type and role again. Stored in !_S_PLT_BNGInput_Unique_YesNo."
        : "Run Import Shared Parameter to enable Unique in this project (it needs the !_S_PLT_BNGInput_Unique_YesNo parameter).";

    /// <summary>The Unique flag (<c>!_S_PLT_BNGInput_Unique_YesNo</c>) — a Unique floor neither sends nor receives inputs from same-type floors.</summary>
    public bool Unique
    {
        get => _unique;
        set
        {
            if (!UniqueSupported || _unique == value)
            {
                return;
            }

            _unique = value;
            OnPropertyChanged();
            UpdateStatus();
        }
    }

    /// <summary>Floors are linked when they share a type and a metric role (a new "Lawn" floor never feeds a retained one), unless either is Unique.</summary>
    public (long TypeId, string Role)? LinkKey =>
        _unique || TypeId < 0 || Role == BngPhaseRoles.Excluded ? null : (TypeId, Role);

    /// <summary>Takes every shared input from a linked floor of the same type and role, recalculating once.</summary>
    public void CopyInputsFrom(BngFloorRow source)
    {
        if (UsesProposedHabitat && source.UsesProposedHabitat)
        {
            if (!ReferenceEquals(_habitat, source._habitat) || _habitatQuery != source._habitatQuery)
            {
                SetHabitatCore(source._habitat, source._habitatQuery);
            }

            if (_condition != source._condition)
            {
                _condition = source._condition;
                OnPropertyChanged(nameof(Condition));
                OnPropertyChanged(nameof(ConditionSelection));
            }
        }

        if (IsBaselineRow && source.IsBaselineRow)
        {
            if (!ReferenceEquals(_baselineHabitat, source._baselineHabitat) || _baselineQuery != source._baselineQuery)
            {
                SetBaselineHabitatCore(source._baselineHabitat, source._baselineQuery);
            }

            if (_baselineCondition != source._baselineCondition)
            {
                _baselineCondition = source._baselineCondition;
                OnPropertyChanged(nameof(BaselineCondition));
                OnPropertyChanged(nameof(BaselineConditionSelection));
            }

            if (_irreplaceable != source._irreplaceable)
            {
                _irreplaceable = source._irreplaceable;
                OnPropertyChanged(nameof(IrreplaceableSelection));
            }
        }

        if (_strategicSignificance != source._strategicSignificance)
        {
            _strategicSignificance = source._strategicSignificance;
            OnPropertyChanged(nameof(StrategicSignificance));
            OnPropertyChanged(nameof(StrategicSignificanceSelection));
        }

        if (_yearOffset != source._yearOffset)
        {
            _yearOffset = source._yearOffset;
            OnPropertyChanged(nameof(YearOffset));
            OnPropertyChanged(nameof(YearOffsetValue));
        }

        IsAutoMatched = source.IsAutoMatched;
        Recalculate();
    }

    // ---- Proposed habitat (A-2 new floor, or A-3 enhancement target) ----

    public ObservableCollection<BngHabitatOption> HabitatSuggestions { get; } = [];

    /// <summary>The conditions the metric offers for the chosen proposed habitat.</summary>
    public ObservableCollection<string> ConditionOptions { get; } = [];

    public IReadOnlyList<string> StrategicSignificanceOptions => _catalog.StrategicSignificance.Select(option => option.Description).ToList();

    public BngHabitat? Habitat => _habitat;

    /// <summary>The proposed habitat search box's text — the chosen habitat's description once one is picked.</summary>
    public string HabitatQuery
    {
        get => _habitatQuery;
        set => SetField(ref _habitatQuery, value);
    }

    public string? Condition
    {
        get => _condition;
        set
        {
            if (_condition != value)
            {
                _condition = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConditionSelection));
                Recalculate();
            }
        }
    }

    /// <summary>
    /// <see cref="Condition"/> typed as object, for two-way binding to ComboBox.SelectedItem. A
    /// ComboBox pushes null back whenever its item list is reset (and while a recycled row is being
    /// re-bound), which isn't a user choice — the dropdown has no "clear" option — so null is ignored.
    /// A real change here is the user editing this row, which counts as reviewing an auto-matched
    /// habitat (unlike <see cref="Condition"/> set by bulk edit, which may touch rows nobody looked at).
    /// </summary>
    public object? ConditionSelection
    {
        get => _condition;
        set
        {
            if (value is string condition && condition != _condition)
            {
                ConfirmedByEdit();
                Condition = condition;
            }
        }
    }

    // ---- Baseline habitat (A-1, Existing-phase floors) ----

    public ObservableCollection<BngHabitatOption> BaselineHabitatSuggestions { get; } = [];

    public ObservableCollection<string> BaselineConditionOptions { get; } = [];

    public BngHabitat? BaselineHabitat => _baselineHabitat;

    public string BaselineQuery
    {
        get => _baselineQuery;
        set => SetField(ref _baselineQuery, value);
    }

    public string? BaselineCondition
    {
        get => _baselineCondition;
        set
        {
            if (_baselineCondition != value)
            {
                _baselineCondition = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BaselineConditionSelection));
                Recalculate();
            }
        }
    }

    /// <summary>Same null-ignoring, edit-confirms-the-habitat two-way wrapper as <see cref="ConditionSelection"/>.</summary>
    public object? BaselineConditionSelection
    {
        get => _baselineCondition;
        set
        {
            if (value is string condition && condition != _baselineCondition)
            {
                ConfirmedByEdit();
                BaselineCondition = condition;
            }
        }
    }

    /// <summary>The irreplaceable answers the A-1 dropdown offers for the baseline habitat (both only where G-1 says "Yes/No").</summary>
    public IReadOnlyList<string> IrreplaceableOptions => _baselineHabitat?.Irreplaceable switch
    {
        "Yes" => ["Yes"],
        "Yes/No" => ["No", "Yes"],
        null => [],
        _ => ["No"]
    };

    /// <summary>Only a real choice for "Yes/No" habitats; otherwise the single option applies.</summary>
    public bool IrreplaceableIsChoice => IsBaselineRow && _baselineHabitat?.Irreplaceable == "Yes/No";

    public object? IrreplaceableSelection
    {
        get => _irreplaceable ?? (IrreplaceableOptions is [var only] ? only : null);
        set
        {
            if (value is string answer && answer != _irreplaceable)
            {
                ConfirmedByEdit();
                _irreplaceable = answer;
                OnPropertyChanged();
                Recalculate();
            }
        }
    }

    // ---- Shared inputs ----

    public string? StrategicSignificance
    {
        get => _strategicSignificance;
        set
        {
            if (_strategicSignificance != value)
            {
                _strategicSignificance = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StrategicSignificanceSelection));
                Recalculate();
            }
        }
    }

    /// <summary>Same null-ignoring, edit-confirms-the-habitat two-way wrapper as <see cref="ConditionSelection"/>.</summary>
    public object? StrategicSignificanceSelection
    {
        get => _strategicSignificance;
        set
        {
            if (value is string strategic && strategic != _strategicSignificance)
            {
                ConfirmedByEdit();
                StrategicSignificance = strategic;
            }
        }
    }

    public int YearOffset
    {
        get => _yearOffset;
        set
        {
            if (_yearOffset != value)
            {
                _yearOffset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(YearOffsetValue));
                Recalculate();
            }
        }
    }

    /// <summary><see cref="YearOffset"/> as the double a NumberBox binds to (NaN when cleared → 0); an edit confirms the habitat.</summary>
    public double YearOffsetValue
    {
        get => _yearOffset;
        set
        {
            var offset = double.IsNaN(value) ? 0 : (int)Math.Round(value);
            if (offset != _yearOffset)
            {
                ConfirmedByEdit();
                YearOffset = offset;
            }
        }
    }

    /// <summary>True while this row's habitat (baseline for an Existing floor, else proposed) is only a guess from the floor's family/type name.</summary>
    public bool IsAutoMatched
    {
        get => _isAutoMatched;
        private set
        {
            SetField(ref _isAutoMatched, value);
            OnPropertyChanged(nameof(ConfirmVisibility));
        }
    }

    /// <summary>Shows the row's own "✓ Confirm" button only while its habitat is an unconfirmed guess.</summary>
    public Visibility ConfirmVisibility => _isAutoMatched ? Visibility.Visible : Visibility.Collapsed;

    public BngFloorResult Result { get; private set; }

    public string Status { get; private set; }

    public string StatusDisplay => Status switch
    {
        BngFloorStatus.AutoMatched => "Auto-matched",
        BngFloorStatus.NeedsInfo => "Needs info",
        BngFloorStatus.CheckData => "Check data",
        BngFloorStatus.Ready => "Ready to write",
        _ => Status
    };

    public SolidColorBrush StatusBrush => Status switch
    {
        BngFloorStatus.Calculated => new SolidColorBrush(Colors.SeaGreen),
        BngFloorStatus.Ready => new SolidColorBrush(Colors.SteelBlue),
        BngFloorStatus.AutoMatched => new SolidColorBrush(Colors.MediumPurple),
        BngFloorStatus.CheckData => new SolidColorBrush(Colors.DarkOrange),
        BngFloorStatus.Stale => new SolidColorBrush(Colors.Goldenrod),
        BngFloorStatus.Failed => new SolidColorBrush(Colors.Firebrick),
        _ => new SolidColorBrush(Colors.Gray)
    };

    /// <summary>The one-line reason for <see cref="Status"/>, shown under it in the status column.</summary>
    public string StatusReason => Status switch
    {
        BngFloorStatus.Failed => _writeError ?? "The last write to Revit failed.",
        BngFloorStatus.AutoMatched => Result.Issues.Count > 0
            ? $"Guessed from the name. Also: {Result.Issues[0]}"
            : "Guessed from the name — confirm it to write.",
        BngFloorStatus.NeedsInfo => string.Join(" ", Result.Issues),
        BngFloorStatus.CheckData => Result.Issues.Count > 0 ? Result.Issues[0] : "The metric flags this row.",
        BngFloorStatus.Stale => "Area, phases or inputs changed since the last write.",
        BngFloorStatus.Ready => ParametersBound ? "Not written to Revit yet." : "BNG parameters aren't set up in this project — run Parameters first.",
        _ => _lastUpdated is null ? string.Empty : $"Written {_lastUpdated}"
    };

    /// <summary>Everything behind the row's status — shown as the status column's tooltip.</summary>
    public string StatusDetails
    {
        get
        {
            var lines = new List<string> { $"{RoleDisplay} — {RoleDetails}" };
            if (_writeError is not null)
            {
                lines.Add($"Write failed: {_writeError}");
            }

            if (!ParametersBound)
            {
                lines.Add("The BNG parameters aren't set up in this project yet — run Parameters from the LIM ribbon before writing.");
            }

            if (IsAutoMatched)
            {
                lines.Add($"Habitat guessed from the name '{DisplayName}' — confirm it or pick another.");
            }

            lines.AddRange(Result.Issues);
            lines.Add(_lastUpdated is null ? "Not written to Revit yet." : $"Last written: {_lastUpdated}");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Post-intervention habitat units this floor contributes (A-2, A-3, or A-1 retained).</summary>
    public string HabitatUnitsDisplay => Result.HabitatUnitsText;

    public string BaselineUnitsDisplay => IsBaselineRow ? Result.BaselineUnitsText : "—";

    /// <summary>The habitat remembered per floor type: the baseline habitat for an Existing floor, else the proposed one.</summary>
    public BngHabitat? MappedHabitat => IsBaselineRow ? _baselineHabitat : _habitat;

    public string InputSignature => BngFloorCalculator.ComputeSignature(BuildInput(), _catalog);

    /// <summary>True when Revit doesn't already hold this row's current inputs and result.</summary>
    public bool NeedsWrite =>
        !string.Equals(_storedSignature, InputSignature, StringComparison.Ordinal) ||
        !string.Equals(_storedStatus, Result.Outcome.ToString(), StringComparison.Ordinal) ||
        _unique != _storedUnique;

    public bool HasAnyInput =>
        Role == BngPhaseRoles.Excluded ||
        _habitat is not null || _baselineHabitat is not null ||
        !string.IsNullOrWhiteSpace(_condition) || !string.IsNullOrWhiteSpace(_baselineCondition) ||
        !string.IsNullOrWhiteSpace(_strategicSignificance) || _yearOffset != 0;

    /// <summary>Picks a proposed habitat (from search, bulk edit, or the type's remembered mapping) as confirmed.</summary>
    public void SetHabitat(BngHabitat? habitat)
    {
        SetHabitatCore(habitat, habitat?.Description);
        IsAutoMatched = false;
        Recalculate();
    }

    /// <summary>Picks a baseline habitat as confirmed.</summary>
    public void SetBaselineHabitat(BngHabitat? habitat)
    {
        SetBaselineHabitatCore(habitat, habitat?.Description);
        IsAutoMatched = false;
        Recalculate();
    }

    /// <summary>Pre-fills a guessed habitat into this row's primary habitat (baseline for an Existing floor), flagged Auto-matched until confirmed.</summary>
    public void SetAutoMatchedHabitat(BngHabitat habitat)
    {
        if (IsBaselineRow)
        {
            SetBaselineHabitatCore(habitat, habitat.Description);
        }
        else
        {
            SetHabitatCore(habitat, habitat.Description);
        }

        IsAutoMatched = true;
        Recalculate();
    }

    /// <summary>Clears the auto-match flag ahead of an input edit, which recalculates (and re-evaluates status) itself.</summary>
    private void ConfirmedByEdit() => IsAutoMatched = false;

    public void ConfirmAutoMatch()
    {
        if (IsAutoMatched)
        {
            IsAutoMatched = false;
            Recalculate();
        }
    }

    public BngFloorInput BuildInput() => new(
        _phaseRole,
        _enhanced,
        _baselineHabitat?.Description ?? NullIfBlank(_baselineQuery),
        _baselineCondition,
        _irreplaceable,
        _habitat?.Description ?? NullIfBlank(_habitatQuery),
        _condition,
        _strategicSignificance,
        _yearOffset,
        AreaHectares);

    public BngFloorValues BuildValues()
    {
        var result = Result;
        return new BngFloorValues(
            _habitat?.Description ?? string.Empty,
            _condition ?? string.Empty,
            _strategicSignificance ?? string.Empty,
            _yearOffset,
            result.BroadHabitat,
            result.AreaHectares,
            result.Distinctiveness,
            result.DistinctivenessScore,
            result.ConditionScore,
            result.StrategicSignificance,
            result.StrategicSignificanceMultiplier,
            result.StandardTimeToTarget,
            result.TimeToTargetStatus,
            result.FinalTimeToTarget,
            result.FinalTimeToTargetMultiplier,
            result.StandardDifficulty,
            result.AppliedDifficulty,
            result.FinalDifficulty,
            result.DifficultyMultiplier,
            result.HabitatUnits ?? 0,
            result.Outcome.ToString(),
            string.Join(" | ", result.Issues),
            InputSignature,
            _catalog.DisplayVersion,
            Role,
            _baselineHabitat?.Description ?? string.Empty,
            _baselineCondition ?? string.Empty,
            _irreplaceable ?? string.Empty,
            _enhanced,
            result.BaselineUnits ?? 0,
            _unique);
    }

    public void MarkWritten(string lastUpdated)
    {
        _storedSignature = InputSignature;
        _storedStatus = Result.Outcome.ToString();
        _storedUnique = _unique;
        _lastUpdated = lastUpdated;
        _writeError = null;
        UpdateStatus();
    }

    public void MarkWriteFailed(string error)
    {
        _writeError = error;
        UpdateStatus();
    }

    private void SetHabitatCore(BngHabitat? habitat, string? query)
    {
        _habitat = habitat;
        HabitatQuery = habitat?.Description ?? query ?? string.Empty;
        _condition = RefreshConditionOptions(ConditionOptions, habitat, _condition);
        OnPropertyChanged(nameof(Condition));
        OnPropertyChanged(nameof(ConditionSelection));
        OnPropertyChanged(nameof(Habitat));
    }

    private void SetBaselineHabitatCore(BngHabitat? habitat, string? query)
    {
        _baselineHabitat = habitat;
        BaselineQuery = habitat?.Description ?? query ?? string.Empty;
        _baselineCondition = RefreshConditionOptions(BaselineConditionOptions, habitat, _baselineCondition);

        // Keep the irreplaceable answer only while the new habitat still offers it.
        if (_irreplaceable is not null && !IrreplaceableOptions.Contains(_irreplaceable))
        {
            _irreplaceable = null;
        }

        OnPropertyChanged(nameof(BaselineCondition));
        OnPropertyChanged(nameof(BaselineConditionSelection));
        OnPropertyChanged(nameof(BaselineHabitat));
        OnPropertyChanged(nameof(IrreplaceableOptions));
        OnPropertyChanged(nameof(IrreplaceableSelection));
        OnPropertyChanged(nameof(IrreplaceableIsChoice));
    }

    /// <summary>Refills a condition dropdown for <paramref name="habitat"/>, keeping <paramref name="current"/> only if the habitat offers it (the metric's dropdown wouldn't).</summary>
    private string? RefreshConditionOptions(ObservableCollection<string> options, BngHabitat? habitat, string? current)
    {
        options.Clear();
        if (habitat is null)
        {
            return current;
        }

        foreach (var option in _catalog.GetConditionOptions(habitat))
        {
            options.Add(option);
        }

        if (current is not null)
        {
            var kept = options.FirstOrDefault(option => string.Equals(option, current, StringComparison.OrdinalIgnoreCase));
            return kept ?? (options.Count == 1 ? options[0] : null);
        }

        // Habitats whose only option is e.g. "N/A - Other" need no choice at all.
        return options.Count == 1 ? options[0] : null;
    }

    private void OnRoleChanged()
    {
        OnPropertyChanged(nameof(Role));
        OnPropertyChanged(nameof(RoleDisplay));
        OnPropertyChanged(nameof(RoleBrush));
        OnPropertyChanged(nameof(UsesProposedHabitat));
        OnPropertyChanged(nameof(BaselineUnitsDisplay));
    }

    private void Recalculate()
    {
        Result = BngFloorCalculator.Calculate(BuildInput(), _catalog);
        _writeError = null;
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(HabitatUnitsDisplay));
        OnPropertyChanged(nameof(BaselineUnitsDisplay));
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var status = EvaluateStatus();
        var changed = status != Status;
        Status = status;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusDetails));
        OnPropertyChanged(nameof(StatusReason));
        if (changed)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string EvaluateStatus() =>
        BngFloorStatusEvaluator.Evaluate(Result.Outcome, IsAutoMatched, InputSignature, _storedSignature, _writeError is not null);

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(propertyName);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
