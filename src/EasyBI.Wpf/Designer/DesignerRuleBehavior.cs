using EasyBI.Core;
using EasyBI.Wpf.Resources;
using Microsoft.Xaml.Behaviors;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// A rule of a <see cref="ReportDesigner"/>, declared in XAML: while attached, it is one of the designer's
/// <see cref="ReportDesigner.Rules"/>. Derive from it to write a rule
/// of the application:
/// <code>
/// public sealed class ThreeMeasuresAtMost : DesignerRuleBehavior
/// {
///     protected override string? Check(VisualChange change) =&gt; change.After.Values.Count &gt; 3 ? "Three measures at most" : null;
/// }
/// </code>
/// </summary>
public abstract class DesignerRuleBehavior : Behavior<ReportDesigner>
{
    /// <summary>The message shown when the rule refuses a change; null for the rule's own.</summary>
    public string? Message { get; set; }

    /// <summary>Null to accept the change, or why it is refused. Called to try changes: quick, without side effect.</summary>
    protected abstract string? Check(VisualChange change);

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Rules.Add(Check);
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Rules.Remove(Check);
        base.OnDetaching();
    }
}

/// <summary>The type of the visual cannot change.</summary>
public sealed class LockVisualTypeBehavior : DesignerRuleBehavior
{
    protected override string? Check(VisualChange change) =>
        change.Changes(v => v.Type) ? Message ?? Strings.RuleVisualTypeLocked : null;
}

/// <summary>
/// The fields of the wells cannot change (none added, removed or moved), their settings can: aggregation, format, sort,
/// filter values...
/// </summary>
public sealed class LockFieldsBehavior : DesignerRuleBehavior
{
    protected override string? Check(VisualChange change) =>
        change.FieldsChanged ? Message ?? Strings.RuleFieldsLocked : null;
}

/// <summary>Nothing of the visual can change: the designer shows it.</summary>
public sealed class ReadOnlyDesignerBehavior : DesignerRuleBehavior
{
    protected override string? Check(VisualChange change) => Message ?? Strings.RuleReadOnly;
}
