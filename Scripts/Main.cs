using Godot;
using VerticalDistrict.Core;
using System.Globalization;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Game;

/// <summary>Godot adapter. All construction mutations are validated core commands.</summary>
public partial class Main : Control
{
    public GameSession Session { get; private set; } = null!;
    public ConstructionWorld World => Session.World;
    public ContentCatalog Catalog { get; private set; } = null!;
    public string ActiveTool { get; private set; } = "select";
    public string ActiveDefinition { get; private set; } = "office";
    public long? SelectedId { get; private set; }
    public TowerCanvas Canvas { get; private set; } = null!;
    public bool BlocksWorldInput => !_initialized || _reset?.Visible == true || AnyManagementDialogVisible;
    public void ReleaseSearchFocus() => _search.ReleaseFocus();
    private Label _funds = null!, _summary = null!, _inspector = null!, _status = null!, _toolLabel = null!;
    private Label _ledger = null!;
    private Button _demolishSelection = null!;
    private LineEdit _search = null!;
    private VBoxContainer _roomList = null!;
    private readonly Dictionary<string, Button> _toolButtons = new();
    private ConfirmationDialog _reset = null!;
    private bool _resetExample;
    private bool _initialized;
    private static readonly Color Ink = new("dce8e9"), Muted = new("8dabb7"), Accent = new("8ed9bd");

    public override void _Ready()
    {
        try
        {
            Catalog = ContentCatalog.Load(Godot.FileAccess.GetFileAsString("res://Data/construction.catalog.json"));
            LoadSessionDefinitions();
            Session = new GameSession(Catalog, _rules, _locations);
            CreateTheme();
            BuildInterface();
            BuildManagementDialogs();
            StartSite(true);
            _initialized = true;
            if (OS.GetCmdlineUserArgs().Contains("--construction-smoke"))
                Callable.From(RunSmoke).CallDeferred();
            else Callable.From(() => _menu.PopupCentered()).CallDeferred();
        }
        catch (Exception ex)
        {
            GD.PushError($"Vertical District startup failed: {ex}");
            AddChild(new Label { Text = "Unable to open the construction catalogue.\n" + ex.Message, Position = new Vector2(30, 30) });
            if (OS.GetCmdlineUserArgs().Contains("--construction-smoke")) GetTree().Quit(1);
        }
    }

    public static string Money(long value) => "$" + (value / 100m).ToString("N0", CultureInfo.InvariantCulture);
    private void CreateTheme()
    {
        Theme = new Theme { DefaultFontSize = 15 };
        Theme.SetColor("font_color", "Label", Ink);
        Theme.SetColor("font_color", "Button", Ink);
        Theme.SetColor("font_hover_color", "Button", Colors.White);
        Theme.SetColor("font_pressed_color", "Button", Accent);
        Theme.SetColor("font_disabled_color", "Button", Muted.Darkened(.25f));
        Theme.SetStylebox("normal", "Button", Box("1a2e3c", "344d59", 6));
        Theme.SetStylebox("hover", "Button", Box("294351", "80b6b0", 6));
        Theme.SetStylebox("pressed", "Button", Box("2e514e", "8ed9bd", 6));
        Theme.SetStylebox("disabled", "Button", Box("14232e", "253946", 6));
        Theme.SetStylebox("focus", "Button", Box("223c48", "8ed9bd", 6));
        Theme.SetStylebox("normal", "LineEdit", Box("112431", "344d59", 5));
        Theme.SetColor("font_color", "LineEdit", Ink);
        Theme.SetColor("font_placeholder_color", "LineEdit", Muted);
        Theme.SetConstant("separation", "VBoxContainer", 10);
        Theme.SetConstant("separation", "HBoxContainer", 10);
    }

