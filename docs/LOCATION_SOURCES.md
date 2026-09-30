# Location eligibility sources

Release snapshot checked **2026-09-30**, stored in `Data/locations.json`. The game reads this local snapshot and does not update geography from the web during play. Refreshing it requires an explicit content update and regression tests. Check date records this research pass; it is not the publication date of every source.

Subway eligibility means an operating passenger railway with actual subway infrastructure in the chosen urban area. A mixed underground/elevated network qualifies through the underground part. Planned systems, monorails, elevated or at-grade-only rail, and road vehicles in tunnels do not automatically qualify.

These are geography and rank permission checks only. They do not implement station construction, arrivals, queues, journeys, capacity, operating demand, or business effects. Any UI that uses these definitions must preserve that distinction.

## Snapshot decisions

| ID | Menu label | Subway status | Basis |
| --- | --- | --- | --- |
| `tokyo` | Tokyo | Eligible | Tokyo Metro is an operating subway. Eligibility does not imply that every fictional parcel has a station connection. |
| `osaka` | Osaka | Eligible | Osaka Metro subway lines qualify; New Tram alone would not establish subway eligibility. |
| `seoul` | Seoul | Eligible | Operating metropolitan subway qualifies. City transport authority corroboration used because the operator English page could not be fetched. |
| `dubai` | Dubai | Eligible | Red and Green lines qualify through operating underground infrastructure. Planned Blue Line infrastructure is not the basis for eligibility. |
| `abu-dhabi` | Abu Dhabi | Unknown | No qualifying operating subway was established from the checked official sources. ART is explicitly rail-less buses. Future or planned metro proposals do not qualify; unverified eligibility remains disabled. |
| `new-york` | New York | Eligible | Operating New York City Subway qualifies; above-ground sections do not exclude its subway infrastructure. |
| `shanghai` | Shanghai | Eligible | Operating Shanghai Metro underground lines qualify. Official city evidence corroborates the operator; the operator English website timed out. |
| `hong-kong` | Hong Kong | Eligible | MTR's operating subway infrastructure qualifies; this is not a claim that every MTR mode or station is underground. |
| `beijing` | Beijing | Eligible | Operating Beijing Subway qualifies. Fictional site availability remains a separate gate. |
| `las-vegas` | Las Vegas | Ineligible | The checked passenger systems do not qualify: elevated Monorail is not a subway and Vegas Loop moves road vehicles in tunnels, not subway trains. |
| `san-francisco` | San Francisco | Eligible | BART and Muni have operating underground infrastructure; mixed surface and elevated sections do not disqualify the network. |
| `chicago` | Chicago | Eligible | CTA qualifies through actual Red and Blue Line subway sections even though the network is called the L. |
| `hawaii` | Hawaii | Ineligible | Hawaii is the requested menu label; these parcels are in Honolulu on Oahu. Skyline's elevated and short at-grade alignment has no subway infrastructure. Hawaii-only docks are optional on a compatible waterfront parcel. |

## Fictional sites and gameplay values

All parcels are invented scenarios; no real address, permission, property, waterfront engineering approval, or actual station connection is asserted. Each location has a `central` and `transit` profile. Eligible networks have a hypothetical connection only on the transit profile. The other eleven locations also have a fictional `waterfront` profile; Beijing and Las Vegas currently have no such profile. Waterfront alone never authorizes a dock outside Hawaii.

The menu label Hawaii is retained from the requested location list, but its sites explicitly refer to Honolulu on Oahu. The rail conclusion applies to that urban area and Skyline, not every place in the state. HART's overview describes the final project as well as its design; its construction page separately distinguishes operating segments from future work. Neither projected extensions nor final-project length were used as proof of operation.

`SubwayNetwork` and site `SubwayConnection` are separate facts. Unknown network status rejects a subway with an explanatory message, even if a future scenario sets a site's hypothetical connection to true. `ValidateDock` always requires the stable `hawaii` location ID and a waterfront site. Normal dock rank is 4, explicitly a draft gate from the master prompt. It is optional and is not a universal progression objective.

No subway rank was specified in the available prompt, so the geography layer uses a provisional minimum of 1; facility definitions may require a higher rank. `relaxRankGate: true` is an explicit sandbox option that changes only these rank checks. It never changes network, location or site restrictions, and invalid ranks outside 1-7 remain invalid.

All demand modifiers are neutral 1.0 with `ProvisionalNeutral` status. They describe no real economic differences and currently produce no simulated demand. Non-neutral values are rejected until a genuine operating demand model is integrated and approved.

Abu Dhabi remains **Unknown**, hence disabled, rather than inventing a definitive negative fact. The authority identifies ART as rail-less buses; the official service pages checked did not positively establish an operating subway. A metro proposal or a news claim about future construction would not resolve that uncertainty. Seoul's operator English page returned a gateway error and Shanghai's timed out; city-government transport pages supply authoritative corroboration instead.

## Authoritative references

### Tokyo (`tokyo`)

