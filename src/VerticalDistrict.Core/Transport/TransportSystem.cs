namespace VerticalDistrict.Core.Transport;

/// <summary>
/// Deterministic single-thread transport. Empty cars answer the oldest hall call; occupied cars
/// serve destinations in their current direction, collecting matching-direction calls en route.
/// Doors, boarding and alighting occupy real ticks. Route choice includes walking, stair time,
/// expected elevator door time and a transfer penalty. Rendering has no simulation side effects.
/// </summary>
public sealed partial class TransportSystem
{
    private readonly ConstructionWorld _world;
    private readonly SortedDictionary<int, Bank> _banks = [];
    private readonly List<StairDefinition> _stairs = [];
    private readonly SortedDictionary<long, Journey> _journeys = [];
    private readonly Queue<int> _waitSamples = [];
    private readonly Dictionary<(int, int, int, int, bool), RouteLeg[]?> _routes = [];
    private int _topologyVersion;
    private int _observedWorldVersion;
    private long _abandoned;
    private long _occupiedCarTicks;
    private long _availableSeatTicks;
    public long CurrentTick { get; private set; }
    public int TopologyVersion => _topologyVersion;
    public IReadOnlyList<BankView> Banks => _banks.Values.Select(b => new BankView(Clone(b.Definition), b.OutOfService,
        Waiting(b.Definition.Id).Count())).ToArray();
    public IReadOnlyList<StairDefinition> Stairs => _stairs.ToArray();
    public IReadOnlyList<CarView> Cars => _banks.Values.Select(b => new CarView(b.Definition.Id, b.Car.State,
        b.Car.Floor, DrawFloor(b.Car), b.Car.TargetFloor, b.Car.Direction, b.Car.Passengers.Count,
        b.Definition.Capacity, b.Car.Passengers.ToArray())).ToArray();
    public IReadOnlyList<JourneyView> Journeys => _journeys.Values.Select(View).ToArray();
    public JourneyView? JourneyFor(long personId) => _journeys.TryGetValue(personId, out var journey) ? View(journey) : null;
    public TransportMetrics Metrics
    {
        get
        {
            var samples = _waitSamples.Order().ToArray();
            var queued = _journeys.Values.Where(j => j.State == JourneyState.Waiting).ToArray();
            var overloaded = queued.GroupBy(j => j.Floor).Where(g => g.Count() > _banks.Values
                .Where(b => !b.OutOfService && b.Definition.Stops.Contains(g.Key)).Sum(b => b.Definition.Capacity))
                .Select(g => g.Key).Order().ToArray();
            return new TransportMetrics(samples.Length == 0 ? 0 : samples.Average(),
                samples.Length == 0 ? 0 : samples[(int)Math.Ceiling(samples.Length * .95) - 1], samples.Length,
                _abandoned, _availableSeatTicks == 0 ? 0 : (double)_occupiedCarTicks / _availableSeatTicks,
                overloaded, queued.Length, _journeys.Values.Count(j => j.State == JourneyState.Riding));
        }
    }

    public TransportSystem(ConstructionWorld world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _observedWorldVersion = world.TopologyVersion;
    }

    public CommandResult ValidateBank(BankDefinition definition, int? replacingId = null)
    {
        if (definition is null || definition.Id <= 0 || definition.X is < 0 or >= ConstructionWorld.Width)
            return Fail("A bank needs a positive ID and a shaft inside the building.");
        if (definition.MinFloor < ConstructionWorld.MinFloor || definition.MaxFloor > ConstructionWorld.MaxFloor
            || definition.MinFloor >= definition.MaxFloor) return Fail("A shaft must span at least two supported floors.");
        if (definition.Capacity is < 1 or > 256 || definition.TravelTicksPerFloor is < 1 or > 120
            || definition.DoorTicks is < 1 or > 120) return Fail("Invalid elevator capacity or timing.");
        if (definition.Stops is not { Length: >= 2 } || definition.Stops.Distinct().Count() != definition.Stops.Length
            || definition.Stops.Any(s => s < definition.MinFloor || s > definition.MaxFloor))
            return Fail("Select at least two unique stops within the shaft.");
        if (_banks.ContainsKey(definition.Id) && replacingId != definition.Id) return Fail("Bank ID already exists.");
        for (var floor = definition.MinFloor; floor <= definition.MaxFloor; floor++)
            if (!_world.Floors.Contains(floor) || HasRoom(definition.X, floor))
                return Fail($"Shaft requires an empty supported bay on floor {floor}.");
        if (_banks.Values.Any(b => b.Definition.Id != replacingId && b.Definition.X == definition.X
            && b.Definition.MinFloor <= definition.MaxFloor && b.Definition.MaxFloor >= definition.MinFloor))
            return Fail("Shafts may not overlap.");
        if (_stairs.Any(s => s.X == definition.X && s.LowerFloor <= definition.MaxFloor && s.LowerFloor + 1 >= definition.MinFloor))
            return Fail("Shaft overlaps a staircase.");
        return Ok("Elevator bank is valid.");
    }

