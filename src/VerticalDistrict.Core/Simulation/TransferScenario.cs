using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

/// <summary>A costed public/service commute whose upper offices require two elevator rides.</summary>
public static class TransferScenario
{
    public const int LowerBankId = 1, UpperBankId = 2, TransferFloor = 9, TopFloor = 19;

    public static GameSession Create(ContentCatalog catalog, SimulationRules rules, LocationCatalog locations,
        string locationId = "tokyo", string siteId = "central")
    {
        var game = new GameSession(catalog, rules, locations, locationId, siteId);
        for (var floor = 1; floor <= TopFloor; floor++) Must(game.World.BuildFloor(floor));
        Must(game.BuildRoom("lobby", 0, 0)); Must(game.BuildRoom("service-room", 6, 0));
        Must(game.BuildRoom("lobby", 16, TransferFloor));
        for (var i = 0; i < 3; i++) Must(game.BuildRoom("office", i * 5, TopFloor));
        Must(game.Transport.InstallBank(new BankDefinition(LowerBankId, 27, 0, TransferFloor,
            [0, TransferFloor], 6), rules.ElevatorCostMinor));
        Must(game.Transport.AddCar(LowerBankId, 2, 28, rules.ElevatorCostMinor));
        Must(game.Transport.InstallBank(new BankDefinition(UpperBankId, 25, TransferFloor, TopFloor,
            [TransferFloor, TopFloor], 6), rules.ElevatorCostMinor));
        return game;
    }

    private static void Must(CommandResult result)
    { if (!result.Success) throw new InvalidOperationException("Cannot construct the transfer scenario: " + result.Message); }
}
