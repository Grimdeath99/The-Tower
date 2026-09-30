namespace VerticalDistrict.Core.Transport;

public sealed partial class TransportSystem
{
    public TransportSnapshot CaptureSnapshot() => new(1, CurrentTick, _topologyVersion, _observedWorldVersion,
        _banks.Values.Select(b => new BankSnapshot(Clone(b.Definition), b.OutOfService,
            new CarSnapshot(b.Definition.Id, b.Car.State, b.Car.Floor, b.Car.TargetFloor, b.Car.TravelStartFloor,
                b.Car.Direction, b.Car.Timer, b.Car.Duration, b.Car.Passengers.ToArray()))).ToArray(),
        _stairs.OrderBy(s => s.LowerFloor).ThenBy(s => s.X).ToArray(),
        _journeys.Values.Select(j => new JourneySnapshot(j.PersonId, j.State, j.Floor, j.X,
            j.DestinationFloor, j.DestinationX, j.Service, j.PatienceTicks, j.Route.ToArray(), j.LegIndex,
            j.RemainingTicks, j.LegDuration, j.WaitSinceTick, j.TotalWaitTicks, j.RouteVersion)).ToArray(),
        _waitSamples.ToArray(), _abandoned, _occupiedCarTicks, _availableSeatTicks);

    /// <summary>Builds a new system, so rejected snapshots cannot partially mutate the live game.</summary>
    public static TransportSystem RestoreSnapshot(ConstructionWorld world, TransportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(snapshot);
        Require(snapshot.SchemaVersion == 1, "Unsupported transport schema.");
        Require(snapshot.CurrentTick >= 0 && snapshot.TopologyVersion >= 0 && snapshot.ObservedWorldTopologyVersion >= 0
            && snapshot.ObservedWorldTopologyVersion <= world.TopologyVersion, "Invalid transport clock or topology.");
        Require(snapshot.Banks is { Length: <= 8320 } && snapshot.Stairs is { Length: <= 8320 }
            && snapshot.Journeys is { Length: <= 100000 } && snapshot.WaitSamples is { Length: <= 2048 }, "Invalid transport collections.");
        Require(snapshot.AbandonedTrips >= 0 && snapshot.OccupiedCarTicks >= 0 && snapshot.AvailableSeatTicks >= snapshot.OccupiedCarTicks,
            "Invalid transport counters.");
        var result = new TransportSystem(world);
        foreach (var state in snapshot.Banks!)
        {
            Require(state is not null && state.Definition is not null && state.Car is not null, "Missing bank data.");
            var definition = state!.Definition!;
            var valid = result.ValidateBank(definition);
            Require(valid.Success, valid.Message);
            var copied = Clone(definition);
            var bank = new Bank(copied) { OutOfService = state.IsOutOfService };
            var car = state.Car!;
            Require(car.BankId == definition.Id && Enum.IsDefined(car.State), "Car has invalid bank or state.");
            Require(car.Floor >= definition.MinFloor && car.Floor <= definition.MaxFloor
                && car.TargetFloor >= definition.MinFloor && car.TargetFloor <= definition.MaxFloor
                && car.TravelStartFloor >= definition.MinFloor && car.TravelStartFloor <= definition.MaxFloor,
                "Car position is outside its shaft.");
            Require(car.Direction is >= -1 and <= 1 && car.Timer >= 0 && car.Duration >= car.Timer
                && car.Duration <= 260 * 120, "Invalid car direction or timer.");
            Require(car.Passengers is not null && car.Passengers.Length <= definition.Capacity
                && car.Passengers.Distinct().Count() == car.Passengers.Length, "Car capacity or duplicate ownership is invalid.");
            Require(car.State != CarState.Traveling || (car.Timer > 0 && car.Floor == car.TravelStartFloor
                && car.Floor != car.TargetFloor && car.Direction == Math.Sign(car.TargetFloor - car.Floor)
                && car.Duration == Math.Abs(car.TargetFloor - car.Floor) * definition.TravelTicksPerFloor), "Invalid traveling car.");
            Require(car.State is not (CarState.Opening or CarState.Unloading or CarState.Boarding or CarState.Closing) || car.Timer > 0,
                "Active door phase requires a timer.");
            Require(!state.IsOutOfService || car.State is CarState.Traveling or CarState.OutOfService, "Disabled car has an invalid state.");
            Require(car.State != CarState.OutOfService || (state.IsOutOfService && car.Passengers!.Length == 0), "Out-of-service car cannot retain passengers.");
            bank.Car.State = car.State;
            bank.Car.Floor = car.Floor;
            bank.Car.TargetFloor = car.TargetFloor;
            bank.Car.TravelStartFloor = car.TravelStartFloor;
            bank.Car.Direction = car.Direction;
            bank.Car.Timer = car.Timer;
            bank.Car.Duration = car.Duration;
            bank.Car.Passengers.AddRange(car.Passengers!);
            result._banks.Add(definition.Id, bank);
        }
        foreach (var stair in snapshot.Stairs!)
        {
            Require(stair is not null && stair.X is >= 0 and < ConstructionWorld.Width && stair.Direction is >= -1 and <= 1
                && stair.LowerFloor >= ConstructionWorld.MinFloor && stair.LowerFloor < ConstructionWorld.MaxFloor
                && world.Floors.Contains(stair.LowerFloor) && world.Floors.Contains(stair.LowerFloor + 1), "Invalid staircase geometry.");
            Require(!result._stairs.Any(s => s.LowerFloor == stair!.LowerFloor && s.X == stair.X) && !result.HasRoom(stair!.X, stair.LowerFloor)
                && !result.HasRoom(stair.X, stair.LowerFloor + 1)
                && !result._banks.Values.Any(b => b.Definition.X == stair.X && b.Definition.MinFloor <= stair.LowerFloor + 1
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
                && state.RouteVersion >= -1 && state.RouteVersion <= snapshot.TopologyVersion, "Invalid journey timer or route.");
            for (var index = 0; index < state.Route!.Length; index++)
            {
                var leg = state.Route[index];
                Require(leg is not null && Enum.IsDefined(leg.Kind)
                    && CoordinatesValid(leg.FromFloor, leg.FromX) && CoordinatesValid(leg.ToFloor, leg.ToX), "Invalid route leg.");
                Require(index == 0 || (state.Route[index - 1].ToFloor == leg!.FromFloor
                    && state.Route[index - 1].ToX == leg.FromX), "Disconnected route legs.");
                if (leg!.Kind == RouteKind.Walk) Require(leg.FromFloor == leg.ToFloor && leg.FromX != leg.ToX && leg.BankId == 0, "Invalid horizontal route.");
                if (leg.Kind == RouteKind.Stair) Require(Math.Abs(leg.FromFloor - leg.ToFloor) == 1 && leg.FromX == leg.ToX && leg.BankId == 0, "Invalid stair route.");
                if (leg.Kind == RouteKind.Elevator) Require(leg.FromFloor != leg.ToFloor && leg.FromX == leg.ToX && leg.BankId > 0, "Invalid elevator route.");
            }
            if (state.Route.Length > 0)
                Require(state.Route[^1].ToFloor == state.DestinationFloor && state.Route[^1].ToX == state.DestinationX, "Route does not reach its destination.");
            var journey = new Journey(state.PersonId, state.Floor, state.X, state.DestinationFloor,
                state.DestinationX, state.Service, state.PatienceTicks)
            {
                State = state.State, Route = state.Route.ToArray(), LegIndex = state.LegIndex,
                RemainingTicks = state.RemainingTicks, LegDuration = state.LegDuration,
                WaitSinceTick = state.WaitSinceTick, TotalWaitTicks = state.TotalWaitTicks, RouteVersion = state.RouteVersion
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
                    Require(result._banks.TryGetValue(leg.BankId, out var bank) && bank.Car.Passengers.Contains(state.PersonId)
                        && bank.Definition.X == leg.FromX && bank.Definition.Stops.Contains(leg.ToFloor)
                        && (state.Service || !bank.Definition.ServiceOnly), "Riding person is missing their car or access rights.");
                if (state.RouteVersion == snapshot.TopologyVersion && snapshot.ObservedWorldTopologyVersion == world.TopologyVersion)
                {
                    if (state.State == JourneyState.Waiting)
                        Require(result._banks.TryGetValue(leg.BankId, out var bank) && !bank.OutOfService
                            && bank.Definition.X == leg.FromX && bank.Definition.Stops.Contains(leg.FromFloor)
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
            result._journeys.Add(state.PersonId, journey);
        }
        var ownership = new HashSet<long>();
        foreach (var bank in result._banks.Values)
            foreach (var id in bank.Car.Passengers)
                Require(ownership.Add(id) && result._journeys.TryGetValue(id, out var journey)
                    && journey.State == JourneyState.Riding && journey.CurrentLeg?.BankId == bank.Definition.Id,
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException($"Invalid transport save: {message}");
    }

    private static bool CoordinatesValid(int floor, int x) => floor is >= ConstructionWorld.MinFloor and <= ConstructionWorld.MaxFloor
        && x is >= 0 and < ConstructionWorld.Width;
}
