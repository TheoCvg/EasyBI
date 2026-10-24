# Value labels

A visual can show its own text for values of a dimension: "IDF" for "Île-de-France". Selecting a dimension's chip in the
designer shows **Value labels**: pick a value (type to search among many), type its text and add it; each label can be
edited or removed. The labels apply everywhere in the visual (axis, groups, legend, tooltips, tables, matrices, the
selection's description); filters and cross-filtering keep the values themselves. They are saved with the visual's field
(`FieldRef.Labels`, keyed by the value's invariant text, omitted when there are none); `field.Display(value)` and
`field.WithLabel(value, text)` (EasyBI.Core) read and set them. Dates have none: their periods follow their grain.
