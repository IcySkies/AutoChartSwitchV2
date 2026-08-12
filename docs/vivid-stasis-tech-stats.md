# vivid/stasis tech-stat renderer reference

This documents the current `vivid/stasis` song-select tech-stat renderer as
decompiled from the supplied GameMaker data. The source is a GMS2 VM build
(bytecode 17), not YYC. The relevant current event is
`gml_Object_o_songselect_main_Draw_0`; the separate `o_techstats` object is an
older five-stat implementation.

## Rows

| Order | Label | Internal key |
| --- | --- | --- |
| 1 | CHIP | `note` |
| 2 | TECH | `tech` |
| 3 | STREAM | `speed` |
| 4 | CHORD | `multi` |
| 5 | BURST | `fill` |
| 6 | GIMMICK | `gimmick` |

`GIMMICK` is conditional. It is shown only when `global.gimmick_stat > 0`;
ordinary charts therefore use the five-row layout. The game sets it to zero
for a nonpositive chart mod weight and otherwise calculates
`power(mod_weight, 0.77)`.

## Preserved assets

Original extracted sprites are under
`src/AutoChartSwitch.App/Assets/TechStats/Original`:

- `sp_techstats2025_0`: six-row label sprite, 36x40 pixels.
- `sp_techstats2025_1`: five-row label content, 30x37 pixels on a 36x40
  sprite canvas.
- `sp_font_techstat_0` through `_9`: digit glyphs, each with 4x5 cropped
  content on a 32x32 sprite canvas.

Plain PNGs are tightly cropped. The `_padded` variants preserve the original
sprite canvas. `sprites.tsv` records frame, origin, target, source, and
bounding dimensions.

The game creates its numeric font with:

```gml
font_add_sprite(sp_font_techstat, 48, true, 1)
```

Frame zero therefore maps to ASCII `0`; the font is proportional and uses a
separation value of 1. Numbers are white and right aligned.

## Geometry

All coordinates are logical GameMaker pixels relative to the stat page.

| Part | Six rows | Five rows |
| --- | --- | --- |
| Label sprite | `(4, 3)`, frame 0 | `(4, 3)`, frame 1 |
| Row top | `3 + 7*i` | `3 + 8*i` |
| Bar left | `42` | `42` |
| Bar right | `42 + 47*fraction` | `42 + 47*fraction` |
| Bar bottom | `rowTop + 4` | `rowTop + 4` |
| Number anchor | right aligned at `(120, rowTop)` | same |

The bar uses a filled `draw_rectangle`. Its 47-unit endpoint span rasterizes
to 48 source pixels because both endpoints are included.

For the standalone overlay, the supplied 508x200 reference captures are the
authoritative output layout. They establish this direct pixel geometry:

| Part | Overlay pixels |
| --- | --- |
| Surface | `508x200` |
| Label sprite | `(0, 0)`, scaled to `180x200` |
| Six-row tops | `0, 35, 70, 105, 140, 175` |
| Five-row tops | `0, 40, 80, 120, 160` |
| Bar | left `189`, height `25`, maximum width `240` |
| Number anchor | right aligned at `x = 508` |
| Digit cell | `20x25`, with a `25` pixel advance |

This corresponds to 5x nearest-neighbor sprites, a crop flush with the label
sprite, inclusive GameMaker rectangle rasterization, and a number column flush
with the capture's right edge. Fractional bars advance in five-pixel source
steps using `ceil(48 * fraction) * 5`.

## Bar animation

Displayed numbers read and round the target globals immediately. Bar values
ease separately once per game step:

```gml
animated = lerp(animated, target, 0.2)
```

The number therefore snaps to a newly selected chart or difficulty while bar
length and value-dependent color approach it, retaining 80 percent of the
remaining distance each step.

## Color and overflow

GameMaker HSV components use the range 0 through 255. For animated value
`v <= 200`:

```text
hue        = 55 + v
saturation = 200
brightness = 255
fraction   = min(v, 200) / 200
```

For `v > 200`, two filled rectangles are drawn in order:

1. A full-width base with HSV `((current_time / 2) % 255, 200, 255)`.
2. A white overlay from the left with fraction
   `min(v - 200, 200) / 200`.

The native hue-cycle period is 510 milliseconds because GameMaker
`current_time` is in milliseconds. At exactly 200, the static value-derived
HSV ramp still applies. From just above 200 to 400, white progressively covers
the cycling base; at 400 and above, the bar is completely white. The displayed
number remains uncapped. In the standalone overlay, a displayed number whose
rounded value is 400 or above uses the same rainbow hue cycle; lower values
retain the original white digit sprites.

## Research provenance

The focused assets, all 4,878 root decompilation outputs, raw strings, and 302
FFmpeg-extracted reference frames were retained in
`~/Scraps/vs_re/analysis` during research. UndertaleModCli reported no target
load/decompile failures. A rewritten disposable copy was also loaded and its
high-level resource counts matched the source before it was deleted. The
original `data.win` was never modified.
