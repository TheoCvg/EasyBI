using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

/// <summary>Layout operations on the tree of a dashboard. Shapes read: <c>Row(a,b)</c> side by side, <c>Col(a,b)</c>
/// stacked, <c>|</c> between lines, <c>a:4</c> 4 columns wide, <c>a^2</c> 2 rows high, <c>a~300</c> 300 px wide.</summary>
public class DashboardLayoutEngineTests
{
    private static LayoutNode I(string id, int? columns = null, int? rows = null, double? width = null) =>
        new() { Id = id, VisualId = id, ColumnSpan = columns, RowSpan = rows, Width = width };

    private static LayoutNode Row(params LayoutNode[] children) => LayoutNode.Row(children);

    private static LayoutNode Col(params LayoutNode[] children) => LayoutNode.Column(children);

    private static DashboardLayoutDefinition Grid(params LayoutNode[] lines) => new() { Bands = [new BandLayout { Items = lines }] };

    private static DashboardLayoutDefinition Bands(params BandLayout[] bands) => new() { Bands = bands };

    private static string Shape(DashboardLayoutDefinition layout, int band = 0) =>
        string.Join(" | ", layout.Bands[band].Items.Select(Shape));

    private static string Shape(LayoutNode node)
    {
        var size = (node.ColumnSpan is { } c ? $":{c}" : "") + (node.RowSpan is { } r ? $"^{r}" : "") + (node.Width is { } w ? $"~{w}" : "");
        return node.IsItem
            ? (node.VisualId ?? "_") + size
            : (node.Split == SplitDirection.Row ? "Row" : "Col") + size + "(" + string.Join(",", node.Children.Select(Shape)) + ")";
    }

    private static LayoutNode New(string id) => I(id);

    // --- Placing visuals and normalizing ---

    [Fact]
    public void Placing_nothing_gives_one_band_with_one_empty_item()
    {
        var layout = DashboardLayoutEngine.Place(null, []);

        Assert.Equal("_", Shape(layout));
        Assert.Equal(BandMode.Fit, Assert.Single(layout.Bands).Mode);
    }

    [Fact]
    public void Visuals_without_a_layout_are_placed_two_per_line()
    {
        Assert.Equal("Row(a,b) | c", Shape(DashboardLayoutEngine.Place(null, ["a", "b", "c"])));
    }

    [Fact]
    public void Placing_empties_unknown_and_repeated_visuals_and_adds_the_missing_ones()
    {
        var layout = DashboardLayoutEngine.Place(Grid(Row(I("a"), I("gone"), I("a"))), ["a", "b"]);

        Assert.Equal("Row(a,_,_) | b", Shape(layout));
    }

    [Theory]
    [InlineData("Row(Row(a,b),c)", "Row(a,b,c)")]       // same direction: merged
    [InlineData("Row(Col(a))", "a")]                     // a single child is the container
    [InlineData("Col(a,b)", "a | b")]                    // lines are already stacked
    [InlineData("Row(a,Col(b:4,c))", "Row(a,Col(b,c))")] // a width inside a stack means nothing
    [InlineData("Row(a:4)", "Row(a:4)")]                 // ...but a narrowed item alone keeps its line
    public void Normalizing_simplifies_the_tree(string tree, string expected)
    {
        var layout = Grid(tree switch
        {
            "Row(Row(a,b),c)" => Row(Row(I("a"), I("b")), I("c")),
            "Row(Col(a))" => Row(Col(I("a"))),
            "Col(a,b)" => Col(I("a"), I("b")),
            "Row(a,Col(b:4,c))" => Row(I("a"), Col(I("b", columns: 4), I("c"))),
            _ => Row(I("a", columns: 4)),
        });

        Assert.Equal(expected, Shape(DashboardLayoutEngine.Normalize(layout)));
    }

    // --- Adding next to an item ---