- [Tokyo Metro: Route and station information](https://www.tokyometro.jp/lang_en/station/index.html) — checked 2026-09-30. Operator publishes the operating subway map, line information, stations, and transfer search.

### Osaka (`osaka`)

- [Osaka Metro: Subway route map](https://subway-tr.osakametro.co.jp/en/guide/routemap.php) — checked 2026-09-30. Operator identifies the map and current service status as subway service.

### Seoul (`seoul`)

- [Seoul Transport Operation and Information Service: Seoul subway information](https://topis.seoul.go.kr/eng/page/transInfo_1_2.jsp) — checked 2026-09-30. City transport service describes operating subway lines and station guidance.
- [Seoul Metropolitan Government: Public transportation](https://english.seoul.go.kr/service/movement/public-transportation/) — checked 2026-09-30. Current official passenger guide explains use of subway stations, tickets and Seoul Subway application.

### Dubai (`dubai`)

- [Roads and Transport Authority: Metro and tram stations map](https://www.rta.ae/wps/portal/rta/ae/public-transport/metro-stations-map) — checked 2026-09-30. Operating Red and Green metro routes include Union and BurJuman.
- [Roads and Transport Authority: Masar magazine, issue 1](https://rta.ae/wpsv5/links/magazine/masar/RTAmag_I_e.pdf) — checked 2026-09-30. Historical infrastructure description identifies Union Square as an underground transfer station; paired with current operating route evidence.

### Abu Dhabi (`abu-dhabi`)

- [Abu Dhabi Mobility: Automated Rapid Transit project](https://admobility.gov.ae/en/art-project) — checked 2026-09-30. Authority describes ART as electric public buses on a rail-less system, not a subway.
- [Abu Dhabi Mobility: Public transport services](https://admobility.gov.ae/en/automated-rapid-transit) — checked 2026-09-30. Current authority service listing did not establish an operating underground railway. Absence from this page is not treated as conclusive proof of no network.

### New York (`new-york`)

- [Metropolitan Transportation Authority: New York City Subway map](https://www.mta.info/map/5256) — checked 2026-09-30. MTA publishes the current city subway diagram and service lines.

### Shanghai (`shanghai`)

- [Shanghai Municipal Government: Shanghai Metro extends reach beyond 900 km](https://english.shanghai.gov.cn/en-Latest-WhatsNew/20251223/43ad9e0a91d74169a5f13f671c38bdb6.html) — checked 2026-09-30. Official city report of operating metro extension includes underground stations and distinguishes a station not yet opened.
- [Shanghai Municipal Government: How to take metro in Shanghai](https://english.shanghai.gov.cn/en-Transportation/20231214/c727f5e15eff4b8c9340651dd95f3f7c.html) — checked 2026-09-30. Current official passenger guide describes metro ticketing and journeys.

### Hong Kong (`hong-kong`)

- [MTR Corporation: System map and station layouts](https://www.mtr.com.hk/en/customer/services/system_map.html) — checked 2026-09-30. Operator lists operating urban lines and their station layouts, including Central, Admiralty and Tsim Sha Tsui.
- [MTR Corporation: Travel tips during typhoons](https://www.mtr.com.hk/en/customer/main/typhoon_readiness.html) — checked 2026-09-30. Operator explicitly distinguishes underground railway services from open sections.

### Beijing (`beijing`)

- [Beijing Subway: Subway map and first/last trains](https://www.bjsubway.com/en/index.html) — checked 2026-09-30. Operator publishes subway map and operating first/last train information.

### Las Vegas (`las-vegas`)

- [Las Vegas Monorail: Official route map](https://www.lvmonorail.com/route-map/) — checked 2026-09-30. Operator describes an elevated monorail route.
- [The Boring Company: Loop](https://boringcompany.com/loop) — checked 2026-09-30. Operator describes passengers transported by Tesla vehicles in tunnels. Underground road transport does not establish a subway railway.

### San Francisco (`san-francisco`)

- [Bay Area Rapid Transit: BART Wireless Technology Program](https://www.bart.gov/about/projects/wireless) — checked 2026-09-30. BART describes completed cellular coverage in SFMTA tunnels and underground stations including Central Subway.
- [Bay Area Rapid Transit: Civic Center / UN Plaza station](https://www.bart.gov/stations/civc) — checked 2026-09-30. Operator lists current San Francisco train services and station departures.

### Chicago (`chicago`)

- [Chicago Transit Authority: L service overview](https://www.transitchicago.com/assets/1/6/ctamap_LMap.pdf) — checked 2026-09-30. Operator identifies Red and Blue Line service via downtown subway.
- [Chicago Transit Authority: Lake Red Line station](https://lapi.transitchicago.com/station/lake/) — checked 2026-09-30. Operator explicitly categorizes the operating Lake station as Subway and gives service arrivals.

### Hawaii (`hawaii`)

- [Honolulu Department of Transportation Services: Skyline](https://www.honolulu.gov/dts/skyline/) — checked 2026-09-30. Operating Skyline passenger information and schedules.
- [Honolulu Authority for Rapid Transportation: The project](https://honolulutransit.org/about/the-project/) — checked 2026-09-30. Authority describes an elevated alignment except for a short at-grade section; this does not qualify as subway infrastructure.
- [Honolulu Authority for Rapid Transportation: Construction overview](https://honolulutransit.org/construction/) — checked 2026-09-30. Authority separates currently operating segments from future construction. Planned service is not counted.

## Verification

`dotnet run --project tests/VerticalDistrict.Geography.Tests -c Release` checks all 13 locations, each available fictional site, all seven ranks, both sandbox settings, independent site/network facts, Hawaii-only waterfront restrictions, explicit unknown status, immutable read-only validation, and malformed catalog data.

These checks establish data validity and permission behavior. They do not establish playable progression in any location; the required end-to-end progression routes and actual transit arrivals remain dependent work.
