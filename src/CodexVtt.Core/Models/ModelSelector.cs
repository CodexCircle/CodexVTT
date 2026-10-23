using CodexVtt.Core.Hardware;

namespace CodexVtt.Core.Models;

public enum CompatibilityIssue
{
    InsufficientMemory,
    InsufficientDisk,
    TooSlow,
    NotPinned,
}

public sealed record ModelAssessment(ModelDescriptor Model, IReadOnlyList<CompatibilityIssue> Issues)
{
    public bool IsCompatible => Issues.Count == 0;
}

/// <summary>
/// Motivazione della raccomandazione: codice stabile più parametri, così la UI la traduce
/// (italiano/inglese) senza testi nel core.
/// </summary>
public sealed record SelectionReason(string Code, IReadOnlyDictionary<string, string> Args)
{
    public const string RamLimit = "ram_limit";
    public const string CpuBudget = "cpu_budget";
    public const string CompatibilityRuntime = "compat_runtime";
    public const string Recommended = "recommended";
    public const string NoneCompatible = "none_compatible";
}

public sealed record ModelRecommendation(
    ModelDescriptor? Recommended,
    IReadOnlyList<ModelAssessment> Assessments,
    IReadOnlyList<SelectionReason> Reasons);

/// <summary>
/// Selezione adattiva deterministica (ADR-0003): funzione pura di profilo hardware, catalogo e
/// modelli installati. Sceglie solo tra modelli compatibili; l'override resta all'utente.
/// </summary>
public static class ModelSelector
{
    public static ModelRecommendation Recommend(
        HardwareProfile hardware,
        ModelCatalog catalog,
        IReadOnlySet<string> installedIds)
    {
        var rules = catalog.Selection;
        var maxCost = MaxCost(hardware, rules);
        var assessments = catalog.Models
            .Select(m => Assess(m, hardware, rules, maxCost, installedIds.Contains(m.Id)))
            .ToList();

        var best = assessments
            .Where(a => a.IsCompatible)
            .OrderByDescending(a => a.Model.QualityRank)
            .ThenBy(a => a.Model.RelativeCost)
            .FirstOrDefault();

        var reasons = new List<SelectionReason>
        {
            Reason(SelectionReason.RamLimit,
                ("totalBytes", hardware.TotalMemoryBytes.ToString(Invariant)),
                ("limitBytes", ((long)(hardware.TotalMemoryBytes * rules.MaxRamFraction)).ToString(Invariant))),
            Reason(SelectionReason.CpuBudget,
                ("cores", hardware.LogicalCores.ToString(Invariant)),
                ("maxCost", maxCost.ToString(Invariant))),
        };
        if (!hardware.SupportsStandardRuntime)
            reasons.Add(Reason(SelectionReason.CompatibilityRuntime));
        reasons.Add(best is null
            ? Reason(SelectionReason.NoneCompatible)
            : Reason(SelectionReason.Recommended, ("modelId", best.Model.Id), ("model", best.Model.DisplayName)));

        return new ModelRecommendation(best?.Model, assessments, reasons);
    }

    public static ModelAssessment Assess(
        ModelDescriptor model,
        HardwareProfile hardware,
        SelectionRules rules,
        int maxCost,
        bool installed)
    {
        var issues = new List<CompatibilityIssue>();
        if (!installed && !model.IsPinned)
            issues.Add(CompatibilityIssue.NotPinned);
        if (model.RamRequiredBytes > hardware.TotalMemoryBytes * rules.MaxRamFraction)
            issues.Add(CompatibilityIssue.InsufficientMemory);
        if (!installed && model.SizeBytes * (1 + rules.DiskHeadroomFraction) > hardware.FreeDiskBytes)
            issues.Add(CompatibilityIssue.InsufficientDisk);
        if (model.RelativeCost > maxCost)
            issues.Add(CompatibilityIssue.TooSlow);
        return new ModelAssessment(model, issues);
    }

    public static int MaxCost(HardwareProfile hardware, SelectionRules rules)
    {
        var byCores = rules.CostBudgetByCores
            .OrderByDescending(b => b.MinCores)
            .FirstOrDefault(b => hardware.LogicalCores >= b.MinCores)?.MaxCost ?? 1;
        return hardware.SupportsStandardRuntime ? byCores : Math.Min(byCores, rules.NoAvxMaxCost);
    }

    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    private static SelectionReason Reason(string code, params (string Key, string Value)[] args) =>
        new(code, args.ToDictionary(a => a.Key, a => a.Value));
}
