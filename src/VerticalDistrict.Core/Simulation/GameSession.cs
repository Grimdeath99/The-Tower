using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

/// <summary>Owns the stable simulation boundary; domain updates run in a fixed deterministic order.</summary>
public sealed partial class GameSession
{
    private readonly SortedDictionary<long, RoomOperation> _operations = new();
    private readonly SortedDictionary<long, PersonState> _people = new();
    private readonly List<DailyReport> _reports = new();
    private readonly List<GameNotice> _notices = new();
    private long _nextPersonId = 1;
    private uint _randomState = 20260930;
    private long _todayRevenue, _todayExpenses;
    private int _todayArrivals, _todayDepartures, _todayAbandoned;
    public ConstructionWorld World { get; private set; }
    public TransportSystem Transport { get; private set; }
    public SimulationRules Rules { get; }
    public LocationCatalog Locations { get; }
    public string LocationId { get; }
    public string SiteId { get; }
    public bool Sandbox { get; }
    public long Tick { get; private set; }
    public long Day => (Tick + 28500) / 86400 + 1;
    public int Hour => (int)((Tick + 28500) / 3600 % 24);
    public int Minute => (int)((Tick + 28500) / 60 % 60);
    public int Reputation { get; private set; } = 75;
    public int CompletedTrips { get; private set; }
    public int PeakPopulation { get; private set; }
    public int Population => _people.Values.Count(p => p.Role != "Staff");
    public int Satisfaction => Reputation;
    public int Cleanliness => _operations.Count == 0 ? 100 : (int)_operations.Values.Average(r => r.Cleanliness);
    public bool Insolvent => World.CashMinor < -10_000_000;
    public IReadOnlyList<RoomOperation> Operations => _operations.Values.ToArray();
    public IReadOnlyList<PersonState> People => _people.Values.ToArray();
    public IReadOnlyList<DailyReport> Reports => _reports.AsReadOnly();
    public IReadOnlyList<GameNotice> Notices => _notices.AsReadOnly();
    public long TodayProfitMinor => _todayRevenue - _todayExpenses;
    public string ClockText => $"Day {Day}  {Hour:00}:{Minute:00}";

    public GameSession(ContentCatalog catalog, SimulationRules rules, LocationCatalog locations,
        string locationId = "tokyo", string siteId = "central", bool sandbox = false)
    {
        locations.Get(locationId).GetSite(siteId);
        Rules = rules; Locations = locations; LocationId = locationId; SiteId = siteId; Sandbox = sandbox;
        World = new ConstructionWorld(catalog, sandbox ? 2_500_000_000 : 250_000_000, sandbox ? 7 : 1);
        World.SetSimulationClock(Tick);
        Transport = new TransportSystem(World);
        Notice("Welcome. Build a ground lobby, connect upper floors, and keep facilities staffed and clean.", "Tutorial");
    }
    public RoomOperation? OperationFor(long id) => _operations.GetValueOrDefault(id);
    public int Occupancy(long roomId) => _people.Values.Count(p => p.RoomId == roomId && p.Role != "Staff" && p.Activity == PersonActivity.Visiting);
    public int ReservedCapacity(long roomId) => _people.Values.Count(p => p.RoomId == roomId && p.Role != "Staff" && p.Activity is not (PersonActivity.Leaving or PersonActivity.Stranded));
    private RoomInstance? Room(long id) => World.Rooms.FirstOrDefault(r => r.Id == id);
    private RoomInstance? Entrance => World.Rooms.FirstOrDefault(r => r.DefinitionId == "lobby" && r.Floor == 0 && _operations.GetValueOrDefault(r.Id)?.Open == true);
    public bool IsAccessible(long roomId, bool service = false)
    {
        var room = Room(roomId); var entrance = Entrance;
        return room != null && entrance != null && Transport.CanReach(entrance.Floor, entrance.X, room.Floor, room.X, service);
    }
    public bool HasUtilities(long roomId)
    {
        // A provisional municipal connection supports the first 16 rooms; staffed plants extend capacity.
        var capacity = 16 + World.Rooms.Where(r => r.DefinitionId == "utility-room" && IsAccessible(r.Id)
            && _operations.TryGetValue(r.Id, out var op) && op.Open && op.Staff > 0).Sum(r => Rules.For(r.DefinitionId)!.Capacity);
        return World.Rooms.OrderBy(r => r.Id).Take(capacity).Any(r => r.Id == roomId);
    }
    public string OperatingWarning(long id)
    {
        var op = OperationFor(id); var room = Room(id);
        if (op == null || room == null) return "Not commissioned";
        var rule = Rules.For(room.DefinitionId)!;
        if (!op.Open) return "Closed by manager";
        if (!IsAccessible(id)) return "No route from a ground lobby";
        if (!HasUtilities(id)) return "Utility capacity exceeded";
        if (op.Staff < rule.Staff) return "Understaffed";
        if (op.Condition < 30) return "Maintenance required";
        if (op.Cleanliness < 35 || op.Dirty) return "Cleaning required";
        if (Hour < rule.OpenHour || Hour >= rule.CloseHour) return "Outside opening hours";
        return "Operating";
    }
    private bool Ready(RoomInstance room, RoomOperation op, BusinessRule rule, bool hours = true)
        => op.Open && op.Staff >= rule.Staff && op.Condition >= 30 && op.Cleanliness >= 35
           && IsAccessible(room.Id) && HasUtilities(room.Id) && (!hours || Hour >= rule.OpenHour && Hour < rule.CloseHour);
    public void SynchronizeConstruction()
    {
        foreach (var room in World.Rooms)
            if (!_operations.ContainsKey(room.Id) && Rules.For(room.DefinitionId) is { } rule)
                _operations.Add(room.Id, new RoomOperation(room.Id, rule.PriceMinor, true, rule.Staff, 100, 100,
                    false, null, false, -1, -86400, false, 0, 0, 0, false, 0, 0));
        foreach (var id in _operations.Keys.Where(id => Room(id) == null).ToArray()) _operations.Remove(id);
    }
    public void Step()
    {
        Tick = checked(Tick + 1);
        World.SetSimulationClock(Tick);
        Transport.Step(Tick);
        ResolvePeople();
        if (Tick % 60 == 0)
        {
            SynchronizeConstruction();
            UpdateBusinesses();
            AssignServiceWork();
            PeakPopulation = Math.Max(PeakPopulation, Population);
        }
        if ((Tick + 28500) % 3600 == 0) HourlyCondition();
        if (Tick >= _billing.NextDueTick) CloseDay();
        if (Tick % 3600 == 0) EvaluatePromotion();
    }
    public void Advance(int seconds) { if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds)); for (var i = 0; i < seconds; i++) Step(); }
    private uint RandomValue()
    {
        var x = _randomState; x ^= x << 13; x ^= x >> 17; x ^= x << 5;
        return _randomState = x;
    }
    private void Notice(string text, string kind = "Info")
    {
        _notices.Add(new GameNotice(Tick, text, kind));
        if (_notices.Count > 60) _notices.RemoveAt(0);
    }
    private bool Post(long roomId, long amount, string category, string description)
        => PostBatch([new FinancialPosting(roomId, amount, category, description)]);
    private void SetOp(RoomOperation operation) => _operations[operation.RoomId] = operation;
}
