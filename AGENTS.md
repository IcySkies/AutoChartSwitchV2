# Repository Instructions

## Desktop Program Changes

- After changing desktop-program source, project, or solution files, run `dotnet build AutoChartSwitchV2.sln --configuration Release` before considering the work complete.
- Resolve all build errors and report the Release build result in the final response.

## VividStasis Mod Changes

- The authoritative GameMaker mod source is `../In-gameInfoAPI`; never edit the loader mirror directly.
- After changing the mod, run `../In-gameInfoAPI/tools/Sync-ModToLoader.ps1` and `../In-gameInfoAPI/tools/Verify-ModSync.ps1`.
- The installed loader package is `../SourceFiles/VividStasisModLoader/mods/VividStasisGameInfoAPI v3.0.0`.
- After synchronization, run `../SourceFiles/VividStasisModLoader/vividstasisModLoader.exe` against the configured vivid/stasis installation before considering the work complete.
- Verify the newest loader log ends with `Patch flow completed` and contains no missing-entry, patch, or compile errors.
- Report the loader run and its validation result in the final response.