    [Fact]
    public void An_item_added_beside_an_item_of_a_line_joins_the_line()
    {
        var layout = DashboardLayoutEngine.AddNextTo(Grid(Row(I("a"), I("b"))), "a", ItemSide.Right, New("n"));

        Assert.Equal("Row(a,n,b)", Shape(layout));
        Assert.Equal("Row(n,a,b)", Shape(DashboardLayoutEngine.AddNextTo(Grid(Row(I("a"), I("b"))), "a", ItemSide.Left, New("n"))));
    }

    [Fact]
    public void An_item_added_below_an_item_of_a_line_is_stacked_with_it_in_its_columns()
    {
        var layout = DashboardLayoutEngine.AddNextTo(Grid(Row(I("a"), I("b", columns: 4))), "b", ItemSide.Below, New("n"));

        Assert.Equal("Row(a,Col:4(b,n))", Shape(layout));
    }

    [Fact]
    public void Next_to_a_whole_line_item_a_line_is_made_or_inserted()
    {
        Assert.Equal("Row(a,n) | b", Shape(DashboardLayoutEngine.AddNextTo(Grid(I("a"), I("b")), "a", ItemSide.Right, New("n"))));
        Assert.Equal("a | n | b", Shape(DashboardLayoutEngine.AddNextTo(Grid(I("a"), I("b")), "a", ItemSide.Below, New("n"))));
        Assert.Equal("n | a | b", Shape(DashboardLayoutEngine.AddNextTo(Grid(I("a"), I("b")), "a", ItemSide.Above, New("n"))));
    }

    [Fact]
    public void A_line_without_room_for_one_more_column_drops_its_constraints()
    {
        var layout = DashboardLayoutEngine.AddNextTo(Grid(Row(I("a", columns: 6), I("b", columns: 6))), "a", ItemSide.Right, New("n"));

        Assert.Equal("Row(a,n,b)", Shape(layout));
    }

    [Fact]
    public void A_new_item_fills_the_free_space_its_anchor_leaves()
    {
        // The user narrowed the only item to 6 columns: the item added on its right takes the rest
        var layout = DashboardLayoutEngine.AddNextTo(Grid(I("a", columns: 6)), "a", ItemSide.Right, New("n"));
        var band = BandArrangement.Arrange(layout.Bands[0], new ArrangeSettings(1190, 12, 100, 10, 280));

        Assert.Equal("Row(a:6,n)", Shape(layout));
        Assert.Equal((6, 6), band.Columns["n"]);
        Assert.Empty(band.FreeSpaces);
    }

    [Fact]
    public void Scrolling_and_wrapping_bands_insert_after_or_before_the_anchor()
    {
        var strip = Bands(new BandLayout { Mode = BandMode.Scroll, Items = [I("a"), I("b")] });

        Assert.Equal("a | n | b", Shape(DashboardLayoutEngine.AddNextTo(strip, "a", ItemSide.Right, New("n"))));
        Assert.Equal("n | a | b", Shape(DashboardLayoutEngine.AddNextTo(strip, "a", ItemSide.Left, New("n"))));
    }

    // --- Moving ---

    [Fact]
    public void A_moved_item_goes_between_two_items_of_a_line()
    {
        var layout = DashboardLayoutEngine.MoveNextTo(Grid(Row(I("a"), I("b")), I("x")), "x", "a", ItemSide.Right);

        Assert.Equal("Row(a,x,b)", Shape(layout));
    }

    [Fact]
    public void A_moved_item_becomes_greedy_in_width_and_keeps_its_height()
    {
        var layout = DashboardLayoutEngine.MoveNextTo(Grid(Row(I("a"), I("x", columns: 4, rows: 2)), I("b")), "x", "b", ItemSide.Right);

        Assert.Equal("a | Row(b,x^2)", Shape(layout));
    }

    [Fact]
    public void A_stack_left_with_one_item_is_merged_away()
    {
        var layout = DashboardLayoutEngine.MoveNextTo(Grid(Row(I("a"), Col(I("x"), I("y")))), "y", "a", ItemSide.Below);

        Assert.Equal("Row(Col(a,y),x)", Shape(layout));
    }

