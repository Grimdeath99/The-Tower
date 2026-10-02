using Godot;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;
using System.Globalization;

namespace VerticalDistrict.Game;

/// <summary>Editable transport controls; all mutations pass through authoritative core commands.</summary>
public partial class TransportPanel : AcceptDialog
{
    public event Action<string>? Changed;
    private GameSession? _session;
    private OptionButton _bank = null!, _connectorType = null!, _car = null!;
    private SpinBox _newCarX = null!;
    private Button _addCar = null!, _removeCar = null!, _pauseCar = null!, _resumeCar = null!;
    private SpinBox _x = null!, _min = null!, _max = null!, _capacity = null!, _travel = null!, _doors = null!;
    private SpinBox _stairFloor = null!, _stairX = null!;
    private LineEdit _stops = null!;
    private CheckBox _service = null!;
    private Label _status = null!, _metrics = null!, _carDetails = null!, _stairDetails = null!, _costs = null!;
    private Button _install = null!, _update = null!, _remove = null!, _disable = null!, _restore = null!;
    private double _refreshElapsed;
    private bool _built;

    public override void _Ready()
    {
        Title = "Transport control";
        MinSize = new Vector2I(850, 620);
        Size = new Vector2I(920, 720);
        GetOkButton().Text = "Close";
        BuildInterface();
        _built = true;
    }

