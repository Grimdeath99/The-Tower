using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;

var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "locations.json"));
var catalog = LocationCatalog.Load(json);
var cases = new List<(string Name, Action Test)>();
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action<JsonObject> change)
{
    var node = JsonNode.Parse(json)!.AsObject();
    change(node);
    try { LocationCatalog.Load(node.ToJsonString()); }
    catch (ContentValidationException) { return; }
    throw new Exception("Malformed catalog was accepted.");
}
JsonObject First(JsonObject node) => node["locations"]![0]!.AsObject();

cases.Add(("Thirteen stable IDs, explicit scopes and dated provenance", () =>
{
    var expected = new[] { "tokyo", "osaka", "seoul", "dubai", "abu-dhabi", "new-york", "shanghai", "hong-kong",
        "beijing", "las-vegas", "san-francisco", "chicago", "hawaii" };
    Assert(catalog.Locations.Select(l => l.Id).SequenceEqual(expected), "Required locations changed.");
    Assert(catalog.SnapshotDate == "2026-09-30", "Snapshot must remain explicitly pinned.");
    foreach (var location in catalog.Locations)
    {
        Assert(ReferenceEquals(location, catalog.Get(location.Id)), "Lookup does not return immutable definition.");
        Assert(location.Sources.Count > 0 && location.Sources.All(s => s.CheckedOn == catalog.SnapshotDate), "Missing dated provenance.");
        Assert(location.Demand == new DemandModifiers(1, 1, 1, 1, "ProvisionalNeutral"), "Demand must not pretend to be an economic fact.");
    }
    Assert(catalog.Get("hawaii").GeographicScope.Contains("Honolulu"), "Hawaii's site must be explicit.");
}));

foreach (var location in catalog.Locations)
{
    cases.Add(($"{location.Name}: rank, site, subway and dock gates at all seven ranks", () =>
    {
        foreach (var site in location.Sites)
        foreach (var rank in Enumerable.Range(1, 7))
        foreach (var relax in new[] { false, true })
        {
            var subway = catalog.ValidateSubway(location.Id, site.Id, rank, relax);
            var expectedSubway = location.HasOperatingSubway && site.SubwayConnection && (relax || rank >= catalog.SubwayMinimumRank);
            Assert(subway.Success == expectedSubway, $"Wrong subway gate: {site.Id}/{rank}/{relax}.");
            var dock = catalog.ValidateDock(location.Id, site.Id, rank, relax);
            var expectedDock = location.Id == "hawaii" && site.Waterfront && (relax || rank >= catalog.DockMinimumRank);
            Assert(dock.Success == expectedDock, $"Wrong dock gate: {site.Id}/{rank}/{relax}.");
            Assert(subway.CostMinor == 0 && dock.CostMinor == 0 && subway.EntityId is null && dock.EntityId is null,
                "Validation must not represent construction or charge money.");
        }
    }));
}

