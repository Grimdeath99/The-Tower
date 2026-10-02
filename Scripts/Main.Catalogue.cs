using Godot;
using VerticalDistrict.Core;

namespace VerticalDistrict.Game;

public partial class Main
{
    private OptionButton _catalogueCategory = null!, _catalogueFilter = null!, _catalogueEntry = null!;
    private AcceptDialog _catalogueDialog = null!;
    private RichTextLabel _catalogueText = null!;
    private string _catalogueContext = "";

    private void RefreshCatalogueUnlocks()
    {
        var context = $"{World.Rank}:{Session.LocationId}:{Session.SiteId}";
        if (context == _catalogueContext) return;
        _catalogueContext = context; RefreshRoomList();
        if (_catalogueDialog?.Visible == true) RefreshCatalogueEntry();
    }

    private void BuildCatalogueFilters(VBoxContainer body)
    {
        _catalogueCategory = new OptionButton();
        _catalogueCategory.AddItem("All categories");
        foreach (var category in Catalog.Definitions.Select(d => d.Category).Distinct().Order()) _catalogueCategory.AddItem(category);
        _catalogueCategory.ItemSelected += _ => RefreshRoomList(); body.AddChild(_catalogueCategory);
        _catalogueFilter = new OptionButton();
        foreach (var filter in new[] { "All facilities", "Unlocked here", "Management models" }) _catalogueFilter.AddItem(filter);
        _catalogueFilter.ItemSelected += _ => RefreshRoomList(); body.AddChild(_catalogueFilter);
        body.AddChild(Button("Facility guide", () => OpenCatalogueEntry(ActiveDefinition), "Costs, capacity, access, unlocks and current operating coverage."));
    }

    private IEnumerable<FacilityDefinition> FilteredCatalogue() => Catalog.Definitions.Where(d =>
        (string.IsNullOrWhiteSpace(_search.Text) || (d.Id + " " + d.Name + " " + d.Category + " " + d.Operations.Model).Contains(_search.Text, StringComparison.OrdinalIgnoreCase))
        && (_catalogueCategory.Selected <= 0 || d.Category == _catalogueCategory.GetItemText(_catalogueCategory.Selected))
        && (_catalogueFilter.Selected != 1 || CatalogueGate(d).Length == 0)
        && (_catalogueFilter.Selected != 2 || HasManagementLifecycle(d)));

    private static bool HasManagementLifecycle(FacilityDefinition d) => d.Operations.Model is "Office" or "Home" or "Condo" or "Food" or "Shop" or "Hotel" or "Service";

    private string CatalogueGate(FacilityDefinition d)
    {
        if (World.Rank < d.MinimumRank) return $"Requires rank {d.MinimumRank}; current rank {World.Rank}.";
        var gate = d.Id switch {
            "subway" => _locations.ValidateSubway(Session.LocationId, Session.SiteId, World.Rank),
            "dock" => _locations.ValidateDock(Session.LocationId, Session.SiteId, World.Rank),
            _ => new CommandResult(true, "")
        };
        return gate.Success ? "" : gate.Message;
    }

    private static string ModelCoverage(string model) => model switch {
        "Office" or "Home" => "Durable leases, agreed rent and returning members. Approved variants are not supplied.",
        "Condo" => "Persistent ownership, physical purchase and returning residents. Reacquisition refunds the recorded purchase price.",
        "Food" => "Selectable original café offerings, frozen quotes, physical queues, timed service and payment on completion. Lunch demand changes from 11:00 to 14:00; approved facility variants are not supplied.",
        "Hotel" => "Exclusive booking, actual check-in, quoted payment, checkout and routed cleaning. Suites and luxury services remain pending.",
        "Service" => "Workers physically travel from this depot to clean and repair rooms, then return. Staffing bounds concurrent jobs.",
        "Utility" => "Abstract municipal capacity for 16 rooms; each open, staffed and accessible plant adds capacity. No pipe or cable simulation.",
        "Security" => "Abstract coverage reduces the security satisfaction penalty above 16 peak occupants. Incident response is not implemented.",
        "Public" => "Structural circulation. Ground lobbies provide the entrance used by physical routes; atria are public room shells.",
        "Shop" => "Selectable original retail offerings. Reachable customers share finite demand, queue and pay once on completed service. Accepted quotes survive offering and price changes. No inventory simulation.",
        "Cinema" => "Initial prototype: periodic audience and three durations. Persistent screenings, tickets and cancellation remain unfinished.",
        "Event" => "Initial prototype: paid preparation and a timed audience. Persistent venue bookings and the full event lifecycle remain unfinished.",
        "Advertising" => "Initial prototype: daily exposure income. Durable advertising contracts and purchased campaigns remain unfinished.",
        "Parking" => "Initial prototype: routed drivers and departure fees. Vehicle capacity and advanced access remain unfinished.",
        "Terminal" => "Infrastructure incomplete: this prototype creates routed visitor batches. Platforms, scheduled trains or boats, and terminal capacity are not implemented.",
        _ => "Operating coverage has not been verified."
    };

