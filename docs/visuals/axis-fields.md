# Several axis fields

An axis with several fields (Month, then Region) is drawn as a hierarchy: each bar labelled by the innermost field
("North"), each outer value once, centered under its group of bars ("2024-01"), with a line between the groups. When
the bars are too narrow for their labels, every n-th group label shows, and the innermost labels give way to the groups'
when fewer than two a group would fit. Categories not ordered by their outer fields (sorted on a measure) keep joined
labels ("2024-01 / North"). Tooltips keep the whole category.

`CategoryLevels.Of(request.Chart)` (EasyBI.Core) gives a connector the short labels and the groups of each outer field
(`CategoryGroup`: label, first category, count, center); LiveCharts2 draws the groups on extra X axes, OxyPlot on axes of
the next tiers.
