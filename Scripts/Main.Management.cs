using Godot;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Persistence;
using VerticalDistrict.Core.Simulation;
using System.IO;

namespace VerticalDistrict.Game;

public partial class Main
{
    private SimulationRules _rules = null!;
    private LocationCatalog _locations = null!;
    private FixedStepRunner _runner = null!;
    private string _newLocationId = "tokyo", _newSiteId = "central";
    private bool _newSandbox;
    private int _exampleFloorCount = 4;
    private string _saveDirectory = "", _manualFileName = "district.json";
    private Label _rankLabel = null!, _rankCapLabel = null!, _locationLabel = null!, _clockLabel = null!, _speedLabel = null!;
    private Button _manageSelection = null!;
    private AcceptDialog _newGame = null!, _saves = null!, _facility = null!, _reportsDialog = null!, _menu = null!, _settings = null!;
    private TransportPanel _transportPanel = null!;
    private VBoxContainer _facilityBody = null!, _saveBody = null!;
    private RichTextLabel _reportText = null!;
    private OptionButton _citySelect = null!, _siteSelect = null!;
    private Label _siteNotes = null!;
    private CheckButton _sandboxCheck = null!;
    private readonly List<Window> _managementWindows = new();
    private double _hudTime, _cueCooldown;
    private long _lastAutosaveBucket, _lastNoticeTick = -1;
    private int _autosaveHours = 3;
    private AudioStreamPlayer _cuePlayer = null!;
    private double _volume = .3;
    private bool AnyManagementDialogVisible => _managementWindows.Any(w => w.Visible);
    public string Overlay { get; private set; } = "None";

