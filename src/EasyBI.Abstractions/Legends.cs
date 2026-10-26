namespace EasyBI.Abstractions;

/// <summary>
/// Where the legend of a chart goes. A visual chooses one (<see cref="VisualDefinition.LegendPosition"/>);
/// <see cref="Auto"/> lets the chart decide from its size. A side only applies when the chart has room for it.
/// </summary>
public enum ChartLegend { Auto, Right, Bottom, Top, Left, Hidden }

/// <summary>
/// The pages of a chart's legend, shared by a connector and its host: when the legend has more entries than its room
/// shows, the connector shows one page of them and reports how many pages there are (<see cref="Update"/>); the host
/// shows a pager next to the legend and turns the pages (<see cref="GoTo"/>), which the connector follows
/// (<see cref="Changed"/>).
/// </summary>
public sealed class LegendPager
{
    /// <summary>Where the legend is drawn (never <see cref="ChartLegend.Auto"/>): the host puts its pager next to it.</summary>
    public ChartLegend Position { get; private set; } = ChartLegend.Hidden;

    /// <summary>How many pages the legend has; 1 when all its entries show.</summary>
    public int PageCount { get; private set; } = 1;

    /// <summary>The page shown, from 0.</summary>
    public int Page { get; private set; }

    /// <summary>Raised when the position, the number of pages or the page changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Called by the connector each time it lays out its legend; the page stays within the new count.</summary>
    public void Update(ChartLegend position, int pageCount)
    {
        pageCount = Math.Max(1, pageCount);
        var page = Math.Min(Page, pageCount - 1);
        if (position == Position && pageCount == PageCount && page == Page) return;
        (Position, PageCount, Page) = (position, pageCount, page);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows a page (kept within the pages).</summary>
    public void GoTo(int page)
    {
        page = Compat.Clamp(page, 0, PageCount - 1);
        if (page == Page) return;
        Page = page;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
