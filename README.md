# AutoChartSwitch V2

AutoChartSwitch V2 monitors a live `vivid/stasis` session and publishes the
currently highlighted chart to OBS. It is intended to be one reusable part of
a larger project.

## Runtime flow

- Song-select changes publish chart metadata immediately.
- Worldcross lobby decisions publish chart metadata for both the player who
  chose the song and clients that receive the choice online.
- `OPENING`, `MIDDLE`, `FINALE`, `ENCORE`, and `BACKSTAGE` are published as
  `OPN`, `MID`, `FIN`, `ENC`, and `BKS`.
- Unknown, custom, blank, or missing difficulty names are published as
  `SHATTER`.
- The chart loading screen switches to the configured entry scene.
- Gameplay start refreshes the selected chart metadata without switching scenes.
- The visible chart-exit transition switches to the configured exit scene.
- Gameplay exits that skip the transition reset lifecycle state without switching scenes.
- No queue, showcase-video playback tracking, or source-update delay is used.

## Build and test

```powershell
dotnet restore AutoChartSwitchV2.sln --disable-parallel
dotnet build AutoChartSwitchV2.sln --configuration Release --no-restore
dotnet test tests\AutoChartSwitch.Tests\AutoChartSwitch.Tests.csproj --configuration Release --no-restore
```

Run the app with:

```powershell
dotnet run --project src\AutoChartSwitch.App\AutoChartSwitch.App.csproj
```

## Development source files

Large vivid/stasis development files are kept in the sibling `SourceFiles`
directory:

- `..\SourceFiles\VividStasisModLoader`
- `..\SourceFiles\UndertaleModTool`
- `..\SourceFiles\data.win`
- `..\SourceFiles\VIVIDSTASIS_MOD_GUIDE.md`

## Game bridge

The authoritative mod source is `..\In-gameInfoAPI`. Run
`..\In-gameInfoAPI\tools\Sync-ModToLoader.ps1` and
`..\In-gameInfoAPI\tools\Verify-ModSync.ps1`, then enable the installed
`..\SourceFiles\VividStasisModLoader\mods\VividStasisGameInfoAPI v3.0.0`
package and run the loader against the supported `vivid/stasis` installation.
The separately installed `VividStasisGameInfoRelay.exe` owns `127.0.0.1:28745`.
Start the relay independently of AutoChartSwitch V2. The app locates its
`AutoChartSwitchV2/bridge-relay.json` discovery file under the configured game
directory or beside a running relay executable, then connects to its subscriber
port and receives length-prefixed UTF-8 JSON events. The relay can start before
or after the game or app, and the app waits and reconnects asynchronously.

The app writes the relative staging folder `AutoChartSwitchV2/Jackets` to
`bridge.ini`. GameMaker stores exports from that path in its
`%LOCALAPPDATA%\VIVIDSTASIS` sandbox. The mod exports selected jackets there on
demand as 500x500 nearest-neighbor PNGs, and the app copies them into the
configured jacket output folder before publishing to OBS. No bulk pre-export
is required. The packaged `Memories_Sacrifice_jacket.png` is copied into the
configured output folder when it does not already contain a fallback.

For Title, Artist, Illustrator, and Charter text, the app uses `HYPixel 9px
MERGED` when every character is available. Unsupported text first uses the
game's formatted variant when present; otherwise its OBS source switches to
`Unifont`. Illustrator and Charter share the Credits source, so either field
can cause that source to use `Unifont`.

The bridge mod targets vivid/stasis `6.2.2.2 [F3D3B703]`. The loader log must
end with `Patch flow completed` and contain no missing-entry or compile errors.

## Jackets and difficulty images

Use `..\SourceFiles\UndertaleModTool\Scripts\Resource Exporters\ExportAutoChartSwitchJackets.csx`
to export embedded `song_*` jacket sprites into the jacket cache configured in
the app. Custom-song jacket paths are resolved relative to the configured game
path. Difficulty images remain external and are resolved by canonical code,
for example `OPN.png`, `MID.png`, or `SHATTER.png`.

## OBS mappings

Configure seven OBS inputs: title text, artist text, credits FreeType text,
difficulty-code text, difficulty-number text, jacket image, and difficulty
image. Missing image assets produce a warning while text and tech stats still
publish.