    private string CatalogueDescription(FacilityDefinition d)
    {
        var rule = _rules.For(d.Id)!;
        var gate = CatalogueGate(d);
        var placement = d.Id switch {
            "subway" => "Basements only. Eligible subway network AND a connected fictional site; location rank requirement also applies.",
            "dock" => "Ground waterfront in Hawaii only; a waterfront site and location rank requirement apply.",
            "parking" => "Basements only.",
            _ => "Supported, clear floor footprint with a route from a ground lobby. Entrance is the leftmost bay on the room's bottom floor."
        };
        return $"{d.Name} · {d.Id}\n{d.Category} · {rule.Model}\n\n{ModelCoverage(rule.Model)}\n\n"
            + $"BUILD\n{d.Width} × {d.Height} bays/floors · {FinanceMoney(d.CostMinor)}\nRank {d.MinimumRank} or later. {(gate.Length == 0 ? "Unlocked at this site; footprint and funds are checked when placing." : gate)}\n{placement}\n\n"
            + $"OPERATE\nCapacity {rule.Capacity} · minimum staff {rule.Staff}\nDaily upkeep {FinanceMoney(rule.UpkeepMinor)} + {FinanceMoney(rule.StaffSalaryMinor)} per hired staff\nDefault asking price {FinanceMoney(rule.PriceMinor)} · hours {rule.OpenHour:00}:00–{rule.CloseHour:00}:00\n"
            + $"Requires public access, utility capacity and maintained condition/cleanliness. Service workers also need a physical route.\n{d.Operations.AccessRule}\n\n"
            + $"COVERAGE\nOriginal provisional content. Noise {d.Operations.Noise} is metadata only; neighbor effects are not implemented.\n{d.Provenance.VisualStatus}\nShared original interface sounds only; facility audio pending.";
    }

    private void BuildCatalogueDialog()
    {
        _catalogueDialog = Dialog("Facility guide", new Vector2I(760, 760), out var body);
        _catalogueEntry = new OptionButton();
        foreach (var d in Catalog.Definitions) _catalogueEntry.AddItem(d.Name);
        _catalogueEntry.ItemSelected += _ => RefreshCatalogueEntry(); body.AddChild(_catalogueEntry);
        _catalogueText = new RichTextLabel { FitContent = true, BbcodeEnabled = false, SelectionEnabled = true };
        body.AddChild(_catalogueText);
        body.AddChild(Button("Select for construction", () => {
            var d = Catalog.Definitions[_catalogueEntry.Selected]; var gate = CatalogueGate(d);
            if (gate.Length > 0) { SetStatus(gate); return; }
            _catalogueDialog.Hide(); SetTool("room", d.Id);
        }));
    }

    private void RefreshCatalogueEntry() => _catalogueText.Text = CatalogueDescription(Catalog.Definitions[_catalogueEntry.Selected]);

    private void OpenCatalogueEntry(string id)
    {
        _catalogueEntry.Select(Array.FindIndex(Catalog.Definitions.ToArray(), d => d.Id == id));
        RefreshCatalogueEntry(); _catalogueDialog.PopupCentered();
    }
}
