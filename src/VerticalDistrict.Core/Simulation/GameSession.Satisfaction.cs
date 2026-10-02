using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    private readonly SortedDictionary<long, RoomExperience> _experiences = new();
    private readonly List<ComplaintState> _complaints = new();
    private readonly List<SatisfactionSample> _satisfactionHistory = new();
    private long _nextComplaintId = 1;
    public IReadOnlyList<ComplaintState> Complaints => _complaints.ToArray();
    public IReadOnlyList<SatisfactionSample> SatisfactionHistory => _satisfactionHistory.ToArray();
    public IReadOnlyList<ComplaintState> ActiveComplaints => _complaints.Where(c => c.ResolvedTick == null).ToArray();

    private void SynchronizeSatisfaction()
    {
        foreach (var id in _operations.Keys)
            _experiences.TryAdd(id, new RoomExperience(id, Reputation, 0, 100));
        foreach (var id in _experiences.Keys.Where(id => !_operations.ContainsKey(id)).ToArray()) _experiences.Remove(id);
        _complaints.RemoveAll(c => !_operations.ContainsKey(c.RoomId));
    }

    /// <summary>Pure assessment: actual conditions plus the last completed commute and activity outcomes.</summary>
    public DemandAssessment DemandFor(long roomId)
    {
        if (Room(roomId) is not { } room || OperationFor(roomId) is not { } op || Rules.For(room.DefinitionId) is not { } rule)
            return new(0, 0, [], ["Unknown room."]);
        var experience = _experiences.GetValueOrDefault(roomId) ?? new(roomId, Reputation, 0, 100);
        var product = ProductForRoom(roomId);
        var referencePrice = product == null ? rule.PriceMinor : ProductSuggestedPrice(roomId);
        var ratio = referencePrice == 0 ? 1m : (decimal)op.PriceMinor / referencePrice;
        var value = Math.Clamp(100 - (int)Math.Min(100, Math.Max(0, ratio - 1) * 50), 0, 100);
        var accessible = IsAccessible(roomId);
        var available = op.Open && accessible && HasUtilities(roomId) && op.Staff >= rule.Staff && !op.Dirty;
        var travel = Math.Clamp(100 - (int)Math.Min(100, Math.Max(0, experience.LastTravelSeconds - Rules.Management.TravelComfortSeconds) / 6), 0, 100);
        var waiting = Transport.Journeys.Where(j => j.State == JourneyState.Waiting
            && _people.TryGetValue(j.PersonId, out var person) && person.RoomId == roomId && person.Role != "Staff")
            .Select(j => Tick - j.WaitSinceTick).DefaultIfEmpty(0).Max();
        travel = Math.Min(travel, Math.Clamp(100 - (int)Math.Min(100, Math.Max(0, waiting - Rules.Management.TravelComfortSeconds) / 6), 0, 100));
        var factors = new SatisfactionFactor[] {
            new(product == null ? "Value at asking price" : "Value at offering price", value), new("Cleanliness", op.Cleanliness), new("Condition", op.Condition),
            new("Service and access", available ? 100 : 0), new("Travel and elevator waits", travel), new("Completed activities", experience.CompletionScore)
        };
        var securityCapacity = World.Rooms.Where(r => r.DefinitionId == "security-room" && IsAccessible(r.Id)
            && _operations.TryGetValue(r.Id, out var security) && security.Open && security.Staff > 0)
            .Sum(r => Rules.For(r.DefinitionId)!.Capacity);
        var securityCovered = PeakPopulation <= 16 || securityCapacity >= PeakPopulation;
        if (!securityCovered) factors = [.. factors, new("Security coverage", 50)];
        var reasons = new List<string>();
        if (!op.Open) reasons.Add("Closed by manager.");
        if (!accessible) reasons.Add("No route from a ground lobby.");
        if (op.Staff < rule.Staff) reasons.Add("Insufficient staff for service.");
        if (!HasUtilities(roomId)) reasons.Add("Utility capacity exceeded.");
        if (op.Dirty || op.Cleanliness < 70) reasons.Add("Room is awaiting cleaning.");
        if (op.Condition < 70) reasons.Add("Room condition needs maintenance.");
        if ((op.Dirty || op.Cleanliness < 70 || op.Condition < 70) && !IsAccessible(roomId, true)) reasons.Add("Maintenance cannot reach this floor.");
        if (value < 75) reasons.Add("Price is high relative to current demand.");
        if (travel < 80) reasons.Add("Long journeys or elevator waits.");
        if (experience.CompletionScore < 75) reasons.Add("Recent activities could not be completed.");
        if (!securityCovered) reasons.Add("Security coverage is below peak demand.");
        var location = Locations.Get(LocationId).Demand;
        var modifier = rule.Model switch { "Office" => location.Office, "Home" or "Condo" => location.Residential, "Hotel" => location.Hotel, _ => location.Retail };
        // Price has a direct effect as well as its slower effect through satisfaction.
        var score = (int)Math.Clamp((100m - Math.Max(0, ratio - 1) * 45) * modifier
            * (.5m + experience.Satisfaction / 200m) * Math.Min(op.Cleanliness, op.Condition) / 100m
            * ProductDemandMultiplier(roomId) / 100m, 0, 100);
        if (!available || op.Condition < 30 || op.Cleanliness < 35) score = 0;
        return new(score, experience.Satisfaction, Array.AsReadOnly(factors), reasons.AsReadOnly());
    }

    private void RecordTravelExperience(PersonState person)
    {
        if (!_experiences.TryGetValue(person.RoomId, out var current)) return;
        var seconds = Math.Clamp(Tick - person.CreatedAt, 0, 86400);
        _experiences[person.RoomId] = current with { LastTravelSeconds = seconds };
    }

    private void RecordManagementOutcome(long roomId, bool completed)
    {
        if (_experiences.TryGetValue(roomId, out var current))
            _experiences[roomId] = current with { CompletionScore = Math.Clamp(current.CompletionScore + (completed ? 8 : -15), 0, 100) };
    }

    private void UpdateSatisfaction()
    {
        SynchronizeSatisfaction();
        foreach (var id in _experiences.Keys.ToArray())
        {
            var assessment = DemandFor(id);
            if ((Tick + 28500) % 3600 == 0)
            {
                var current = _experiences[id];
                var target = (int)assessment.Factors.Average(f => f.Score);
                var step = Math.Clamp(target - current.Satisfaction, -Rules.Management.SatisfactionStepPerHour, Rules.Management.SatisfactionStepPerHour);
                _experiences[id] = current with { Satisfaction = current.Satisfaction + step };
            }
            var causes = assessment.Reasons.ToHashSet(StringComparer.Ordinal);
            foreach (var complaint in _complaints.Where(c => c.RoomId == id && c.ResolvedTick == null).ToArray())
                if (!causes.Contains(complaint.Message)) _complaints[_complaints.IndexOf(complaint)] = complaint with { ResolvedTick = Tick };
            foreach (var reason in causes.Order(StringComparer.Ordinal))
                if (!_complaints.Any(c => c.RoomId == id && c.Message == reason && c.ResolvedTick == null))
                    _complaints.Add(new ComplaintState(_nextComplaintId++, id, ComplaintCode(reason), reason, Tick, null));
        }
        var resolved = _complaints.Where(c => c.ResolvedTick != null).OrderByDescending(c => c.ResolvedTick).Skip(Rules.Management.ComplaintHistoryLimit).Select(c => c.Id).ToHashSet();
        _complaints.RemoveAll(c => resolved.Contains(c.Id));
        if ((Tick + 28500) % 3600 == 0)
        {
            Reputation = _experiences.Count == 0 ? 75 : (int)_experiences.Values.Average(e => e.Satisfaction);
            _satisfactionHistory.Add(new(Tick, Reputation));
            if (_satisfactionHistory.Count > Rules.Management.SatisfactionHistoryHours) _satisfactionHistory.RemoveAt(0);
        }
    }

    private static string ComplaintCode(string message) => message switch {
        "Closed by manager." => "Closed", "No route from a ground lobby." => "Access",
        "Insufficient staff for service." => "Staff", "Utility capacity exceeded." => "Utilities",
        "Room is awaiting cleaning." => "Cleaning", "Room condition needs maintenance." => "Condition",
        "Maintenance cannot reach this floor." => "ServiceRoute", "Price is high relative to current demand." => "Price",
        "Long journeys or elevator waits." => "Travel", "Recent activities could not be completed." => "Completion",
        "Security coverage is below peak demand." => "Security", _ => "Unknown"
    };

    private SatisfactionSnapshot CaptureSatisfaction() => new(_nextComplaintId, _experiences.Values.ToArray(), _complaints.ToArray(), _satisfactionHistory.ToArray());
    private void RestoreSatisfaction(SatisfactionSnapshot state)
    {
        Require(state != null && state.Experiences != null && state.Complaints != null && state.History != null && state.NextComplaintId > 0, "Missing satisfaction state.");
        _experiences.Clear(); _complaints.Clear(); _satisfactionHistory.Clear();
        foreach (var item in state.Experiences)
        {
            Require(item != null && _operations.ContainsKey(item.RoomId) && !_experiences.ContainsKey(item.RoomId)
                && item.Satisfaction is >= 0 and <= 100 && item.CompletionScore is >= 0 and <= 100 && item.LastTravelSeconds is >= 0 and <= 86400, "Invalid room experience.");
            _experiences.Add(item.RoomId, item);
        }
        Require(_experiences.Count == _operations.Count, "Missing room experience.");
        var ids = new HashSet<long>(); var active = new HashSet<(long, string)>();
        foreach (var item in state.Complaints)
        {
            Require(item != null && item.Id > 0 && item.Id < state.NextComplaintId && ids.Add(item.Id) && _operations.ContainsKey(item.RoomId)
                && item.CreatedTick >= 0 && item.CreatedTick <= Tick && (item.ResolvedTick == null || item.ResolvedTick >= item.CreatedTick && item.ResolvedTick <= Tick)
                && !string.IsNullOrWhiteSpace(item.Message) && item.Message.Length <= 200 && item.Code != "Unknown" && item.Code == ComplaintCode(item.Message), "Invalid complaint.");
            Require(item.ResolvedTick != null || active.Add((item.RoomId, item.Code)), "Duplicate active complaint.");
            _complaints.Add(item);
        }
        Require(_complaints.Count(c => c.ResolvedTick != null) <= Rules.Management.ComplaintHistoryLimit && active.Count <= _operations.Count * 11, "Complaint history exceeds limits.");
        long lastTick = -1;
        foreach (var sample in state.History)
        {
            Require(sample != null && sample.Tick > lastTick && sample.Tick <= Tick && (sample.Tick + 28500) % 3600 == 0
                && (lastTick < 0 || sample.Tick - lastTick == 3600) && sample.Score is >= 0 and <= 100, "Invalid satisfaction history.");
            _satisfactionHistory.Add(sample); lastTick = sample.Tick;
        }
        Require(state.History.Length <= Rules.Management.SatisfactionHistoryHours, "Satisfaction history exceeds limits.");
        _nextComplaintId = state.NextComplaintId;
    }
}