    private static StyleBoxFlat Box(string color, string border, int radius = 0)
    {
        var s = new StyleBoxFlat { BgColor = new Color(color), BorderColor = new Color(border),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius, ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 12, ContentMarginBottom = 12 };
        return s;
    }
    private static Label Text(string text, int size = 15, Color? color = null)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) label.AddThemeColorOverride("font_color", color.Value);
        return label;
    }
    private static Label Wrap(string text, int size = 14, Color? color = null)
    {
        var label = Text(text, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }
    private Button Button(string text, Action action, string hint = "")
    {
        var button = new Button { Text = text, TooltipText = hint, MouseDefaultCursorShape = CursorShape.PointingHand,
            FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38) };
        button.Pressed += action;
        return button;
    }
    private static PanelContainer Panel(Control child, string background = "112330")
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(background, "2b414e"));
        panel.AddChild(child);
        return panel;
    }

    private void BuildInterface()
    {
        var background = new ColorRect { Color = new Color("0b1721"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var page = new VBoxContainer();
        AddChild(page);
        page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        page.AddThemeConstantOverride("separation", 0);

        var header = new HBoxContainer();
        var brand = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        brand.AddThemeConstantOverride("separation", 2);
        brand.AddChild(Text("VERTICAL  DISTRICT", 26, Accent));
        brand.AddChild(Text("A CITY, ONE FLOOR AT A TIME", 11, Muted));
        header.AddChild(brand);
        var numbers = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0) };
        numbers.AddThemeConstantOverride("separation", 0);
        numbers.AddChild(Text("DISTRICT FUNDS", 11, Muted));
        _funds = Text("", 25, Accent);
        numbers.AddChild(_funds);
        header.AddChild(numbers);
        var rank = new VBoxContainer { CustomMinimumSize = new Vector2(175, 0) };
        rank.AddThemeConstantOverride("separation", 2);
        _rankLabel = Text("", 13);
        _rankCapLabel = Text("", 12, Muted);
        rank.AddChild(_rankLabel);
        rank.AddChild(_rankCapLabel);
        header.AddChild(rank);
        header.AddChild(Button("New district", OpenNewGame, "Choose a city and fictional building site."));
        header.AddChild(Button("Example tower", () => ConfirmReset(true), "Every starter room is purchased through normal construction commands."));
        page.AddChild(Panel(header, "101f2a"));

        var strip = new HBoxContainer();
        strip.AddChild(Text("DISTRICT OPERATIONS", 12, Accent));
        strip.AddChild(Text("/", 12, Muted));
        _locationLabel = Text("", 12, Muted);
        strip.AddChild(_locationLabel);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        strip.AddChild(spacer);
        _summary = Text("", 12, Muted);
        strip.AddChild(_summary);
        page.AddChild(Panel(strip, "142a37"));
        page.AddChild(BuildOperationsBar());

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 0);
        page.AddChild(body);
        var library = new VBoxContainer();
        var left = Panel(library);
        left.CustomMinimumSize = new Vector2(244, 0);
        body.AddChild(left);
        library.AddChild(Text("BUILD YOUR DISTRICT", 16));
        library.AddChild(Text("01   STRUCTURE", 11, Muted));
        AddTool(library, "floor", "F   Floor slab  ·  " + Money(World.FloorCostMinor), "Add a full-width slab adjacent to an existing floor.");
        library.AddChild(Text("02   FACILITIES", 11, Muted));
        _search = new LineEdit { PlaceholderText = "Search rooms…", ClearButtonEnabled = true };
        _search.TextChanged += _ => RefreshRoomList();
        library.AddChild(_search);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _roomList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_roomList);
        library.AddChild(scroll);
        RefreshRoomList();
        library.AddChild(Text("03   SITE TOOLS", 11, Muted));
        AddTool(library, "select", "V   Inspect", "Select a room or slab to inspect it.");
        AddTool(library, "demolish", "X   Demolish", "Remove rooms for a 50% salvage refund. Empty outside slabs have no refund.");
        library.AddChild(Wrap("Build a lobby and connect upper floors. Keep services staffed. F6 saves; F9 opens saved districts.", 12, Muted));

        var center = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        center.AddThemeConstantOverride("separation", 0);
        body.AddChild(center);
        var viewTools = new HBoxContainer();
        _toolLabel = Text("INSPECT", 13, Accent);
        _toolLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        viewTools.AddChild(_toolLabel);
        viewTools.AddChild(Button("−", () => Canvas.ChangeZoom(.8f)));
        viewTools.AddChild(Button("+", () => Canvas.ChangeZoom(1.25f)));
        viewTools.AddChild(Button("Ground", () => Canvas.ResetView(), "Home: return to the ground floor."));
        center.AddChild(Panel(viewTools, "142a37"));
        Canvas = new TowerCanvas { Name = "TowerCanvas", OwnerGame = this, SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true, MouseFilter = MouseFilterEnum.Stop };
        center.AddChild(Canvas);

        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var detailScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        detailScroll.AddChild(details);
        var right = Panel(detailScroll);
        right.CustomMinimumSize = new Vector2(250, 0);
        body.AddChild(right);
        details.AddChild(Text("SITE INSPECTOR", 16));
        _inspector = Wrap("", 15);
        _inspector.CustomMinimumSize = new Vector2(220, 190);
        details.AddChild(_inspector);
        _demolishSelection = Button("Salvage selected room", DemolishSelection);
        details.AddChild(_demolishSelection);
        _manageSelection = Button("Manage selected facility", OpenSelectedFacility);
        details.AddChild(_manageSelection);
        _locateSelection = Button("Locate selection", LocateSelection);
        details.AddChild(_locateSelection);
        details.AddChild(new HSeparator());
        details.AddChild(Text("YOUR FIRST FLOORS", 12, Accent));
        details.AddChild(Wrap("1. Extend floors and reserve clear shaft bays.\n\n2. Use Transport to connect them to a ground lobby.\n\n3. Staff a service depot. Monitor queues, cleanliness and operating profit.", 14, Muted));
        details.AddChild(new HSeparator());
        details.AddChild(Text("RECENT TRANSACTIONS", 12, Accent));
        _ledger = Wrap("", 13, Muted);
        _ledger.SizeFlagsVertical = SizeFlags.ExpandFill;
        details.AddChild(_ledger);
        details.AddChild(Wrap("DEVELOPMENT BUILD  /  0.2\nOriginal provisional balance and temporary art. Full catalogue remains unverified.", 12, Muted));

        _status = Text("", 14);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        page.AddChild(Panel(_status, "142a37"));
        var shortcuts = Text("  MIDDLE DRAG / ARROWS  Pan     WHEEL  Zoom     HOME  Ground     F  Floor     V  Inspect     X  Demolish     ESC / RIGHT CLICK  Cancel", 12, Muted);
        shortcuts.CustomMinimumSize = new Vector2(0, 32);
        page.AddChild(shortcuts);
        _reset = new ConfirmationDialog { Title = "Start a new district?", DialogText = "Unsaved changes in the current district will be discarded. Use Save before starting over.", MinSize = new Vector2I(440, 140) };
        _reset.Confirmed += () => { if (_resetCommute) StartOfficeCommute(); else StartSite(_resetExample); };
        AddChild(_reset);
    }

    private void AddTool(VBoxContainer target, string id, string caption, string hint)
    {
        var b = Button(caption, () => SetTool(id), hint);
        b.Alignment = HorizontalAlignment.Left;
        b.ToggleMode = true;
        _toolButtons[id] = b;
        target.AddChild(b);
    }
    private void RefreshRoomList()
    {
        foreach (var child in _roomList.GetChildren()) { _roomList.RemoveChild(child); child.QueueFree(); }
        foreach (var d in Catalog.Definitions.Where(d => string.IsNullOrWhiteSpace(_search.Text) || (d.Name + d.Category).Contains(_search.Text, StringComparison.OrdinalIgnoreCase)))
        {
            var b = Button($"{d.Name}\n{d.Width} × {d.Height}  ·  {Money(d.CostMinor)}", () => SetTool("room", d.Id), d.Description);
            b.Alignment = HorizontalAlignment.Left;
            b.CustomMinimumSize = new Vector2(210, 62);
            b.AddThemeFontSizeOverride("font_size", 14);
            b.ToggleMode = true;
            b.ButtonPressed = ActiveTool == "room" && ActiveDefinition == d.Id;
            b.Name = d.Id;
            _roomList.AddChild(b);
        }
        if (_roomList.GetChildCount() == 0) _roomList.AddChild(Wrap("No matching room shells.", 14, Muted));
    }
    public void SetTool(string tool, string? definition = null)
    {
        _search.ReleaseFocus();
        ActiveTool = tool;
        if (definition != null) ActiveDefinition = definition;
        foreach (var entry in _toolButtons) entry.Value.SetPressedNoSignal(entry.Key == tool);
        foreach (var child in _roomList.GetChildren()) if (child is Button b) b.SetPressedNoSignal(tool == "room" && b.Name == ActiveDefinition);
        _toolLabel.Text = tool == "room" ? "BUILD  /  " + Catalog.Get(ActiveDefinition).Name.ToUpperInvariant() : tool.ToUpperInvariant();
        SetStatus(tool switch { "floor" => "Choose a floor adjacent to the tower. Cost: " + Money(World.FloorCostMinor),
            "room" => "Move over a floor to preview placement. Left click to build; right click to cancel.",
            "demolish" => "Click a room to salvage it, or an empty outermost floor to remove its slab.",
            _ => "Select a room, person or elevator to inspect it. People lists riders and off-screen workers. Space pauses time." });
        Canvas.QueueRedraw();
    }
    private void ConfirmReset(bool example) { _resetCommute = false; _resetExample = example; _reset.PopupCentered(); }
    private void StartSite(bool example)
    {
        Session = example
            ? OperatingExampleScenario.Create(Catalog, _rules, _locations, _newLocationId, _newSiteId, _newSandbox, _exampleFloorCount)
            : new GameSession(Catalog, _rules, _locations, _newLocationId, _newSiteId, _newSandbox);
        ResetSessionView();
        SetStatus(example ? "District open. Workers arrive at 08:00; hotels check in after 14:00. All starter construction was paid from your budget." : "A new foundation. Build a lobby on G, then add floors and a transport connection.");
    }
    public RoomInstance? RoomAt(int x, int floor) => World.Rooms.FirstOrDefault(r => { var d = Catalog.Get(r.DefinitionId); return x >= r.X && x < r.X + d.Width && floor >= r.Floor && floor < r.Floor + d.Height; });
    public CommandResult PreviewAt(int x, int floor)
    {
        if (x < 0 || x >= ConstructionWorld.Width) return new CommandResult(false, "Outside the 32-bay building boundary.");
        return ActiveTool switch
        {
            "floor" => World.ValidateFloor(floor),
            "room" => Session.ValidateRoom(ActiveDefinition, x, floor),
            "demolish" => RoomAt(x, floor) is { } r ? Session.ValidateDemolishRoom(r.Id) : Session.ValidateDemolishFloor(floor),
            _ => new CommandResult(true, "Click to inspect this bay.")
        };
    }
    public void ClickAt(int x, int floor)
    {
        _search.ReleaseFocus();
        if (ActiveTool == "select")
        {
            ClearEntitySelection();
            SelectedId = RoomAt(x, floor)?.Id;
            Refresh();
            if (SelectedId == null) SetStatus(World.Floors.Contains(floor) ? $"Floor {FloorName(floor)} · structural slab · {ConstructionWorld.Width} bays. Choose a room to build here." : "Unbuilt space. Choose Floor slab to extend your tower.");
            return;
        }
        var valid = PreviewAt(x, floor);
        if (!valid.Success) { SetStatus("CANNOT BUILD: " + valid.Message); return; }
        var result = ActiveTool switch
        {
            "floor" => World.BuildFloor(floor),
            "room" => Session.BuildRoom(ActiveDefinition, x, floor),
            "demolish" => RoomAt(x, floor) is { } room ? Session.DemolishRoom(room.Id) : Session.DemolishFloor(floor),
            _ => valid
        };
        if (result.Success && ActiveTool == "room") { ClearEntitySelection(); SelectedId = result.EntityId; }
        Refresh();
        if (result.Success) PlayCue(660);
        SetStatus((result.Success ? "DONE: " : "NOT CHANGED: ") + result.Message);
    }
    private void DemolishSelection()
    {
        if (SelectedId is not { } id) return;
        var result = Session.DemolishRoom(id);
        if (result.Success) SelectedId = null;
        Refresh();
        SetStatus(result.Message);
    }
    public void SetStatus(string value) => _status.Text = value;
    public static string FloorName(int floor) => floor == 0 ? "G" : floor < 0 ? "B" + -floor : floor.ToString("00");
    private void Refresh()
    {
        _funds.Text = Money(World.CashMinor);
        _summary.Text = $"{World.Floors.Count} FLOORS   /   {World.Rooms.Count} FACILITIES   /   {Session.Population} PEOPLE";
        RefreshOperationsHud();
        var room = World.Rooms.FirstOrDefault(r => r.Id == SelectedId);
        _demolishSelection.Disabled = room == null;
        _manageSelection.Disabled = room == null;
        _manageSelection.Text = "Manage selected facility";
        var inspectingEntity = RefreshEntityInspector();
        if (!inspectingEntity && room != null)
        {
            var d = Catalog.Get(room.DefinitionId);
            var op = Session.OperationFor(room.Id);
            _inspector.Text = $"{d.Name}\n{Session.OperatingWarning(room.Id)}\n\nFloor {FloorName(room.Floor)} · bay {room.X + 1}\nOccupancy {Session.Occupancy(room.Id)}/{_rules.For(d.Id)?.Capacity}\nStaff {op?.Staff} · condition {op?.Condition}%\nCleanliness {op?.Cleanliness}%\nBusiness cash receipts {Money(op?.GrossRevenueMinor ?? 0)}\nBusiness cash payments {Money(op?.CostsMinor ?? 0)}\n\nConstruction {Money(d.CostMinor)}\nSalvage {Money(d.CostMinor / 2)}";
        }
        else if (!inspectingEntity)
        {
            SelectedId = null;
            _inspector.Text = "Select a room, person or elevator car.\n\nPeople lists everyone, including riders and people off screen.\n\nGround is G (index 0).\nBasements descend B1–B10.";
        }
        // Ledger entries remain owned by the core; presentation formats their public record fields.
        _ledger.Text = string.Join("\n\n", World.Ledger.TakeLast(4).Reverse().Select(e => $"{e.Description}\n{(e.AmountMinor < 0 ? "" : "+")}{FinanceMoney(e.AmountMinor)}"));
        RefreshPeopleBrowser();
        RefreshFinances();
        RefreshReports();
        Canvas.QueueRedraw();
    }
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!_initialized) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || _search.HasFocus() || BlocksWorldInput) return;
        switch (key.Keycode)
        {
            case Key.F: SetTool("floor"); break;
            case Key.V: case Key.Escape: SetTool("select"); break;
            case Key.X: SetTool("demolish"); break;
            case Key.Home: Canvas.ResetView(); break;
            case Key.Pageup: Canvas.Pan(new Vector2(0, 64 * Canvas.Zoom)); break;
            case Key.Pagedown: Canvas.Pan(new Vector2(0, -64 * Canvas.Zoom)); break;
            case Key.Space: _runner.SetSpeed(_runner.Speed == 0 ? 1 : 0); break;
            case Key.F6: SaveManual(); break;
            case Key.F9: OpenSaveMenu(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private async void RunSmoke()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            StartSite(false);
            var initial = World.CashMinor;
            SetTool("floor");
            ClickAt(0, 1);
            if (!World.Floors.Contains(1) || World.CashMinor != initial - World.FloorCostMinor) throw new Exception("Slab command/UI integration failed.");
            SetTool("room", "office");
            var buildPoint = Canvas.GetGlobalRect().Position + Canvas.CellCenter(0, 1);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = buildPoint }, true);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = buildPoint }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (World.Rooms.Count != 1) throw new Exception("Canvas input did not construct room.");
            var after = World.CashMinor;
            ClickAt(0, 1);
            if (World.Rooms.Count != 1 || World.CashMinor != after) throw new Exception("Invalid placement changed session.");
            // A real viewport pointer event over the library must never reach construction input.
            _search.GrabFocus();
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = _toolButtons["floor"].GetGlobalRect().GetCenter() }, true);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = _toolButtons["floor"].GetGlobalRect().GetCenter() }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (World.Rooms.Count != 1 || World.CashMinor != after) throw new Exception("UI click leaked into tower.");
            if (_search.HasFocus()) throw new Exception("Choosing a construction tool left keyboard focus in search.");
            SetTool("select"); ClickAt(0, 1);
            if (SelectedId == null || _demolishSelection.Disabled) throw new Exception("Room inspection failed.");
            _demolishSelection.EmitSignal(BaseButton.SignalName.Pressed);
            if (World.Rooms.Count != 0) throw new Exception("Inspector salvage did not run.");
            Canvas.ChangeZoom(1.25f); Canvas.Pan(new Vector2(20, 20)); Canvas.ResetView();
            StartSite(true);
            Session.Advance(600);
            if (Session.Population == 0 || Session.CompletedTrips == 0) throw new Exception("Operating example did not produce completed physical journeys.");
            var savedState = Session.Serialize();
            var savePath = System.IO.Path.Combine(_saveDirectory, "district.json");
            if (!SaveTo(savePath)) throw new Exception("In-game save failed.");
            Session.Advance(300);
            LoadFrom(savePath);
            if (Session.Serialize() != savedState) throw new Exception("In-game load changed authoritative state.");
            _transportPanel.OpenFor(Session);
            if (!_transportPanel.Visible || !BlocksWorldInput) throw new Exception("Transport dialog did not block world input.");
            _transportPanel.Hide();
            OpenReports(); _reportsDialog.Hide();
            VerifyInterfaceTextScaling();
            await VerifyOfficeCommuteInspection();
            await VerifyFinanceInspection();
            GD.Print("CONSTRUCTION_SMOKE_PASS: scene, catalogue, viewport input, atomic construction, selection, salvage, UI isolation, camera, office journeys, save/load, transport controls, reports, text scaling, commute scenario, person/car selection, rider inspection, paused inspection invariance, finance overview, transactions, scheduled bills, midnight save continuity.");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError("CONSTRUCTION_SMOKE_FAIL: " + ex); GetTree().Quit(1); }
    }
}