    public CommandResult InstallBank(BankDefinition definition, long costMinor = 0)
    {
        var valid = ValidateBank(definition);
        if (!valid.Success) return valid;
        var payment = Charge(costMinor, "Installed elevator bank.");
        if (!payment.Success) return payment;
        var copied = Clone(definition);
        _banks.Add(copied.Id, new Bank(copied));
        Changed();
        return new CommandResult(true, "Elevator bank installed.", costMinor, definition.Id);
    }

    public CommandResult ConfigureBank(BankDefinition definition)
    {
        if (!_banks.TryGetValue(definition.Id, out var bank)) return Fail("Bank does not exist.");
        var valid = ValidateBank(definition, definition.Id);
        if (!valid.Success) return valid;
        if (bank.Car.Passengers.Count != 0 || bank.Car.State == CarState.Traveling)
            return Fail("Wait for the car to stop and unload before changing its configuration.");
        if (definition.X != bank.Definition.X || definition.MinFloor != bank.Definition.MinFloor
            || definition.MaxFloor != bank.Definition.MaxFloor) return Fail("Rebuild the bank to change shaft geometry.");
        bank.Definition = Clone(definition);
        Changed();
        return Ok("Elevator stops and service policy updated.");
    }

    public CommandResult RemoveBank(int bankId, bool recover = false)
    {
        if (!_banks.TryGetValue(bankId, out var bank)) return Fail("Bank does not exist.");
        if (bank.Car.State == CarState.Traveling) return Fail("Wait for the car to reach a floor before removing the shaft.");
        if (!recover && (bank.Car.Passengers.Count != 0 || Waiting(bankId).Any()))
            return Fail("Bank has passengers; recover them explicitly before removing it.");
        foreach (var id in bank.Car.Passengers.ToArray())
        {
            var journey = _journeys[id];
            journey.Floor = bank.Car.Floor;
            journey.X = bank.Definition.X;
            journey.State = JourneyState.Unreachable;
        }
        _banks.Remove(bankId);
        Changed();
        return Ok("Elevator bank removed; affected passengers will find another route.");
    }

    public CommandResult BuildStair(int lowerFloor, int x, long costMinor = 0)
        => BuildVerticalConnection(lowerFloor, x, 0, costMinor);

    public CommandResult BuildEscalator(int lowerFloor, int x, int direction, long costMinor = 0)
        => direction is not (-1 or 1) ? Fail("An escalator direction must be +1 (up) or -1 (down).")
            : BuildVerticalConnection(lowerFloor, x, direction, costMinor);

    private CommandResult BuildVerticalConnection(int lowerFloor, int x, int direction, long costMinor)
    {
        if (x is < 0 or >= ConstructionWorld.Width || lowerFloor < ConstructionWorld.MinFloor
            || lowerFloor >= ConstructionWorld.MaxFloor || !_world.Floors.Contains(lowerFloor)
            || !_world.Floors.Contains(lowerFloor + 1)) return Fail("Stairs and escalators need two adjacent floor slabs.");
        if (HasRoom(x, lowerFloor) || HasRoom(x, lowerFloor + 1)
            || _banks.Values.Any(b => b.Definition.X == x && b.Definition.MinFloor <= lowerFloor + 1 && b.Definition.MaxFloor >= lowerFloor)
            || _stairs.Any(s => s.X == x && s.LowerFloor == lowerFloor))
            return Fail("Stair and escalator bays must be free of rooms and transport structures.");
        var description = direction == 0 ? "Built staircase." : direction == 1 ? "Built upward escalator." : "Built downward escalator.";
        var payment = Charge(costMinor, description);
        if (!payment.Success) return payment;
        _stairs.Add(new StairDefinition(lowerFloor, x, direction));
        Changed();
        return new CommandResult(true, description, costMinor);
    }

