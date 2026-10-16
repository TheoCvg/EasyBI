# EasyBI

Bibliothèque de **self-service BI pour WPF**. Le développeur déclare *ce qui est analysable*,
l'utilisateur final construit ses visuels par glisser-déposer, et le rendu est délégué à des
**connecteurs** interchangeables (LiveCharts2, OxyPlot, ...).

```
 Source (IQueryable) ─▶ Modèle sémantique ─▶ AnalyticQuery ─▶ ResultSet ─▶ ChartData ─▶ IVisualRenderer
                         (dev)                (utilisateur)    (moteur)     (pivot)      (connecteur)
```

## Projets

| Projet | Rôle | Dépendances |
|---|---|---|
| `EasyBI.Abstractions` | **La norme** : modèle, requête, `ResultSet`, `VisualDefinition`, `ReportDefinition` | aucune |
| `EasyBI.Core` | Builder de modèle, moteur de requête sur `IQueryable` (arbres d'expressions), pivot, JSON | Abstractions |
| `EasyBI.Wpf` | `IVisualRenderer`, `EasyBIContext`, `VisualView`, `ReportDesigner`, renderers KPI et Table intégrés | Core |
| `EasyBI.Renderers.LiveCharts2` | Connecteur LiveCharts2 (barres, courbes, secteurs) | Wpf + LiveCharts2 |
| `EasyBI.Renderers.OxyPlot` | Connecteur OxyPlot (barres, courbes, secteurs) | Wpf + OxyPlot |
| `samples/EasyBI.Sample` | Démo : tableau de bord + designer + bascule de renderer + sauvegarde JSON | |
| `tests/EasyBI.Core.Tests` | Tests du moteur et de la sérialisation | |

## Côté développeur WPF

```csharp
var model = SemanticModel.For(dbContext.Sales, "sales", "Ventes")
    .Dimension(s => s.Date, "Date")
    .Dimension(s => s.Region, "Région")
    .Dimension(s => s.Product.Category, "Catégorie")
    .Measure(s => s.Amount, "Chiffre d'affaires", format: "#,##0 €")
    .Count("Nombre de ventes");

Context = EasyBIContext.Create()
    .AddModel(model)
    .UseRenderer(new LiveCharts2Renderer())
    .UseRenderer(new OxyPlotRenderer());
```

```xml
<designer:ReportDesigner Context="{Binding Context}" />                       <!-- conception -->
<bi:VisualView Context="{Binding Context}" Definition="{Binding Visual}" />   <!-- affichage -->
```

## Écrire un connecteur

Un connecteur implémente une seule interface. Il reçoit des données **déjà requêtées et pivotées**
(`request.Chart` : catégories + séries) et le `ResultSet` brut si besoin :

```csharp
public sealed class MyRenderer : IVisualRenderer
{
    public string Name => "MaLib";
    public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType> { MarkType.Bar });
    public FrameworkElement Render(RenderRequest request) { /* request.Chart.Categories, request.Chart.Series */ }
}
```

`EasyBIContext.ResolveRenderer` négocie les capacités : renderer actif s'il gère le type de visuel,
sinon un autre renderer branché, sinon un renderer intégré. Le designer grise les types que personne ne sait dessiner.

## Format de rapport (`easybi/v1`)

```json
{
  "schema": "easybi/v1",
  "title": "Tableau de bord",
  "visuals": [
    {
      "title": "CA par région et canal",
      "model": "sales",
      "mark": "Bar",
      "axis": [ { "field": "Region", "grain": "None" } ],
      "legend": { "field": "Channel", "grain": "None" },
      "values": [ { "measure": "Amount" } ],
      "filters": [ { "field": "Product.Category", "operator": "In", "values": [ "Services" ] } ]
    }
  ]
}
```

Le même fichier s'affiche à l'identique avec n'importe quel connecteur.

## Lancer

```
dotnet test
dotnet run --project samples/EasyBI.Sample
```

## Limites connues du MVP / pistes

- Interactivité : pas encore de filtrage croisé (clic sur une barre → filtre les autres visuels).
- Filtres : uniquement « valeur parmi » sur les dimensions non-date ; pas de plage de dates.
- `Top` s'applique aux lignes agrégées, pas aux catégories quand une légende est présente.
- Le moteur exécute la requête via `Task.Run` + `ToList()` ; un provider EF Core dédié utiliserait `ToListAsync`.
  La traduction SQL d'EF Core (GroupBy sur clé composite + projection en `object[]`) reste à valider.
- Au plus 4 dimensions de regroupement (axe + légende).
- Pas de thème sombre, pas de mise en page libre du tableau de bord.
