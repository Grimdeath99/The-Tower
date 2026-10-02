using Godot;
using VerticalDistrict.Core.Simulation;

namespace VerticalDistrict.Game;

public partial class Main
{
    private AcceptDialog _managementDialog = null!;
    private OptionButton _managementView = null!, _staffDepot = null!;
    private HBoxContainer _staffActions = null!;
    private RichTextLabel _managementText = null!, _facilityLiveText = null!;
    private Label _managementClock = null!;
    private readonly List<long> _staffDepotIds = new();
    private long? _facilityRoomId;

    private void BuildManagementLoopDialog()
    {
        _managementDialog = Dialog("Tower management", new Vector2I(940, 730), out var body);
        _managementClock = Text("", 15, Accent); body.AddChild(_managementClock);
        _managementView = new OptionButton();
        foreach (var name in new[] { "Staff and services", "Demand and satisfaction", "Progression" }) _managementView.AddItem(name);
        _managementView.ItemSelected += _ => RefreshManagementLoop(); body.AddChild(_managementView);
        _staffActions = new HBoxContainer();
        _staffDepot = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _staffActions.AddChild(_staffDepot);
        _staffActions.AddChild(Button("Hire +1", () => AdjustServiceStaff(1)));
        _staffActions.AddChild(Button("Dismiss −1", () => AdjustServiceStaff(-1)));
        body.AddChild(_staffActions);
        _managementText = new RichTextLabel { FitContent = true, BbcodeEnabled = false, SelectionEnabled = true,
            CustomMinimumSize = new Vector2(0, 470), MouseFilter = MouseFilterEnum.Stop };
        body.AddChild(_managementText);
        body.AddChild(Button("Daily reports and notices", () => { _managementDialog.Hide(); OpenReports(); }));
    }

    private void OpenManagementLoop()
    {
        _managementDialog.PopupCentered(); RefreshManagementLoop();
    }

    private void AdjustServiceStaff(int change)
    {
        if (_staffDepot.Selected < 0 || _staffDepot.Selected >= _staffDepotIds.Count) return;
        var id = _staffDepotIds[_staffDepot.Selected];
        if (Session.OperationFor(id) is { } op) ApplyOperation(Session.SetStaff(id, op.Staff + change));
    }