    [Fact]
    public void An_item_can_move_next_to_the_container_it_leaves()
    {
        // Free spaces are anchored on containers: the anchor survives the item leaving it
        var layout = Grid(Row(I("a", columns: 3), Col(I("x", rows: 1), I("y", rows: 1)) with { Id = "stack", ColumnSpan = 3 }));

        Assert.Equal("Row(a:3,Col:3(x^1),y^1)", Shape(DashboardLayoutEngine.MoveNextTo(layout, "y", "stack", ItemSide.Right)));
    }

    [Theory]
    [InlineData("a", 2, "b | a | c")]
    [InlineData("c", 0, "c | a | b")]
    [InlineData("a", 3, "b | c | a")]
    public void A_moved_item_becomes_a_line_at_an_index_of_the_band_before_the_move(string item, int index, string expected)
    {
        Assert.Equal(expected, Shape(DashboardLayoutEngine.MoveTo(Grid(I("a"), I("b"), I("c")), item, 0, index)));
    }

    [Fact]
    public void A_band_emptied_by_a_move_disappears()
    {
        var layout = Bands(new BandLayout { Items = [I("x")] }, new BandLayout { Mode = BandMode.Wrap, Items = [I("a"), I("b")] });

        layout = DashboardLayoutEngine.MoveNextTo(layout, "x", "a", ItemSide.Right);

        Assert.Equal(BandMode.Wrap, Assert.Single(layout.Bands).Mode);
        Assert.Equal("a | x | b", Shape(layout));
    }

    [Fact]
    public void Moving_an_item_next_to_itself_changes_nothing()
    {
        var layout = DashboardLayoutEngine.Normalize(Grid(Row(I("a"), I("b"))));

        Assert.Equal("Row(a,b)", Shape(DashboardLayoutEngine.MoveNextTo(layout, "a", "a", ItemSide.Right)));
    }

    // --- Sizes ---

    [Fact]
    public void Resizing_constrains_the_item_or_the_stack_it_is_in()
    {
        var layout = Grid(Row(I("a"), Col(I("x"), I("y"))));

        Assert.Equal("Row(a:4,Col(x,y))", Shape(DashboardLayoutEngine.Resize(layout, "a", 4, null)));
        Assert.Equal("Row(a,Col:5(x,y))", Shape(DashboardLayoutEngine.Resize(layout, "x", 5, null)));
        Assert.Equal("Row(a,Col(x^2,y))", Shape(DashboardLayoutEngine.Resize(layout, "x", null, 2)));
    }

    [Fact]
    public void A_width_leaves_a_column_to_each_sibling()
    {
        Assert.Equal("Row(a:10,b,c)", Shape(DashboardLayoutEngine.Resize(Grid(Row(I("a"), I("b"), I("c"))), "a", 12, null)));
        Assert.Equal("Row(a:8,b:4)", Shape(DashboardLayoutEngine.Resize(Grid(Row(I("a"), I("b", columns: 4))), "a", 9, null)));
    }

    [Fact]
    public void An_item_alone_on_its_line_can_be_narrowed()
    {
        Assert.Equal("a:6 | b", Shape(DashboardLayoutEngine.Resize(Grid(I("a"), I("b")), "a", 6, null)));
    }

    [Fact]
    public void Automatic_size_removes_every_constraint_of_an_item()
    {
        var layout = Grid(Row(I("a", rows: 2), Col(I("x", rows: 1), I("y")) with { Id = "stack", ColumnSpan = 4 }));

        Assert.True(DashboardLayoutEngine.IsConstrained(layout, "y")); // through its stack
        Assert.Equal("Row(a^2,Col(x^1,y))", Shape(DashboardLayoutEngine.SetAuto(layout, "y")));
        Assert.Equal("Row(a,Col:4(x^1,y))", Shape(DashboardLayoutEngine.SetAuto(layout, "a")));
        Assert.False(DashboardLayoutEngine.IsConstrained(DashboardLayoutEngine.SetAuto(layout, "a"), "a"));
    }

    [Fact]
    public void Wrapping_items_are_resized_in_pixels()
    {
        var layout = Bands(new BandLayout { Mode = BandMode.Wrap, Items = [I("a"), I("b")] });

        Assert.Equal("a^2~420 | b", Shape(DashboardLayoutEngine.ResizeWidth(layout, "a", 420, 2)));
        Assert.Equal("a~160 | b", Shape(DashboardLayoutEngine.ResizeWidth(layout, "a", 20, null))); // minimum width
    }

