namespace VerticalDistrict.Core.Transport;

public sealed partial class TransportSystem
{
    public TransportSnapshot CaptureSnapshot() => new(2, CurrentTick, _topologyVersion, _observedWorldVersion,
        _banks.Values.Select(b => new BankSnapshot(Clone(b.Definition), b.OutOfService,
            CaptureCar(b, b.Car), b.Cars.Values.Where(c => c.Id != 1).Select(c => CaptureCar(b, c)).ToArray())).ToArray(),
        _stairs.OrderBy(s => s.LowerFloor).ThenBy(s => s.X).ToArray(),
        _journeys.Values.Select(j => new JourneySnapshot(j.PersonId, j.State, j.Floor, j.X,
            j.DestinationFloor, j.DestinationX, j.Service, j.PatienceTicks, j.Route.ToArray(), j.LegIndex,
            j.RemainingTicks, j.LegDuration, j.WaitSinceTick, j.TotalWaitTicks, j.RouteVersion,
            j.AssignedCarId, j.CompletedRides)).ToArray(),
        _waitSamples.ToArray(), _abandoned, _occupiedCarTicks, _availableSeatTicks);

    private static CarSnapshot CaptureCar(Bank bank, Car car) => new(bank.Definition.Id, car.State, car.Floor,
        car.TargetFloor, car.TravelStartFloor, car.Direction, car.Timer, car.Duration, car.Passengers.ToArray(), car.Id, car.OutOfService);