    private void RefreshManagementLoop()
    {
        RefreshFacilityLiveState();
        if (_managementDialog == null || !_managementDialog.Visible) return;
        _managementClock.Text = Session.ClockText + $"  ·  {Session.PhysicalPopulation} physically inside  ·  {Session.ActiveComplaints.Count} active warnings";
        _staffActions.Visible = _managementView.Selected == 0;
        var lines = new List<string>();
        if (_managementView.Selected == 0)
        {
            var depotIds = World.Rooms.Where(r => r.DefinitionId == "service-room").Select(r => r.Id).ToArray();
            if (!_staffDepotIds.SequenceEqual(depotIds))
            {
                var selected = _staffDepot.Selected >= 0 && _staffDepot.Selected < _staffDepotIds.Count ? _staffDepotIds[_staffDepot.Selected] : 0;
                _staffDepot.Clear(); _staffDepotIds.Clear();
                foreach (var id in depotIds) { _staffDepotIds.Add(id); _staffDepot.AddItem(FinanceRoomName(id)); }
                if (_staffDepotIds.Count > 0) _staffDepot.Select(Math.Max(0, _staffDepotIds.IndexOf(selected)));
            }
            lines.Add("SERVICE STAFF\nHire at a depot. Dispatched workers must return before dismissal. Wages are billed daily, including while closed.");
            foreach (var depot in Session.StaffSummaries)
                lines.Add($"{FinanceRoomName(depot.DepotRoomId)}\nHired {depot.Hired} · available {depot.Available} · traveling {depot.Traveling} · working {depot.Working} · returning/blocked {depot.ReturningOrBlocked}\nDaily wages {FinanceMoney(depot.DailyWagesMinor)} · {(depot.Open ? "open" : "closed")} · {(depot.Accessible ? "service access available" : "no service route")}");
            if (depotIds.Length == 0) lines.Add("No service depot. Build and connect a service room to hire housekeeping and maintenance staff.");
            var active = Session.ServiceTasks.Where(t => t.Status is not (ServiceTaskStatus.Completed or ServiceTaskStatus.Cancelled)).ToArray();
            lines.Add($"TASK QUEUE · {active.Length} unresolved");
            foreach (var task in active.OrderBy(t => t.Id)) lines.Add(ServiceTaskLine(task));
            if (active.Length == 0) lines.Add("No pending work.");
            lines.Add("RECENT FINISHED WORK");
            lines.AddRange(Session.ServiceTasks.Where(t => t.Status is ServiceTaskStatus.Completed or ServiceTaskStatus.Cancelled).TakeLast(10).Reverse().Select(ServiceTaskLine));
        }
        else if (_managementView.Selected == 1)
        {
            lines.Add($"Satisfaction {Session.Satisfaction}% · changes by at most {_rules.Management.SatisfactionStepPerHour} points per room per game hour.\nDemand uses asking price, location profile, access, staff, condition and room satisfaction. Location modifiers are provisional neutral values.");
            foreach (var model in new[] { "Office", "Home", "Food", "Shop", "Hotel" })
            {
                var rooms = World.Rooms.Where(r => _rules.For(r.DefinitionId)?.Model == model).ToArray();
                if (rooms.Length == 0) continue;
                var demand = (int)rooms.Average(r => Session.DemandFor(r.Id).Score);
                var capacity = rooms.Sum(r => _rules.For(r.DefinitionId)!.Capacity);
                var contracted = rooms.Sum(r => Session.ContractedOccupancy(r.Id));
                lines.Add($"{model}: {rooms.Length} rooms · demand {demand}% · physical capacity {capacity}" +
                    (model is "Office" or "Home" ? $" · contracted {contracted} · vacant capacity {capacity - contracted}" : ""));
                foreach (var room in rooms)
                {
                    var assessment = Session.DemandFor(room.Id);
                    var best = assessment.Factors.MaxBy(f => f.Score)!; var worst = assessment.Factors.MinBy(f => f.Score)!;
                    lines.Add($"  {FinanceRoomName(room.Id)} · demand {assessment.Score}% · satisfaction {assessment.Satisfaction}%\n  Strongest: {best.Name} {best.Score}% · weakest: {worst.Name} {worst.Score}%" +
                        (assessment.Reasons.Count > 0 ? "\n  " + string.Join(" ", assessment.Reasons) : "\n  No active operating problems."));
                }
            }
            lines.Add("HOURLY TREND\n" + string.Join(" · ", Session.SatisfactionHistory.TakeLast(12).Select(s => $"{s.Tick / 3600}h: {s.Score}%")));
            lines.Add("ACTIVE COMPLAINTS / OPERATING WARNINGS");
            lines.AddRange(Session.ActiveComplaints.Select(c => $"#{c.Id} · {FinanceRoomName(c.RoomId)}: {c.Message}"));
            if (Session.ActiveComplaints.Count == 0) lines.Add("No active complaints.");
        }
        else
        {
            lines.Add($"RANK {World.Rank} / 7 · {Catalog.GetRank(World.Rank).Name}\nUnlocked height: {Catalog.GetRank(World.Rank).AboveGroundFloorCap} above-ground floors plus ten basements.");
            lines.Add("Original provisional objectives. A transition is awarded once after its real operating requirements are met. Terminals are optional.");
            if (World.Rank < 7)
            {
                var p = _rules.Promotions.Single(p => p.Rank == World.Rank + 1);
                var diversity = World.Rooms.Where(r => Session.OperationFor(r.Id)?.Open == true && Session.IsAccessible(r.Id)).Select(r => _rules.For(r.DefinitionId)?.Model).Distinct().Count();
                var hotels = World.Rooms.Count(r => r.DefinitionId == "hotel-room" && Session.IsAccessible(r.Id) && Session.OperationFor(r.Id)!.Condition >= 80);
                lines.Add($"NEXT · {Catalog.GetRank(p.Rank).Name}\nPeak occupants (excluding service staff) {Session.PeakPopulation} / {p.Population}\nLast closed operating profit {FinanceMoney(Session.Reports.LastOrDefault()?.ProfitMinor ?? 0)} / {FinanceMoney(p.DailyProfitMinor)}\nAccessible open facility types {diversity} / {p.Diversity}\nSatisfaction {Session.Satisfaction}% / {p.Satisfaction}%\nCleanliness {Session.Cleanliness}% / {p.Cleanliness}%\nQuality hotel rooms {hotels} / {p.HotelRooms}\nCompleted physical journeys {Session.CompletedTrips} / {p.SuccessfulTrips}");
                var unmet = Session.UnmetRankRequirements();
                lines.Add(unmet.Count == 0 ? "Requirements met. Award occurs at the next hourly progression review." : "STILL NEEDED\n" + string.Join("\n", unmet));
            }
            else lines.Add(Session.Sandbox ? "Sandbox starts with all ranks unlocked; this does not represent earned objectives."
                : "Current provisional objectives achieved. Continue operating your district.");
            lines.Add("RECENT AWARDS\n" + string.Join("\n", Session.Notices.Where(n => n.Kind == "Promotion").Select(n => n.Text)));
        }
        _managementText.Text = string.Join("\n\n", lines);
    }