    // --- Removing ---

    [Fact]
    public void Removing_an_item_gives_its_room_to_its_siblings()
    {
        Assert.Equal("a", Shape(DashboardLayoutEngine.Remove(Grid(Row(I("a"), I("b"))), "b")));
        Assert.Equal("Row(a,x)", Shape(DashboardLayoutEngine.Remove(Grid(Row(I("a"), Col(I("x"), I("y")))), "y")));
    }

    [Fact]
    public void A_narrowed_item_left_alone_keeps_its_width_and_a_free_space()
    {
        Assert.Equal("Row(a:4)", Shape(DashboardLayoutEngine.Remove(Grid(Row(I("a", columns: 4), I("b"))), "b")));
    }

    [Fact]
    public void Removing_the_last_item_of_a_band_removes_the_band()
    {
        var layout = DashboardLayoutEngine.Remove(Bands(new BandLayout { Items = [I("a")] }, new BandLayout { Items = [I("b")] }), "a");

        Assert.Single(layout.Bands);
        Assert.Equal("b", Shape(layout));
    }

    // --- Bands ---

    [Fact]
    public void A_grid_band_becomes_a_wrapping_band_keeping_constrained_widths_in_pixels()
    {
        // 12 columns over 1210 px with 10 px gaps: a column is 91.67 px, 6 columns are 600 px
        var layout = DashboardLayoutEngine.SetBandMode(Grid(Row(I("a", columns: 6), I("b"))), 0, BandMode.Wrap, 1210, 10);

        Assert.Equal(BandMode.Wrap, layout.Bands[0].Mode);
        Assert.Equal("a~600 | b", Shape(layout));
    }

    [Fact]
    public void A_wrapping_band_becomes_a_grid_with_the_lines_it_showed()
    {
        var wrap = Bands(new BandLayout { Mode = BandMode.Wrap, Items = [I("a", width: 600), I("b"), I("c"), I("d")] });

        // 600 + 280 + 280 fit in 1200 px, d wraps; 600 px are 6 columns of 100 px
        var layout = DashboardLayoutEngine.SetBandMode(wrap, 0, BandMode.Fit, 1200, 0, 280);

        Assert.Equal("Row(a:6,b,c) | d", Shape(layout));
    }

    [Fact]
    public void Bands_can_be_inserted_and_removed()
    {
        var layout = DashboardLayoutEngine.InsertBand(Grid(I("a")), 0, BandMode.Scroll, [I("n")]);

        Assert.Equal([BandMode.Scroll, BandMode.Fit], layout.Bands.Select(b => b.Mode));
        Assert.Equal("a", Shape(DashboardLayoutEngine.RemoveBand(layout, 0)));
    }

    // --- The user's walk-through ---

    [Fact]
    public void A_dashboard_grows_from_one_greedy_item_without_ever_leaving_a_gap()
    {
        var settings = new ArrangeSettings(1190, 12, 100, 10, 280);
        BandArrangement Arrange(DashboardLayoutDefinition l) => BandArrangement.Arrange(l.Bands[0], settings);

        // 1-2. An item alone takes the whole line
        var layout = DashboardLayoutEngine.Place(null, ["v1"]);
        var v1 = layout.Bands[0].Items[0].Id;
        Assert.Equal(new System.Windows.Rect(0, 0, 1190, 320), Arrange(layout).Items[v1]);

        // 3. Constrained to half the width: the rest is a free space offering to add an item
        layout = DashboardLayoutEngine.Resize(layout, v1, 6, null);
        Assert.Equal(v1, Assert.Single(Arrange(layout).FreeSpaces).AnchorId);

        // 4. Two lines high (6 rows): the free space follows
        layout = DashboardLayoutEngine.Resize(layout, v1, null, 6);
        Assert.Equal(650, Assert.Single(Arrange(layout).FreeSpaces).Bounds.Height);

        // 5. An item added in the free space is greedy both ways
        layout = DashboardLayoutEngine.AddNextTo(layout, v1, ItemSide.Right, I("v2"));
        Assert.Equal(new System.Windows.Rect(600, 0, 590, 650), Arrange(layout).Items["v2"]);

        // Then one more below it: they share the height next to the tall item
        layout = DashboardLayoutEngine.AddNextTo(layout, "v2", ItemSide.Below, I("v3"));
        Assert.Equal("Row(v1:6^6,Col(v2,v3))", Shape(layout));
        Assert.Equal(320, Arrange(layout).Items["v3"].Height);
        Assert.Empty(Arrange(layout).FreeSpaces);
    }

