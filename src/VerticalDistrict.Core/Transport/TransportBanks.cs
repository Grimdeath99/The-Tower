namespace VerticalDistrict.Core.Transport;

public sealed partial class TransportSystem
{
    private static ShaftDefinition[] ShaftDefinitions(BankDefinition definition)
        => [new(1, definition.X), .. definition.AdditionalShafts ?? []];
    private static bool IsUnavailable(Bank bank, Car car) => bank.OutOfService || car.OutOfService;

    public CommandResult AddCar(int bankId, int carId, int x, long costMinor = 0)
    {
        if (!_banks.TryGetValue(bankId, out var bank)) return Fail("Bank does not exist.");
        if (carId <= 1 || bank.Cars.ContainsKey(carId)) return Fail("Choose an unused car/shaft ID greater than 1; ID 1 is the primary shaft.");
        var definition = bank.Definition with { AdditionalShafts = [.. bank.Definition.AdditionalShafts ?? [], new(carId, x)] };
        var valid = ValidateBank(definition, bankId); if (!valid.Success) return valid;
        var payment = Charge(costMinor, $"Installed car {carId} and its separate shaft in bank {bankId}.");
        if (!payment.Success) return payment;
        bank.Definition = Clone(definition);
        var car = new Car(carId, x, definition.Stops.Min()); bank.Cars.Add(carId, car);
        ApplyCarAvailability(bank, car);
        if (!IsUnavailable(bank, car)) ReleaseWaitingAssignments(bank);
        Changed();
        return new(true, "Coordinated car installed in its own shaft.", costMinor, carId);
    }

    public CommandResult RemoveCar(int bankId, int carId)
    {
        if (!_banks.TryGetValue(bankId, out var bank) || !bank.Cars.TryGetValue(carId, out var car)) return Fail("Car does not exist.");
        if (carId == 1) return Fail("The primary shaft defines this bank. Remove the entire bank to remove its primary car.");
        if (car.State == CarState.Traveling || car.Passengers.Count != 0 || AssignedOrWaiting(bankId).Any(j => j.AssignedCarId == carId))
            return Fail("Stop and unload this car, and resolve its assigned waiting or approaching passengers before removing its shaft.");
        bank.Cars.Remove(carId);
        bank.Definition = bank.Definition with { AdditionalShafts = (bank.Definition.AdditionalShafts ?? []).Where(s => s.Id != carId).ToArray() };
        Changed(); return Ok("Car and its separate shaft removed.");
    }

    public CommandResult SetCarOutOfService(int bankId, int carId, bool outOfService)
    {
        if (!_banks.TryGetValue(bankId, out var bank) || !bank.Cars.TryGetValue(carId, out var car)) return Fail("Car does not exist.");
        if (car.OutOfService == outOfService) return Ok("Car service state already matches.");
        car.OutOfService = outOfService; ApplyCarAvailability(bank, car);
        if (!IsUnavailable(bank, car)) ReleaseWaitingAssignments(bank);
        Changed();
        return Ok(outOfService ? "Car disabled; a moving car will land and unload safely before stopping. Waiting people can use another car."
            : bank.OutOfService ? "Car restored, but the whole bank remains disabled." : "Car restored and available for coordinated calls.");
    }

    private void ApplyCarAvailability(Bank bank, Car car)
    {
        if (IsUnavailable(bank, car) && car.State != CarState.Traveling)
        {
            Evacuate(bank, car); car.State = CarState.OutOfService; car.Timer = 0;
        }
        else if (!IsUnavailable(bank, car) && car.State == CarState.OutOfService) car.State = CarState.Idle;
    }

    // A deliberate increase in fleet capacity can help the existing backlog. Preserve
    // queue ages and physical positions; already walking approaches remain committed.
    private void ReleaseWaitingAssignments(Bank bank)
    {
        foreach (var journey in Waiting(bank.Definition.Id).Where(j => j.AssignedCarId != null))
        {
            journey.AssignedCarId = null;
            journey.Route[journey.LegIndex] = journey.CurrentLeg! with { CarId = 0 };
        }
    }

    // An assigned approach remains a physical Walk leg immediately before its elevator leg.
    // This is a derived projection over ordinary journeys, not a second passenger queue.
    private static RouteLeg? HallJourneyLeg(Journey journey)
    {
        if (journey.CurrentLeg is { Kind: RouteKind.Elevator } elevator) return elevator;
        return journey.AssignedCarId != null && journey.State == JourneyState.Walking
            && journey.LegIndex + 1 < journey.Route.Length && journey.Route[journey.LegIndex + 1].Kind == RouteKind.Elevator
            ? journey.Route[journey.LegIndex + 1] : null;
    }
    private IEnumerable<Journey> AssignedOrWaiting(int bankId) => _journeys.Values.Where(j =>
        (j.State == JourneyState.Waiting || j.State == JourneyState.Walking && j.AssignedCarId != null)
        && HallJourneyLeg(j)?.BankId == bankId);

    private void AssignHallCalls(Bank bank)
    {
        var available = bank.Cars.Values.Where(car => !IsUnavailable(bank, car)).ToArray();
        if (available.Length == 0) return;
        foreach (var journey in Waiting(bank.Definition.Id).Where(j => j.AssignedCarId == null)
            .OrderBy(j => j.WaitSinceTick).ThenBy(j => j.PersonId).ToArray())
        {
            // Commit once. Current queues never reshuffle merely because a nearer car appears.
            // Existing load, capacity and destinations influence the next unassigned call.
            var car = available.OrderBy(candidate => PickupEstimate(bank, candidate, journey)).ThenBy(candidate => candidate.Id).First();
            var leg = journey.CurrentLeg!; var route = journey.Route.ToList();
            route[journey.LegIndex] = leg with { FromX = car.X, ToX = car.X, CarId = car.Id };
            if (car.X != leg.ToX)
                route.Insert(journey.LegIndex + 1, new(RouteKind.Walk, leg.ToFloor, car.X, leg.ToFloor, leg.ToX));
            if (car.X != leg.FromX)
                route.Insert(journey.LegIndex, new(RouteKind.Walk, leg.FromFloor, leg.FromX, leg.FromFloor, car.X));
            journey.Route = route.ToArray(); journey.AssignedCarId = car.Id;
            BeginLeg(journey);
        }
    }

    private long PickupEstimate(Bank bank, Car car, Journey journey)
    {
        var definition = bank.Definition;
        var committed = AssignedOrWaiting(definition.Id).Count(j => j.AssignedCarId == car.Id) + car.Passengers.Count;
        var cycles = committed / definition.Capacity;
        var floor = car.State == CarState.Traveling ? car.TargetFloor : car.Floor;
        var estimate = (long)car.Timer + Math.Abs(floor - journey.Floor) * definition.TravelTicksPerFloor
            + (long)cycles * ((definition.MaxFloor - definition.MinFloor) * definition.TravelTicksPerFloor * 2 + definition.DoorTicks * 4 + 4);
        if (car.Passengers.Count > 0)
        {
            var destination = car.Passengers.Select(id => _journeys[id].CurrentLeg!.ToFloor).OrderBy(f => Math.Abs(f - floor)).ThenBy(f => f).First();
            var direction = Math.Sign(destination - floor);
            if (Math.Sign(journey.CurrentLeg!.ToFloor - journey.Floor) != direction || Math.Sign(journey.Floor - floor) != direction)
                estimate += Math.Abs(destination - floor) * definition.TravelTicksPerFloor + definition.DoorTicks * 2 + 2;
        }
        return estimate;
    }
}
