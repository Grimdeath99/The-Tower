using Godot;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Transport;
using VerticalDistrict.Core.Simulation;

namespace VerticalDistrict.Game;

/// <summary>Replaceable, original pixel-style presentation; contains no authoritative state.</summary>
public partial class TowerCanvas : Control
{
    public Main OwnerGame { get; set; } = null!;
    public float Zoom { get; private set; } = .9f;
    private Vector2 _pan;
    private Vector2 _hover;
    private bool _hovering;
    private bool _viewInitialized;
    private const int Bay = 24, Story = 64;
    private Vector2 Origin => new(Mathf.Round((Size.X - ConstructionWorld.Width * Bay * Zoom) / 2 + _pan.X), Mathf.Round(Size.Y * .78f + _pan.Y));
    private static Color C(string value) => new(value);
    public override void _Ready()
    {
        MouseExited += () => { _hovering = false; QueueRedraw(); };
        Resized += () => { if (!_viewInitialized && Size.X > 100 && Size.Y > 100) ResetView(); QueueRedraw(); };
        MouseDefaultCursorShape = CursorShape.Cross;
    }
    public void ResetView() { _viewInitialized = Size.X > 100 && Size.Y > 100; _pan = Vector2.Zero; Zoom = Math.Clamp((Size.X - 105) / (ConstructionWorld.Width * Bay), .45f, 1.1f); QueueRedraw(); }
    public void ChangeZoom(float factor)
    {
        Zoom = Mathf.Clamp(Zoom * factor, .35f, 2.4f);
        QueueRedraw();
    }
    public void Pan(Vector2 amount)
    {
        _pan += amount;
        _pan.X = Mathf.Clamp(_pan.X, -1000 * Zoom, 1000 * Zoom);
        _pan.Y = Mathf.Clamp(_pan.Y, -11 * Story * Zoom, 250 * Story * Zoom);
        QueueRedraw();
    }
    public Vector2 CellCenter(int x, int floor) => Origin + new Vector2((x + .5f) * Bay, -(floor + .5f) * Story) * Zoom;
    private (int X, int Floor) Cell(Vector2 position)
    {
        var world = (position - Origin) / Zoom;
        return ((int)Math.Floor(world.X / Bay), (int)Math.Floor(-world.Y / Story));
    }
    public override void _GuiInput(InputEvent @event)
    {
        if (OwnerGame.BlocksWorldInput) return;
        if (@event is InputEventMouseMotion motion)
        {
            _hover = motion.Position;
            _hovering = true;
            if ((motion.ButtonMask & MouseButtonMask.Middle) != 0) Pan(motion.Relative);
            QueueRedraw();
        }
        if (@event is not InputEventMouseButton button || !button.Pressed) return;
        OwnerGame.ReleaseSearchFocus();
        _hover = button.Position;
        _hovering = true;
        switch (button.ButtonIndex)
        {
            case MouseButton.WheelUp: ChangeZoom(1.1f); break;
            case MouseButton.WheelDown: ChangeZoom(1 / 1.1f); break;
            case MouseButton.Right: OwnerGame.SetTool("select"); break;
            case MouseButton.Left:
                if (OwnerGame.ActiveTool == "select" && TryInspectAt(button.Position)) break;
                var cell = Cell(button.Position);
                OwnerGame.ClickAt(cell.X, cell.Floor);
                break;
        }
        AcceptEvent();
        QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (GetViewport().GuiGetFocusOwner() is LineEdit || OwnerGame.BlocksWorldInput) return;
        var direction = new Vector2((Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0),
            (Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0));
        if (direction != Vector2.Zero) Pan(direction * (float)delta * 460);
    }
    private void Rect(float x, float y, float w, float h, string color) => DrawRect(new Rect2(x, y, w, h), C(color));
    private void Caption(Vector2 point, string text, int size, Color color, float width = -1) => DrawString(ThemeDB.FallbackFont, point, text, HorizontalAlignment.Left, width, size, color);
    public override void _Draw()
    {
        if (OwnerGame?.Session == null) return;
        DrawBackdrop();
        DrawSetTransform(Origin, 0, new Vector2(Zoom, Zoom));
        var left = -Origin.X / Zoom;
        var right = (Size.X - Origin.X) / Zoom;
        var soilHeight = Math.Max(0, (Size.Y - Origin.Y) / Zoom);
        if (soilHeight > 0)
        {
            Rect(left, 0, right - left, soilHeight, "172d37");
            for (var yy = 26; yy < soilHeight; yy += 25)
                for (var xx = (int)left; xx < right; xx += 42)
                    Rect(xx + (yy % 3) * 7, yy, 7, 2, "203a43");
            Rect(left, 0, right - left, 5, "738f88");
            Rect(left, 5, right - left, 8, "3f5756");
        }
        var width = ConstructionWorld.Width * Bay;
        for (var f = ConstructionWorld.MinFloor; f <= ConstructionWorld.MaxFloor; f++)
        {
            var y = -(f + 1) * Story;
            if ((y + Story) * Zoom + Origin.Y < 0 || y * Zoom + Origin.Y > Size.Y) continue;
            var built = OwnerGame.World.Floors.Contains(f);
            if (built)
            {
                Rect(0, y, width, Story, f < 0 ? "2a4249" : "253e49");
                Rect(0, y + 5, width, 2, "37505a");
                for (var x = 0; x < width; x += Bay)
                {
                    Rect(x, y + 7, 1, Story - 13, "2d4853");
                    Rect(x + 7, y + 20, 10, 1, "36535d");
                    Rect(x + 12, y + 15, 1, 11, "36535d");
                }
                Rect(0, y + Story - 7, width, 7, "89978c");
                Rect(0, y + Story - 7, width, 2, "c2c1a6");
                Rect(-5, y, 5, Story, "6e8a8c");
                Rect(width, y, 5, Story, "6e8a8c");
                Rect(-7, y, width + 14, 4, "9baba2");
            }
            else
            {
                DrawLine(new Vector2(0, y + Story), new Vector2(width, y + Story), C("29434f"), 1);
                for (var x = 0; x <= width; x += Bay) Rect(x, y + Story - 2, 2, 3, "3a5560");
            }
            Caption(new Vector2(-38, y + 35), Main.FloorName(f), 14, C(built ? "c7ded7" : "597783"));
        }
        foreach (var room in OwnerGame.World.Rooms)
        {
            var def = OwnerGame.Catalog.Get(room.DefinitionId);
            var y = -(room.Floor + def.Height) * Story;
            if ((y + def.Height * Story) * Zoom + Origin.Y < 0 || y * Zoom + Origin.Y > Size.Y) continue;
            DrawRoom(room, def);
            DrawOverlay(room, def);
        }
        DrawTransport();
        DrawPeople();
        DrawStreet(width);
        DrawPreview();
        DrawSetTransform(Vector2.Zero);
        Caption(new Vector2(22, 29), "ARCHITECTURAL CUTAWAY", 11, C("97b7c0"));
        Caption(new Vector2(22, 48), "32 BAYS  /  " + Mathf.RoundToInt(Zoom * 100) + "%", 11, C("648998"));
        if (_hovering && OwnerGame.ActiveTool != "select")
        {
            var cell = Cell(_hover);
            var result = OwnerGame.PreviewAt(cell.X, cell.Floor);
            var message = (result.Success ? "VALID" : "BLOCKED") + "  ·  " + result.Message;
            var panel = new Rect2(16, Size.Y - 70, Math.Max(200, Size.X - 32), 54);
            DrawRect(panel, C("102631"));
            DrawRect(panel, C(result.Success ? "8ed9bd" : "efa68c"), false, 1);
            Caption(panel.Position + new Vector2(12, 20), message, 12, C(result.Success ? "a5e8cd" : "ffd0bd"), panel.Size.X - 24);
            var price = OwnerGame.ActiveTool == "room" ? OwnerGame.Catalog.Get(OwnerGame.ActiveDefinition).CostMinor : OwnerGame.ActiveTool == "floor" ? OwnerGame.World.FloorCostMinor : 0;
            Caption(panel.Position + new Vector2(12, 40), $"Floor {Main.FloorName(cell.Floor)}  ·  bay {cell.X + 1}" + (price > 0 ? "  ·  " + Main.Money(price) : "  ·  right click to cancel"), 12, C("93b2ba"));
        }
    }
    private void DrawBackdrop()
    {
        for (var y = 0; y < Size.Y; y += 4)
            DrawRect(new Rect2(0, y, Size.X, 4), C("132a3b").Lerp(C("42616a"), y / Math.Max(1, Size.Y)));
        // Static decorative cityscape. It is not simulated building or population state.
        Rect(Size.X - 122, 57, 28, 28, "b3c4b0");
        Rect(Size.X - 129, 63, 14, 29, "1c3445");
        var horizon = Size.Y * .78f;
        for (var i = 0; i < 19; i++)
        {
            var x = i * 73 - 19;
            var h = 80 + (i * 47 % 180);
            var w = 40 + i * 19 % 45;
            Rect(x, horizon - h, w, h, i % 2 == 0 ? "29424e" : "2b4853");
            Rect(x + 8, horizon - h - 6, w - 16, 6, "2b4651");
            for (var wy = horizon - h + 14; wy < horizon - 12; wy += 16)
                for (var wx = x + 8; wx < x + w - 7; wx += 12)
                    Rect(wx, wy, 4, 6, (int)(wx + wy) % 7 < 3 ? "687b76" : "3a5660");
        }
    }
    private void DrawRoom(RoomInstance room, FacilityDefinition d)
    {
        var x = room.X * Bay + 2;
        var y = -(room.Floor + d.Height) * Story + 4;
        var w = d.Width * Bay - 4;
        var h = d.Height * Story - 11;
        var tint = new Color(d.ColorHex);
        DrawRect(new Rect2(x, y, w, h), tint.Darkened(.50f));
        DrawRect(new Rect2(x, y, w, 13), tint.Darkened(.16f));
        Rect(x + 3, y + 14, w - 6, 1, "b3aa89");
        // Windows and wall panels establish a consistent modular pixel scale.
        for (var xx = x + 7; xx < x + w - 18; xx += 27)
        {
            Rect(xx, y + 18, 19, 20, "284658");
            Rect(xx + 2, y + 20, 15, 15, "70999c");
            Rect(xx + 3, y + 21, 6, 13, "91b7b2");
            Rect(xx + 9, y + 20, 1, 16, "c0c8ad");
        }
        var bottom = y + h;
        switch (d.Id)
        {
            case "office":
                for (var xx = x + 9; xx < x + w - 22; xx += 29)
                {
                    Rect(xx, bottom - 16, 24, 4, "b8b29a"); Rect(xx + 2, bottom - 12, 2, 10, "605b56");
                    Rect(xx + 20, bottom - 12, 2, 10, "605b56"); Rect(xx + 7, bottom - 26, 12, 10, "243e4b");
                    Rect(xx + 9, bottom - 24, 8, 6, "83bbb3"); Rect(xx + 11, bottom - 9, 9, 7, "456478");
                }
                break;
            case "studio": case "hotel-room":
                Rect(x + 7, bottom - 19, 31, 16, "e4d2aa"); Rect(x + 7, bottom - 19, 9, 9, "ece5cf");
                Rect(x + 17, bottom - 15, 20, 10, d.Id == "studio" ? "8eae9c" : "a094ad");
                Rect(x + 5, bottom - 23, 4, 23, "735b50"); Rect(x + w - 23, bottom - 15, 16, 13, "9f8971");
                Rect(x + w - 19, bottom - 26, 8, 8, "edcf8c"); Rect(x + w - 16, bottom - 18, 2, 4, "b0a18d");
                break;
            case "cafe":
                for (var xx = x + 9; xx < x + w - 22; xx += 30)
                { Rect(xx, bottom - 16, 19, 4, "c6ad88"); Rect(xx + 8, bottom - 12, 3, 12, "80715d"); Rect(xx - 3, bottom - 9, 5, 8, "b67d69"); Rect(xx + 20, bottom - 9, 5, 8, "b67d69"); }
                break;
            case "service-room":
                for (var xx = x + 8; xx < x + w - 18; xx += 23)
                { Rect(xx, bottom - 27, 19, 25, "a4b4b0"); Rect(xx + 3, bottom - 22, 13, 4, "36576b"); Rect(xx + 5, bottom - 13, 9, 8, "718c8d"); }
                break;
            case "lobby":
                Rect(x + 13, bottom - 16, w * .45f, 14, "9a7862"); Rect(x + 11, bottom - 19, w * .45f + 4, 4, "e0c99b");
                Rect(x + 24, bottom - 27, 11, 8, "314d57");
                Rect(x + w - 42, bottom - 12, 28, 9, "c0aa85"); Rect(x + w - 43, bottom - 18, 29, 7, "76988b");
                break;
            default:
                Rect(x + 15, bottom - 15, w - 30, 10, "849e92");
                Rect(x + w / 2 - 3, y + 22, 6, h - 42, "6b8a78");
                Rect(x + w / 2 - 20, y + 30, 40, 28, "799c81");
                break;
        }
        // Small planter, wall trims and floor finish remain replaceable original placeholders.
        Rect(x + w - 8, bottom - 11, 5, 9, "a78b6c"); Rect(x + w - 10, bottom - 17, 8, 8, "79a788");
        Rect(x, bottom - 2, w, 2, "b9b49c");
        Rect(x, y, 2, h, "c0b8a0"); Rect(x + w - 2, y, 2, h, "a6a58e");
        Caption(new Vector2(x + 5, y + 10), d.Name.ToUpperInvariant(), 9, C("f1ecda"), w - 10);
        if (OwnerGame.SelectedId == room.Id) DrawRect(new Rect2(x - 1, y - 1, w + 2, h + 2), C("b1f0cc"), false, 2);
    }
    private void DrawStreet(int width)
    {
        foreach (var xx in new[] { -52, width + 38 })
        {
            Rect(xx, -32, 4, 31, "7e8c78");
            Rect(xx - 10, -44, 23, 19, "628879"); Rect(xx - 5, -53, 14, 16, "7b9f85");
            Rect(xx - 13, -34, 27, 10, "486f66");
        }
        Caption(new Vector2(6, 26), "VERTICAL DISTRICT  /  " + OwnerGame.Session.LocationId.ToUpperInvariant(), 10, C("90a9a4"));
    }
    private void DrawPreview()
    {
        if (!_hovering || OwnerGame.ActiveTool == "select") return;
        var cell = Cell(_hover);
        var validation = OwnerGame.PreviewAt(cell.X, cell.Floor);
        var x = cell.X * Bay;
        var y = -(cell.Floor + 1) * Story;
        var w = Bay;
        var h = Story;
        if (OwnerGame.ActiveTool == "floor") { x = 0; w = ConstructionWorld.Width * Bay; }
        if (OwnerGame.ActiveTool == "room")
        {
            var d = OwnerGame.Catalog.Get(OwnerGame.ActiveDefinition);
            w = d.Width * Bay; h = d.Height * Story; y = -(cell.Floor + d.Height) * Story;
        }
        if (OwnerGame.ActiveTool == "demolish" && OwnerGame.RoomAt(cell.X, cell.Floor) is { } room)
        {
            var d = OwnerGame.Catalog.Get(room.DefinitionId);
            x = room.X * Bay; y = -(room.Floor + d.Height) * Story; w = d.Width * Bay; h = d.Height * Story;
        }
        else if (OwnerGame.ActiveTool == "demolish") { x = 0; w = ConstructionWorld.Width * Bay; }
        var color = C(validation.Success ? "a5e8cd" : "efa68c");
        DrawRect(new Rect2(x, y, w, h), new Color(color, .20f));
        DrawRect(new Rect2(x, y, w, h), color, false, 2);
        Caption(new Vector2(x + 5, y + 25), validation.Success ? "+" : "×", 22, color);
    }

