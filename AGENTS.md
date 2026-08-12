# Repository Instructions

## Desktop Program Changes

- After changing desktop-program source, project, or solution files, run `dotnet build AutoChartSwitchV2.sln --configuration Release` before considering the work complete.
- Resolve all build errors and report the Release build result in the final response.

## VividStasis Mod Changes

- After changing any file under `../SourceFiles/VividStasisModLoader/mods/AutoChartSwitch Game Bridge v2.0.0/`, run `../SourceFiles/VividStasisModLoader/vividstasisModLoader.exe` against the configured vivid/stasis installation before considering the work complete.
- Verify the newest loader log ends with `Patch flow completed` and contains no missing-entry, patch, or compile errors.
- Report the loader run and its validation result in the final response.
