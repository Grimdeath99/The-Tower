using System.Text.Json.Nodes;

namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    // Historical sessions contain exactly one car per bank. Hydration is deliberately limited to
    // absent new fields: relabeling a current multi-car save as an old version is not a migration.
    private static void UpgradeLegacyTransportJson(JsonObject transport)
    {
        Require(transport["schemaVersion"]?.GetValue<int>() == 1, "Legacy sessions require their original transport schema.");
        Require(transport["banks"] is JsonArray && transport["journeys"] is JsonArray, "Missing legacy transport collections.");
        foreach (var node in transport["banks"]!.AsArray())
        {
            Require(node is JsonObject, "Missing legacy bank.");
            var bank = node!.AsObject();
            Require(bank["definition"] is JsonObject && bank["car"] is JsonObject && !bank.ContainsKey("additionalCars"), "Invalid legacy bank state.");
            var definition = bank["definition"]!.AsObject();
            var car = bank["car"]!.AsObject();
            Require(!definition.ContainsKey("additionalShafts") && !car.ContainsKey("carId") && !car.ContainsKey("isOutOfService"),
                "Legacy banks cannot contain newer car or shaft state.");
            definition["additionalShafts"] = null;
            car["carId"] = 1;
            car["isOutOfService"] = false;
            bank["additionalCars"] = new JsonArray();
        }
        foreach (var node in transport["journeys"]!.AsArray())
        {
            Require(node is JsonObject, "Missing legacy journey.");
            var journey = node!.AsObject();
            Require(!journey.ContainsKey("assignedCarId") && !journey.ContainsKey("completedRides") && journey["route"] is JsonArray,
                "Legacy journeys cannot contain newer dispatch state.");
            var index = journey["legIndex"]?.GetValue<int>() ?? -1;
            var route = journey["route"]!.AsArray();
            Require(index >= 0 && index <= route.Count, "Invalid legacy route progress.");
            var assigned = journey["state"]?.GetValue<string>() is "Waiting" or "Riding";
            var completed = 0;
            for (var legIndex = 0; legIndex < route.Count; legIndex++)
            {
                Require(route[legIndex] is JsonObject, "Missing legacy route leg.");
                var leg = route[legIndex]!.AsObject();
                Require(!leg.ContainsKey("carId"), "Legacy routes cannot contain newer car assignments.");
                var elevator = leg["kind"]?.GetValue<string>() == "Elevator";
                // Previous legs are historical receipts of a completed ride, not live assignments.
                leg["carId"] = elevator && (legIndex < index || legIndex == index && assigned) ? 1 : 0;
                if (elevator && legIndex < index) completed++;
            }
            journey["assignedCarId"] = assigned ? JsonValue.Create(1) : null;
            journey["completedRides"] = completed;
        }
        transport["schemaVersion"] = 2;
    }
}
