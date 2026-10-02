using Godot;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Game;

public partial class Main
{
    private Button? _condoBuyback;
    private CheckButton? _facilityOpen;

    private void AppendOwnershipInspection(long roomId, List<string> lines)
    {
        var ownership = Session.OwnershipFor(roomId);
        lines.Add("OWNERSHIP · " + Session.CondoStateFor(roomId));
        if (ownership != null)
        {
            lines.Add($"Agreement #{ownership.Id} · owner #{ownership.OwnerId} · {ownership.Status}\n"
                + $"Assigned residents {ownership.ResidentIds.Length} · physically in room {Session.Occupancy(roomId)}\n"
                + $"Resident IDs: {string.Join(", ", ownership.ResidentIds)}\n"
                + $"Agreed purchase price {FinanceMoney(ownership.AgreedPriceMinor)}\n"
                + (ownership.SaleLedgerSequence is { } sale ? $"Purchase transaction #{sale}"
                    : ownership.Status == CondoOwnershipStatus.PendingSale ? "Payment due only on the first physical arrival."
                    : "Imported zero-price ownership; no historical sale receipt.")
                + (ownership.BuybackLedgerSequence is { } buyback ? $" · buyback transaction #{buyback}" : "")
                + (ownership.EndReason.Length > 0 ? "\n" + ownership.EndReason : ""));
        }
        else if (Session.Ownerships.LastOrDefault(o => o.RoomId == roomId) is { } previous)
            lines.Add($"Previous agreement #{previous.Id}: {previous.Status}. "
                + (previous.BuybackLedgerSequence is { } receipt ? $"Buyback transaction #{receipt}. " : "") + previous.EndReason);
        lines.Add("Residents leave at 08:00 and return at 18:00 using physical routes. An empty owned home stays owned.\n"
            + "Provisional policy: no rent or ongoing owner charges. Buyback refunds the original paid price, closes the home and waits for residents to exit before reopening or demolition.");
        if (_condoBuyback != null)
        {
            _condoBuyback.Text = ownership?.Status == CondoOwnershipStatus.Owned
                ? "Repurchase ownership · " + FinanceMoney(ownership.AgreedPriceMinor) : "No active ownership to repurchase";
            var unfunded = ownership is { AgreedPriceMinor: > 0 } && World.CashMinor < ownership.AgreedPriceMinor;
            _condoBuyback.Disabled = ownership?.Status != CondoOwnershipStatus.Owned || unfunded;
            _condoBuyback.TooltipText = ownership?.Status == CondoOwnershipStatus.Owned && unfunded
                ? "Insufficient cash to refund the recorded purchase price." : "Refund once, close the unit and request physical departure.";
        }
    }

    private async System.Threading.Tasks.Task VerifyOwnershipInspection()
    {
        Session = new GameSession(Catalog, _rules, _locations, sandbox: true);
        void Must(VerticalDistrict.Core.CommandResult result) { if (!result.Success) throw new Exception(result.Message); }
        Must(World.BuildFloor(1)); Must(Session.BuildRoom("lobby", 0, 0));
        Must(Session.BuildRoom("service-room", 6, 0));
        var result = Session.BuildRoom("condo", 0, 1); Must(result); var roomId = result.EntityId!.Value;
        Must(Session.Transport.InstallBank(new BankDefinition(1, 27, 0, 1, [0, 1], 12), _rules.ElevatorCostMinor));
        for (var tick = 0; tick < 45_000 && Session.OwnershipFor(roomId)?.Status != CondoOwnershipStatus.Owned; tick++) Session.Step();
        var agreement = Session.OwnershipFor(roomId) ?? throw new Exception("No accepted condo offer.");
        if (agreement.Status != CondoOwnershipStatus.Owned) throw new Exception("Condo purchase did not complete.");
        _runner.Reset(); _runner.SetSpeed(0); ClearEntitySelection(); SelectedId = roomId; Canvas.ResetView(); Refresh();
        var before = Session.Serialize();
        OpenSelectedFacility(); RefreshFacilityLiveState();
        if (!BlocksWorldInput || !_facilityLiveText.Text.Contains("OWNERSHIP") || !_facilityLiveText.Text.Contains("Purchase transaction #")
            || !_facilityLiveText.Text.Contains("Assigned residents 2") || _condoBuyback?.Disabled != false)
            throw new Exception("Ownership inspector is missing actual ownership, payment or resident state.");
        await CaptureManagementView("business-ownership");
        if (Session.Serialize() != before) throw new Exception("Ownership inspection mutated paused state.");
        _facility.Hide();
        await CaptureManagementView("business-cutaway");
        _search.Text = "condo";
        _search.EmitSignal(LineEdit.SignalName.TextChanged, _search.Text);
        if (_roomList.GetChildren().OfType<Button>().Count() != 1) throw new Exception("Catalogue search did not filter the stable condo ID.");
        _search.Text = "";
        _search.EmitSignal(LineEdit.SignalName.TextChanged, _search.Text);
        var homesIndex = Enumerable.Range(0, _catalogueCategory.ItemCount).Single(i => _catalogueCategory.GetItemText(i) == "Homes");
        _catalogueCategory.Select(homesIndex); RefreshRoomList();
        if (_roomList.GetChildren().OfType<Button>().Any(b => Catalog.Get(b.Name.ToString()).Category != "Homes"))
            throw new Exception("Catalogue category filter returned a different category.");
        _catalogueCategory.Select(0); _catalogueFilter.Select(1); RefreshRoomList();
        if (_roomList.GetChildren().OfType<Button>().Any(b => b.Name.ToString() is "dock" or "subway"))
            throw new Exception("Catalogue unlock filter ignored independent Tokyo central site restrictions.");
        _catalogueFilter.Select(0); RefreshRoomList(); OpenCatalogueEntry("condo");
        if (!BlocksWorldInput || !_catalogueText.Text.Contains("Reacquisition") || !_catalogueText.Text.Contains("Daily upkeep"))
            throw new Exception("Facility guide lacks operating policy, costs or input isolation.");
        await CaptureManagementView("business-catalogue"); _catalogueDialog.Hide();
        if (Session.Serialize() != before) throw new Exception("Catalogue inspection changed authoritative state.");
        OpenSelectedFacility();
        var cash = World.CashMinor;
        _condoBuyback!.EmitSignal(BaseButton.SignalName.Pressed);
        if (World.CashMinor != cash - agreement.AgreedPriceMinor || !_condoBuyback.Disabled
            || _facilityOpen?.ButtonPressed != false || Session.OwnershipFor(roomId)?.Status != CondoOwnershipStatus.Evacuating)
            throw new Exception("Ownership repurchase UI did not issue its validated command.");
        var afterBuyback = Session.Serialize();
        _condoBuyback.EmitSignal(BaseButton.SignalName.Pressed);
        if (Session.Serialize() != afterBuyback) throw new Exception("Repeated buyback changed authoritative state.");
        await CaptureManagementView("business-buyback"); _facility.Hide();
    }
}
