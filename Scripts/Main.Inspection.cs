using Godot;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Game;

public partial class Main
{
    public long? SelectedPersonId { get; private set; }
    public int? SelectedBankId { get; private set; }
    public int? SelectedCarId { get; private set; }
    private AcceptDialog _peopleDialog = null!;
    private ItemList _peopleList = null!;
    private LineEdit _peopleSearch = null!;
    private OptionButton _peopleFilter = null!;
    private Label _peopleSummary = null!, _personDetails = null!;
    private Button _locatePerson = null!, _locateSelection = null!;
    private readonly List<long> _listedPeople = new();
    private bool _refreshingPeople;
    private SpinBox _commuteOffices = null!, _commuteCapacity = null!;
    private bool _resetCommute;

    private void BuildPeopleBrowser()
    {
        _peopleDialog = Dialog("People & journeys", new Vector2I(820, 750), out var body);
        body.AddChild(Wrap("Inspect a worker in a queue, inside a car, at work, or off screen. Selecting and locating people does not change their journey.", 14, Muted));
        var filters = new HBoxContainer();
        _peopleSearch = new LineEdit { PlaceholderText = "Find ID or role…", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _peopleSearch.TextChanged += _ => RefreshPeopleBrowser(); filters.AddChild(_peopleSearch);
        _peopleFilter = new OptionButton();
        foreach (var label in new[] { "All people", "Waiting", "Riding", "At a room", "Walking", "Needs a route" }) _peopleFilter.AddItem(label);
        _peopleFilter.ItemSelected += _ => RefreshPeopleBrowser(); filters.AddChild(_peopleFilter); body.AddChild(filters);
        _peopleSummary = Wrap("", 14, Accent); body.AddChild(_peopleSummary);
        _peopleList = new ItemList { CustomMinimumSize = new Vector2(0, 210), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _peopleList.ItemSelected += index =>
        {
            if (!_refreshingPeople && index >= 0 && index < _listedPeople.Count) SelectPerson(_listedPeople[(int)index]);
        };
        _peopleList.ItemActivated += index =>
        {
            if (index >= 0 && index < _listedPeople.Count) { SelectPerson(_listedPeople[(int)index]); LocateSelectedPerson(); }
        };
        body.AddChild(_peopleList);
        _personDetails = Wrap("Choose a person to inspect their current journey.", 15); body.AddChild(_personDetails);
        _locatePerson = Button("Locate selected person", LocateSelectedPerson); body.AddChild(_locatePerson);
    }

    private void OpenPeople()
    {
        _peopleSearch.Text = ""; _peopleFilter.Select(0);
        _peopleDialog.PopupCentered(); RefreshPeopleBrowser();
    }

    public void SelectPerson(long id)
    {
        SelectedId = null; SelectedBankId = null; SelectedCarId = null; SelectedPersonId = id;
        SetTool("select"); Refresh();
    }

    public void SelectBank(int id, int carId = 1)
    {
        SelectedId = null; SelectedPersonId = null; SelectedBankId = id; SelectedCarId = carId;
        SetTool("select"); Refresh();
    }

    private void ClearEntitySelection()
    { SelectedId = null; SelectedPersonId = null; SelectedBankId = null; SelectedCarId = null; }

    private void LocateSelectedPerson()
    {
        if (SelectedPersonId is not { } id || Session.InspectPerson(id) == null) return;
        _peopleDialog.Hide(); Canvas.FocusPerson(id);
    }

    private void LocateSelection()
    {
        if (SelectedPersonId is { } person) Canvas.FocusPerson(person);
        else if (SelectedBankId is { } bank) Canvas.FocusBank(bank, SelectedCarId ?? 1);
    }

    private static string ClockAt(long tick)
    {
        var absolute = tick + 28500;
        return $"Day {absolute / 86400 + 1} {absolute / 3600 % 24:00}:{absolute / 60 % 60:00}";
    }

    private string PersonDetails(long id)
    {
        var view = Session.InspectPerson(id);
        if (view == null) return $"Person #{id}\nThis person is no longer in this district. They may have completed their departure. Choose another person in People.";
        var p = view.Person; var journey = view.Journey;
        var phase = p.Activity == PersonActivity.Visiting ? "At destination" : p.Activity == PersonActivity.Working ? "Doing service work" : journey.State.ToString();
        var lines = new List<string>
        {
            $"{p.Role} #{id} · {phase}", $"Activity: {p.Activity}",
            "Location: " + view.LocationLabel, "Destination: " + view.DestinationLabel,
            $"Entered: {ClockAt(p.CreatedAt)}", $"Satisfaction: {p.Satisfaction}%"
        };
        if (journey.BankId.HasValue) lines.Add($"Elevator bank: {journey.BankId}");
        if (journey.CarId.HasValue) lines.Add($"Assigned car: {journey.CarId}");
        lines.Add($"Completed transfers in this journey: {journey.TransferCount}");
        if (journey.RemainingRoute is { Count: > 0 } route)
            lines.Add("Remaining route (highlighted in the cutaway):\n" + string.Join("\n", route.Select(leg =>
                leg.Kind == RouteKind.Elevator ? $"Bank {leg.BankId}: {FloorName(leg.FromFloor)} → {FloorName(leg.ToFloor)}"
                : leg.Kind == RouteKind.Stair ? $"Stairs/escalator: {FloorName(leg.FromFloor)} → {FloorName(leg.ToFloor)}"
                : $"Walk on {FloorName(leg.FromFloor)}: bay {leg.FromX} → {leg.ToX}")));
        if (journey.State == JourneyState.Waiting)
            lines.Add($"Current wait: {view.CurrentWaitTicks}s / {view.PatienceTicks}s patience");
        lines.Add($"This journey's accumulated wait: {journey.TotalWaitTicks + view.CurrentWaitTicks}s");
        if (view.NextActionTick.HasValue) lines.Add("Next scheduled action: " + ClockAt(view.NextActionTick.Value));
        if (journey.State is JourneyState.Unreachable or JourneyState.Abandoned || p.Activity == PersonActivity.Stranded)
            lines.Add(Session.Transport.DiagnoseRoute(journey.Floor, (int)journey.X, journey.DestinationFloor,
                journey.DestinationX, journey.Service).Reason + " This person retries from their last safe location.");
        return string.Join("\n", lines);
    }

    private bool RefreshEntityInspector()
    {
        _locateSelection.Visible = SelectedPersonId.HasValue || SelectedBankId.HasValue;
        _locateSelection.Disabled = true;
        if (SelectedPersonId is { } personId)
        {
            _inspector.Text = PersonDetails(personId);
            _locateSelection.Disabled = Session.InspectPerson(personId) == null;
            return true;
        }
        if (SelectedBankId is not { } bankId) return false;
        var view = Session.InspectBank(bankId);
        if (view == null) { _inspector.Text = $"Elevator bank {bankId}\nThis bank has been removed."; return true; }
        var car = view.Cars.FirstOrDefault(c => c.CarId == SelectedCarId) ?? view.Car; var definition = view.Bank.Definition;
        _locateSelection.Disabled = false;
        _manageSelection.Disabled = false; _manageSelection.Text = "Configure selected bank";
        var stops = string.Join(", ", definition.Stops.Select(FloorName));
        var hallCalls = view.HallCalls.Count == 0 ? "No waiting hall calls" : string.Join("\n", view.HallCalls.Select(q =>
            $"{FloorName(q.Floor)} {(q.Direction > 0 ? "↑" : "↓")}: {q.WaitingCount} waiting · oldest {q.OldestWaitTicks}s"));
        _inspector.Text = $"BANK {bankId} · CAR {car.CarId}\nShaft {car.ShaftId} · bay {car.X}\n{car.State}\nFloor {car.DrawFloor:0.0} → {FloorName(car.TargetFloor)}\n" +
            $"Passengers {car.PassengerCount}/{car.Capacity}\nServed: {stops}\n" +
            $"Bank fleet: {view.Bank.AvailableCarCount}/{view.Bank.CarCount} available\n" +
            $"Travel {definition.TravelTicksPerFloor}s/floor · doors {definition.DoorTicks}s\n" +
            (definition.ServiceOnly ? "Service staff only" : "Public and service access") +
            "\n\n" + hallCalls + "\n\nOnboard IDs: " + (car.PassengerIds.Count == 0 ? "none" : string.Join(", ", car.PassengerIds.Select(id => "#" + id))) +
            "\nBank requested stops: " + (view.PassengerStops.Count == 0 ? "none" : string.Join(", ", view.PassengerStops.Select(FloorName)));
        return true;
    }

    private void RefreshPeopleBrowser()
    {
        if (_peopleDialog == null || !_peopleDialog.Visible || _refreshingPeople) return;
        _refreshingPeople = true;
        try
        {
            var journeys = Session.Transport.Journeys.ToDictionary(j => j.PersonId);
            var people = Session.People.Where(p => journeys.ContainsKey(p.Id));
            var query = _peopleSearch.Text.Trim();
            if (query.Length > 0) people = people.Where(p => ($"#{p.Id} {p.Role} {p.Activity} {journeys[p.Id].State}").Contains(query, StringComparison.OrdinalIgnoreCase));
            people = people.Where(p => _peopleFilter.Selected switch
            {
                1 => journeys[p.Id].State == JourneyState.Waiting,
                2 => journeys[p.Id].State == JourneyState.Riding,
                3 => p.Activity is PersonActivity.Visiting or PersonActivity.Working,
                4 => journeys[p.Id].State == JourneyState.Walking,
                5 => p.Activity == PersonActivity.Stranded || journeys[p.Id].State is JourneyState.Unreachable or JourneyState.Abandoned,
                _ => true
            });
            var matches = people.ToArray();
            var oldScroll = _peopleList.GetVScrollBar().Value;
            _peopleList.Clear(); _listedPeople.Clear();
            foreach (var p in matches.Take(500))
            {
                var j = journeys[p.Id];
                var activity = p.Activity is PersonActivity.Visiting or PersonActivity.Working ? p.Activity.ToString() : j.State.ToString();
                _peopleList.AddItem($"#{p.Id}  {p.Role} · {activity} · floor {j.DrawFloor:0.0} → {FloorName(j.DestinationFloor)}");
                _listedPeople.Add(p.Id);
                if (SelectedPersonId == p.Id) _peopleList.Select(_listedPeople.Count - 1);
            }
            _peopleList.GetVScrollBar().Value = oldScroll;
            _peopleSummary.Text = matches.Length == 0 ? "No people match. Office workers arrive at 08:00 when their office has an accessible route."
                : $"{matches.Length} matching people · {Session.Transport.Metrics.Waiting} waiting · {Session.Transport.Metrics.Riding} riding" +
                    (matches.Length > 500 ? "\nFirst 500 shown; narrow the ID/role filter to locate others." : "");
            _personDetails.Text = SelectedPersonId is { } id ? PersonDetails(id) : "Choose a person to inspect their current journey.";
            _locatePerson.Disabled = SelectedPersonId is not { } selected || Session.InspectPerson(selected) == null;
        }
        finally { _refreshingPeople = false; }
    }

    private void AddCommuteSetup(VBoxContainer body)
    {
        body.AddChild(new HSeparator());
        body.AddChild(Text("OFFICE COMMUTE SCENARIO", 16, Accent));
        body.AddChild(Wrap("A normal-budget district with a ground lobby and upper-floor offices. No stairs: everyone must share the elevator. Workers arrive at 08:00 and leave at 18:00. Use People to inspect any worker.", 14, Muted));
        var row = new HBoxContainer();
        row.AddChild(Text($"Offices ({_rules.For("office")!.Capacity} workers each)", 14));
        _commuteOffices = new SpinBox { MinValue = 1, MaxValue = 6, Step = 1, Value = 3 }; row.AddChild(_commuteOffices);
        row.AddChild(Text("Elevator capacity", 14));
        _commuteCapacity = new SpinBox { MinValue = 1, MaxValue = 256, Step = 1, Value = 6 }; row.AddChild(_commuteCapacity);
        body.AddChild(row);
        body.AddChild(Button("Start office commute", () =>
        {
            var location = _locations.Locations[_citySelect.Selected];
            _newLocationId = location.Id; _newSiteId = location.Sites[_siteSelect.Selected].Id; _newSandbox = false;
            _resetTransfer = false; _resetCommute = true; _newGame.Hide(); _reset.PopupCentered();
        }));
    }

    private void StartOfficeCommute()
    {
        Session = OfficeCommuteScenario.Create(Catalog, _rules, _locations, (int)_commuteOffices.Value, (int)_commuteCapacity.Value, _newLocationId, _newSiteId);
        ResetSessionView();
        SetStatus($"Office commute ready: {_commuteOffices.Value * _rules.For("office")!.Capacity:0} workers, {_commuteCapacity.Value:0} elevator seats, no stairs. Arrivals at 08:00. Select a person/car or open People.");
    }

    private void ResetSessionView()
    {
        _runner.Reset(); _lastAutosaveBucket = 0; _lastNoticeTick = -1; _manualFileName = "district.json";
        _runner.SetSpeed(OS.GetCmdlineUserArgs().Contains("--construction-smoke") ? 0 : 1);
        ClearEntitySelection();
        foreach (var window in _managementWindows) window.Hide();
        Canvas.ResetView(); SetTool("select"); Refresh();
    }

    private async Task VerifyOfficeCommuteInspection()
    {
        _commuteOffices.Value = 3; _commuteCapacity.Value = 6;
        StartOfficeCommute();
        for (var i = 0; i < 1200 && !Session.Transport.Journeys.Any(j => j.State == JourneyState.Waiting); i++) Session.Step();
        if (Session.People.Count != 24 || Session.Transport.Stairs.Count != 0)
            throw new InvalidOperationException("Office commute scenario is not the intended 24-worker elevator-only tower.");
        var waiting = Session.Transport.Journeys.First(j => j.State == JourneyState.Waiting);
        var unchanged = Session.Serialize();
        Canvas.FocusPerson(waiting.PersonId);
        var point = Canvas.GetGlobalRect().Position + Canvas.PersonScreenPosition(waiting.PersonId)!.Value - new Vector2(0, 9 * Canvas.Zoom);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point }, true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (SelectedPersonId == null || !_inspector.Text.Contains("Current wait:"))
            throw new InvalidOperationException("Viewport person selection did not display a real waiting journey.");
        OpenPeople();
        if (!BlocksWorldInput || _peopleList.ItemCount != 24)
            throw new InvalidOperationException("People browser omitted workers or failed to block world input.");
        _peopleDialog.Hide();
        Canvas.FocusBank(1);
        point = Canvas.GetGlobalRect().Position + Canvas.BankScreenPosition(1)!.Value;
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point }, true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (SelectedBankId != 1 || !_inspector.Text.Contains("Passengers"))
            throw new InvalidOperationException("Viewport elevator selection failed.");
        OpenSelectedFacility();
        if (!_transportPanel.Visible) throw new InvalidOperationException("Selected bank did not open configuration.");
        _transportPanel.Hide();
        Canvas.ChangeZoom(1.1f); Canvas.Pan(new Vector2(15, 25));
        if (Session.Serialize() != unchanged) throw new InvalidOperationException("Inspection, camera or paused frames changed the simulation.");
        for (var i = 0; i < 1200 && !Session.Transport.Journeys.Any(j => j.State == JourneyState.Riding); i++) Session.Step();
        var rider = Session.Transport.Journeys.First(j => j.State == JourneyState.Riding);
        SelectPerson(rider.PersonId); OpenPeople(); _peopleFilter.Select(2); RefreshPeopleBrowser();
        if (!_listedPeople.Contains(rider.PersonId) || !_personDetails.Text.Contains("Riding"))
            throw new InvalidOperationException("People browser could not inspect an onboard passenger.");
        _peopleDialog.Hide();
        var saved = Session.Serialize();
        var path = System.IO.Path.Combine(_saveDirectory, "commute.json");
        if (!SaveTo(path)) throw new InvalidOperationException("Commute save failed.");
        Session.Advance(5); LoadFrom(path);
        if (SelectedPersonId != null || SelectedBankId != null || Session.Serialize() != saved)
            throw new InvalidOperationException("Loading retained stale selection or changed an active commute.");
    }
}
