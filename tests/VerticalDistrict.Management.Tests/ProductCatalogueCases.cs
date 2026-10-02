using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Simulation;

public static partial class Program
{
    static partial void RegisterProductCatalogueCases(List<(string Name, Action Run)> cases)
    {
        cases.Add(("Product catalogue: original defaults preserve cafe and shop terms and alternatives have explicit tradeoffs", () =>
        {
            Check(Rules.Products.Select(product => product.Id).Order().SequenceEqual(
                new[] { "cafe-classic", "cafe-lunch", "shop-essentials", "shop-gifts" }.Order()), "Unexpected original offering inventory.");
            var food = Rules.For("cafe")!; var shop = Rules.For("shop")!;
            var classic = Rules.DefaultProductFor("Food"); var essentials = Rules.DefaultProductFor("Shop");
            Check(classic.Id == "cafe-classic" && classic.PriceFor(food) == 1800 && classic.ServiceSecondsFor(food) == 180,
                "Default cafe terms changed from the established $18 / 180-second service.");
            Check(essentials.Id == "shop-essentials" && essentials.PriceFor(shop) == 4500 && essentials.ServiceSecondsFor(shop) == 120,
                "Default shop terms changed from the established $45 / 120-second service rule.");
            Check(classic.DemandPercent == 100 && classic.LunchDemandPercent == 100
                && essentials.DemandPercent == 100 && essentials.LunchDemandPercent == 100,
                "Default products altered baseline demand.");
            var lunch = Rules.ProductFor("cafe-lunch")!; var gifts = Rules.ProductFor("shop-gifts")!;
            Check(lunch.PriceFor(food) == 2700 && lunch.ServiceSecondsFor(food) == 270
                && lunch.DemandPercent == 80 && lunch.LunchDemandPercent == 160, "Lunch menu lost its declared tradeoffs.");
            Check(gifts.PriceFor(shop) == 7200 && gifts.ServiceSecondsFor(shop) == 216
                && gifts.DemandPercent == 70 && gifts.LunchDemandPercent == 100, "Gift assortment lost its declared tradeoffs.");
        }));
        cases.Add(("Product catalogue: current JSON requires an explicit nonempty product list", () =>
        {
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => root.Remove("products")), Catalog), "products");
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => root["products"] = null), Catalog), "Products");
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => root["products"] = new JsonArray()), Catalog), "Products");
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => root["products"]![0] = null), Catalog), "product definition", "null");
        }));
        cases.Add(("Product catalogue: invalid duplicate and unsupported product identities report the affected entry", () =>
        {
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => root["products"]!.AsArray().Add(ProductNode(root, "cafe-classic").DeepClone())), Catalog), "cafe-classic", "duplicate");
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => ProductNode(root, "cafe-lunch")["id"] = "Bad Product"), Catalog), "Bad Product", "IDs");
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => ProductNode(root, "cafe-lunch")["model"] = "Condo"), Catalog), "cafe-lunch", "unsupported");
        }));
        cases.Add(("Product catalogue: stable defaults and their business-model references must exist and agree", () =>
        {
            foreach (var id in new[] { "cafe-classic", "shop-essentials" })
                CatalogueReject(() => SimulationRules.Load(RulesEdit(root => root["products"]!.AsArray().Remove(ProductNode(root, id))), Catalog), id, "default product");
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => ProductNode(root, "cafe-classic")["model"] = "Shop"), Catalog), "cafe-classic", "mismatched");
            var noFood = ContentCatalog.Load(CatalogueEdit(root => CatalogueFacility(root, "cafe")["operations"]!["model"] = "Public"));
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => CatalogueRule(root, "cafe")["model"] = "Public"), noFood), "cafe-classic", "no matching facility");
        }));
        cases.Add(("Product catalogue: omitted fields and blank descriptive provenance cannot silently default", () =>
        {
            foreach (var field in new[] { "id", "model", "name", "category", "pricePercent", "servicePercent", "demandPercent", "lunchDemandPercent", "provenance" })
                CatalogueReject(() => SimulationRules.Load(RulesEdit(root => ProductNode(root, "cafe-lunch").Remove(field)), Catalog), field);
            foreach (var field in new[] { "name", "category", "provenance" })
            {
                CatalogueReject(() => SimulationRules.Load(RulesEdit(root => ProductNode(root, "cafe-lunch")[field] = " "), Catalog), "cafe-lunch", field);
                CatalogueReject(() => SimulationRules.Load(RulesEdit(root => ProductNode(root, "cafe-lunch")[field] = null), Catalog), "cafe-lunch", field);
            }
        }));
        cases.Add(("Product catalogue: percentage bounds and malformed values fail with useful diagnostics", () =>
        {
            foreach (var (field, maximum) in new[] { ("pricePercent", 500), ("servicePercent", 500), ("demandPercent", 200), ("lunchDemandPercent", 200) })
            foreach (var value in new[] { 0, maximum + 1 })
                CatalogueReject(() => SimulationRules.Load(RulesEdit(root => ProductNode(root, "shop-gifts")[field] = value), Catalog), "shop-gifts", field);
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => ProductNode(root, "shop-gifts")["pricePercent"] = 1.5), Catalog), "pricePercent");
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => ProductNode(root, "shop-gifts")["pricePercents"] = 160), Catalog), "pricePercents");
        }));
        cases.Add(("Product catalogue: fractional prices round down but physical service rounds up within validated bounds", () =>
        {
            var business = Rules.For("cafe")! with { PriceMinor = 1, ServiceSeconds = 1 };
            var lunch = Rules.ProductFor("cafe-lunch")!;
            Check(lunch.PriceFor(business) == 1 && lunch.ServiceSecondsFor(business) == 2,
                "Fractional quote rounding created money or instantaneous fractional service.");
            var bounded = lunch with { PricePercent = 500, ServicePercent = 500 };
            Check(bounded.PriceFor(business with { PriceMinor = 100_000_000 }) == 100_000_000
                && bounded.ServiceSecondsFor(business with { ServiceSeconds = 3600 }) == ProductRule.MaximumAgreedServiceSeconds,
                "Validated product multipliers escaped the asking-price or saved-service limits.");
            Check((lunch with { PricePercent = 1, ServicePercent = 1 }).PriceFor(business with { PriceMinor = 0 }) == 0
                && (lunch with { ServicePercent = 1 }).ServiceSecondsFor(business) == 1,
                "Explicit free pricing or the minimum real service second was lost.");
        }));
        cases.Add(("Product catalogue: added offerings and JSON reordering preserve stable defaults and pure lookups", () =>
        {
            var rules = SimulationRules.Load(RulesEdit(root =>
            {
                var variant = ProductNode(root, "cafe-lunch").DeepClone(); variant["id"] = "cafe-seasonal";
                root["products"]!.AsArray().Add(variant);
                root["products"] = new JsonArray(root["products"]!.AsArray().Reverse().Select(node => node!.DeepClone()).ToArray());
            }), Catalog);
            var before = JsonSerializer.Serialize(rules, SimulationRules.JsonOptions);
            Check(rules.DefaultProductFor("Food").Id == "cafe-classic" && rules.DefaultProductFor("Shop").Id == "shop-essentials"
                && rules.ProductsFor("Food").Count == 3 && rules.ProductsFor("Shop").Count == 2
                && rules.ProductsFor("Condo").Count == 0 && rules.ProductFor("unknown-product") == null,
                "A new offering or changed array order changed defaults or leaked products into an unsupported model.");
            Check(before == JsonSerializer.Serialize(rules, SimulationRules.JsonOptions), "Product lookups modified rule state.");
        }));
    }

    private static JsonObject ProductNode(JsonObject root, string id) => root["products"]!.AsArray()
        .Single(node => node!["id"]!.GetValue<string>() == id)!.AsObject();
}