    /// <summary>Builds a new system, so rejected snapshots cannot partially mutate the live game.</summary>
    public static TransportSystem RestoreSnapshot(ConstructionWorld world, TransportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion == 1) snapshot = UpgradeLegacySnapshot(snapshot);
        Require(snapshot.SchemaVersion == 2, "Unsupported transport schema.");
        Require(snapshot.CurrentTick >= 0 && snapshot.TopologyVersion >= 0 && snapshot.ObservedWorldTopologyVersion >= 0
            && snapshot.ObservedWorldTopologyVersion <= world.TopologyVersion, "Invalid transport clock or topology.");
        Require(snapshot.Banks is { Length: <= 8320 } && snapshot.Stairs is { Length: <= 8320 }
            && snapshot.Journeys is { Length: <= 100000 } && snapshot.WaitSamples is { Length: <= 2048 }, "Invalid transport collections.");
        Require(snapshot.AbandonedTrips >= 0 && snapshot.OccupiedCarTicks >= 0 && snapshot.AvailableSeatTicks >= snapshot.OccupiedCarTicks,
            "Invalid transport counters.");
        var result = new TransportSystem(world);
        foreach (var state in snapshot.Banks!)
        {
            Require(state is not null && state.Definition is not null && state.Car is not null && state.AdditionalCars is not null,
                "Missing bank data.");
            var definition = state!.Definition!;
            var valid = result.ValidateBank(definition);
            Require(valid.Success, valid.Message);
            var copied = Clone(definition);
            var bank = new Bank(copied) { OutOfService = state.IsOutOfService };
            Require(state.Car!.CarId == 1 && state.AdditionalCars!.Length == bank.Cars.Count - 1,
                "The bank must contain exactly one primary car and one car per additional shaft.");
            var restoredCars = new HashSet<int>();
            foreach (var car in new[] { state.Car }.Concat(state.AdditionalCars))
            {
                Require(car is not null && bank.Cars.ContainsKey(car.CarId) && restoredCars.Add(car.CarId), "Unknown or duplicate car ID.");
                RestoreCar(bank, bank.Cars[car!.CarId], car);
            }
            result._banks.Add(definition.Id, bank);
        }
        foreach (var stair in snapshot.Stairs!)
        {
            Require(stair is not null && stair.X is >= 0 and < ConstructionWorld.Width && stair.Direction is >= -1 and <= 1
                && stair.LowerFloor >= ConstructionWorld.MinFloor && stair.LowerFloor < ConstructionWorld.MaxFloor
                && world.Floors.Contains(stair.LowerFloor) && world.Floors.Contains(stair.LowerFloor + 1), "Invalid staircase geometry.");
            Require(!result._stairs.Any(s => s.LowerFloor == stair!.LowerFloor && s.X == stair.X) && !result.HasRoom(stair!.X, stair.LowerFloor)
                && !result.HasRoom(stair.X, stair.LowerFloor + 1)
                && !result._banks.Values.Any(b => ShaftDefinitions(b.Definition).Any(s => s.X == stair.X) && b.Definition.MinFloor <= stair.LowerFloor + 1
                    && b.Definition.MaxFloor >= stair.LowerFloor), "Staircase overlaps another structure.");
            result._stairs.Add(stair!);
        }
        foreach (var state in snapshot.Journeys!)
        {
            Require(state is not null && state.PersonId > 0 && Enum.IsDefined(state.State)
                && !result._journeys.ContainsKey(state.PersonId), "Invalid or duplicate journey ID.");
            Require(CoordinatesValid(state!.Floor, state.X) && CoordinatesValid(state.DestinationFloor, state.DestinationX), "Journey endpoint is invalid.");
            Require(state.PatienceTicks is >= 1 and <= 86400 && state.Route is { Length: <= 10000 }
                && state.LegIndex >= 0 && state.LegIndex <= state.Route.Length && state.RemainingTicks >= 0
                && state.LegDuration >= state.RemainingTicks && state.LegDuration <= 32
                && state.WaitSinceTick >= 0 && state.WaitSinceTick <= snapshot.CurrentTick && state.TotalWaitTicks >= 0
                && state.RouteVersion >= -1 && state.RouteVersion <= snapshot.TopologyVersion
                && state.AssignedCarId is null or > 0 && state.CompletedRides >= 0 && state.CompletedRides <= snapshot.CurrentTick,
                "Invalid journey timer, car assignment, or route.");
            for (var index = 0; index < state.Route!.Length; index++)
            {
                var leg = state.Route[index];
                Require(leg is not null && Enum.IsDefined(leg.Kind)
                    && CoordinatesValid(leg.FromFloor, leg.FromX) && CoordinatesValid(leg.ToFloor, leg.ToX), "Invalid route leg.");
                Require(index == 0 || (state.Route[index - 1].ToFloor == leg!.FromFloor
                    && state.Route[index - 1].ToX == leg.FromX), "Disconnected route legs.");
                if (leg!.Kind == RouteKind.Walk) Require(leg.FromFloor == leg.ToFloor && leg.FromX != leg.ToX && leg.BankId == 0 && leg.CarId == 0, "Invalid horizontal route.");
                if (leg.Kind == RouteKind.Stair) Require(Math.Abs(leg.FromFloor - leg.ToFloor) == 1 && leg.FromX == leg.ToX && leg.BankId == 0 && leg.CarId == 0, "Invalid stair route.");
                if (leg.Kind == RouteKind.Elevator) Require(leg.FromFloor != leg.ToFloor && leg.FromX == leg.ToX && leg.BankId > 0 && leg.CarId >= 0, "Invalid elevator route.");
                if (index >= state.LegIndex && leg.Kind == RouteKind.Elevator && state.State is JourneyState.Walking or JourneyState.Waiting or JourneyState.Riding)
                {
                    var assignedIndex = state.State == JourneyState.Walking ? state.LegIndex + 1 : state.LegIndex;
                    Require(leg.CarId == 0 || index == assignedIndex && state.AssignedCarId == leg.CarId,
                        "Future route contains an orphaned car assignment.");
                    if (state.RouteVersion == snapshot.TopologyVersion && snapshot.ObservedWorldTopologyVersion == world.TopologyVersion)
                        Require(result._banks.TryGetValue(leg.BankId, out var routeBank)
                            && routeBank.Definition.Stops.Contains(leg.FromFloor) && routeBank.Definition.Stops.Contains(leg.ToFloor)
                            && (state.Service || !routeBank.Definition.ServiceOnly)
                            && (leg.CarId == 0 ? routeBank.Cars.Values.Any(c => c.X == leg.FromX)
                                : routeBank.Cars.TryGetValue(leg.CarId, out var routeCar) && routeCar.X == leg.FromX),
                            "Current route references an unknown stop, shaft, or denied service segment.");
                }
            }
            Require(state.CompletedRides >= state.Route.Take(state.LegIndex).Count(leg => leg.Kind == RouteKind.Elevator),
                "Completed ride count disagrees with physical route progress.");
            if (state.Route.Length > 0)
                Require(state.Route[^1].ToFloor == state.DestinationFloor && state.Route[^1].ToX == state.DestinationX, "Route does not reach its destination.");
            var journey = new Journey(state.PersonId, state.Floor, state.X, state.DestinationFloor,
                state.DestinationX, state.Service, state.PatienceTicks)
            {
                State = state.State, Route = state.Route.ToArray(), LegIndex = state.LegIndex,
                RemainingTicks = state.RemainingTicks, LegDuration = state.LegDuration,
                WaitSinceTick = state.WaitSinceTick, TotalWaitTicks = state.TotalWaitTicks, RouteVersion = state.RouteVersion,
                AssignedCarId = state.AssignedCarId, CompletedRides = state.CompletedRides
            };
            if (state.State is JourneyState.Walking or JourneyState.Waiting or JourneyState.Riding)
            {
                Require(journey.CurrentLeg is { } active && active.FromFloor == state.Floor && active.FromX == state.X,
                    "Active journey is not at its current route origin.");
                var leg = journey.CurrentLeg!;
                Require(world.Floors.Contains(state.Floor), "Active journey stands on a missing floor.");
                Require(state.State != JourneyState.Walking || (leg.Kind != RouteKind.Elevator && state.RemainingTicks > 0), "Invalid walking phase.");
                Require(state.State == JourneyState.Walking || leg.Kind == RouteKind.Elevator, "Queue and car ownership require an elevator leg.");
                if (state.State == JourneyState.Riding)
                    Require(result._banks.TryGetValue(leg.BankId, out var bank) && bank.Cars.TryGetValue(leg.CarId, out var car)
                        && car.Passengers.Contains(state.PersonId) && car.X == leg.FromX && bank.Definition.Stops.Contains(leg.ToFloor)
                        && (state.Service || !bank.Definition.ServiceOnly), "Riding person is missing their car or access rights.");
                if (state.RouteVersion == snapshot.TopologyVersion && snapshot.ObservedWorldTopologyVersion == world.TopologyVersion)
                {
                    if (state.State == JourneyState.Waiting)
                        Require(result._banks.TryGetValue(leg.BankId, out var bank)
                            && (leg.CarId == 0 ? !bank.OutOfService && bank.Cars.Values.Any(c => !c.OutOfService) && bank.Cars.Values.Any(c => c.X == leg.FromX)
                                : bank.Cars.TryGetValue(leg.CarId, out var car) && !IsUnavailable(bank, car) && car.X == leg.FromX)
                            && bank.Definition.Stops.Contains(leg.FromFloor)
                            && bank.Definition.Stops.Contains(leg.ToFloor) && (state.Service || !bank.Definition.ServiceOnly),
                            "Waiting person references an unavailable bank.");
                    if (state.State == JourneyState.Walking && leg.Kind == RouteKind.Stair)
                        Require(result._stairs.Any(s => s.X == leg.FromX && s.LowerFloor == Math.Min(leg.FromFloor, leg.ToFloor)
                                && (s.Direction == 0 || s.Direction == Math.Sign(leg.ToFloor - leg.FromFloor))),
                            "Walking person references an unavailable staircase.");
                }
            }
            if (state.State == JourneyState.Arrived)
                Require(state.Floor == state.DestinationFloor && state.X == state.DestinationX && state.LegIndex == state.Route.Length, "Arrival position is inconsistent.");
            if (state.State == JourneyState.Riding)
                Require(state.AssignedCarId.HasValue && journey.CurrentLeg!.CarId == state.AssignedCarId.Value,
                    "A riding passenger must belong to exactly one assigned car.");
            if (state.State == JourneyState.Waiting)
                Require(journey.CurrentLeg!.CarId == (state.AssignedCarId ?? 0), "Queue assignment disagrees with its elevator route.");
            if (state.AssignedCarId.HasValue)
            {
                var assignedLeg = HallJourneyLeg(journey);
                Require(state.State is JourneyState.Walking or JourneyState.Waiting or JourneyState.Riding
                    && assignedLeg is not null && assignedLeg.CarId == state.AssignedCarId
                    && (state.State != JourneyState.Walking || journey.CurrentLeg?.Kind == RouteKind.Walk)
                    && (state.State != JourneyState.Riding && state.RouteVersion != snapshot.TopologyVersion
                        || result._banks.TryGetValue(assignedLeg.BankId, out var bank)
                        && bank.Cars.TryGetValue(assignedLeg.CarId, out var car) && car.X == assignedLeg.FromX),
                    "Car assignment does not match the current physical approach or ride.");
            }
            result._journeys.Add(state.PersonId, journey);
        }
        var ownership = new HashSet<long>();
        foreach (var bank in result._banks.Values)
            foreach (var car in bank.Cars.Values)
            foreach (var id in car.Passengers)
                Require(ownership.Add(id) && result._journeys.TryGetValue(id, out var journey)
                    && journey.State == JourneyState.Riding && journey.CurrentLeg?.BankId == bank.Definition.Id
                    && journey.CurrentLeg.CarId == car.Id && journey.AssignedCarId == car.Id,
                    "Passenger is missing, duplicated, or owned by a different car.");
        foreach (var sample in snapshot.WaitSamples!) { Require(sample >= 0 && sample <= 86400, "Invalid wait sample."); result._waitSamples.Enqueue(sample); }
        result.CurrentTick = snapshot.CurrentTick;
        result._topologyVersion = snapshot.TopologyVersion;
        result._observedWorldVersion = snapshot.ObservedWorldTopologyVersion;
        result._abandoned = snapshot.AbandonedTrips;
        result._occupiedCarTicks = snapshot.OccupiedCarTicks;
        result._availableSeatTicks = snapshot.AvailableSeatTicks;
        return result;
    }

    private static void RestoreCar(Bank bank, Car target, CarSnapshot car)
    {
        var definition = bank.Definition;
        Require(car.BankId == definition.Id && Enum.IsDefined(car.State), "Car has invalid bank or state.");
        Require(car.Floor >= definition.MinFloor && car.Floor <= definition.MaxFloor
            && car.TargetFloor >= definition.MinFloor && car.TargetFloor <= definition.MaxFloor
            && car.TravelStartFloor >= definition.MinFloor && car.TravelStartFloor <= definition.MaxFloor,
            "Car position is outside its shaft.");
        Require(car.Direction is >= -1 and <= 1 && car.Timer >= 0 && car.Duration >= car.Timer
            && car.Duration <= 260 * 120, "Invalid car direction or timer.");
        Require(car.Passengers is not null && car.Passengers.Length <= definition.Capacity
            && car.Passengers.All(id => id > 0) && car.Passengers.Distinct().Count() == car.Passengers.Length,
            "Car capacity or duplicate ownership is invalid.");
        Require(car.State != CarState.Traveling || (car.Timer > 0 && car.Floor == car.TravelStartFloor
            && car.Floor != car.TargetFloor && car.Direction == Math.Sign(car.TargetFloor - car.Floor)
            && car.Duration == Math.Abs(car.TargetFloor - car.Floor) * definition.TravelTicksPerFloor
            && definition.Stops.Contains(car.TargetFloor)), "Invalid traveling car.");
        Require(car.State is not (CarState.Opening or CarState.Unloading or CarState.Boarding or CarState.Closing) || car.Timer > 0,
            "Active door phase requires a timer.");
        var disabled = bank.OutOfService || car.IsOutOfService;
        Require(!disabled || car.State is CarState.Traveling or CarState.OutOfService, "Disabled car has an invalid state.");
        Require(car.State != CarState.OutOfService || (disabled && car.Passengers!.Length == 0), "Out-of-service car cannot retain passengers.");
        target.OutOfService = car.IsOutOfService;
        target.State = car.State; target.Floor = car.Floor; target.TargetFloor = car.TargetFloor;
        target.TravelStartFloor = car.TravelStartFloor; target.Direction = car.Direction;
        target.Timer = car.Timer; target.Duration = car.Duration;
        target.Passengers.AddRange(car.Passengers!);
    }

    // Direct .NET callers can restore original transport DTOs independently of a session. JSON
    // session hydration additionally rejects every newer property under a historical schema.
    private static TransportSnapshot UpgradeLegacySnapshot(TransportSnapshot snapshot)
    {
        Require(snapshot.Banks is not null && snapshot.Journeys is not null, "Missing legacy transport collections.");
        var banks = snapshot.Banks!.Select(bank =>
        {
            Require(bank is not null && bank.Definition is not null && bank.Car is not null, "Missing legacy bank data.");
            Require(bank!.Definition.AdditionalShafts is null or { Length: 0 } && bank.AdditionalCars is null or { Length: 0 }
                && bank.Car.CarId == 1 && !bank.Car.IsOutOfService, "Legacy bank contains newer car state.");
            return bank with { AdditionalCars = [] };
        }).ToArray();
        var journeys = snapshot.Journeys!.Select(journey =>
        {
            Require(journey is not null && journey.Route is not null && journey.LegIndex >= 0 && journey.LegIndex <= journey.Route.Length,
                "Invalid legacy route.");
            Require(journey!.AssignedCarId is null && journey.CompletedRides == 0 && journey.Route.All(leg => leg is not null && leg.CarId == 0),
                "Legacy journey contains newer dispatch state.");
            var assigned = journey.State is JourneyState.Waiting or JourneyState.Riding;
            return journey with
            {
                AssignedCarId = assigned ? 1 : null,
                CompletedRides = journey.Route.Take(journey.LegIndex).Count(leg => leg.Kind == RouteKind.Elevator),
                Route = journey.Route.Select((leg, index) => leg.Kind == RouteKind.Elevator && (index < journey.LegIndex || index == journey.LegIndex && assigned)
                    ? leg with { CarId = 1 } : leg).ToArray()
            };
        }).ToArray();
        return snapshot with { SchemaVersion = 2, Banks = banks, Journeys = journeys };
    }

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new ArgumentException($"Invalid transport save: {message}");
    }

    private static bool CoordinatesValid(int floor, int x) => floor is >= ConstructionWorld.MinFloor and <= ConstructionWorld.MaxFloor
        && x is >= 0 and < ConstructionWorld.Width;
}
