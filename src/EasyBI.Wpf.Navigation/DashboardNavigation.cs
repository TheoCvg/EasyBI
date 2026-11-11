using System.ComponentModel;
using System.Windows.Controls;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Dashboard;
using EasyBI.Wpf.Navigation.Resources;

namespace EasyBI.Wpf.Navigation;

/// <summary>
/// Navigation from report to report in a <see cref="DashboardView"/>: the menu of its visuals offers the
/// <see cref="Links"/> that apply ("Go to ›"), which open the target report on the data the user points at. The
/// filters and values it receives are imposed (shown and applied, not saved with it); going back restores the report
/// left as it was. It only uses EasyBI's public API: the menu of a visual (<see cref="EasyBIContext.VisualMenuOpening"/>),
/// what the user points at (<see cref="InteractionContext"/>) and imposed values (<see cref="FilterContext.Impose"/>,
/// <see cref="VariableContext.Impose"/>).
/// </summary>
public sealed class DashboardNavigation
{
    private readonly DashboardView _dashboard;
    private readonly FilterContext _filters;
    private readonly Func<string, DashboardDocument?> _load;
    private EasyBIContext? _context;

    /// <param name="dashboard">
    /// The dashboard showing the reports. Its <see cref="DashboardView.Context"/> may come later (a binding): the menus
    /// of the visuals follow it.
    /// </param>
    /// <param name="filters">The filters of the dashboard's page, which the reports' filters are loaded into.</param>
    /// <param name="load">Gives the report of an id, as saved (null when there is none).</param>
    public DashboardNavigation(DashboardView dashboard, FilterContext filters, Func<string, DashboardDocument?> load)
    {
        _dashboard = dashboard;
        _filters = filters;
        _load = load;
        Follow(dashboard.Context);
        DependencyPropertyDescriptor.FromProperty(DashboardView.ContextProperty, typeof(DashboardView))
            .AddValueChanged(dashboard, (_, _) => Follow(dashboard.Context));
    }

    // The menus of the visuals are those of the dashboard's current context
    private void Follow(EasyBIContext? context)
    {
        if (_context == context) return;
        if (_context is not null) _context.VisualMenuOpening -= OnMenuOpening;
        _context = context;
        if (_context is not null) _context.VisualMenuOpening += OnMenuOpening;
    }

    /// <summary>The links offered in the menu of the visuals.</summary>
    public IList<ReportLink> Links { get; } = [];

    /// <summary>The path the user followed.</summary>
    public NavigationHistory History { get; } = new();

    /// <summary>Raised once a report is shown, after a link was followed or going back.</summary>
    public event EventHandler? Navigated;

    /// <summary>Shows a report as the start of a new path. Returns false when the loader does not know it.</summary>
    public bool Start(string reportId) => _load(reportId) is { } document && Start(reportId, document);

    /// <summary>Shows <paramref name="document"/> (a report opened from a file...) as the start of a new path.</summary>
    public bool Start(string reportId, DashboardDocument document)
    {
        Show(document, NavigationArguments.None);
        History.Start(new NavigationEntry(reportId, document.Report.Title, NavigationArguments.None));
        Navigated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Opens the target of <paramref name="link"/> on what <paramref name="interaction"/> points at, remembering the
    /// current report as the user leaves it. Returns false when the loader does not know the target.
    /// </summary>
    public bool Follow(ReportLink link, InteractionContext interaction)
    {
        if (_load(link.Target) is not { } document) return false;
        var model = link.TargetModel ?? interaction.Visual.Model;
        var arguments = link.ArgumentsFor(interaction, _dashboard.Context?.Models.FirstOrDefault(m => m.Metadata.Name == model)?.Metadata);
        var left = Capture();
        Show(document, arguments);
        History.Push(new NavigationEntry(link.Target, document.Report.Title, arguments), left);
        Navigated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Goes back to the report at <paramref name="index"/> of the path, as the user left it.</summary>
    public bool GoBack(int index)
    {
        if (History.GoBack(index) is not { State: DashboardDocument left } entry) return false;
        Show(left, entry.Arguments);
        Navigated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Goes back to the previous report, as the user left it.</summary>
    public bool GoBack() => GoBack(History.Path.Count - 2);

    // The report as the user leaves it: what is saved, the imposed values being given again going back
    private DashboardDocument Capture()
    {
        var document = _dashboard.Capture();
        return document with { Report = document.Report with { Filters = _filters.SavedFilters } };
    }

    private void Show(DashboardDocument document, NavigationArguments arguments)
    {
        _filters.Load(document.Report.Filters);
        _dashboard.Load(document);
        if (arguments.Filters.Count > 0) _filters.Impose(arguments.Filters);
        // Without a context yet, the dashboard keeps the report's variables until it gets one: nothing to impose on
        if (arguments.Parameters.Count > 0) _dashboard.Context?.Variables.Impose(arguments.Parameters);
    }

    // "Go to ›" and the links that apply, in the menu of the visuals of this dashboard
    private void OnMenuOpening(object? sender, VisualMenuOpeningEventArgs e)
    {
        if (!_dashboard.IsAncestorOf(e.View)) return;
        var interaction = e.Interaction;
        var links = Links.Where(l => l.AppliesTo(interaction)).ToArray();
        if (links.Length == 0) return;
        var goTo = new MenuItem { Header = Strings.GoTo };
        foreach (var link in links)
        {
            var item = new MenuItem { Header = link.Caption };
            item.Click += (_, _) => Follow(link, interaction);
            goTo.Items.Add(item);
        }
        e.Menu.Items.Add(goTo);
    }
}
