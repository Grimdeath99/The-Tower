using System.Globalization;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

/// <summary>
/// A view of one existing person, with no simulation advancement or reservations. CurrentWaitTicks
/// is zero outside Waiting. NextActionTick is an absolute deadline only for Visiting or Working;
/// an arriving person's ActionAt is an internal duration and is never presented as a deadline.
/// </summary>
public sealed record PersonInspection(PersonState Person, JourneyView Journey, int PatienceTicks,
    long CurrentWaitTicks, string DestinationLabel, string LocationLabel, long? NextActionTick);

public sealed record HallCallInspection(int Floor, int Direction, int WaitingCount, long OldestWaitTicks);
public sealed record BankInspection(BankView Bank, CarView Car, IReadOnlyList<HallCallInspection> HallCalls,
    IReadOnlyList<int> PassengerStops);

public sealed partial class GameSession
{
    /// <summary>Returns null once the stable ID has departed; reading never retries or creates a journey.</summary>
    public PersonInspection? InspectPerson(long id)
    {
        if (!_people.TryGetValue(id, out var person) || Transport.JourneyFor(id) is not { } journey) return null;
        var wait = journey.State == JourneyState.Waiting ? Math.Max(0, Tick - journey.WaitSinceTick) : 0;
        long? nextAction = person.Activity is PersonActivity.Visiting or PersonActivity.Working ? person.ActionAt : null;
        return new PersonInspection(person, journey, journey.PatienceTicks, wait,
            InspectionDestination(person, journey), InspectionLocation(person, journey), nextAction);
    }

    /// <summary>Hall calls and passenger requests are derived from current logical ownership, including transfer legs.</summary>
    public BankInspection? InspectBank(int id)
    {
        var bank = Transport.Banks.FirstOrDefault(view => view.Definition.Id == id);
        var car = Transport.Cars.FirstOrDefault(view => view.BankId == id);
        if (bank is null || car is null) return null;
        var journeys = Transport.Journeys.Where(journey => journey.BankId == id).ToArray();
        var hallCalls = journeys.Where(journey => journey.State == JourneyState.Waiting && journey.NextStopFloor.HasValue)
            .GroupBy(journey => (journey.Floor, Direction: Math.Sign(journey.NextStopFloor!.Value - journey.Floor)))
            .OrderBy(group => group.Key.Floor).ThenBy(group => group.Key.Direction)
            .Select(group => new HallCallInspection(group.Key.Floor, group.Key.Direction, group.Count(),
                group.Max(journey => Math.Max(0, Tick - journey.WaitSinceTick)))).ToArray();
        var stops = journeys.Where(journey => journey.State == JourneyState.Riding && journey.NextStopFloor.HasValue)
            .Select(journey => journey.NextStopFloor!.Value).Distinct()
            .OrderBy(floor => car.Direction < 0 ? -floor : floor).ToArray();
        return new BankInspection(bank, car, Array.AsReadOnly(hallCalls), Array.AsReadOnly(stops));
    }

    private string InspectionDestination(PersonState person, JourneyView journey)
    {
        if (person.Activity == PersonActivity.Stranded) return "Exit when a route becomes available";
        if (person.Activity == PersonActivity.Leaving)
        {
            var actualExit = World.Rooms.FirstOrDefault(room => room.DefinitionId == "lobby"
                && room.Floor == journey.DestinationFloor && room.X == journey.DestinationX);
            return actualExit is not null ? "Exit via " + InspectionRoomLabel(actualExit)
                : $"Exit at floor {journey.DestinationFloor}, column {journey.DestinationX}";
        }
        if (person.Activity == PersonActivity.Returning)
            return Room(person.RoomId) is { } depot ? "Return to " + InspectionRoomLabel(depot) : "Service depot unavailable";
        if (person.Role == "Staff" && person.ServiceTargetId is { } serviceTarget)
            return Room(serviceTarget) is { } target ? "Service at " + InspectionRoomLabel(target) : "Service destination unavailable";
        return Room(person.RoomId) is { } room ? InspectionRoomLabel(room)
            : $"Floor {journey.DestinationFloor}, column {journey.DestinationX}";
    }

    private string InspectionLocation(PersonState person, JourneyView journey)
    {
        var floor = journey.DrawFloor.ToString("0.00", CultureInfo.InvariantCulture);
        if (journey.State == JourneyState.Riding)
            return $"Elevator #{journey.BankId}, floor {floor}";
        if (journey.State == JourneyState.Waiting)
            return $"Floor {journey.Floor}, waiting for elevator #{journey.BankId}";
        if (person.Activity is PersonActivity.Visiting or PersonActivity.Working)
        {
            var occupiedId = person.Role == "Staff" ? person.ServiceTargetId ?? person.RoomId : person.RoomId;
            if (Room(occupiedId) is { } occupied) return "Inside " + InspectionRoomLabel(occupied);
        }
        return $"Floor {floor}, column {journey.X.ToString("0.0", CultureInfo.InvariantCulture)}";
    }

    private string InspectionRoomLabel(RoomInstance room)
        => $"{World.Catalog.Get(room.DefinitionId).Name} #{room.Id} (floor {room.Floor})";
}
