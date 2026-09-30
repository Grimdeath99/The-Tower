using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

/// <summary>A paid operating example shared by the playable menu and deterministic acceptance fixtures.</summary>
public static class OperatingExampleScenario
{
    public static GameSession Create(ContentCatalog catalog, SimulationRules rules, LocationCatalog locations,
        string locationId = "tokyo", string siteId = "central", bool sandbox = false, int floorCount = 4)
    {
        if (floorCount is not (4 or 20)) throw new ArgumentOutOfRangeException(nameof(floorCount), "Choose the four-floor or twenty-floor example.");
        var game = new GameSession(catalog, rules, locations, locationId, siteId, sandbox);
        for (var floor = 1; floor < floorCount; floor++) Must(game.World.BuildFloor(floor));
        foreach (var room in new[] { ("lobby", 0), ("cafe", 6), ("service-room", 10) })
            Must(game.BuildRoom(room.Item1, room.Item2, 0));
        // The compact layout is the original operating example. The large variant spreads the same
        // businesses through a genuinely connected 20-floor tower, without changing game limits.
        var officeFloors = floorCount == 4 ? new[] { 1, 1, 1 } : new[] { 1, 6, 12 };
        var homeFloors = floorCount == 4 ? new[] { 2, 2, 2 } : new[] { 4, 10, 15 };
        var hotelFloors = floorCount == 4 ? new[] { 3, 3, 3 } : new[] { 16, 18, 19 };
        for (var i = 0; i < 3; i++) Must(game.BuildRoom("office", i * 5, officeFloors[i]));
        for (var i = 0; i < 3; i++) Must(game.BuildRoom("studio", i * 4, homeFloors[i]));
        for (var i = 0; i < 3; i++) Must(game.BuildRoom("hotel-room", i * 3, hotelFloors[i]));
        Must(game.BuildRoom("utility-room", 14, 0));
        Must(game.BuildRoom("security-room", 18, 0));
        Must(game.Transport.InstallBank(new BankDefinition(1, 27, 0, floorCount - 1,
            Enumerable.Range(0, floorCount).ToArray(), 12, 2, 2), rules.ElevatorCostMinor));
        for (var floor = 0; floor < floorCount - 1; floor++) Must(game.Transport.BuildStair(floor, 29, rules.StairCostMinor));
        return game;
    }

    private static void Must(CommandResult result)
    {
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }
}
