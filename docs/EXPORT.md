# Windows desktop export

The project has a Windows x86_64 export preset using its existing Compatibility renderer, Godot 4.7.2 .NET, and the pinned .NET SDK. The build script validates the exact installed engine version and matching export-template versions. It stores templates under `.build/templates/4.7.2.stable.mono`; it does not install templates globally or change the engine version.

Prepare templates without building the evolving game:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\export-windows.ps1 -AllowDownloads -PrepareTemplatesOnly
```

Build, export, and run the exported construction smoke test:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\export-windows.ps1 -AllowDownloads
```

`-AllowDownloads` permits fetching the official matching template archive and missing Microsoft 8.0.28 Windows runtime packages from NuGet's official flat-container endpoint. Packages are cached under `.build/templates/runtime-packages`, and restore uses local package feeds. The flag can be omitted after these are cached. An already downloaded official archive can be supplied with `-TemplateArchivePath`; `-GodotPath` selects an installed engine when automatic discovery cannot find it. The export-template archive contains other platforms, but the script extracts only the four named Windows x86_64 files after checking `templates/version.txt`.

The output is `artifacts/windows/VerticalDistrict.exe` plus `VerticalDistrict.pck` and the generated .NET runtime data directory. Keep that complete folder together. The script exports into a fresh staging directory and only publishes it after the exported smoke passes. Any previous export is retained as `artifacts/windows.previous-<id>`. Export uses the engine's `ExportRelease` publishing pipeline and excludes C# source contents, tests, documentation, build artifacts, and editor-only addon directories. The Main scene, its dependencies, and `Data/*.json` are included. The installed Godot AI export plugin removes its editor helper autoload from the exported settings without modifying `project.godot`; required runtime-helper dependencies remain eligible in the preset if needed.

The script records restore, build, export, and outside-editor smoke logs in `artifacts/export-*.log`. It requires a successful process exit, actual executable/PCK/game-assembly outputs, no unexpected engine errors, and the current `CONSTRUCTION_SMOKE_PASS` marker. It does not infer success from an old artifact. Export and smoke child processes use workspace-local `APPDATA` and `LOCALAPPDATA` under `.build/export-profile`; the user's editor settings and saved games remain untouched. Smoke-generated saves are retained under `artifacts/export-smoke-evidence-<id>` rather than distributed with the game. The known Windows certificate-store startup diagnostic is recorded and excluded from the smoke error filter because this game path is offline.

After automated checks, launch the executable normally and exercise construction, transport, operation controls, manual save/load, and pause/speed controls. A headless exported smoke does not establish graphical performance or complete release acceptance.

Verified on 2026-09-30: the financial-management `ExportRelease` build completed with zero warnings/errors, Windows export completed, and the fresh exported executable passed its expanded headless smoke outside the editor. The smoke covered scene/catalogue loading, viewport input, atomic construction, selection/salvage, UI click isolation, camera controls, office journeys, session save/load, transport controls, reports, and text scaling. It also exercised the 24-worker commute scenario with a six-seat elevator and no stairs, actual person/car viewport selection, the rider list, inspection without advancing or changing the paused simulation, and clearing stale selection when loading the active commute. The new exported checks passed the finance overview, transaction filtering, scheduled bills, and save/load continuity across midnight settlement. The package includes session schema 3 with ledger-derived financial reports, frozen pending invoices, schema-2 migration, and the shared 4-floor/20-floor operating presets. Before this export, the integrated verifier passed all 139 cases and the expanded Godot scene; the dedicated finance suite includes the seven-day operating baseline and separate 20-floor continuation fixture.

See [build evidence](../artifacts/export-build.log), [export evidence](../artifacts/export-windows.log), and the [exported-process smoke log](../artifacts/export-smoke-process.log). The complete distribution contains 189 files totaling 189,635,538 bytes. The package is a playable preview; these checks do not establish production content completion, graphical performance, or full interactive exported acceptance.

References: [official Godot 4.7.2 archive](https://godotengine.org/download/archive/4.7.2-stable/), [Windows export documentation](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_windows.html), and the [Godot 4.7.2 C# export implementation](https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs).