    public CommandResult RemoveStair(int lowerFloor, int x)
    {
        var stair = _stairs.Find(s => s.LowerFloor == lowerFloor && s.X == x);
        if (stair is null) return Fail("No stairs or escalator exist at this bay and lower floor.");
        if (_journeys.Values.Any(j => j.State == JourneyState.Walking && j.CurrentLeg is { Kind: RouteKind.Stair } leg
            && Math.Min(leg.FromFloor, leg.ToFloor) == lowerFloor && leg.FromX == x))
            return Fail("Wait for people on this connection to reach the next floor.");
        _stairs.Remove(stair);
        Changed();
        return Ok(stair.Direction == 0 ? "Staircase removed." : "Escalator removed.");
    }

    public bool Occupies(int x, int floor) => _banks.Values.Any(b => b.Definition.X == x
        && floor >= b.Definition.MinFloor && floor <= b.Definition.MaxFloor)
        || _stairs.Any(s => s.X == x && (s.LowerFloor == floor || s.LowerFloor + 1 == floor));

    public bool CanReach(int fromFloor, int fromX, int toFloor, int toX, bool service = false)
    {
        ObserveWorld();
        return EndpointsValid(fromFloor, fromX, toFloor, toX) && FindRoute(fromFloor, fromX, toFloor, toX, service) is not null;
    }

    public CommandResult RequestJourney(long personId, int fromFloor, int fromX, int toFloor, int toX,
        bool service = false, int patienceTicks = 300)
    {
        if (personId <= 0 || patienceTicks is < 1 or > 86400) return Fail("Invalid person ID or patience.");
        if (!EndpointsValid(fromFloor, fromX, toFloor, toX)) return Fail("Journey endpoints need existing floors and valid bays.");
        if (_journeys.TryGetValue(personId, out var previous) && previous.State is not (JourneyState.Arrived or JourneyState.Abandoned or JourneyState.Unreachable))
            return Fail("This person already owns an active journey.");
        ObserveWorld();
        var journey = new Journey(personId, fromFloor, fromX, toFloor, toX, service, patienceTicks);
        _journeys[personId] = journey;
        Replan(journey);
        return new CommandResult(true, journey.State == JourneyState.Unreachable ? "Destination is unreachable." : "Journey requested.", EntityId: personId);
    }

    public CommandResult CancelJourney(long personId)
    {
        if (!_journeys.TryGetValue(personId, out var journey)) return Fail("Journey does not exist.");
        if (journey.State is JourneyState.Arrived or JourneyState.Abandoned) return Ok("Journey already ended.");
        // A riding cancellation waits for a safe stop: immediate deletion would teleport a passenger.
        if (journey.State == JourneyState.Riding) return Fail("A riding passenger may cancel after reaching a floor.");
        if (journey.State == JourneyState.Walking && journey.CurrentLeg is { Kind: RouteKind.Stair })
            return Fail("A person on stairs or an escalator may cancel after reaching the next floor.");
        if (journey.State == JourneyState.Walking && journey.CurrentLeg is { } walk)
            journey.X = walk.FromX + Math.Sign(walk.ToX - walk.FromX) * (journey.LegDuration - journey.RemainingTicks);
        journey.State = JourneyState.Abandoned;
        _abandoned++;
        return Ok("Journey cancelled.");
    }

    /// <summary>Removes finished journey history when its owner leaves the population.</summary>
    public CommandResult ForgetJourney(long personId)
    {
        if (!_journeys.TryGetValue(personId, out var journey)) return Ok("Journey already removed.");
        if (journey.State is JourneyState.Walking or JourneyState.Waiting or JourneyState.Riding)
            return Fail("Finish or cancel an active journey before removing it.");
        _journeys.Remove(personId);
        return Ok("Journey history removed.");
    }

