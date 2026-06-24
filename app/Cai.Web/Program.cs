using Cai.Scoring;
using Cai.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

// cai.canine.dev OWNS the rubric catalogs (the versioned, archived standard). Resolve their root from config, else the
// repo's /rubrics dir relative to the app — so it runs from a clone with no extra setup.
var rubricsRoot = builder.Configuration["Rubrics:Root"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "rubrics"));
builder.Services.AddSingleton(new RubricCatalogStore(rubricsRoot));

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

// ── The standard API (what the Watchdog surveyor and anyone else calls) ───────────────────────────────────────────
var api = app.MapGroup("/api");

// The published rubric versions, newest first.
api.MapGet("/rubrics", (RubricCatalogStore store) =>
    Results.Ok(new { latest = store.Latest(), versions = store.Versions() }));

// A version's full catalog — the 124 dimensions × 10 lenses it defines. "latest" resolves to the newest.
api.MapGet("/rubrics/{version}/catalog", (string version, RubricCatalogStore store) =>
{
    var resolved = version == "latest" ? store.Latest() : version;
    if (resolved is null)
    {
        return Results.NotFound(new { error = "no rubric versions published" });
    }

    var catalog = store.Get(resolved);
    return catalog is null
        ? Results.NotFound(new { error = $"unknown rubric version '{version}'", published = store.Versions() })
        : Results.Text(catalog.ToJson(), "application/json");
});

// Score an evidence bundle — the open, reproducible fold. POST the bundle JSON; get the CAI + per-lens contributions.
api.MapPost("/score", async (HttpRequest req) =>
{
    try
    {
        using var reader = new StreamReader(req.Body);
        var bundle = EvidenceBundle.Parse(await reader.ReadToEndAsync());
        var s = CaiScorer.Score(bundle);
        return Results.Ok(new
        {
            cai = Math.Round(s.Headline, 2),
            band = s.Band.Label(),
            rubricVersion = s.RubricVersion,
            lenses = s.Contributions.Select(c => new { c.Lens, c.Score, c.Weight, c.Contribution }),
        });
    }
    catch (Exception e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

// Verify a published headline reproduces from its evidence.
api.MapPost("/verify", async (HttpRequest req) =>
{
    try
    {
        using var reader = new StreamReader(req.Body);
        var bundle = EvidenceBundle.Parse(await reader.ReadToEndAsync());
        var v = CaiScorer.Verify(bundle);
        return Results.Ok(new { reproduced = v.Reproduced, computed = Math.Round(v.Computed, 2), claimed = v.Claimed, delta = Math.Round(v.Delta, 2), tolerance = v.Tolerance });
    }
    catch (Exception e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

app.MapRazorComponents<App>();

app.Run();