    private string ServiceTaskLine(ServiceTask task) => $"Task #{task.Id} · {task.Kind} · {task.Status}\n{FinanceRoomName(task.RoomId)} · worker {(task.WorkerPersonId?.ToString() ?? "unassigned")}" +
        (task.WorkDueTick is { } due && task.Status == ServiceTaskStatus.InProgress ? $" · {Math.Max(0, due - Session.Tick)}s work remaining" : "") + $"\n{task.Reason}";

    private void RefreshFacilityLiveState()
    {
        if (_facility == null || !_facility.Visible || _facilityLiveText == null || _facilityRoomId is not { } id || Session.OperationFor(id) is not { } op) return;
        _facilityOpen?.SetPressedNoSignal(op.Open);
        var room = World.Rooms.Single(r => r.Id == id); var rule = _rules.For(room.DefinitionId)!;
        var assessment = Session.DemandFor(id);
        var assignment = rule.Model == "Condo" ? $"assigned residents {Session.CondoResidentCapacity(id)} · "
            : rule.Model is "Office" or "Home" ? $"contracted members {Session.ContractedOccupancy(id)} · " : "";
        var lines = new List<string> { Session.OperatingWarning(id),
            $"Capacity {rule.Capacity} · {assignment}physically in room {Session.Occupancy(id)} · in room or arriving {Session.ReservedCapacity(id)}",
            $"Cleanliness {op.Cleanliness}% · condition {op.Condition}% · demand {assessment.Score}% · satisfaction {assessment.Satisfaction}%",
            $"Public access: {(Session.IsAccessible(id) ? "connected" : "unreachable")} · service access: {(Session.IsAccessible(id, true) ? "connected" : "unreachable")}\nAsking price {FinanceMoney(op.PriceMinor)} · hired staff {op.Staff} · daily wages {FinanceMoney(op.Staff * rule.StaffSalaryMinor)}" };
        if (!Session.IsAccessible(id)) lines.Add("Public route: " + Session.DiagnoseRoomAccess(id).Reason);
        if (!Session.IsAccessible(id, true)) lines.Add("Service route: " + Session.DiagnoseRoomAccess(id, true).Reason);
        if (Session.TenantFor(id) is { } tenant)
            lines.Add($"Tenant #{tenant.Id} · {tenant.Status} · {tenant.MemberIds.Length} members\nAgreed daily rent {FinanceMoney(tenant.AgreedRentMinor)} · renewal Day {tenant.RenewalDay}\nLast physical occupancy Day {tenant.LastOccupiedDay} · last paid Day {tenant.LastPaidDay}" + (tenant.DepartureReason.Length > 0 ? "\n" + tenant.DepartureReason : ""));
        if (rule.Model == "Condo") AppendOwnershipInspection(id, lines);
        if (rule.Model == "Hotel")
        {
            lines.Add("Hotel inventory: " + Session.HotelStateFor(id));
            if (Session.HotelBookingFor(id) is { } booking)
                lines.Add($"Booking #{booking.Id} · {booking.Status} · guest #{booking.PersonId}\n{booking.Nights} nights at {FinanceMoney(booking.AgreedNightlyPriceMinor)} · charged {FinanceMoney(booking.ChargedMinor)}");
        }
        if (rule.Model is "Food" or "Shop") AppendRetailInspection(id, lines);
        lines.Add("SATISFACTION FACTORS\n" + string.Join(" · ", assessment.Factors.Select(f => $"{f.Name} {f.Score}%")));
        lines.Add("CURRENT CAUSES\n" + (assessment.Reasons.Count == 0 ? "No operating problems." : string.Join("\n", assessment.Reasons)));
        lines.Add("RECENT ROOM TRANSACTIONS\n" + string.Join("\n", Session.FinanceTransactions().Where(t => IsRoomTransaction(t) && t.EntityId == id).TakeLast(5).Reverse().Select(t => $"#{t.Sequence} · {FinanceMoney(t.AmountMinor)} · {t.Description}")));
        _facilityLiveText.Text = string.Join("\n\n", lines);
    }

