using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

/// <summary>
/// Shared playable commute example. All structures use ordinary costed commands and the
/// normal starting budget. Its single elevator is the only route to the offices.
/// </summary>
public static class OfficeCommuteScenario
{
    public const int BankId = 1;
    public const int ShaftX = 27;
    public const int TopFloor = 3;
    public const int DefaultOfficeCount = 3;
    public const int DefaultElevatorCapacity = 6;

    public static GameSession Create(ContentCatalog catalog, SimulationRules rules, LocationCatalog locations,
        int officeCount = DefaultOfficeCount, int elevatorCapacity = DefaultElevatorCapacity,
        string locationId = "tokyo", string siteId = "central")
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(locations);
        if (officeCount is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(officeCount), "Choose one through six offices.");
        if (elevatorCapacity is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(elevatorCapacity), "Elevator capacity must be from 1 through 256.");
        var session = new GameSession(catalog, rules, locations, locationId, siteId, sandbox: false);
        for (var floor = 1; floor <= TopFloor; floor++) Require(session.World.BuildFloor(floor));
        Require(session.BuildRoom("lobby", 0, 0));
        Require(session.Transport.InstallBank(new BankDefinition(BankId, ShaftX, 0, TopFloor,
            [0, 1, 2, 3], elevatorCapacity), rules.ElevatorCostMinor));
        var width = catalog.Get("office").Width;
        for (var index = 0; index < officeCount; index++)
            Require(session.BuildRoom("office", index % 3 * width, TopFloor - index / 3));
        return session;
    }

    private static void Require(CommandResult result)
    {
        if (!result.Success) throw new InvalidOperationException("Cannot construct the office commute scenario: " + result.Message);
    }
}