    public CommandResult SetBankOutOfService(int bankId, bool outOfService)
    {
        if (!_banks.TryGetValue(bankId, out var bank)) return Fail("Bank does not exist.");
        if (bank.OutOfService == outOfService) return Ok("Service state already matches.");
        bank.OutOfService = outOfService;
        if (outOfService && bank.Car.State != CarState.Traveling)
        {
            Evacuate(bank);
            bank.Car.State = CarState.OutOfService;
            bank.Car.Timer = 0;
        }
        else if (!outOfService && bank.Car.State == CarState.OutOfService) bank.Car.State = CarState.Idle;
        Changed();
        return Ok(outOfService ? "Bank disabled; moving car will unload safely at its next stop." : "Bank repaired and available.");
    }

    public void Step(long tick)
    {
        if (tick != checked(CurrentTick + 1)) throw new ArgumentOutOfRangeException(nameof(tick), "Transport requires each logical second exactly once.");
        CurrentTick = tick;
        ObserveWorld();
        foreach (var journey in _journeys.Values)
        {
            if (journey.State == JourneyState.Unreachable && journey.RouteVersion != _topologyVersion) Replan(journey);
            else if (journey.State == JourneyState.Waiting)
            {
                if (journey.RouteVersion != _topologyVersion) Replan(journey);
                if (journey.State == JourneyState.Waiting && CurrentTick - journey.WaitSinceTick >= journey.PatienceTicks)
                {
                    journey.TotalWaitTicks += (int)(CurrentTick - journey.WaitSinceTick);
                    journey.State = JourneyState.Abandoned;
                    _abandoned++;
                }
            }
            else if (journey.State == JourneyState.Walking && --journey.RemainingTicks <= 0)
            {
                var leg = journey.CurrentLeg!;
                journey.Floor = leg.ToFloor;
                journey.X = leg.ToX;
                journey.LegIndex++;
                if (journey.RouteVersion != _topologyVersion) Replan(journey); else BeginLeg(journey);
            }
        }
        foreach (var bank in _banks.Values)
        {
            TickCar(bank);
            if (!bank.OutOfService)
            {
                _occupiedCarTicks = checked(_occupiedCarTicks + bank.Car.Passengers.Count);
                _availableSeatTicks = checked(_availableSeatTicks + bank.Definition.Capacity);
            }
        }
    }

    private void TickCar(Bank bank)
    {
        var car = bank.Car;
        if (car.Timer > 0 && --car.Timer > 0) return;
        switch (car.State)
        {
            case CarState.Idle: Dispatch(bank); break;
            case CarState.Traveling:
                car.Floor = car.TargetFloor;
                if (bank.OutOfService) { Evacuate(bank); car.State = CarState.OutOfService; }
                else Phase(car, CarState.Opening, bank.Definition.DoorTicks);
                break;
            case CarState.Opening: Phase(car, CarState.Unloading, 1); break;
            case CarState.Unloading:
                foreach (var id in car.Passengers.ToArray())
                {
                    var journey = _journeys[id];
                    if (journey.CurrentLeg!.ToFloor != car.Floor) continue;
                    car.Passengers.Remove(id);
                    journey.Floor = car.Floor;
                    journey.X = bank.Definition.X;
                    journey.LegIndex++;
                    if (journey.RouteVersion != _topologyVersion) Replan(journey); else BeginLeg(journey);
                }
                Phase(car, CarState.Boarding, 1);
                break;
            case CarState.Boarding:
                var queue = Waiting(bank.Definition.Id).Where(j => j.Floor == car.Floor)
                    .OrderBy(j => j.WaitSinceTick).ThenBy(j => j.PersonId).ToArray();
                // Recheck oldest-call ownership after alighting. A continuous stream at terminal
                // floors must not refill an empty car forever while an older intermediate call waits.
                if (car.Passengers.Count == 0 && Waiting(bank.Definition.Id)
                    .OrderBy(j => j.WaitSinceTick).ThenBy(j => j.PersonId).FirstOrDefault() is { } oldest
                    && oldest.Floor != car.Floor) queue = [];
                if (car.Passengers.Count == 0 && queue.Length > 0)
                    car.Direction = Math.Sign(queue[0].CurrentLeg!.ToFloor - car.Floor);
                foreach (var journey in queue)
                {
                    if (car.Passengers.Count >= bank.Definition.Capacity) break;
                    if (Math.Sign(journey.CurrentLeg!.ToFloor - car.Floor) != car.Direction) continue;
                    var wait = checked((int)(CurrentTick - journey.WaitSinceTick));
                    journey.TotalWaitTicks += wait;
                    _waitSamples.Enqueue(wait);
                    if (_waitSamples.Count > 2048) _waitSamples.Dequeue();
                    journey.State = JourneyState.Riding;
                    car.Passengers.Add(journey.PersonId);
                }
                Phase(car, CarState.Closing, bank.Definition.DoorTicks);
                break;
            case CarState.Closing: car.State = CarState.Idle; Dispatch(bank); break;
            case CarState.OutOfService: break;
        }
    }