    private async System.Threading.Tasks.Task VerifyManagementLoopInspection()
    {
        Session = OperatingExampleScenario.Create(Catalog, _rules, _locations, floorCount: 20);
        Session.Advance(40000);
        Refresh();
        var before = Session.Serialize(); _runner.SetSpeed(0);
        OpenManagementLoop();
        if (!BlocksWorldInput || !_managementText.Text.Contains("TASK QUEUE")) throw new Exception("Service panel did not expose tasks or isolate input.");
        var depot = _staffDepotIds[_staffDepot.Selected];
        var hired = Session.OperationFor(depot)!.Staff;
        _staffActions.GetChildren().OfType<Button>().Single(b => b.Text == "Hire +1").EmitSignal(BaseButton.SignalName.Pressed);
        if (Session.OperationFor(depot)!.Staff != hired + 1) throw new Exception("Service panel hiring did not execute its gameplay command.");
        _staffActions.GetChildren().OfType<Button>().Single(b => b.Text == "Dismiss −1").EmitSignal(BaseButton.SignalName.Pressed);
        if (Session.OperationFor(depot)!.Staff != hired) throw new Exception("Service panel dismissal did not execute its gameplay command.");
        await CaptureManagementView("management-services");
        _managementView.Select(1); RefreshManagementLoop();
        if (!_managementText.Text.Contains("HOURLY TREND")) throw new Exception("Demand panel is missing actual trend state.");
        await CaptureManagementView("management-demand");
        _managementView.Select(2); RefreshManagementLoop();
        if (!_managementText.Text.Contains("RANK")) throw new Exception("Progression panel did not expose the objective.");
        await CaptureManagementView("management-progression");
        _managementDialog.Hide();
        SelectedId = World.Rooms.First(r => r.DefinitionId == "office").Id;
        OpenSelectedFacility(); RefreshFacilityLiveState();
        if (!BlocksWorldInput || !_facilityLiveText.Text.Contains("contracted members")) throw new Exception("Facility panel is missing contract state.");
        await CaptureManagementView("management-room");
        _facility.Hide();
        if (Session.Serialize() != before) throw new Exception("Management inspection mutated authoritative state.");
    }

    private async System.Threading.Tasks.Task CaptureManagementView(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--management-captures")) return;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var result = GetViewport().GetTexture().GetImage().SavePng("res://artifacts/" + name + ".png");
        if (result != Error.Ok) throw new Exception("Could not capture " + name + ": " + result);
    }
}