    // --- Copying and pasting items ---

    [Fact]
    public void Extracting_items_keeps_their_arrangement_and_sizes()
    {
        var layout = Grid(Row(I("a", 4), Col(I("b", rows: 2), I("c")), I("d")), I("e"));

        Assert.Equal("Row(a:4,Col(b^2,c))", Shape(DashboardLayoutEngine.Extract(layout, new HashSet<string> { "a", "b", "c" })));
        Assert.Equal("Row(a:4,b^2)", Shape(DashboardLayoutEngine.Extract(layout, new HashSet<string> { "a", "b" })));
        Assert.Equal("e", Shape(DashboardLayoutEngine.Extract(layout, new HashSet<string> { "e" })));
    }

    [Fact]
    public void Copied_lines_go_after_the_line_of_the_anchor_or_at_the_end()
    {
        var layout = Grid(Row(I("a"), I("b")), I("c"));
        var copied = Grid(Row(I("x", 4), I("y")));

        Assert.Equal("Row(a,b) | Row(x:4,y) | c", Shape(DashboardLayoutEngine.Insert(layout, copied, "b")));
        Assert.Equal("Row(a,b) | c | Row(x:4,y)", Shape(DashboardLayoutEngine.Insert(layout, copied, null)));
    }

    [Fact]
    public void Copied_bands_of_another_mode_come_as_new_bands_and_widths_follow_the_columns()
    {
        var layout = Grid(I("a"), I("b"));
        var copied = new DashboardLayoutDefinition { Columns = 24, Bands = [new BandLayout { Mode = BandMode.Scroll, Items = [I("x"), I("y")] }, new BandLayout { Items = [I("z", 12)] }] };

        var pasted = DashboardLayoutEngine.Insert(layout, copied, "a");

        Assert.Equal("a | z:6 | b", Shape(pasted));
        Assert.Equal(BandMode.Scroll, pasted.Bands[1].Mode);
        Assert.Equal("x | y", Shape(pasted, 1));
    }

    // --- Moving with the keyboard ---

    private static DashboardLayoutDefinition Step(DashboardLayoutDefinition layout, string id, ItemSide direction, bool toNewBand = false) =>
        DashboardLayoutEngine.Step(layout, id, direction, toNewBand);

    private static string Shapes(DashboardLayoutDefinition layout) =>
        string.Join(" / ", layout.Bands.Select((_, i) => Shape(layout, i)));

    [Fact]
    public void Two_items_on_two_lines_join_then_swap()
    {
        var layout = Grid(I("a"), I("b"));

        Assert.Equal("Row(a,b)", Shapes(Step(layout, "b", ItemSide.Above)));
        Assert.Equal("b | a", Shapes(Step(Step(layout, "b", ItemSide.Above), "b", ItemSide.Above)));
        Assert.Equal("Row(a,b)", Shapes(Step(layout, "a", ItemSide.Below)));
        Assert.Equal("b | a", Shapes(Step(Step(layout, "a", ItemSide.Below), "a", ItemSide.Below)));
    }