    private void Dispatch(Bank bank)
    {
        var car = bank.Car;
        if (bank.OutOfService) { car.State = CarState.OutOfService; return; }
        int target;
        if (car.Passengers.Count != 0)
        {
            target = car.Passengers.Select(id => _journeys[id].CurrentLeg!.ToFloor)
                .OrderBy(f => Math.Abs(f - car.Floor)).ThenBy(f => f).First();
            car.Direction = Math.Sign(target - car.Floor);
            if (car.Passengers.Count < bank.Definition.Capacity)
            {
                var pickup = Waiting(bank.Definition.Id).Where(j => j.Floor != car.Floor
                    && Math.Sign(j.Floor - car.Floor) == car.Direction && Math.Abs(j.Floor - car.Floor) < Math.Abs(target - car.Floor)
                    && Math.Sign(j.CurrentLeg!.ToFloor - j.Floor) == car.Direction)
                    .OrderBy(j => Math.Abs(j.Floor - car.Floor)).ThenBy(j => j.PersonId).FirstOrDefault();
                if (pickup is not null) target = pickup.Floor;
            }
        }
        else
        {
            var oldest = Waiting(bank.Definition.Id).OrderBy(j => j.WaitSinceTick).ThenBy(j => j.PersonId).FirstOrDefault();
            if (oldest is null) { car.Direction = 0; return; }
            target = oldest.Floor;
            car.Direction = target == car.Floor ? Math.Sign(oldest.CurrentLeg!.ToFloor - car.Floor) : Math.Sign(target - car.Floor);
        }
        car.TargetFloor = target;
        if (target == car.Floor) Phase(car, CarState.Opening, bank.Definition.DoorTicks);
        else
        {
            car.TravelStartFloor = car.Floor;
            Phase(car, CarState.Traveling, Math.Abs(target - car.Floor) * bank.Definition.TravelTicksPerFloor);
        }
    }

    private void Evacuate(Bank bank)
    {
        foreach (var id in bank.Car.Passengers.ToArray())
        {
            var journey = _journeys[id];
            journey.Floor = bank.Car.Floor;
            journey.X = bank.Definition.X;
            journey.State = JourneyState.Unreachable;
            journey.RouteVersion = -1;
        }
        bank.Car.Passengers.Clear();
    }

    private void Replan(Journey journey)
    {
        var previousWait = journey.State == JourneyState.Waiting ? journey.WaitSinceTick : -1;
        var previousLeg = journey.State == JourneyState.Waiting ? journey.CurrentLeg : null;
        var route = FindRoute(journey.Floor, journey.X, journey.DestinationFloor, journey.DestinationX, journey.Service);
        journey.Route = route ?? [];
        journey.LegIndex = 0;
        journey.RouteVersion = _topologyVersion;
        if (route is null)
        {
            if (previousWait >= 0) journey.TotalWaitTicks += checked((int)(CurrentTick - previousWait));
            journey.State = JourneyState.Unreachable;
            return;
        }
        BeginLeg(journey);
        if (previousWait >= 0)
        {
            if (journey.State == JourneyState.Waiting && journey.CurrentLeg == previousLeg)
                journey.WaitSinceTick = previousWait;
            else journey.TotalWaitTicks += checked((int)(CurrentTick - previousWait));
        }
    }

