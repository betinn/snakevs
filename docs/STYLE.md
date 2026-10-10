# Snake VS visual standard

All colors live in `src/SnakeVs.Game/Graphics/Palette.cs`. Do not hard-code colors anywhere else.

## Direction

Retro arcade look on a modern, clean base: a dark navy night background, a soft checkered board,
neon-like snakes with a gentle glow, and a chunky 5x7 pixel font. Shapes are rounded, never sharp.

## Palette

| Role | Hex | Use |
|---|---|---|
| Background | `#0D1021` | Screen background, Android splash and adaptive icon background |
| Board | `#141933` / `#171D3A` | Board checker cells |
| Board border | `#2A3260` | Frame around the board, empty progress |
| Player snake | `#6CF08A` -> `#1E9E55` | Head to tail gradient |
| CPU snake | `#FF6B8A` -> `#B02A5A` | Head to tail gradient, "VS" badge |
| Apple | `#FF4D5E` | Food, apple progress pips |
| Golden apple | `#FFD23F` | Bonus item, records, "new record" |
| Obstacle | `#39406B` / `#4D5791` / `#252A4A` | Body / top highlight / drop shadow |
| Text | `#F2F4FF` / `#8C93B8` | Primary / secondary text |
| Primary button | `#2ECC71` with `#0B2615` text | Main action of every screen |
| Button | `#232B57` | Secondary actions |
| Danger | `#FF6B6B` | Game over title |

Power-up colors: Slow `#4FC3F7`, Ghost `#B388FF`, Double `#FFD23F`, Shrink `#FF9F43`, Shield `#5C8DFF`.

## Typography

- Built-in pixel font (`PixelFont`), upper case only, with Portuguese accents (Á À Â Ã É Ê Í Ó Ô Õ Ú Ç).
- Scale steps (font pixels -> design units): titles 7-18, values 6-9, buttons 5, body 3, captions 2-2.5.
- Text that may not fit uses `PixelFont.FitScale` instead of overflowing.
- Important text gets a 1-pixel dark drop shadow (`DrawShadowed`).

## Layout

- Design box: 720 x 1280 units, always fully visible. The virtual screen grows along the longer axis,
  so there are never black bars (phones 16:9 to 21:9, tablets, landscape on large screens).
- Board: 20 x 30 cells, cell size is a whole number of units. HUD (112 units) sits right above the board.
- Safe area: content respects display cutouts reported by the system.
- Minimum touch target: 84 x 84 units (about 9 mm on a phone). Menu buttons are 96-100 units tall.
- Primary action is always the green button at the top of a button stack.

## Motion and feedback

- Snakes move cell by cell but are drawn interpolated between steps (smooth sliding).
- Pickups pulse; expiring items blink during their last 2-2.5 s.
- Every event has sound + particles; crashes add screen shake and a longer vibration.
- Scene changes fade through the background color in about 0.15 s; panels pop in with an ease-out-back.

## Assets

No image, font or audio files are shipped: textures, icons, launcher icons and sounds are generated in code.
Launcher icons and store graphics come from `StoreArt.cs` (`SnakeVs --store-art <dir>`).