    [Fact]
    public void Down_leaves_a_line_joins_the_next_one_and_goes_on_into_the_band_below()
    {
        var layout = Bands(new BandLayout { Items = [Row(I("a"), I("b"), I("c")), I("d")] }, new BandLayout { Items = [I("e")] });
        string[] expected =
        [
            "Row(a,b) | c | d / e",
            "Row(a,b) | Row(c,d) / e",
            "Row(a,b) | d | c / e",
            "Row(a,b) | d / c | e",
            "Row(a,b) | d / Row(c,e)",
            "Row(a,b) | d / e | c",
            "Row(a,b) | d / e / c", // past the last band: a new one
        ];

        foreach (var step in expected)
        {
            layout = Step(layout, "c", ItemSide.Below);
            Assert.Equal(step, Shapes(layout));
        }
    }

    [Fact]
    public void Up_does_the_same_upwards()
    {
        var layout = Bands(new BandLayout { Items = [I("a")] }, new BandLayout { Items = [I("b"), Row(I("c"), I("d"))] });
        string[] expected =
        [
            "a / b | d | c",
            "a / Row(b,d) | c",
            "a / d | b | c",
            "a | d / b | c", // into the band above: a new line at its end
            "Row(a,d) / b | c",
            "d | a / b | c",
            "d / a / b | c", // out of the first band: a new band
        ];

        foreach (var step in expected)
        {
            layout = Step(layout, "d", ItemSide.Above);
            Assert.Equal(step, Shapes(layout));
        }
    }

    [Fact]
    public void Shift_puts_the_item_in_a_new_band()
    {
        var layout = Grid(Row(I("a"), I("b")), I("c"));

        Assert.Equal("a | c / b", Shapes(Step(layout, "b", ItemSide.Below, toNewBand: true)));
        Assert.Equal("b / a | c", Shapes(Step(layout, "b", ItemSide.Above, toNewBand: true)));
    }

    [Fact]
    public void Left_and_right_swap_an_item_with_its_neighbour()
    {
        var grid = Grid(Row(I("a"), I("b"), I("c")));
        var flow = Bands(new BandLayout { Mode = BandMode.Wrap, Items = [I("a"), I("b"), I("c")] });

        Assert.Equal("Row(a,c,b)", Shape(Step(grid, "b", ItemSide.Right)));
        Assert.Equal("Row(b,a,c)", Shape(Step(grid, "b", ItemSide.Left)));
        Assert.Equal("Row(a,b,c)", Shape(Step(grid, "c", ItemSide.Right))); // end of the line
        Assert.Equal("a | c | b", Shape(Step(flow, "b", ItemSide.Right)));
        Assert.Equal("b | a | c", Shape(Step(flow, "b", ItemSide.Left)));
    }

    // --- Extending over the free room ---

    [Fact]
    public void An_item_beside_fixed_widths_extends_over_the_free_room()
    {
        Assert.Equal("Row(a:3,b)", Shape(DashboardLayoutEngine.Fill(Grid(Row(I("a", 3), I("b", 4))), "b", width: true)));
        Assert.Equal("a", Shape(DashboardLayoutEngine.Fill(Grid(I("a", 6)), "a", width: true))); // alone on a narrowed line
        // A greedy neighbour already takes the room: nothing to fill
        Assert.Equal("Row(a:3,b)", Shape(DashboardLayoutEngine.Fill(Grid(Row(I("a", 3), I("b"))), "a", width: true)));
    }

    [Fact]
    public void An_item_of_a_stack_of_fixed_heights_extends_down_its_free_room()
    {
        var layout = Grid(Row(I("a", rows: 4), Col(I("b", rows: 1), I("c", rows: 1))));

        Assert.Equal("Row(a^4,Col(b^1,c))", Shape(DashboardLayoutEngine.Fill(layout, "c", width: false)));
        // Not in a stack: the height of a line has no free room
        Assert.Equal("Row(a^4,Col(b^1,c^1))", Shape(DashboardLayoutEngine.Fill(layout, "a", width: false)));
    }

    [Fact]
    public void An_item_of_a_flow_band_of_fixed_widths_extends_over_the_rest_of_the_line()
    {
        var layout = Bands(new BandLayout { Mode = BandMode.Wrap, Items = [I("a", width: 300), I("b", width: 300)] });

        Assert.Equal("a~300 | b", Shape(DashboardLayoutEngine.Fill(layout, "b", width: true)));
    }
}