cases.Add(("Mixed networks qualify; elevated, road, and unverified systems do not", () =>
{
    foreach (var id in new[] { "chicago", "san-francisco", "dubai", "hong-kong" })
        Assert(catalog.ValidateSubway(id, "transit", 7).Success, $"Actual subway infrastructure excluded: {id}.");
    Assert(catalog.Get("hawaii").SubwayNetwork == SubwayEligibility.Ineligible, "Skyline is not a subway.");
    Assert(catalog.Get("las-vegas").SubwayNetwork == SubwayEligibility.Ineligible, "Monorail and road Loop are not subway rail.");
    Assert(catalog.Get("abu-dhabi").SubwayNetwork == SubwayEligibility.Unknown, "Unverified operation must remain unknown.");
    Assert(catalog.ValidateSubway("abu-dhabi", "transit", 7).Message.Contains("unverified"), "Unknown must explain uncertainty.");
}));
cases.Add(("Fictional site connectivity cannot bypass network truth, including sandbox", () =>
{
    var node = JsonNode.Parse(json)!.AsObject();
    foreach (var item in node["locations"]!.AsArray())
        item!["sites"]![0]!["subwayConnection"] = true;
    var independent = LocationCatalog.Load(node.ToJsonString());
    foreach (var id in new[] { "hawaii", "las-vegas", "abu-dhabi" })
    foreach (var relax in new[] { false, true })
        Assert(!independent.ValidateSubway(id, "central", 7, relax).Success, $"Network restriction bypassed: {id}.");
}));
cases.Add(("Every non-Hawaii waterfront is rejected even in sandbox", () =>
{
    var node = JsonNode.Parse(json)!.AsObject();
    foreach (var item in node["locations"]!.AsArray()) item!["sites"]![0]!["waterfront"] = true;
    var waterfronts = LocationCatalog.Load(node.ToJsonString());
    foreach (var location in waterfronts.Locations.Where(l => l.Id != "hawaii"))
        Assert(!waterfronts.ValidateDock(location.Id, "central", 7, true).Success, $"Waterfront wrongly grants dock: {location.Id}.");
}));
cases.Add(("Rank relaxation affects only rank and retains explicit waterfront requirements", () =>
{
    var node = JsonNode.Parse(json)!.AsObject();
    node["subwayMinimumRank"] = 5;
    var ranks = LocationCatalog.Load(node.ToJsonString());
    Assert(!ranks.ValidateSubway("tokyo", "transit", 4).Success && ranks.ValidateSubway("tokyo", "transit", 5).Success, "Subway rank boundary.");
    Assert(ranks.ValidateSubway("tokyo", "transit", 1, true).Success, "Explicit sandbox rank relaxation.");
    Assert(!ranks.ValidateSubway("tokyo", "central", 1, true).Success, "Sandbox bypassed site connection.");
    Assert(!ranks.ValidateDock("hawaii", "waterfront", 3).Success && ranks.ValidateDock("hawaii", "waterfront", 4).Success, "Dock rank boundary.");
    Assert(ranks.ValidateDock("hawaii", "waterfront", 1, true).Success, "Explicit dock rank relaxation.");
    Assert(!ranks.ValidateDock("hawaii", "central", 7, true).Success, "Sandbox bypassed waterfront.");
}));
cases.Add(("Unknown IDs and invalid ranks fail without throwing or bypasses", () =>
{
    foreach (var rank in new[] { int.MinValue, -1, 0, 8, int.MaxValue })
    {
        Assert(!catalog.ValidateSubway("tokyo", "transit", rank, true).Success, "Invalid subway rank accepted.");
        Assert(!catalog.ValidateDock("hawaii", "waterfront", rank, true).Success, "Invalid dock rank accepted.");
    }
    Assert(!catalog.ValidateSubway("TOKYO", "transit", 7).Success, "ID case must be stable.");
    Assert(!catalog.ValidateSubway("unknown", "transit", 7).Success, "Unknown location accepted.");
    Assert(!catalog.ValidateDock("hawaii", "unknown", 7).Success, "Unknown site accepted.");
    Assert(!catalog.ValidateSubway(null!, null!, 7).Success, "Null lookup accepted.");
}));
cases.Add(("Validation preserves immutable catalog state", () =>
{
    var before = JsonSerializer.Serialize(catalog.Locations);
    for (var i = 0; i < 100; i++)
    {
        catalog.ValidateSubway("tokyo", "transit", 7);
        catalog.ValidateDock("hawaii", "waterfront", 7);
    }
    Assert(JsonSerializer.Serialize(catalog.Locations) == before, "Validation mutated definitions.");
    Assert(((IList<LocationDefinition>)catalog.Locations).IsReadOnly, "Locations are mutable.");
    Assert(((IList<SiteProfile>)catalog.Get("tokyo").Sites).IsReadOnly, "Sites are mutable.");
    Assert(((IList<NetworkSource>)catalog.Get("tokyo").Sources).IsReadOnly, "Provenance is mutable.");
}));
cases.Add(("Malformed schema, counts, missing fields and unknown properties rejected", () =>
{
    Reject(n => n["schemaVersion"] = 99);
    Reject(n => n["snapshotDate"] = "2026-02-30");
    Reject(n => n["locations"]!.AsArray().RemoveAt(0));
    Reject(n => n["locations"]![1]!["id"] = "tokyo");
    Reject(n => First(n)["id"] = "unapproved-location");
    Reject(n => n["locations"]![0] = null);
    Reject(n => First(n)["name"] = " ");
    Reject(n => First(n).Remove("subwayNetwork"));
    Reject(n => First(n)["subwayNetwork"] = "Planned");
    Reject(n => First(n)["subwayNetwork"] = 1);
    Reject(n => n["surprise"] = true);
    Reject(n => n.Remove("dockMinimumRank"));
    Reject(n => n["dockMinimumRank"] = 0);
    Reject(n => n["subwayMinimumRank"] = 8);
}));
cases.Add(("Invalid sites, source dates and invented demand data rejected", () =>
{
    Reject(n => First(n)["sites"] = new JsonArray());
    Reject(n => First(n)["sites"]![1]!["id"] = "central");
    Reject(n => First(n)["sites"]![0]!.AsObject().Remove("waterfront"));
    Reject(n => First(n)["sites"]![0]!.AsObject().Remove("subwayConnection"));
    Reject(n => First(n)["sources"] = new JsonArray());
    Reject(n => First(n)["sources"]![0]!["checkedOn"] = "2026-09-29");
    Reject(n => First(n)["sources"]![0]!["url"] = "file:///private");
    Reject(n => First(n)["demand"]!["hotel"] = 1.5);
    Reject(n => First(n)["demand"]!["status"] = "VerifiedEconomicFact");
}));

var failures = 0;
foreach (var (name, test) in cases)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
Console.WriteLine($"{cases.Count - failures}/{cases.Count} geography cases passed; {failures} failed.");
return failures == 0 ? 0 : 1;