    private void BeginLeg(Journey journey)
    {
        if (journey.LegIndex >= journey.Route.Length) { journey.State = JourneyState.Arrived; return; }
        var leg = journey.CurrentLeg!;
        if (leg.Kind == RouteKind.Elevator)
        {
            journey.State = JourneyState.Waiting;
            journey.WaitSinceTick = CurrentTick;
            journey.RemainingTicks = 0;
        }
        else
        {
            journey.State = JourneyState.Walking;
            journey.RemainingTicks = leg.Kind == RouteKind.Stair ? 8 : Math.Max(1, Math.Abs(leg.ToX - leg.FromX));
            journey.LegDuration = journey.RemainingTicks;
        }
    }

    private RouteLeg[]? FindRoute(int fromFloor, int fromX, int toFloor, int toX, bool service)
    {
        var key = (fromFloor, fromX, toFloor, toX, service);
        if (_routes.TryGetValue(key, out var cached)) return cached;
        if (!EndpointsValid(fromFloor, fromX, toFloor, toX)) return null;
        var start = (Floor: fromFloor, X: fromX);
        var goal = (Floor: toFloor, X: toX);
        var points = new HashSet<(int Floor, int X)> { start, goal };
        var usable = _banks.Values.Where(b => !b.OutOfService && (service || !b.Definition.ServiceOnly)).ToArray();
        foreach (var bank in usable) foreach (var floor in bank.Definition.Stops) points.Add((floor, bank.Definition.X));
        foreach (var stair in _stairs) { points.Add((stair.LowerFloor, stair.X)); points.Add((stair.LowerFloor + 1, stair.X)); }
        var byFloor = points.GroupBy(p => p.Floor).ToDictionary(g => g.Key, g => g.OrderBy(p => p.X).ToArray());
        var distance = new Dictionary<(int Floor, int X), int> { [start] = 0 };
        var previous = new Dictionary<(int Floor, int X), RouteLeg>();
        var pending = new PriorityQueue<(int Floor, int X), (int Cost, int Floor, int X)>();
        pending.Enqueue(start, (0, start.Floor, start.X));
        while (pending.TryDequeue(out var here, out var priority))
        {
            if (priority.Cost != distance[here]) continue;
            if (here == goal) break;
            foreach (var point in byFloor[here.Floor])
                if (point != here) Relax(new RouteLeg(RouteKind.Walk, here.Floor, here.X, point.Floor, point.X), Math.Abs(point.X - here.X));
            foreach (var stair in _stairs.Where(s => s.X == here.X && (s.LowerFloor == here.Floor || s.LowerFloor + 1 == here.Floor)
                && (s.Direction == 0 || s.Direction == (here.Floor == s.LowerFloor ? 1 : -1))))
                Relax(new RouteLeg(RouteKind.Stair, here.Floor, here.X,
                    here.Floor == stair.LowerFloor ? here.Floor + 1 : here.Floor - 1, here.X), 8);
            foreach (var bank in usable.Where(b => b.Definition.X == here.X && b.Definition.Stops.Contains(here.Floor)))
                foreach (var floor in bank.Definition.Stops.Order())
                    if (floor != here.Floor) Relax(new RouteLeg(RouteKind.Elevator, here.Floor, here.X, floor, here.X, bank.Definition.Id),
                        Math.Abs(floor - here.Floor) * bank.Definition.TravelTicksPerFloor + bank.Definition.DoorTicks * 2 + 10);

            void Relax(RouteLeg leg, int cost)
            {
                var there = (Floor: leg.ToFloor, X: leg.ToX);
                var candidate = distance[here] + cost;
                if (distance.TryGetValue(there, out var old) && old <= candidate) return;
                distance[there] = candidate;
                previous[there] = leg;
                pending.Enqueue(there, (candidate, there.Floor, there.X));
            }
        }
        RouteLeg[]? result = null;
        if (distance.ContainsKey(goal))
        {
            var path = new List<RouteLeg>();
            var current = goal;
            while (current != start) { var leg = previous[current]; path.Add(leg); current = (leg.FromFloor, leg.FromX); }
            path.Reverse();
            result = path.ToArray();
        }
        // Topology changes clear this bounded cache; endpoint combinations never grow without limit.
        if (_routes.Count >= 4096) _routes.Clear();
        _routes[key] = result;
        return result;
    }