    private void LoadSessionDefinitions()
    {
        _rules = SimulationRules.Load(Godot.FileAccess.GetFileAsString("res://Data/simulation.rules.json"), Catalog);
        _locations = LocationCatalog.Load(Godot.FileAccess.GetFileAsString("res://Data/locations.json"));
        _runner = new FixedStepRunner(_rules);
        _saveDirectory = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--construction-smoke") ? "res://artifacts/smoke-saves/" + Guid.NewGuid().ToString("N") : "user://saves");
    }
    private Control BuildOperationsBar()
    {
        var row = new HBoxContainer();
        _clockLabel = Text("", 15, Accent); row.AddChild(_clockLabel);
        row.AddChild(Button("Ⅱ", () => _runner.SetSpeed(0), "Space: pause/resume. Commands and saves still work while paused."));
        foreach (var speed in new[] { 1, 2, 4 }) { var value = speed; row.AddChild(Button(value + "×", () => _runner.SetSpeed(value))); }
        _speedLabel = Text("", 12, Muted); row.AddChild(_speedLabel);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(Button("People", OpenPeople, "Inspect real people, queues and onboard passengers, including off-screen journeys."));
        row.AddChild(Button("Transport", () => _transportPanel.OpenFor(Session)));
        row.AddChild(Button("Finances", () => OpenFinances(), "Operating result, cash flow, category breakdowns and scheduled bills."));
        row.AddChild(Button("Reports", OpenReports));
        var overlay = new OptionButton { CustomMinimumSize = new Vector2(140, 0) };
        foreach (var name in new[] { "None", "Access", "Traffic", "Services", "Utilities", "Condition" }) overlay.AddItem(name);
        overlay.ItemSelected += index => { Overlay = overlay.GetItemText((int)index); Canvas.QueueRedraw(); };
        overlay.TooltipText = "Overlay: room access, journeys, service cleanliness, utility capacity or condition.";
        row.AddChild(overlay);
        row.AddChild(Button("Save", SaveManual, "F6: save the current district and keep a previous-save backup."));
        row.AddChild(Button("Load", OpenSaveMenu, "F9: manual, backup and rotating autosave slots."));
        row.AddChild(Button("Menu", () => _menu.PopupCentered()));
        return Panel(row, "10232f");
    }
    private AcceptDialog Dialog(string title, Vector2I size, out VBoxContainer body)
    {
        var dialog = new AcceptDialog { Title = title, Size = size, MinSize = new Vector2I(520, 360), Exclusive = true };
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, OffsetLeft = 18, OffsetTop = 18, OffsetRight = -18, OffsetBottom = -60 };
        dialog.AddChild(scroll);
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetLeft = scroll.OffsetTop = 18; scroll.OffsetRight = -18; scroll.OffsetBottom = -60;
        body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(body);
        AddChild(dialog); _managementWindows.Add(dialog);
        return dialog;
    }
    private static void Clear(Control body)
    { foreach (var node in body.GetChildren()) { body.RemoveChild(node); node.QueueFree(); } }
    private void BuildManagementDialogs()
    {
        CreateAudio();
        _transportPanel = new TransportPanel(); AddChild(_transportPanel); _managementWindows.Add(_transportPanel);
        _transportPanel.Changed += message => { SetStatus(message); Refresh(); };
        BuildPeopleBrowser();
        BuildFinancesDialog();
        _facility = Dialog("Facility operations", new Vector2I(650, 650), out _facilityBody);
        _saves = Dialog("Saved districts", new Vector2I(620, 540), out _saveBody);
        _reportsDialog = Dialog("District reports & progression", new Vector2I(820, 700), out var reports);
        _reportText = new RichTextLabel { CustomMinimumSize = new Vector2(0, 580), FitContent = true, BbcodeEnabled = false, MouseFilter = MouseFilterEnum.Stop };
        reports.AddChild(Button("Open detailed finances", () => { _reportsDialog.Hide(); OpenFinances(); }));
        reports.AddChild(_reportText);
        _newGame = Dialog("New district — choose your location", new Vector2I(760, 760), out var newBody);
        newBody.AddChild(Wrap("No rank objective requires a subway or dock. Sites below are fictional; network eligibility is a pinned, dated snapshot.", 15));
        _citySelect = new OptionButton(); foreach (var l in _locations.Locations) _citySelect.AddItem(l.Name); newBody.AddChild(_citySelect);
        _siteSelect = new OptionButton(); newBody.AddChild(_siteSelect);
        _citySelect.ItemSelected += _ => RefreshSites();
        _siteSelect.ItemSelected += _ => RefreshSiteNotes();
        _siteNotes = Wrap("", 14, Muted); newBody.AddChild(_siteNotes);
        _sandboxCheck = new CheckButton { Text = "Sandbox: rank 7 and $25 million (location restrictions still apply)" }; newBody.AddChild(_sandboxCheck);
        newBody.AddChild(Button("Start with an operating example", () => ConfirmNewDistrict(true)));
        newBody.AddChild(Button("Start with a 20-floor operating example", () => ConfirmNewDistrict(true, 20)));
        newBody.AddChild(Button("Start with an empty foundation", () => ConfirmNewDistrict(false)));
        AddCommuteSetup(newBody);
        RefreshSites();
        _menu = Dialog("VERTICAL DISTRICT", new Vector2I(560, 500), out var menu);
        menu.AddChild(Text("A CITY, ONE FLOOR AT A TIME", 18, Accent));
        menu.AddChild(Wrap("Build, connect and operate a district. Keep queues moving, meet service needs and work toward seven ranks. This development build uses original provisional content.", 15, Muted));
        menu.AddChild(Button("Continue district", () => _menu.Hide()));
        menu.AddChild(Button("New district", () => { _menu.Hide(); OpenNewGame(); }));
        menu.AddChild(Button("Save current district", SaveManual));
        menu.AddChild(Button("Load district", () => { _menu.Hide(); OpenSaveMenu(); }));
        menu.AddChild(Button("Settings", () => { _menu.Hide(); _settings.PopupCentered(); }));
        menu.AddChild(Button("Save and quit", () => { if (SaveTo(Path.Combine(_saveDirectory, _manualFileName))) GetTree().Quit(); }, "Saves first. If saving fails, the game stays open."));
        _settings = Dialog("Settings", new Vector2I(600, 500), out var settings);
        settings.AddChild(Text("Interface & construction volume", 15));
        var volume = new HSlider { MinValue = 0, MaxValue = 1, Step = .05, Value = _volume, CustomMinimumSize = new Vector2(400, 30) };
        volume.ValueChanged += value => { _volume = value; ApplyVolume(); SaveSettings(); }; settings.AddChild(volume);
        settings.AddChild(Text("Autosave interval (game hours, 0 disables)", 15));
        var autosave = new SpinBox { MinValue = 0, MaxValue = 12, Step = 1, Value = _autosaveHours };
        autosave.ValueChanged += value => { _autosaveHours = (int)value; _lastAutosaveBucket = _autosaveHours == 0 ? 0 : Session.Tick / (_autosaveHours * 3600); SaveSettings(); }; settings.AddChild(autosave);
        settings.AddChild(Text("Interface text size", 15));
        var font = new SpinBox { MinValue = 14, MaxValue = 20, Step = 1, Value = _interfaceTextSize };
        font.ValueChanged += value => { ApplyInterfaceTextSize((int)value); SaveSettings(); }; settings.AddChild(font);
        settings.AddChild(Wrap("Shortcuts: Space pause · F6 save · F9 load · F floor · V inspect · X demolish · Esc cancel · Home ground · arrows / middle drag pan.\n\nOne real second advances one game minute at 1×. Catch-up work is bounded and retained; effective speed is shown when overloaded.", 14, Muted));
        InitializeInterfaceTextScaling();
    }
    private void RefreshSites()
    {
        _siteSelect.Clear();
        foreach (var site in _locations.Locations[_citySelect.Selected].Sites) _siteSelect.AddItem(site.Name);
        _siteSelect.Select(0); RefreshSiteNotes();
    }
    private void RefreshSiteNotes()
    {
        var location = _locations.Locations[_citySelect.Selected]; var site = location.Sites[_siteSelect.Selected];
        _siteNotes.Text = $"{location.GeographicScope}\n\nSubway network: {location.SubwayNetwork}\nSite connection: {(site.SubwayConnection ? "Available" : "Unavailable")}\nWaterfront: {(site.Waterfront ? "Yes" : "No")}\n\n{location.NetworkNotes}\nSnapshot: {_locations.SnapshotDate}";
    }
    private void OpenNewGame() => _newGame.PopupCentered();
    private void ConfirmNewDistrict(bool example, int floorCount = 4)
    {
        var location = _locations.Locations[_citySelect.Selected];
        _newLocationId = location.Id; _newSiteId = location.Sites[_siteSelect.Selected].Id; _newSandbox = _sandboxCheck.ButtonPressed;
        _exampleFloorCount = floorCount;
        _newGame.Hide(); ConfirmReset(example);
    }
    private void RefreshOperationsHud()
    {
        _clockLabel.Text = Session.ClockText;
        _rankLabel.Text = $"{World.Rank:00} / 07   {Catalog.GetRank(World.Rank).Name.ToUpperInvariant()}";
        _rankCapLabel.Text = $"{Catalog.GetRank(World.Rank).AboveGroundFloorCap} floors + 10 basements";
        _locationLabel.Text = _locations.Get(Session.LocationId).Name.ToUpperInvariant() + " / " + Session.SiteId.ToUpperInvariant();
        _speedLabel.Text = _runner.Speed == 0 ? "PAUSED" : _runner.BacklogSeconds > 60 ? $"{_runner.EffectiveSpeed:0.0}× effective · {_runner.BacklogSeconds:0}s queued" : $"{_runner.Speed}×";
    }
    private void OpenSelectedFacility()
    {
        if (SelectedBankId is { } bankId) { _transportPanel.OpenFor(Session, bankId); return; }
        if (SelectedId is not { } id || Session.OperationFor(id) is not { } op) return;
        Clear(_facilityBody); var room = World.Rooms.Single(r => r.Id == id); var definition = Catalog.Get(room.DefinitionId);
        _facility.Title = definition.Name + " — operations";
        _facilityBody.AddChild(Text(Session.OperatingWarning(id), 17, Accent));
        _facilityBody.AddChild(Wrap($"{definition.Description}\n\nCleanliness {op.Cleanliness}% · Condition {op.Condition}%\nCurrent occupants {Session.Occupancy(id)}, including arrivals/reservations {Session.ReservedCapacity(id)}\n{(op.Dirty ? "Hotel inventory is DIRTY and cannot be resold." : "")}", 14, Muted));
        var price = new SpinBox { MinValue = 0, MaxValue = 1_000_000, Step = .01, Value = op.PriceMinor / 100d, Prefix = "$" };
        _facilityBody.AddChild(Text("Price / rent / contract amount", 14)); _facilityBody.AddChild(price);
        _facilityBody.AddChild(Button("Apply price", () => ApplyOperation(Session.SetPrice(id, checked((long)Math.Round(price.Value * 100, MidpointRounding.AwayFromZero))))));
        var staff = new SpinBox { MinValue = 0, MaxValue = 20, Step = 1, Value = op.Staff };
        _facilityBody.AddChild(Text("Assigned staff (salaries charged daily)", 14)); _facilityBody.AddChild(staff);
        _facilityBody.AddChild(Button("Apply staffing", () => ApplyOperation(Session.SetStaff(id, (int)staff.Value))));
        var open = new CheckButton { Text = "Open for business", ButtonPressed = op.Open };
        open.Toggled += value => { var result = Session.SetOpen(id, value); if (!result.Success) open.SetPressedNoSignal(!value); ApplyOperation(result); };
        _facilityBody.AddChild(open);
        if (room.DefinitionId == "cinema")
        {
            var films = new OptionButton(); foreach (var name in new[] { "City After Rain · 120 min", "Rooftop Garden · 90 min", "The Night Shift · 60 min" }) films.AddItem(name);
            films.Select(op.Film); films.ItemSelected += choice => ApplyOperation(Session.SetFilm(id, (int)choice)); _facilityBody.AddChild(films);
        }
        if (room.DefinitionId == "event-hall") _facilityBody.AddChild(Button("Schedule event in 1 hour · " + Money(_rules.EventPreparationCostMinor), () => ApplyOperation(Session.ScheduleEvent(id))));
        if (room.DefinitionId == "condo") _facilityBody.AddChild(Button("Repurchase ownership · " + Money(op.CondoSaleMinor), () => ApplyOperation(Session.BuyBackCondo(id))));
        _facilityBody.AddChild(Button("Request routed maintenance", () => ApplyOperation(Session.RepairRoom(id))));
        _facilityBody.AddChild(Button("View facility transactions", () => { _facility.Hide(); OpenFinances(id); }));
        _facility.PopupCentered();
    }
    private void ApplyOperation(CommandResult result) { SetStatus(result.Message); if (result.Success) PlayCue(520); Refresh(); RefreshFinances(true); }
    private void OpenReports()
    {
        _reportsDialog.PopupCentered(); RefreshReports();
    }
    private void RefreshReports()
    {
        if (_reportsDialog == null || !_reportsDialog.Visible) return;
        var metrics = Session.Transport.Metrics;
        var lines = new List<string> { Session.ClockText, $"Cash {Money(World.CashMinor)}  ·  Today operating result {Money(Session.TodayProfitMinor)}",
            $"People {Session.Population}  ·  Peak {Session.PeakPopulation}  ·  Satisfaction {Session.Satisfaction}%  ·  Cleanliness {Session.Cleanliness}%",
            $"Elevators: {metrics.Waiting} waiting, {metrics.Riding} riding; average wait {metrics.AverageWaitTicks:0}s, p95 {metrics.P95WaitTicks}s; utilization {metrics.Utilization:P0}",
            $"Abandoned transport trips {metrics.AbandonedTrips}; completed journeys {Session.CompletedTrips}", "", "NEXT RANK" };
        lines.AddRange(World.Rank == 7 ? new[] { "Seven-Star Destination — continued play enabled." } : Session.UnmetRankRequirements());
        lines.AddRange(new[] { "", "RECENT DAILY RESULTS" });
        if (Session.Reports.Count == 0) lines.Add("The first daily report is available after midnight.");
        lines.AddRange(Session.Reports.TakeLast(8).Reverse().Select(r => $"Day {r.Day}: operating revenue {Money(r.RevenueMinor)}, operating expenses {Money(r.ExpensesMinor)}, operating result {Money(r.ProfitMinor)}, arrivals {r.Arrivals}, departures {r.Departures}"));
        lines.AddRange(new[] { "", "WARNINGS & NOTICES" }); lines.AddRange(Session.Notices.TakeLast(12).Reverse().Select(n => n.Kind + ": " + n.Text));
        lines.AddRange(new[] { "", "Latest ledger entries (elapsed game seconds; imported construction retains command ordinals)" });
        lines.AddRange(World.Ledger.TakeLast(20).Reverse().Select(l => $"{(World.UsesSimulationTimestamp(l) ? l.TimestampTicks + "s" : "command #" + l.TimestampTicks)} [{l.Category}] {Money(l.AmountMinor)} — {l.Description}"));
        _reportText.Text = string.Join("\n\n", lines);
    }
    private bool SaveTo(string path)
    {
        try
        {
            Directory.CreateDirectory(_saveDirectory);
            var json = Session.Serialize();
            SaveFileStore.Save(path, json, value => { GameSession.Deserialize(Catalog, _rules, _locations, value); return true; });
            SetStatus("Saved: " + Path.GetFileName(path) + ". Previous save retained as .bak."); PlayCue(880); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { SetStatus("SAVE FAILED; previous files preserved: " + ex.Message); return false; }
    }
    private void SaveManual() => SaveTo(Path.Combine(_saveDirectory, _manualFileName));
    private void LoadFrom(string path)
    {
        try
        {
            var candidate = GameSession.Deserialize(Catalog, _rules, _locations, SaveFileStore.Load(path));
            Session = candidate; _runner.Reset(); _runner.SetSpeed(0); ClearEntitySelection();
            _newLocationId = Session.LocationId; _newSiteId = Session.SiteId; _newSandbox = Session.Sandbox;
            _lastAutosaveBucket = _autosaveHours == 0 ? 0 : Session.Tick / (_autosaveHours * 3600);
            // Recovery saves use a separate file; a corrupted original is never silently replaced.
            _manualFileName = path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ? "recovered.json" : "district.json";
            foreach (var window in _managementWindows) window.Hide();
            Refresh(); Canvas.ResetView(); SetStatus("Loaded " + Path.GetFileName(path) + ". Paused at " + Session.ClockText + ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException)
        { SetStatus("LOAD FAILED; current district unchanged: " + ex.Message); }
    }
    private void OpenSaveMenu()
    {
        Clear(_saveBody);
        _saveBody.AddChild(Wrap("Loading replaces the current district only after validation. Save current changes first. Backup recovery writes future manual saves to recovered.json.", 14, Muted));
        _saveBody.AddChild(Button("Save current district", SaveManual));
        foreach (var name in new[] { "district.json", "district.json.bak", "recovered.json", "district.autosave-1.json", "district.autosave-2.json", "district.autosave-3.json" })
        {
            var path = Path.Combine(_saveDirectory, name); var exists = System.IO.File.Exists(path);
            var button = Button(name + (exists ? "  ·  " + System.IO.File.GetLastWriteTime(path).ToString("g") : "  ·  empty"), () => LoadFrom(path)); button.Disabled = !exists; _saveBody.AddChild(button);
        }
        _saveBody.AddChild(Wrap("Storage: " + _saveDirectory, 12, Muted)); _saves.PopupCentered();
    }
    public override void _Process(double delta)
    {
        if (!_initialized) return;
        _cueCooldown = Math.Max(0, _cueCooldown - delta);
        if (!_menu.Visible && !_newGame.Visible && !_saves.Visible && !_reset.Visible && !_settings.Visible)
            _runner.Advance(delta, Session.Step);
        _hudTime += delta;
        if (_hudTime >= .25)
        {
            _hudTime = 0; Refresh();
            if (Session.Notices.LastOrDefault() is { } notice && notice.Tick > _lastNoticeTick)
            { _lastNoticeTick = notice.Tick; if (notice.Kind is "Warning" or "Promotion" or "Victory") { SetStatus(notice.Text); PlayCue(notice.Kind == "Warning" ? 240 : 960); } }
        }
        Canvas.QueueRedraw();
        if (_autosaveHours > 0 && !OS.GetCmdlineUserArgs().Contains("--construction-smoke"))
        {
            var bucket = Session.Tick / (_autosaveHours * 3600);
            if (bucket > _lastAutosaveBucket) { _lastAutosaveBucket = bucket; SaveTo(Path.Combine(_saveDirectory, $"district.autosave-{bucket % 3 + 1}.json")); }
        }
    }
    private void CreateAudio()
    {
        if (AudioServer.GetBusIndex("Interface") == -1) { AudioServer.AddBus(); AudioServer.SetBusName(AudioServer.BusCount - 1, "Interface"); }
        _cuePlayer = new AudioStreamPlayer { Bus = "Interface" }; AddChild(_cuePlayer);
        var config = new ConfigFile();
        if (!OS.GetCmdlineUserArgs().Contains("--construction-smoke") && config.Load("user://settings.cfg") == Error.Ok)
        { _volume = Math.Clamp(config.GetValue("audio", "volume", .3).AsDouble(), 0, 1); _autosaveHours = Math.Clamp(config.GetValue("save", "hours", 3).AsInt32(), 0, 12); _interfaceTextSize = Math.Clamp(config.GetValue("ui", "text_size", 15).AsInt32(), 14, 20); }
        ApplyVolume();
    }
    private void ApplyVolume()
    { var bus = AudioServer.GetBusIndex("Interface"); if (bus >= 0) { AudioServer.SetBusMute(bus, _volume <= 0); AudioServer.SetBusVolumeDb(bus, (float)(20 * Math.Log10(Math.Max(.001, _volume)))); } }
    private void SaveSettings()
    {
        if (OS.GetCmdlineUserArgs().Contains("--construction-smoke")) return;
        var config = new ConfigFile(); config.SetValue("audio", "volume", _volume); config.SetValue("save", "hours", _autosaveHours); config.SetValue("ui", "text_size", _interfaceTextSize);
        if (config.Save("user://settings.cfg") != Error.Ok) SetStatus("Settings could not be saved. Current session settings still apply.");
    }
    private void PlayCue(int frequency)
    {
        if (_cuePlayer == null || _volume <= 0 || _cueCooldown > 0 || OS.GetCmdlineUserArgs().Contains("--construction-smoke")) return;
        const int samples = 3307; var data = new byte[samples * 2];
        for (var i = 0; i < samples; i++) { var sample = (short)(Math.Sin(i * Math.PI * 2 * frequency / 22050) * 6500 * Math.Pow(1d - (double)i / samples, 2)); data[i * 2] = (byte)sample; data[i * 2 + 1] = (byte)(sample >> 8); }
        _cuePlayer.Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 22050, Data = data }; _cuePlayer.Play(); _cueCooldown = .15;
    }
}