    private void DrawOverlay(RoomInstance room, FacilityDefinition definition)
    {
        if (OwnerGame.Overlay == "None" || OwnerGame.Overlay == "Traffic") return;
        var session = OwnerGame.Session; var op = session.OperationFor(room.Id);
        var value = OwnerGame.Overlay switch
        {
            "Access" => session.IsAccessible(room.Id) ? 100 : 0,
            "Utilities" => session.HasUtilities(room.Id) ? 100 : 0,
            "Services" => op?.Cleanliness ?? 0,
            "Condition" => op?.Condition ?? 0,
            _ => 100
        };
        var color = C("db826d").Lerp(C("76d4a9"), value / 100f);
        var area = new Rect2(room.X * Bay + 2, -(room.Floor + definition.Height) * Story + 4, definition.Width * Bay - 4, definition.Height * Story - 10);
        DrawRect(area, new Color(color, .32f));
        Caption(area.Position + new Vector2(5, 29), value == 0 ? "!" : value.ToString(), 13, Colors.White);
    }
    private void DrawTransport()
    {
        foreach (var bank in OwnerGame.Session.Transport.Banks)
        {
            var d = bank.Definition; var x = d.X * Bay;
            for (var floor = d.MinFloor; floor <= d.MaxFloor; floor++)
            {
                var y = -(floor + 1) * Story;
                if (y * Zoom + Origin.Y > Size.Y || (y + Story) * Zoom + Origin.Y < 0) continue;
                Rect(x, y + 2, Bay, Story - 3, "10252f"); Rect(x + 3, y, 2, Story, "536f77"); Rect(x + Bay - 5, y, 2, Story, "536f77");
                Rect(x + 11, y, 1, Story, "799395");
                if (d.Stops.Contains(floor)) { Rect(x + 6, y + 17, Bay - 12, 39, "46636b"); Rect(x + 12, y + 17, 1, 39, "243c47"); Rect(x + 7, y + 10, 9, 3, bank.IsOutOfService ? "e19479" : "91d8b8"); }
            }
        }
        foreach (var car in OwnerGame.Session.Transport.Cars)
        {
            var bank = OwnerGame.Session.Transport.Banks.First(b => b.Definition.Id == car.BankId).Definition;
            var y = (float)(-(car.DrawFloor + 1) * Story + 16); var x = bank.X * Bay + 4;
            if (y * Zoom + Origin.Y > Size.Y || (y + 44) * Zoom + Origin.Y < 0) continue;
            Rect(x, y, Bay - 8, 42, car.State == CarState.OutOfService ? "9f6e68" : "a7bfae");
            Rect(x + 2, y + 3, Bay - 12, 34, "486978");
            if (car.State is CarState.Opening or CarState.Unloading or CarState.Boarding) Rect(x + 5, y + 3, Bay - 18, 34, "d2ceaa");
            Caption(new Vector2(x + 3, y + 28), car.PassengerCount.ToString(), 10, C("fff1cd"));
            if (OwnerGame.SelectedBankId == car.BankId || OwnerGame.SelectedPersonId is { } personId && car.PassengerIds.Contains(personId))
                DrawRect(new Rect2(x - 3, y - 3, Bay - 2, 48), C("b1f0cc"), false, 2);
        }
        foreach (var stair in OwnerGame.Session.Transport.Stairs)
        {
            var x = stair.X * Bay; var bottom = -stair.LowerFloor * Story - 5;
            var color = stair.Direction == 0 ? C("b8b09b") : C("e0b174");
            for (var i = 0; i < 8; i++) DrawLine(new Vector2(x + i * 3, bottom - i * 8), new Vector2(x + i * 3 + 3, bottom - i * 8), color, 2);
            if (stair.Direction != 0) Caption(new Vector2(x + 4, bottom - 35), stair.Direction > 0 ? "↑" : "↓", 16, color);
        }
    }
    private void DrawPeople()
    {
        var journeys = OwnerGame.Session.Transport.Journeys.ToDictionary(j => j.PersonId);
        foreach (var visual in PersonVisuals())
        {
            var person = visual.Person; var journey = visual.Journey;
            if (journey.State == JourneyState.Riding) continue;
            var x = visual.Position.X; var y = visual.Position.Y;
            if (y * Zoom + Origin.Y < 0 || y * Zoom + Origin.Y > Size.Y || x * Zoom + Origin.X < 0 || x * Zoom + Origin.X > Size.X) continue;
            var walking = journey.State == JourneyState.Walking;
            var stride = walking && OwnerGame.Session.Tick % 2 == 0 ? 2 : 0;
            Rect(x - 2, y - 16, 4, 4, "ddbd91");
            Rect(x - 3, y - 12, 6, 7, person.Role == "Staff" ? "e3c271" : person.Role == "Guest" ? "bd9bd3" : "8cd0c2");
            Rect(x - 3 - stride, y - 5, 2, 5, "203747"); Rect(x + 1 + stride, y - 5, 2, 5, "203747");
            if (journey.State is JourneyState.Waiting or JourneyState.Unreachable or JourneyState.Abandoned)
                Caption(new Vector2(x - 2, y - 21), journey.State == JourneyState.Waiting ? "·" : "!", 11, C("edbe81"));
            if (OwnerGame.SelectedPersonId == person.Id)
            {
                DrawRect(new Rect2(x - 6, y - 20, 12, 23), C("b1f0cc"), false, 1.5f);
                Caption(new Vector2(x - 8, y - 25), "#" + person.Id, 10, C("b1f0cc"));
            }
        }
        if (OwnerGame.Overlay == "Traffic")
            foreach (var group in journeys.Values.Where(j => j.State == JourneyState.Waiting).GroupBy(j => j.Floor))
                Caption(new Vector2(ConstructionWorld.Width * Bay - 140, -group.Key * Story - 22), $"{group.Count()} WAITING", 12, C("f0c889"));
    }
}
