using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class VariableTests
{
    private sealed record Row(string Region, string Channel, decimal Amount);

    private static readonly Row[] Rows =
    [
        new("Nord", "Web", 100m),
        new("Nord", "Shop", 300m),
        new("Sud", "Web", 200m),
        new("Est", "Shop", 50m),
        new("Ouest", "Web", 120m),
    ];

    private static QueryableSemanticModel<Row> Model() =>
        SemanticModel.For(Rows.AsQueryable(), "m")
            .Dimension(r => r.Region, "Région")
            .Dimension(r => r.Channel, "Canal")
            .Measure(r => r.Amount, "CA");

    private static ReportVariable Number(string name, double? value, string? format = null) =>
        new() { Name = name, Value = value?.ToString(System.Globalization.CultureInfo.InvariantCulture), Format = format };

    private static ReportVariable TopRegions(int count = 2, string? countVariable = null, bool bottom = false) => new()
    {
        Name = "TopRegions",
        Caption = "Meilleures régions",
        Kind = VariableKind.Set,
        Model = "m",
        Field = "Region",
        Rule = new SetRule("Amount") { Count = count, CountVariable = countVariable, Bottom = bottom },
    };

    private static VariableContext Variables(params ReportVariable[] variables)
    {
        var context = new VariableContext();
        context.Load(variables);
        return context;
    }

    private static SetSource Source(ISemanticModel model, params FilterSpec[] filters) =>
        new(name => name == model.Metadata.Name ? model : null) { Filters = _ => filters };

    private static async Task<ResultSet> Query(ISemanticModel model, VariableContext variables, VisualDefinition visual,
        IEnumerable<CalculatedMeasure>? measures = null)
    {
        var resolved = await variables.ResolveAsync(visual, Source(model));
        var data = await model.GetDataAsync(resolved.ToDataRequest(model.Metadata, variables.Resolve(measures ?? [])));
        return data.Result;
    }

    private static VisualDefinition ByRegion(params MeasureRef[] values) =>
        new() { Model = "m", Type = VisualType.Table, Axis = [new("Region")], Values = values.Length > 0 ? values : [new("Amount")] };

    [Fact]
    public async Task A_formula_reads_the_value_of_a_number_parameter()
    {
        var measure = new CalculatedMeasure { Name = "calc_ttc", Model = "m", Formula = "[Amount] * [@Rate]" };

        var result = await Query(Model(), Variables(Number("Rate", 1.2)), ByRegion(new MeasureRef("calc_ttc")), [measure]);

        Assert.Equal(480d, (double)result.Rows.Single(r => (string)r[0]! == "Nord")[1]!, 6);
    }

    [Fact]
    public async Task A_formula_compares_rows_with_a_text_parameter()
    {
        var measure = new CalculatedMeasure { Name = "calc_channel", Model = "m", Formula = "SUM([Amount]; [Channel] = [@Channel])" };
        var channel = new ReportVariable { Name = "Channel", DataType = DataType.Text, Value = "Web" };

        var result = await Query(Model(), Variables(channel), ByRegion(new MeasureRef("calc_channel")), [measure]);

        Assert.Equal(100d, Convert.ToDouble(result.Rows.Single(r => (string)r[0]! == "Nord")[1]));
    }

    [Fact]
    public async Task A_parameter_without_a_value_gives_empty_values_in_a_formula()
    {
        var measure = new CalculatedMeasure { Name = "calc_ttc", Model = "m", Formula = "[Amount] * [@Rate]" };

        var result = await Query(Model(), Variables(Number("Rate", null)), ByRegion(new MeasureRef("calc_ttc")), [measure]);

        Assert.All(result.Rows, r => Assert.Null(r[1]));
    }

    [Fact]
    public void A_formula_may_only_use_existing_parameters()
    {
        var metadata = Model().Metadata;
        ReportVariable[] variables = [Number("Rate", 1), TopRegions()];

        Assert.Null(MeasureFormula.Validate("[Amount] * [@Rate]", metadata, variables: variables));
        Assert.Contains("Missing", MeasureFormula.Validate("[Amount] * [@Missing]", metadata, variables: variables));
        Assert.NotNull(MeasureFormula.Validate("[Amount] * [@TopRegions]", metadata, variables: variables));
        Assert.Equal(["Amount"], MeasureFormula.References("[Amount] * [@Rate]"));
        Assert.Equal(["Rate"], MeasureFormula.Variables("[Amount] * [@Rate]"));
    }

    [Fact]
    public void A_typed_formula_keeps_its_parameters_when_stored()
    {
        var stored = MeasureFormula.FromDisplay("[CA] * [@Rate]", Model().Metadata, out var error, variables: [Number("Rate", 1)]);

        Assert.Null(error);
        Assert.Equal("[Amount] * [@Rate]", stored);
    }

    [Fact]
    public async Task A_filter_compares_with_the_value_of_a_parameter()
    {
        var visual = ByRegion() with { Filters = [new FilterSpec("Amount", FilterOperator.GreaterThanOrEqual, [VariableReferences.Reference("Threshold")])] };

        var result = await Query(Model(), Variables(Number("Threshold", 200)), visual);

        Assert.Equal(["Nord", "Sud"], result.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task A_filter_keeps_the_values_of_a_parameter_or_of_a_set()
    {
        var region = new ReportVariable { Name = "Region", DataType = DataType.Text, Value = "Est" };
        var visual = ByRegion() with { Filters = [new FilterSpec("Region", FilterOperator.In, ["[@Region]", "[@TopRegions]"])] };

        var result = await Query(Model(), Variables(region, TopRegions()), visual);

        Assert.Equal(["Est", "Nord", "Sud"], result.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task A_filter_leaves_out_the_values_of_a_set()
    {
        var visual = ByRegion() with { Filters = [new FilterSpec("Region", FilterOperator.NotIn, ["[@TopRegions]"])] };

        var result = await Query(Model(), Variables(TopRegions()), visual);

        Assert.Equal(["Est", "Ouest"], result.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task A_parameter_gives_the_number_of_groups_a_visual_keeps()
    {
        var visual = ByRegion() with { Sort = new SortSpec("Amount", Descending: true), Top = 10, TopVariable = "N" };

        var result = await Query(Model(), Variables(Number("N", 3)), visual);

        Assert.Equal(["Nord", "Sud", "Ouest"], result.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task A_rule_chooses_the_values_with_the_highest_or_lowest_measure()
    {
        var model = Model();

        Assert.Equal(["Nord", "Sud"], await Variables(TopRegions()).MembersAsync("TopRegions", Source(model)));
        Assert.Equal(["Est", "Ouest"], (await Variables(TopRegions(bottom: true)).MembersAsync("TopRegions", Source(model))).Order());
        Assert.Equal(["Nord", "Sud", "Ouest"], await Variables(TopRegions(countVariable: "N"), Number("N", 3)).MembersAsync("TopRegions", Source(model)));
    }

    [Fact]
    public async Task A_rule_goes_through_the_report_filters_of_its_model()
    {
        var web = new FilterSpec("Channel", FilterOperator.In, ["Web"]);

        var members = await Variables(TopRegions()).MembersAsync("TopRegions", Source(Model(), web));

        Assert.Equal(["Sud", "Ouest"], members);
    }

    [Fact]
    public async Task The_values_of_a_set_are_computed_once_until_a_variable_changes()
    {
        var model = new CountingModel(Model());
        var variables = Variables(TopRegions(countVariable: "N"), Number("N", 2));

        await variables.MembersAsync("TopRegions", Source(model));
        await variables.MembersAsync("TopRegions", Source(model));
        Assert.Equal(1, model.Requests);

        variables.SetValue("N", "3");
        Assert.Equal(3, (await variables.MembersAsync("TopRegions", Source(model))).Count);
        Assert.Equal(2, model.Requests);

        variables.Refresh();
        await variables.MembersAsync("TopRegions", Source(model));
        Assert.Equal(3, model.Requests);
    }

    [Fact]
    public async Task A_field_grouped_by_a_set_shows_the_set_first_then_the_others()
    {
        var visual = new VisualDefinition { Model = "m", Type = VisualType.Table, Axis = [new("Region") { Set = "TopRegions" }], Values = [new("Amount")] };
        var variables = Variables(TopRegions());

        var resolved = await variables.ResolveAsync(visual, Source(Model()));
        var result = await Query(Model(), variables, visual);

        Assert.Equal(["Nord", "Sud"], resolved.Axis[0].SetMembers);
        Assert.Equal("Region@TopRegions", result.Columns[0].Key);
        Assert.Equal([true, false], result.Rows.Select(r => r[0]));
        Assert.Equal([600m, 170m], result.Rows.Select(r => r[1]));
        Assert.Equal(["Meilleures régions", "Others"], result.Rows.Select(r => resolved.Axis[0].Display(r[0])));
    }

    [Fact]
    public async Task A_field_grouped_by_a_set_travels_to_a_remote_source_with_its_values()
    {
        var visual = new VisualDefinition { Model = "m", Type = VisualType.Table, Axis = [new("Region") { Set = "TopRegions" }], Values = [new("Amount")] };
        var resolved = await Variables(TopRegions()).ResolveAsync(visual, Source(Model()));

        // What a client sends, and what a server answers
        var request = DataSerializer.DeserializeRequest(DataSerializer.Serialize(resolved.ToDataRequest(Model().Metadata)));
        var data = DataSerializer.DeserializeData(DataSerializer.Serialize(await Model().GetDataAsync(request)));

        Assert.Equal(["Nord", "Sud"], request.Visual.Axis[0].SetMembers);
        Assert.Equal([true, false], data.Result.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task A_visual_keeps_its_own_labels_for_a_set()
    {
        var visual = ByRegion() with { Axis = [new FieldRef("Region") { Set = "TopRegions" }.WithLabel(false, "Le reste")] };

        var resolved = await Variables(TopRegions()).ResolveAsync(visual, Source(Model()));

        Assert.Equal("Le reste", resolved.Axis[0].Display(false));
        Assert.Equal("Meilleures régions", resolved.Axis[0].Display(true));
    }

    [Fact]
    public async Task Clicking_the_set_or_the_others_filters_on_the_set()
    {
        var visual = ByRegion() with { Axis = [new FieldRef("Region") { Set = "TopRegions" }] };
        var variables = Variables(TopRegions());
        var resolved = await variables.ResolveAsync(visual, Source(Model()));

        var inSet = new SelectedElement(resolved.Axis, [true]).Filters.Single();
        var others = new SelectedElement(resolved.Axis, [false]).Filters.Single();
        var both = new CrossFilter("v", "m", [new SelectedElement(resolved.Axis, [true]), new SelectedElement(resolved.Axis, [false])]).Filters.Single();

        Assert.Equal(FilterOperator.In, inSet.Operator);
        Assert.Equal(["[@TopRegions]"], inSet.Values);
        Assert.Equal(FilterOperator.NotIn, others.Operator);
        Assert.Equal(["[@TopRegions]"], others.Values);
        var result = await Query(Model(), variables, ByRegion() with { Filters = [others] });
        Assert.Equal(["Est", "Ouest"], result.Rows.Select(r => r[0]));
        Assert.Equal(FilterOperator.AnyOf, both.Operator);
    }

    [Fact]
    public async Task A_matrix_puts_a_set_on_its_rows()
    {
        var visual = new VisualDefinition
        {
            Model = "m",
            Type = VisualType.Matrix,
            Axis = [new("Region") { Set = "TopRegions" }],
            Legend = new("Channel"),
            Values = [new("Amount")],
        };

        var result = await Query(Model(), Variables(TopRegions()), visual);

        Assert.Equal(4, result.Rows.Count);
    }

    [Fact]
    public async Task Titles_and_texts_show_the_values_of_variables()
    {
        var visual = ByRegion() with { Title = "Top {N} – {TopRegions} – {Missing} – {{N}}" };
        var text = new VisualDefinition { Type = VisualType.Text, Text = new TextContent { Text = "Taux : {Rate}" } };
        var variables = Variables(Number("N", 2), Number("Rate", 0.125, "P1"), TopRegions());

        var resolved = await variables.ResolveAsync(visual, Source(Model()));
        var resolvedText = await variables.ResolveAsync(text, Source(Model()));

        Assert.Equal("Top 2 – Nord, Sud – {Missing} – {N}", resolved.Title);
        Assert.Equal("Taux : 12.5 %", resolvedText.Text!.Text);
    }

    [Fact]
    public async Task A_visual_referencing_a_missing_variable_cannot_be_queried()
    {
        var visual = ByRegion() with { Filters = [new FilterSpec("Region", FilterOperator.In, ["[@Missing]"])] };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Variables().ResolveAsync(visual, Source(Model())));

        Assert.Contains("Missing", error.Message);
    }

    [Fact]
    public void Reports_without_variables_keep_their_json_and_variables_round_trip()
    {
        var plain = ReportSerializer.Serialize(new ReportDefinition());
        Assert.DoesNotContain("variables", plain);

        var report = new ReportDefinition { Variables = [Number("N", 10), TopRegions(countVariable: "N")] };
        var read = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));

        Assert.Equal(report.Variables[0], read.Variables[0]);
        Assert.Equal(report.Variables[1].Rule, read.Variables[1].Rule);
        Assert.Equal(VariableKind.Set, read.Variables[1].Kind);
    }

    [Fact]
    public void The_report_validator_reports_missing_variables()
    {
        var report = new ReportDefinition
        {
            Visuals = [ByRegion() with { Title = "Top {N}", TopVariable = "N", Filters = [new FilterSpec("Region", FilterOperator.In, ["[@Missing]"])] }],
            Variables = [TopRegions(countVariable: "Count"), new ReportVariable { Name = "bad name" }],
        };

        var problems = ReportValidator.Validate(report, [Model().Metadata]);

        Assert.Contains(problems, p => p.Contains("Missing"));
        Assert.Contains(problems, p => p.Contains("\"N\""));
        Assert.Contains(problems, p => p.Contains("Count"));
        Assert.Contains(problems, p => p.Contains("bad name"));
    }

    [Fact]
    public void Copied_visuals_bring_the_variables_they_use()
    {
        var variables = Variables(Number("N", 3), TopRegions(countVariable: "N"), Number("Unused", 1));
        var visual = ByRegion() with { Axis = [new FieldRef("Region") { Set = "TopRegions" }] };
        var json = VisualTransfer.Copy([visual], new MeasureContext(), variables: variables);

        var target = Variables(Number("N", 7));
        VisualTransfer.Paste(json, new MeasureContext(), _ => true, variables: target);

        Assert.Equal(["N", "TopRegions"], target.Variables.Select(v => v.Name));
        // The target keeps its own parameter of the same name
        Assert.Equal("7", target.Find("N")!.Value);
    }

    private sealed class CountingModel(ISemanticModel inner) : ISemanticModel
    {
        public int Requests { get; private set; }

        public ModelMetadata Metadata => inner.Metadata;

        public Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
        {
            Requests++;
            return inner.GetDataAsync(request, cancellationToken);
        }

        public Task<DistinctValues> GetDistinctValuesAsync(DistinctValuesRequest request, CancellationToken cancellationToken = default) =>
            inner.GetDistinctValuesAsync(request, cancellationToken);
    }
}