    public void OpenFor(GameSession session, int selectedBankId = 0, int selectedCarId = 1)
    {
        _session = session;
        if (!_built) return;
        _status.Text = "Select an existing bank to inspect it, or New bank to reserve an empty shaft.";
        _status.AddThemeColorOverride("font_color", new Color("8dabb7"));
        RefreshBanks(selectedBankId);
        SelectCar(selectedCarId);
        _costs.Text = $"Each shaft + car: {Main.Money(session.Rules.ElevatorCostMinor)}   |   Stairs: {Main.Money(session.Rules.StairCostMinor)}   |   Escalator: {Main.Money(session.Rules.EscalatorCostMinor)}";
        _stairFloor.Value = Math.Max(0, session.World.Floors.Min());
        _stairX.Value = FindEmptyBay();
        RefreshMetrics();
        PopupCentered(Size);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _session == null || !_built) return;
        _refreshElapsed += delta;
        if (_refreshElapsed < .5) return;
        _refreshElapsed = 0;
        RefreshMetrics();
    }

    private void BuildInterface()
    {
        var margin = new MarginContainer();
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.OffsetLeft = 18; margin.OffsetTop = 12; margin.OffsetRight = -18; margin.OffsetBottom = -56;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(content);
        content.AddChild(Label("ELEVATOR BANKS", 18, "8ed9bd"));
        content.AddChild(Wrap("A bank coordinates cars in separate shafts. Local service stops at each selected floor; express service passes other floors. Transfers require alighting and walking through the supported floor corridor to another bank."));
        var selector = new HBoxContainer();
        selector.AddChild(Label("Selected bank", 14));
        _bank = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 36) };
        _bank.ItemSelected += _ => LoadSelectedBank();
        selector.AddChild(_bank);
        content.AddChild(selector);
        var cars = new HBoxContainer();
        _car = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _car.ItemSelected += _ => RefreshMetrics(); cars.AddChild(_car);
        _pauseCar = ActionButton("Pause car", () => MutateCar(true)); cars.AddChild(_pauseCar);
        _resumeCar = ActionButton("Restore car", () => MutateCar(false)); cars.AddChild(_resumeCar);
        _removeCar = ActionButton("Remove empty car + shaft", RemoveSelectedCar); cars.AddChild(_removeCar);
        content.AddChild(cars);
        var addCar = new HBoxContainer();
        addCar.AddChild(Label("Additional shaft bay", 14)); _newCarX = Number(0, ConstructionWorld.Width - 1, 28); addCar.AddChild(_newCarX);
        _addCar = ActionButton("Build coordinated car + shaft", AddCar); addCar.AddChild(_addCar); content.AddChild(addCar);

        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 10);
        _x = Number(0, ConstructionWorld.Width - 1, 0);
        _min = Number(ConstructionWorld.MinFloor, ConstructionWorld.MaxFloor - 1, 0);
        _max = Number(ConstructionWorld.MinFloor + 1, ConstructionWorld.MaxFloor, 1);
        _capacity = Number(1, 256, 8);
        _travel = Number(1, 120, 2);
        _doors = Number(1, 120, 2);
        Field(grid, "Shaft bay", _x); Field(grid, "Bottom floor", _min); Field(grid, "Top floor", _max);
        Field(grid, "Capacity", _capacity); Field(grid, "Seconds / floor", _travel); Field(grid, "Door seconds", _doors);
        content.AddChild(grid);

        var stopRow = new HBoxContainer();
        stopRow.AddChild(Label("Served floors", 14));
        _stops = new LineEdit { PlaceholderText = "0, 1, 2, 3", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Enter at least two different floor numbers, separated by commas. Negative numbers are basement floors." };
        stopRow.AddChild(_stops);
        stopRow.AddChild(ActionButton("Serve every floor", () =>
        {
            var low = (int)_min.Value; var high = (int)_max.Value;
            if (high <= low) { Report(new CommandResult(false, "Top floor must be higher than bottom floor.")); return; }
            _stops.Text = string.Join(", ", Enumerable.Range(low, high - low + 1));
        }));
        content.AddChild(stopRow);
        _service = new CheckBox { Text = "Service staff only", TooltipText = "Guests, residents, customers and office workers cannot use this bank." };
        content.AddChild(_service);
        _costs = Label("", 13, "8dabb7");
        content.AddChild(_costs);
        var actions = new HBoxContainer();
        _install = ActionButton("Build bank", Install);
        _update = ActionButton("Apply settings", UpdateBank);
        _remove = ActionButton("Remove empty bank", RemoveBank);
        _disable = ActionButton("Pause service", () => MutateSelected(id => _session!.TriggerLiftDisruption(id)));
        _restore = ActionButton("Restore service", () => MutateSelected(id => _session!.Transport.SetBankOutOfService(id, false)));
        foreach (var button in new[] { _install, _update, _remove, _disable, _restore }) actions.AddChild(button);
        content.AddChild(actions);
        content.AddChild(Wrap("Settings require every car to be stopped and empty. Pausing a car preserves riders until its next safe stop; other cars keep serving the bank. Removing a car or bank is rejected while its passengers or assignments remain."));
        content.AddChild(new HSeparator());

        content.AddChild(Label("STAIRS & ESCALATORS", 18, "8ed9bd"));
        var stairRow = new HBoxContainer();
        _connectorType = new OptionButton { CustomMinimumSize = new Vector2(170, 36) };
        _connectorType.AddItem("Stairs (both ways)", 0);
        _connectorType.AddItem("Escalator up", 1);
        _connectorType.AddItem("Escalator down", 2);
        stairRow.AddChild(_connectorType);
        _stairFloor = Number(ConstructionWorld.MinFloor, ConstructionWorld.MaxFloor - 1, 0);
        _stairX = Number(0, ConstructionWorld.Width - 1, 0);
        stairRow.AddChild(Label("Lower floor", 14)); stairRow.AddChild(_stairFloor);
        stairRow.AddChild(Label("Bay", 14)); stairRow.AddChild(_stairX);
        stairRow.AddChild(ActionButton("Build", () =>
        {
            if (_session == null) return;
            var selection = _connectorType.GetItemId(_connectorType.Selected);
            var direction = selection == 2 ? -1 : selection;
            Report(direction == 0
                ? _session.Transport.BuildStair((int)_stairFloor.Value, (int)_stairX.Value, _session.Rules.StairCostMinor)
                : _session.Transport.BuildEscalator((int)_stairFloor.Value, (int)_stairX.Value, direction, _session.Rules.EscalatorCostMinor));
        }));
        stairRow.AddChild(ActionButton("Remove", () =>
        {
            if (_session != null) Report(_session.Transport.RemoveStair((int)_stairFloor.Value, (int)_stairX.Value));
        }));
        content.AddChild(stairRow);
        _stairDetails = Wrap(""); content.AddChild(_stairDetails);
        content.AddChild(new HSeparator());
        content.AddChild(Label("LIVE SERVICE", 18, "8ed9bd"));
        _metrics = Wrap(""); content.AddChild(_metrics);
        _carDetails = Wrap(""); content.AddChild(_carDetails);
        _status = Wrap(""); content.AddChild(_status);
    }

    private int SelectedBankId => _bank.Selected >= 0 ? _bank.GetItemId(_bank.Selected) : 0;
    private int SelectedCarId => _car.Selected >= 0 ? _car.GetItemId(_car.Selected) : 1;

    private void RefreshBanks(int selectedId)
    {
        if (_session == null) return;
        _bank.Clear(); _bank.AddItem("New bank", 0);
        var index = 0; var selected = 0;
        foreach (var bank in _session.Transport.Banks)
        {
            index++;
            var d = bank.Definition;
            _bank.AddItem($"Bank {d.Id}  |  {bank.CarCount} cars  |  floors {d.MinFloor} to {d.MaxFloor}" + (d.ServiceOnly ? "  |  staff" : ""), d.Id);
            if (d.Id == selectedId) selected = index;
        }
        _bank.Select(selected);
        LoadSelectedBank();
    }

    private void LoadSelectedBank()
    {
        if (_session == null) return;
        var bank = _session.Transport.Banks.FirstOrDefault(b => b.Definition.Id == SelectedBankId);
        var isNew = bank == null;
        var lower = _session.World.Floors.Min();
        var upper = Math.Max(lower + 1, _session.World.Floors.Max());
        var definition = bank?.Definition ?? new BankDefinition(NextBankId(), FindEmptyBay(), lower, upper,
            Enumerable.Range(lower, upper - lower + 1).ToArray());
        _x.Value = definition.X; _min.Value = definition.MinFloor; _max.Value = definition.MaxFloor;
        _capacity.Value = definition.Capacity; _travel.Value = definition.TravelTicksPerFloor; _doors.Value = definition.DoorTicks;
        _stops.Text = string.Join(", ", definition.Stops);
        _service.ButtonPressed = definition.ServiceOnly;
        _x.Editable = isNew; _min.Editable = isNew; _max.Editable = isNew;
        _install.Disabled = !isNew;
        _update.Disabled = isNew; _remove.Disabled = isNew; _disable.Disabled = isNew; _restore.Disabled = isNew;
        _car.Clear();
        foreach (var car in _session.Transport.Cars.Where(c => c.BankId == SelectedBankId))
            _car.AddItem($"Car {car.CarId} · shaft {car.ShaftId} · bay {car.X}", car.CarId);
        _addCar.Disabled = isNew; _newCarX.Value = FindEmptyBay();
        RefreshMetrics();
    }

    private int FindEmptyBay()
    {
        if (_session == null) return 0;
        for (var x = 0; x < ConstructionWorld.Width; x++)
            if (!_session.World.Floors.Any(f => _session.Transport.Occupies(x, f))
                && !_session.World.Rooms.Any(r => r.X <= x && r.X + _session.World.Catalog.Get(r.DefinitionId).Width > x)) return x;
        return 0;
    }

    private int NextBankId()
    {
        if (_session == null) return 1;
        var ids = _session.Transport.Banks.Select(b => b.Definition.Id).ToHashSet();
        var id = 1;
        while (ids.Contains(id)) id++;
        return id;
    }

    private bool TryDefinition(int id, out BankDefinition definition)
    {
        definition = null!;
        var parts = _stops.Text.Split(',', StringSplitOptions.TrimEntries);
        var floors = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var floor))
            { Report(new CommandResult(false, "Served floors must be whole numbers separated by commas, for example -1, 0, 3.")); return false; }
            floors.Add(floor);
        }
        if (floors.Count < 2 || floors.Distinct().Count() != floors.Count)
        { Report(new CommandResult(false, "Select at least two different floors; remove duplicate numbers.")); return false; }
        definition = new BankDefinition(id, (int)_x.Value, (int)_min.Value, (int)_max.Value, floors.ToArray(),
            (int)_capacity.Value, (int)_travel.Value, (int)_doors.Value, _service.ButtonPressed,
            _session?.Transport.Banks.FirstOrDefault(b => b.Definition.Id == id)?.Definition.AdditionalShafts);
        return true;
    }

    private void Install()
    {
        if (_session == null || !TryDefinition(NextBankId(), out var definition)) return;
        var result = _session.Transport.InstallBank(definition, _session.Rules.ElevatorCostMinor);
        if (result.Success) RefreshBanks(definition.Id);
        Report(result);
    }

    private void UpdateBank()
    {
        if (_session == null || SelectedBankId == 0 || !TryDefinition(SelectedBankId, out var definition)) return;
        var result = _session.Transport.ConfigureBank(definition);
        if (result.Success) RefreshBanks(definition.Id);
        Report(result);
    }

    private void RemoveBank()
    {
        if (_session == null || SelectedBankId == 0) return;
        var result = _session.Transport.RemoveBank(SelectedBankId);
        if (result.Success) RefreshBanks(0);
        Report(result);
    }

    private void MutateSelected(Func<int, CommandResult> action)
    {
        if (_session == null || SelectedBankId == 0) return;
        Report(action(SelectedBankId));
    }

    private void SelectCar(int carId)
    {
        for (var i = 0; i < _car.ItemCount; i++) if (_car.GetItemId(i) == carId) { _car.Select(i); break; }
        RefreshMetrics();
    }
    private void AddCar()
    {
        if (_session == null || SelectedBankId == 0) return;
        var ids = _session.Transport.Cars.Where(c => c.BankId == SelectedBankId).Select(c => c.CarId).ToHashSet();
        var next = 2; while (ids.Contains(next)) next++;
        var result = _session.Transport.AddCar(SelectedBankId, next, (int)_newCarX.Value, _session.Rules.ElevatorCostMinor);
        if (result.Success) { RefreshBanks(SelectedBankId); SelectCar(next); }
        Report(result);
    }
    private void RemoveSelectedCar()
    {
        if (_session == null || SelectedBankId == 0) return;
        var result = _session.Transport.RemoveCar(SelectedBankId, SelectedCarId);
        if (result.Success) RefreshBanks(SelectedBankId);
        Report(result);
    }
    private void MutateCar(bool disabled)
    {
        if (_session == null || SelectedBankId == 0) return;
        Report(_session.Transport.SetCarOutOfService(SelectedBankId, SelectedCarId, disabled));
    }

    internal void VerifyCarControls(int bankId, int carId)
    {
        if (_session == null) throw new InvalidOperationException("No transport session.");
        RefreshBanks(bankId); SelectCar(carId);
        _pauseCar.EmitSignal(BaseButton.SignalName.Pressed);
        if (!_session.Transport.Cars.Single(c => c.BankId == bankId && c.CarId == carId).IsOutOfService)
            throw new InvalidOperationException("Pause car did not issue its validated command.");
        _resumeCar.EmitSignal(BaseButton.SignalName.Pressed);
        if (_session.Transport.Cars.Single(c => c.BankId == bankId && c.CarId == carId).IsOutOfService)
            throw new InvalidOperationException("Restore car did not issue its validated command.");
    }

    private void Report(CommandResult result)
    {
        _status.Text = result.Message;
        _status.AddThemeColorOverride("font_color", new Color(result.Success ? "8ed9bd" : "f2b58c"));
        RefreshMetrics();
        Changed?.Invoke(result.Message);
    }

    private void RefreshMetrics()
    {
        if (_session == null) return;
        var m = _session.Transport.Metrics;
        var overloaded = m.OverloadedFloors.Count == 0 ? "none" : string.Join(", ", m.OverloadedFloors);
        _metrics.Text = $"Waiting {m.Waiting}   |   Riding {m.Riding}   |   Seat utilization {m.Utilization:P0}\n"
            + $"Completed boarding waits (last {m.WaitSampleCount}, max 2048): average {m.AverageWaitTicks:F1}s · p95 {m.P95WaitTicks}s\n"
            + $"Oldest current wait {_session.Transport.Journeys.Where(j => j.State == JourneyState.Waiting).Select(j => _session.Tick - j.WaitSinceTick).DefaultIfEmpty().Max()}s · abandoned journeys {m.AbandonedTrips} (lifetime)\n"
            + $"Queue exceeds available bank seats on floors: {overloaded} (current pressure)";
        var banks = _session.Transport.Banks.ToDictionary(b => b.Definition.Id);
        _carDetails.Text = _session.Transport.Cars.Count == 0 ? "No elevator banks installed." : string.Join("\n", _session.Transport.Cars.Select(car =>
            $"Bank {car.BankId} / car {car.CarId} / bay {car.X}: {car.State}   |   floor {car.DrawFloor:F1} → {car.TargetFloor}   |   {car.PassengerCount}/{car.Capacity} onboard   |   {banks[car.BankId].WaitingCount} bank queue"));
        _stairDetails.Text = _session.Transport.Stairs.Count == 0 ? "No stairs or escalators installed. Escalators carry people only in their configured direction; each connection joins two adjacent floors." :
            string.Join("   •   ", _session.Transport.Stairs.OrderBy(s => s.LowerFloor).ThenBy(s => s.X).Select(s =>
                $"{(s.Direction == 0 ? "Stairs" : "Escalator")} {s.LowerFloor} {(s.Direction == 0 ? "↔" : s.Direction > 0 ? "→" : "←")} {s.LowerFloor + 1}, bay {s.X}"));
        var selected = banks.GetValueOrDefault(SelectedBankId);
        _disable.Disabled = selected == null || selected.IsOutOfService;
        _restore.Disabled = selected == null || !selected.IsOutOfService;
        var chosen = _session.Transport.Cars.FirstOrDefault(c => c.BankId == SelectedBankId && c.CarId == SelectedCarId);
        var fleet = _session.Transport.Cars.Where(c => c.BankId == SelectedBankId).ToArray();
        var hallAssignments = _session.Transport.Journeys.Where(j => j.BankId == SelectedBankId
            && (j.State == JourneyState.Waiting || j.State == JourneyState.Walking && j.CarId.HasValue)).ToArray();
        _update.Disabled = selected == null || fleet.Any(c => c.PassengerCount > 0 || c.State == CarState.Traveling);
        _remove.Disabled = _update.Disabled || hallAssignments.Length > 0;
        _pauseCar.Disabled = chosen == null || chosen.IsOutOfService || selected?.IsOutOfService == true;
        _resumeCar.Disabled = chosen == null || !chosen.IsOutOfService || selected?.IsOutOfService == true;
        _removeCar.Disabled = chosen == null || chosen.CarId == 1 || chosen.PassengerCount > 0
            || chosen.State == CarState.Traveling || hallAssignments.Any(j => j.CarId == chosen.CarId);
    }

    private static Label Label(string text, int size = 14, string? color = null)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color != null) label.AddThemeColorOverride("font_color", new Color(color));
        return label;
    }
    private static Label Wrap(string text)
    {
        var label = Label(text);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }
    private static SpinBox Number(int min, int max, int value) => new()
    { MinValue = min, MaxValue = max, Value = value, Step = 1, Rounded = true, CustomMinimumSize = new Vector2(96, 34) };
    private static void Field(GridContainer grid, string title, Control input)
    { grid.AddChild(Label(title)); grid.AddChild(input); }
    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 36) };
        button.Pressed += action;
        return button;
    }
}