    private JourneyView View(Journey journey)
    {
        double floor = journey.Floor, x = journey.X;
        int? bankId = null;
        if (journey.State == JourneyState.Riding && journey.CurrentLeg is { } ride && _banks.TryGetValue(ride.BankId, out var bank))
        { floor = DrawFloor(bank.Car); x = bank.Definition.X; bankId = ride.BankId; }
        else if (journey.State == JourneyState.Walking && journey.CurrentLeg is { } walk)
        {
            var progress = 1d - (double)journey.RemainingTicks / Math.Max(1, journey.LegDuration);
            floor += (walk.ToFloor - walk.FromFloor) * progress;
            x += (walk.ToX - walk.FromX) * progress;
        }
        else if (journey.State == JourneyState.Waiting) bankId = journey.CurrentLeg?.BankId;
        var queuedOrRiding = journey.State is JourneyState.Waiting or JourneyState.Riding;
        return new JourneyView(journey.PersonId, journey.State, journey.Floor, floor, x,
            journey.DestinationFloor, journey.DestinationX, bankId,
            journey.State == JourneyState.Waiting ? journey.WaitSinceTick : 0, journey.TotalWaitTicks, journey.Service,
            queuedOrRiding ? journey.CurrentLeg?.ToFloor : null, journey.PatienceTicks);
    }

    private IEnumerable<Journey> Waiting(int bankId) => _journeys.Values.Where(j => j.State == JourneyState.Waiting && j.CurrentLeg?.BankId == bankId);
    private bool EndpointsValid(int ff, int fx, int tf, int tx) => _world.Floors.Contains(ff) && _world.Floors.Contains(tf)
        && fx is >= 0 and < ConstructionWorld.Width && tx is >= 0 and < ConstructionWorld.Width;
    private bool HasRoom(int x, int floor) => _world.Rooms.Any(r => x >= r.X && x < r.X + _world.Catalog.Get(r.DefinitionId).Width
        && floor >= r.Floor && floor < r.Floor + _world.Catalog.Get(r.DefinitionId).Height);
    private CommandResult Charge(long cost, string description)
    {
        if (cost < 0) return Fail("Construction cost cannot be negative.");
        if (cost > 0 && cost > _world.CashMinor) return Fail("Insufficient construction funds.");
        return cost == 0 ? Ok(description)
            : _world.ApplyOperatingTransaction(-cost, CurrentTick, "Transport.Construction", null, description);
    }
    private void ObserveWorld() { if (_observedWorldVersion != _world.TopologyVersion) { _observedWorldVersion = _world.TopologyVersion; Changed(); } }
    private void Changed() { _topologyVersion = checked(_topologyVersion + 1); _routes.Clear(); }
    private static void Phase(Car car, CarState state, int ticks) { car.State = state; car.Timer = ticks; car.Duration = ticks; }
    private static double DrawFloor(Car car) => car.State == CarState.Traveling
        ? car.TravelStartFloor + (car.TargetFloor - car.TravelStartFloor) * (1d - (double)car.Timer / car.Duration) : car.Floor;
    private static BankDefinition Clone(BankDefinition bank) => bank with { Stops = bank.Stops.Order().ToArray() };
    private static CommandResult Ok(string message) => new(true, message);
    private static CommandResult Fail(string message) => new(false, message);

    private sealed class Bank(BankDefinition definition)
    {
        public BankDefinition Definition = definition;
        public bool OutOfService;
        public readonly Car Car = new(definition.Stops.Min());
    }
    private sealed class Car(int floor)
    {
        public CarState State;
        public int Floor = floor, TargetFloor = floor, TravelStartFloor = floor, Direction, Timer, Duration;
        public readonly List<long> Passengers = [];
    }
    private sealed class Journey(long personId, int floor, int x, int destinationFloor, int destinationX, bool service, int patienceTicks)
    {
        public readonly long PersonId = personId;
        public readonly int DestinationFloor = destinationFloor, DestinationX = destinationX, PatienceTicks = patienceTicks;
        public readonly bool Service = service;
        public JourneyState State;
        public int Floor = floor, X = x, LegIndex, RemainingTicks, LegDuration, TotalWaitTicks, RouteVersion;
        public long WaitSinceTick;
        public RouteLeg[] Route = [];
        public RouteLeg? CurrentLeg => LegIndex < Route.Length ? Route[LegIndex] : null;
    }
}
