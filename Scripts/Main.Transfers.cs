using Godot;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Game;

public partial class Main
{
    private bool _resetTransfer;

    private void AddTransferSetup(VBoxContainer body)
    {
        body.AddChild(new HSeparator());
        body.AddChild(Text("TWO-BANK TRANSFER SCENARIO", 16, Accent));
        body.AddChild(Wrap("24 workers commute to floor 19 through a lower bank with two cars in separate shafts, a walk across floor 9, and an upper bank. Ground service staff use the same permitted transfer route. Normal budget and real construction costs; no stairs bypass the transfer.", 14, Muted));
        body.AddChild(Button("Start transfer scenario", () =>
        {
            var location = _locations.Locations[_citySelect.Selected];
            _newLocationId = location.Id; _newSiteId = location.Sites[_siteSelect.Selected].Id; _newSandbox = false;
            _resetCommute = false; _resetTransfer = true; _newGame.Hide(); _reset.PopupCentered();
        }));
    }

    private void StartTransferScenario()
    {
        Session = TransferScenario.Create(Catalog, _rules, _locations, _newLocationId, _newSiteId);
        ResetSessionView();
        SetStatus("Transfer scenario: 24 workers, two lower-bank cars, floor 9 transfer and upper offices on floor 19. Select a person to highlight their remaining route.");
    }

    private async Task VerifyTransferInspection()
    {
        Session = TransferScenario.Create(Catalog, _rules, _locations);
        ResetSessionView(); _runner.SetSpeed(0);
        for (var second = 0; second < 1800 && !Session.Transport.Cars.Any(c => c.BankId == 1 && c.CarId == 2 && c.PassengerCount > 0); second++) Session.Step();
        var secondCar = Session.Transport.Cars.Single(c => c.BankId == 1 && c.CarId == 2);
        if (secondCar.PassengerCount == 0 || Session.People.Count != 24 || Session.Transport.Stairs.Count != 0)
            throw new Exception("Transfer scenario did not use both coordinated cars for real workers.");
        var before = Session.Serialize();
        SelectBank(1, 2); Canvas.FocusBank(1, 2); OpenSelectedFacility();
        if (!BlocksWorldInput || !_inspector.Text.Contains("CAR 2") || !_inspector.Text.Contains("Shaft 2"))
            throw new Exception("Additional-car inspection did not identify its physical shaft.");
        await CaptureManagementView("transport-bank");
        if (Session.Serialize() != before) throw new Exception("Bank inspection changed paused authoritative state.");
        _transportPanel.VerifyCarControls(1, 2);
        _transportPanel.Hide();
        var saved = Session.Serialize(); Session = GameSession.Deserialize(Catalog, _rules, _locations, saved);
        if (Session.Serialize() != saved) throw new Exception("Coordinated-car state changed when loading through the game adapter.");
        for (var second = 0; second < 1800 && !Session.Transport.Journeys.Any(j => j.State == JourneyState.Waiting && j.BankId == 2); second++) Session.Step();
        var transfer = Session.Transport.Journeys.FirstOrDefault(j => j.State == JourneyState.Waiting && j.BankId == 2)
            ?? throw new Exception("No worker reached the upper bank queue through the transfer.");
        if (transfer.Floor != TransferScenario.TransferFloor || transfer.X != 25)
            throw new Exception("Transfer queue is not at its actual upper-bank boarding point.");
        SelectPerson(transfer.PersonId); Canvas.FocusPerson(transfer.PersonId); Refresh();
        before = Session.Serialize(); await CaptureManagementView("transport-transfer");
        OpenPeople();
        if (!_personDetails.Text.Contains("Remaining route") || !_personDetails.Text.Contains("Bank 2"))
            throw new Exception("Person inspector omitted the remaining transfer route.");
        await CaptureManagementView("transport-route"); _peopleDialog.Hide();
        if (Session.Serialize() != before) throw new Exception("Route inspection changed paused state.");
        for (var second = 0; second < 3600 && Session.People.Count(p => p.Role == "Worker" && p.Activity == PersonActivity.Visiting) != 24; second++) Session.Step();
        if (Session.People.Count(p => p.Role == "Worker" && p.Activity == PersonActivity.Visiting) != 24)
            throw new Exception("Adequately served transfer commuters failed to reach their offices.");
        var room = Session.World.Rooms.First(r => r.DefinitionId == "office");
        // Explicit acceptance fixture: create a maintenance need without changing production tuning.
        var worn = Session.CaptureSnapshot();
        worn = worn with { Operations = worn.Operations.Select(op => op.RoomId == room.Id
            ? op with { Condition = 50, Cleanliness = 50 } : op).ToArray() };
        Session = GameSession.Deserialize(Catalog, _rules, _locations, System.Text.Json.JsonSerializer.Serialize(worn, SimulationRules.JsonOptions));
        var upper = Session.Transport.Banks.Single(b => b.Definition.Id == 2).Definition;
        var restriction = Session.Transport.ConfigureBank(upper with { ServiceOnly = true });
        if (!restriction.Success || Session.IsAccessible(room.Id) || !Session.IsAccessible(room.Id, true))
            throw new Exception("Transfer service policy did not distinguish public and staff access.");
        var repair = Session.RepairRoom(room.Id);
        if (!repair.Success) throw new Exception("Transfer service task could not be dispatched: " + repair.Message);
        var workerId = Session.ServiceTaskForRoom(room.Id)!.WorkerPersonId!.Value;
        for (var second = 0; second < 1200 && Session.Transport.JourneyFor(workerId) is not { BankId: 2, State: JourneyState.Riding }; second++) Session.Step();
        if (Session.Transport.JourneyFor(workerId) is not { BankId: 2, State: JourneyState.Riding })
            throw new Exception("Service worker never rode the restricted upper bank after transferring.");
        SelectPerson(workerId); Canvas.FocusPerson(workerId); Refresh();
        before = Session.Serialize(); await CaptureManagementView("transport-service");
        if (before != Session.Serialize()) throw new Exception("Service journey inspection changed paused state.");
        for (var second = 0; second < 2400 && !Session.ServiceTasks.Any(t => t.RoomId == room.Id && t.Status == ServiceTaskStatus.Completed); second++) Session.Step();
        if (!Session.ServiceTasks.Any(t => t.RoomId == room.Id && t.Status == ServiceTaskStatus.Completed))
            throw new Exception("Service staff did not complete real work through both banks.");
        GD.Print("TRANSFER_SMOKE_PASS: coordinated cars, physical shafts, bank/car controls, actual transfer queue and route overlay, paused inspection, active-journey save/load, all24 workers arrive, and transferred service work completes.");
    }
}
