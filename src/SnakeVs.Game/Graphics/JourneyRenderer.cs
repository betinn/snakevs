using Microsoft.Xna.Framework;
using SnakeVs.Core;

namespace SnakeVs.Game.Graphics;

/// <summary>
/// Draws a <see cref="JourneySession"/> through a window of one stage that scrolls over the stacked stages.
/// The camera row is the world row at the top of the window (see <see cref="Stage.Top"/>).
/// </summary>
public static class JourneyRenderer
{
    private const int W = Stage.Width;
    private const int H = Stage.Height;

    /// <summary>Steps the portal doors take to slide open.</summary>
    private const float DoorTicks = 3f;

    /// <summary>Steps over which the lights go out after entering a dark stage.</summary>
    private const float DarkFadeTicks = 8f;

    public static void Draw(Canvas c, JourneySession s, BoardView view, float cameraRow, float time, float shake = 0f)
    {
        var area = view.Area;
        if (shake > 0) area = area.Offset(MathF.Sin(time * 91f) * shake, MathF.Cos(time * 73f) * shake);

        c.FillRoundRect(area.Inflate(6), 14, Palette.BoardBorder);
        c.FillRect(area, Palette.BoardBackground);
        c.BeginClip(area);

        // From here on, cells are world cells: the view is shifted so that the camera row lands on the top edge.
        var world = new BoardView(area.Offset(0, -cameraRow * view.Cell), view.Cell);
        var alpha = s.StepAlpha;
        var tick = s.StageTick;

        DrawFloor(c, world, cameraRow);
        if (s.Previous is { } previous && IsVisible(previous, cameraRow))
        {
            DrawStage(c, s, previous, world, s.PreviousStageTick, 1f, time, live: false);
        }

        var stage = s.Current;
        DrawStage(c, s, stage, world, tick, alpha, time, live: true);

        if (s.Apple is { } apple) BoardRenderer.DrawApple(c, world, apple, time, Palette.Apple, Palette.AppleShine, 1f);
        DrawBonuses(c, s, world, time);
        DrawSaws(c, stage, world, tick, alpha, time, 1f);
        DrawBugs(c, s, world, alpha, time);
        if (s.Rival is { } rival)
        {
            var fade = s.CreaturesDormant && rival.Alive ? 0.45f + 0.25f * MathF.Sin(time * 12f) : 1f;
            BoardRenderer.DrawSnake(c, rival, world, s.RivalMoved ? alpha : 1f, time, fade);
        }

        BoardRenderer.DrawSnake(c, s.Player, world, alpha, time, 1f);

        if (stage.Dark) DrawDarkness(c, s, stage, world, time);

        // Things that give off light are drawn over the darkness.
        DrawLasers(c, stage, world, tick, alpha, time, 1f);
        DrawMeteors(c, stage, world, tick, alpha, time);
        DrawSpecter(c, s, world, alpha, time);
        if (stage.Dark) DrawLightsInTheDark(c, s, stage, world, tick, alpha, time);
        DrawPortalGlow(c, s, stage, world, time);

        c.EndClip();
    }

    private static bool IsVisible(Stage stage, float cameraRow) => stage.Bottom >= cameraRow - 1 && stage.Top <= cameraRow + H;

    private static float TickFloat(long tick, float alpha) => tick + alpha;

    private static void DrawFloor(Canvas c, BoardView world, float cameraRow)
    {
        var first = (int)MathF.Floor(cameraRow);
        for (var row = first; row <= first + H; row++)
        {
            for (var x = (row & 1) == 0 ? 0 : 1; x < W; x += 2)
            {
                c.FillRect(world.Area.X + x * world.Cell, world.Area.Y + row * world.Cell, world.Cell, world.Cell, Palette.BoardCellAlt);
            }
        }
    }

    /// <summary>Everything that belongs to a stage and sits under the snakes: ice, rails, teleporters, walls, portal, phase blocks, lava.</summary>
    private static void DrawStage(Canvas c, JourneySession s, Stage stage, BoardView world, long tick, float alpha, float time, bool live)
    {
        var cell = world.Cell;
        var dim = live ? 1f : 0.55f;

        for (var y = 1; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var local = new Int2(x, y);
                if (!stage.IsIce(local)) continue;
                var p = TopLeft(world, stage.ToWorld(local));
                c.FillRect(p.X, p.Y, cell, cell, Palette.Ice * 0.2f);
                if (((x + y) & 1) == 0) c.Line(p + new Vector2(cell * 0.2f, cell * 0.7f), p + new Vector2(cell * 0.7f, cell * 0.2f), MathF.Max(1.5f, cell * 0.06f), Palette.Ice * 0.3f);
            }
        }

        foreach (var saw in stage.Saws) DrawRail(c, stage, saw, world);

        for (var i = 0; i < stage.Teleports.Count; i++)
        {
            DrawTeleport(c, world.CellCenter(stage.ToWorld(stage.Teleports[i].A)), cell, Palette.Teleport(i), time, 1f);
            DrawTeleport(c, world.CellCenter(stage.ToWorld(stage.Teleports[i].B)), cell, Palette.Teleport(i), time + 1.3f, 1f);
        }

        for (var y = 1; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var local = new Int2(x, y);
                if (!stage.IsStaticWall(local)) continue;
                var p = TopLeft(world, stage.ToWorld(local));
                if (IsEmitter(stage, local)) DrawEmitterBlock(c, p, cell);
                else BoardRenderer.DrawObstacleCell(c, p.X, p.Y, cell, 1f);
            }
        }

        DrawGateWall(c, stage, world, tick, alpha, time);

        foreach (var group in stage.PhaseGroups)
        {
            var solid = group.SolidAt(tick);
            var warning = !solid && group.WarningAt(tick);
            foreach (var local in group.Cells)
            {
                var p = TopLeft(world, stage.ToWorld(local));
                DrawPhaseCell(c, p, cell, solid, warning, time, dim);
            }
        }

        if (!live)
        {
            // The stage left behind: its hazards stay where they were, switched off.
            DrawSaws(c, stage, world, tick, 1f, time, 0.4f);
        }

        if (stage.Lava is { } lava) DrawLava(c, stage, lava, world, tick, alpha, time);
    }

    private static Vector2 TopLeft(BoardView world, Int2 cell) => new(world.Area.X + cell.X * world.Cell, world.Area.Y + cell.Y * world.Cell);

    private static bool IsEmitter(Stage stage, Int2 local)
    {
        foreach (var laser in stage.Lasers)
        {
            if (laser.Emitter == local) return true;
        }

        return false;
    }

    private static void DrawEmitterBlock(Canvas c, Vector2 p, float cell)
    {
        var inset = MathF.Max(1f, cell * 0.06f);
        var r = new RectF(p.X + inset, p.Y + inset, cell - inset * 2, cell - inset * 2);
        c.FillRoundRect(r.Offset(0, cell * 0.08f), cell * 0.18f, Palette.ObstacleShadow);
        c.FillRoundRect(r, cell * 0.18f, Palette.Laser * 0.55f);
        c.FillRoundRect(r.Inflate(-cell * 0.1f), cell * 0.12f, Palette.Emitter);
    }

    /// <summary>The wall row at the top of a stage, with the portal: locked doors, doors sliding open, or the open way.</summary>
    private static void DrawGateWall(Canvas c, Stage stage, BoardView world, long tick, float alpha, float time)
    {
        var cell = world.Cell;
        for (var x = 0; x < W; x++)
        {
            if (stage.IsGateColumn(x)) continue;
            var p = TopLeft(world, new Int2(x, stage.Top));
            BoardRenderer.DrawObstacleCell(c, p.X, p.Y, cell, 1f);
        }

        var left = TopLeft(world, new Int2(stage.GateX, stage.Top));
        var gate = new RectF(left.X, left.Y, cell * Stage.GateWidth, cell);
        var open = stage.GateOpen ? MathHelper.Clamp((TickFloat(tick, alpha) - stage.GateOpenTick) / DoorTicks, 0f, 1f) : 0f;

        if (open > 0f)
        {
            c.FillRect(gate, Palette.Portal * (0.16f + 0.06f * MathF.Sin(time * 6f)));
        }

        if (open < 1f)
        {
            // Two doors that slide into the wall.
            var half = gate.W / 2f * (1f - open);
            var inset = cell * 0.08f;
            var doorH = cell - inset * 2;
            c.FillRoundRect(new RectF(gate.X, gate.Y + inset, half, doorH), cell * 0.12f, Palette.PortalDoor);
            c.FillRoundRect(new RectF(gate.Right - half, gate.Y + inset, half, doorH), cell * 0.12f, Palette.PortalDoor);
            if (half > cell * 0.5f)
            {
                var stripe = cell * 0.12f;
                c.FillRect(gate.X + cell * 0.2f, gate.Center.Y - stripe / 2f, half - cell * 0.4f, stripe, Palette.PortalLock * 0.55f);
                c.FillRect(gate.Right - half + cell * 0.2f, gate.Center.Y - stripe / 2f, half - cell * 0.4f, stripe, Palette.PortalLock * 0.55f);
            }

            if (open <= 0f)
            {
                var pulse = 0.6f + 0.4f * MathF.Sin(time * 3f);
                c.FillCircle(gate.Center, cell * 0.2f, Palette.PortalLock * pulse);
                c.FillCircle(gate.Center, cell * 0.09f, Palette.Pupil);
            }
        }

        foreach (var post in stage.Posts)
        {
            var p = TopLeft(world, stage.ToWorld(post.Cell));
            if (post.Solid)
            {
                BoardRenderer.DrawObstacleCell(c, p.X, p.Y, cell, 1f);
                c.FillRoundRect(new RectF(p.X + cell * 0.18f, p.Y + cell * 0.16f, cell * 0.64f, cell * 0.16f), cell * 0.08f, Palette.Portal * 0.9f);
            }
            else if (stage.GateOpen)
            {
                // About to rise out of the floor.
                var blink = 0.35f + 0.3f * MathF.Sin(time * 14f);
                c.StrokeRect(new RectF(p.X + cell * 0.1f, p.Y + cell * 0.1f, cell * 0.8f, cell * 0.8f), MathF.Max(1.5f, cell * 0.08f), Palette.Portal * blink);
            }
        }
    }

    /// <summary>Light and arrows of an open portal, drawn over everything (it shows the way even in the dark).</summary>
    private static void DrawPortalGlow(Canvas c, JourneySession s, Stage stage, BoardView world, float time)
    {
        if (!stage.GateOpen) return;
        var cell = world.Cell;
        var center = world.CellCenter(new Vector2(stage.GateX + (Stage.GateWidth - 1) / 2f, stage.Top));
        c.DrawGlow(center + new Vector2(0, cell * 0.8f), cell * 4.2f, Palette.Portal * (0.2f + 0.06f * MathF.Sin(time * 5f)));

        // Arrows climbing towards the opening.
        for (var i = 0; i < 3; i++)
        {
            var k = (time * 1.4f + i / 3f) % 1f;
            var y = center.Y + cell * (2.9f - 2.4f * k);
            var a = MathF.Sin(k * MathF.PI) * 0.85f;
            var half = cell * 0.55f;
            var thick = MathF.Max(2f, cell * 0.14f);
            c.Line(new Vector2(center.X - half, y + half * 0.6f), new Vector2(center.X, y), thick, Palette.Portal * a);
            c.Line(new Vector2(center.X + half, y + half * 0.6f), new Vector2(center.X, y), thick, Palette.Portal * a);
        }
    }

    private static void DrawPhaseCell(Canvas c, Vector2 p, float cell, bool solid, bool warning, float time, float dim)
    {
        var inset = MathF.Max(1f, cell * 0.06f);
        var r = new RectF(p.X + inset, p.Y + inset, cell - inset * 2, cell - inset * 2);
        var radius = cell * 0.18f;
        if (solid)
        {
            c.FillRoundRect(r.Offset(0, cell * 0.08f), radius, Palette.ObstacleShadow * dim);
            c.FillRoundRect(r, radius, Palette.PhaseBlock * dim);
            c.FillRoundRect(new RectF(r.X + cell * 0.12f, r.Y + cell * 0.1f, r.W - cell * 0.24f, cell * 0.16f), cell * 0.08f, Palette.PhaseBlockTop * dim);
            return;
        }

        if (warning) c.FillRoundRect(r, radius, Palette.PhaseBlock * ((0.3f + 0.25f * MathF.Sin(time * 26f)) * dim));
        c.StrokeRect(r.Inflate(-cell * 0.08f), MathF.Max(1f, cell * 0.05f), Palette.PhaseBlock * (0.3f * dim));
    }

    private static void DrawRail(Canvas c, Stage stage, Saw saw, BoardView world)
    {
        var thick = MathF.Max(2f, world.Cell * 0.12f);
        var path = saw.Path;
        for (var i = 1; i < path.Count; i++)
        {
            c.Line(world.CellCenter(stage.ToWorld(path[i - 1])), world.CellCenter(stage.ToWorld(path[i])), thick, Palette.Rail);
        }

        if (saw.Loop) c.Line(world.CellCenter(stage.ToWorld(path[^1])), world.CellCenter(stage.ToWorld(path[0])), thick, Palette.Rail);
        else
        {
            c.FillCircle(world.CellCenter(stage.ToWorld(path[0])), thick * 1.2f, Palette.Rail);
            c.FillCircle(world.CellCenter(stage.ToWorld(path[^1])), thick * 1.2f, Palette.Rail);
        }

        // Rails have corners in loops: round them off.
        if (!saw.Loop) return;
        foreach (var cell in path) c.FillCircle(world.CellCenter(stage.ToWorld(cell)), thick / 2f, Palette.Rail);
    }

    private static void DrawSaws(Canvas c, Stage stage, BoardView world, long tick, float alpha, float time, float opacity)
    {
        var cell = world.Cell;
        foreach (var saw in stage.Saws)
        {
            var center = world.CellCenter(SawPosition(stage, saw, tick, alpha));
            c.DrawGlow(center, cell * 1.1f, Palette.SawHub * (0.22f * opacity));
            var spin = time * 11f;
            const int teeth = 8;
            for (var i = 0; i < teeth; i++)
            {
                var a = spin + i * MathHelper.TwoPi / teeth;
                c.FillCircle(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (cell * 0.38f), cell * 0.11f, Palette.SawBlade * opacity);
            }

            c.FillCircle(center, cell * 0.36f, Palette.SawBlade * opacity);
            c.FillCircle(center, cell * 0.24f, Palette.Obstacle * opacity);
            c.FillCircle(center, cell * 0.13f, Palette.SawHub * opacity);
        }
    }

    private static Vector2 SawPosition(Stage stage, Saw saw, long tick, float alpha)
    {
        var now = stage.ToWorld(saw.CellAt(tick));
        var before = stage.ToWorld(saw.CellAt(tick - 1));
        var to = new Vector2(now.X, now.Y);
        return before.IsAdjacentTo(now) ? Vector2.Lerp(new Vector2(before.X, before.Y), to, alpha) : to;
    }

    /// <summary>
    /// A beam can always be read before it can hurt: dark, a dotted line shows where it will pass; warning, a
    /// charge runs along that line from the emitter and the beam fires when it reaches the end; on, the beam
    /// thins out as its time runs down.
    /// </summary>
    private static void DrawLasers(Canvas c, Stage stage, BoardView world, long tick, float alpha, float time, float opacity)
    {
        var cell = world.Cell;
        foreach (var laser in stage.Lasers)
        {
            var state = laser.StateAt(tick);
            var charge = laser.ChargeAt(tick, alpha);
            var emitter = world.CellCenter(stage.ToWorld(laser.Emitter));
            var forward = BoardRenderer.DirVec(laser.Direction);
            var lens = emitter + forward * (cell * 0.3f);
            var blink = 0.5f + 0.5f * MathF.Sin(time * (14f + 26f * charge));
            var lit = state switch
            {
                LaserState.On => 1f,
                LaserState.Warning => 0.35f + 0.65f * MathF.Max(charge, blink),
                _ => 0.25f,
            };
            c.FillCircle(lens, cell * (0.17f + 0.07f * charge), Color.Lerp(Palette.Emitter, Palette.Laser, lit) * opacity);

            if (laser.Beam.Count == 0) continue;
            var from = emitter + forward * (cell * 0.5f);
            var to = world.CellCenter(stage.ToWorld(laser.Beam[^1])) + forward * (cell * 0.5f);

            if (state != LaserState.On)
            {
                // The line of fire, one dot per cell.
                var dot = MathF.Max(1.5f, cell * 0.07f);
                var dim = state == LaserState.Warning ? 0.55f : 0.4f;
                foreach (var local in laser.Beam) c.FillCircle(world.CellCenter(stage.ToWorld(local)), dot, Palette.Laser * (dim * opacity));
                if (state == LaserState.Off) continue;

                var tip = Vector2.Lerp(from, to, charge);
                c.Line(from, to, MathF.Max(2f, cell * 0.06f), Palette.Laser * ((0.2f + 0.3f * blink) * opacity));
                c.Line(from, tip, cell * (0.1f + 0.12f * charge), Palette.Laser * ((0.55f + 0.35f * blink) * opacity));
                c.DrawGlow(tip, cell * 0.7f, Palette.Laser * (0.55f * opacity));
                c.FillCircle(tip, cell * 0.16f, Palette.LaserCore * opacity);
                continue;
            }

            // Full width for most of the burn, thinner over its last step and a half.
            var body = MathHelper.Clamp(laser.BurnLeftAt(tick, alpha) * laser.OnTicks / 1.5f, 0.35f, 1f);
            var flicker = 0.85f + 0.15f * MathF.Sin(time * 60f);
            c.Line(from, to, cell * 0.8f * body, Palette.Laser * (0.2f * opacity));
            c.Line(from, to, cell * 0.4f * flicker * body, Palette.Laser * opacity);
            c.Line(from, to, cell * 0.14f * body, Palette.LaserCore * opacity);
            c.DrawGlow(to, cell * 0.9f * body, Palette.Laser * (0.6f * opacity));
        }
    }

    private static void DrawLava(Canvas c, Stage stage, LavaTide lava, BoardView world, long tick, float alpha, float time)
    {
        var rows = MathHelper.Lerp(lava.RowsAt(tick - 1), lava.RowsAt(tick), alpha);
        var cell = world.Cell;
        var left = world.Area.X;
        var width = W * cell;

        // A glowing line shows where the lava will stop.
        if (rows < lava.MaxRows && tick >= 1)
        {
            var limit = world.Area.Y + (stage.Top + H - lava.MaxRows) * cell;
            c.FillRect(left, limit - 1.5f, width, 3f, Palette.Lava * (0.18f + 0.1f * MathF.Sin(time * 4f)));
        }

        if (rows <= 0f) return;
        var top = world.Area.Y + (stage.Top + H - rows) * cell;
        var bottom = world.Area.Y + (stage.Top + H) * cell;
        c.FillRect(left, top, width, bottom - top, Palette.Lava * 0.94f);
        if (bottom - top > cell * 1.5f) c.FillRect(left, top + cell * 1.5f, width, bottom - top - cell * 1.5f, Palette.LavaDeep * 0.5f);

        // Waves on the surface and a few bubbles.
        var bump = cell * 0.5f;
        for (var i = 0; i * bump < width; i++)
        {
            var wave = MathF.Sin(time * 3f + i * 0.9f) * cell * 0.1f;
            c.FillCircle(new Vector2(left + (i + 0.5f) * bump, top + wave), cell * 0.3f, Palette.LavaGlow);
        }

        c.FillRect(left, top - cell * 0.5f, width, cell * 0.5f, Palette.Lava * 0.12f);
        for (var i = 0; i < 6; i++)
        {
            var k = (time * (0.35f + i * 0.07f) + i * 0.37f) % 1f;
            var x = left + ((i * 0.618f + 0.13f) % 1f) * width;
            var y = bottom - (bottom - top) * k;
            if (y > top + cell * 0.4f) c.FillCircle(new Vector2(x, y), cell * (0.08f + 0.1f * (1f - k)), Palette.LavaGlow * (0.7f * (1f - k)));
        }
    }

    private static void DrawMeteors(Canvas c, Stage stage, BoardView world, long tick, float alpha, float time)
    {
        if (stage.Meteors is not { } meteors) return;
        var cell = world.Cell;
        var (first, last) = meteors.AnnouncedAt(tick);
        for (var strike = first; strike <= last; strike++)
        {
            var impact = meteors.ImpactTick(strike);
            var remaining = impact - tick - alpha; // steps until it lands; below zero once it has
            var target = meteors.Target(strike);
            var center = world.CellCenter(stage.ToWorld(target));

            if (remaining <= 0f)
            {
                // Landed on this step: flash and shock wave.
                var k = MathHelper.Clamp(-remaining, 0f, 1f);
                foreach (var d in MeteorShower.Shape)
                {
                    var local = target + d;
                    if (!Stage.InPlay(local) || stage.IsStaticWall(local)) continue;
                    var p = TopLeft(world, stage.ToWorld(local));
                    c.FillRect(p.X, p.Y, cell, cell, Color.Lerp(Color.White, Palette.Meteor, k) * (1f - k));
                }

                c.DrawRing(center, cell * (0.6f + 1.8f * k), Palette.Meteor * (1f - k));
                continue;
            }

            var closeness = 1f - MathHelper.Clamp(remaining / meteors.WarnTicks, 0f, 1f);
            var blink = 0.5f + 0.5f * MathF.Sin(time * (10f + 24f * closeness));
            foreach (var d in MeteorShower.Shape)
            {
                var local = target + d;
                if (!Stage.InPlay(local) || stage.IsStaticWall(local)) continue;
                var p = TopLeft(world, stage.ToWorld(local));
                c.FillRect(p.X + 1, p.Y + 1, cell - 2, cell - 2, Palette.Meteor * (0.1f + 0.25f * closeness * blink));
            }

            c.DrawRing(center, cell * (1.9f - 1.3f * closeness), Palette.Meteor * (0.45f + 0.4f * closeness));
            c.FillCircle(center, cell * 0.09f, Palette.Meteor);

            // The rock itself comes down during the last steps.
            if (remaining < 3f)
            {
                var fall = remaining / 3f;
                var rock = center + new Vector2(cell * 2.2f, -cell * 6f) * fall;
                c.Line(rock + new Vector2(cell * 0.5f, -cell * 1.4f), rock, cell * 0.3f, Palette.Meteor * 0.5f);
                c.DrawGlow(rock, cell * 1.2f, Palette.Meteor * 0.5f);
                c.FillCircle(rock, cell * 0.3f, Palette.LavaGlow);
            }
        }
    }

    private static void DrawTeleport(Canvas c, Vector2 center, float cell, Color color, float time, float opacity)
    {
        var pulse = 1f + 0.06f * MathF.Sin(time * 5f);
        c.DrawGlow(center, cell * 1.2f, color * (0.3f * opacity));
        c.DrawRing(center, cell * 0.46f * pulse, color * opacity);
        for (var i = 0; i < 3; i++)
        {
            var a = time * 4f + i * MathHelper.TwoPi / 3f;
            c.FillCircle(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (cell * 0.2f), cell * 0.07f, Color.Lerp(color, Color.White, 0.5f) * opacity);
        }
    }

    private static void DrawBonuses(Canvas c, JourneySession s, BoardView world, float time)
    {
        if (s.Golden is { } golden)
        {
            var blink = golden.RemainingMs < 2000 && MathF.Sin(time * 22f) < 0;
            if (!blink) BoardRenderer.DrawApple(c, world, golden.Cell, time, Palette.Golden, Palette.GoldenShine, 1f, sparkle: true);
            c.DrawArc(world.CellCenter(golden.Cell), world.Cell * 0.62f, MathF.Max(2f, world.Cell * 0.07f), (float)golden.Fraction, Palette.Golden * 0.55f);
        }

        if (s.PowerUp is { } power)
        {
            var blink = power.RemainingMs < 2500 && MathF.Sin(time * 20f) < 0;
            if (!blink) BoardRenderer.DrawPowerUp(c, world, power.Kind, world.CellCenter(power.Cell), time, 1f);
        }
    }

    private static Vector2 Slide(Int2 from, Int2 to, float alpha)
    {
        var target = new Vector2(to.X, to.Y);
        return from.IsAdjacentTo(to) ? Vector2.Lerp(new Vector2(from.X, from.Y), target, alpha) : target;
    }

    private static void DrawBugs(Canvas c, JourneySession s, BoardView world, float alpha, float time)
    {
        var cell = world.Cell;
        var waking = s.CreaturesDormant ? 0.45f + 0.3f * MathF.Sin(time * 12f) : 1f;
        foreach (var bug in s.Bugs)
        {
            if (!bug.Alive) continue;
            var center = world.CellCenter(Slide(bug.Previous, bug.Cell, alpha));
            var forward = BoardRenderer.DirVec(bug.Heading);
            var side = new Vector2(-forward.Y, forward.X);

            // Legs first, wiggling as it walks.
            for (var i = -1; i <= 1; i++)
            {
                var swing = MathF.Sin(time * 16f + i * 2f) * cell * 0.08f;
                var root = center + forward * (i * cell * 0.2f);
                var reach = side * (cell * 0.44f) + forward * swing;
                c.Line(root - reach, root + reach, MathF.Max(1.5f, cell * 0.07f), Palette.BugShell * waking);
            }

            c.FillCircle(center + forward * (cell * 0.3f), cell * 0.18f, Palette.BugShell * waking);
            c.FillCircle(center - forward * (cell * 0.05f), cell * 0.33f, Palette.Bug * waking);
            c.Line(center + forward * (cell * 0.22f), center - forward * (cell * 0.36f), MathF.Max(1.5f, cell * 0.06f), Palette.BugShell * waking);
            c.FillCircle(center + forward * (cell * 0.34f) + side * (cell * 0.09f), cell * 0.05f, Palette.Eye * waking);
            c.FillCircle(center + forward * (cell * 0.34f) - side * (cell * 0.09f), cell * 0.05f, Palette.Eye * waking);
        }
    }

    private static void DrawSpecter(Canvas c, JourneySession s, BoardView world, float alpha, float time)
    {
        if (s.Specter is not { } specter) return;
        var cell = world.Cell;
        var center = world.CellCenter(Slide(specter.Previous, specter.Cell, alpha));
        center.Y += MathF.Sin(time * 4f) * cell * 0.1f;

        var color = Palette.Specter;
        var opacity = 0.92f;
        if (specter.Stunned)
        {
            color = Palette.SpecterStunned;
            opacity = specter.StunTicks <= 4 && MathF.Sin(time * 26f) < 0 ? 0.75f : 0.4f;
        }
        else if (s.CreaturesDormant)
        {
            opacity = 0.35f + 0.25f * MathF.Sin(time * 12f);
        }

        c.DrawGlow(center, cell * 1.7f, color * (0.3f * opacity));
        c.BitmapCentered(Icons.Ghost, center, cell * 0.15f, color * opacity);
    }

    /// <summary>
    /// Darkness of a blacked-out stage, cell by cell: full a few cells away from the head, lighter around pickups,
    /// teleporters, lava and the open portal.
    /// </summary>
    private static void DrawDarkness(Canvas c, JourneySession s, Stage stage, BoardView world, float time)
    {
        var cell = world.Cell;
        var strength = MathHelper.Clamp(TickFloat(s.StageTick, s.StepAlpha) / DarkFadeTicks, 0f, 1f) * 0.97f;
        if (strength <= 0f) return;

        Span<(Vector2 Position, float Radius)> lights = stackalloc (Vector2, float)[12];
        var count = 0;
        void Light(Vector2 at, float radius, Span<(Vector2, float)> into)
        {
            if (count < into.Length) into[count++] = (at, radius);
        }

        var p = s.Player;
        var head = new Vector2(p.Head.X, p.Head.Y);
        if (p.Length > 1 && p.Alive && p.Head.IsAdjacentTo(p.Body[1])) head = Vector2.Lerp(new Vector2(p.Body[1].X, p.Body[1].Y), head, s.StepAlpha);
        Light(head, stage.LightRadius + 0.15f * MathF.Sin(time * 7f), lights);
        if (s.Apple is { } apple) Light(new Vector2(apple.X, apple.Y), 2.2f, lights);
        if (s.Golden is { } golden) Light(new Vector2(golden.Cell.X, golden.Cell.Y), 1.8f, lights);
        if (s.PowerUp is { } power) Light(new Vector2(power.Cell.X, power.Cell.Y), 1.8f, lights);
        if (stage.GateOpen) Light(new Vector2(stage.GateX + (Stage.GateWidth - 1) / 2f, stage.Top + 1), 3.6f, lights);
        foreach (var (a, b) in stage.Teleports)
        {
            var wa = stage.ToWorld(a);
            var wb = stage.ToWorld(b);
            Light(new Vector2(wa.X, wa.Y), 1.4f, lights);
            Light(new Vector2(wb.X, wb.Y), 1.4f, lights);
        }

        var lavaTop = stage.Lava is { } lava ? stage.Top + H - lava.RowsAt(s.StageTick) : int.MaxValue;
        for (var y = stage.Top; y <= stage.Bottom; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var lit = y >= lavaTop ? 0.8f : y == lavaTop - 1 ? 0.45f : 0f;
                var at = new Vector2(x, y);
                for (var i = 0; i < count && lit < 1f; i++)
                {
                    var (position, radius) = lights[i];
                    var d = Vector2.Distance(at, position);
                    lit = MathF.Max(lit, MathHelper.Clamp((radius - d) / 1.6f + 0.35f, 0f, 1f));
                }

                if (lit >= 1f) continue;
                c.FillRect(world.Area.X + x * cell, world.Area.Y + y * cell, cell, cell, Palette.Darkness * (strength * (1f - lit)));
            }
        }
    }

    /// <summary>In the dark the blades' hubs and the bugs' eyes still glow, so they can be seen coming.</summary>
    private static void DrawLightsInTheDark(Canvas c, JourneySession s, Stage stage, BoardView world, long tick, float alpha, float time)
    {
        var cell = world.Cell;
        foreach (var saw in stage.Saws)
        {
            var center = world.CellCenter(SawPosition(stage, saw, tick, alpha));
            c.DrawGlow(center, cell * 0.8f, Palette.SawHub * 0.5f);
            c.FillCircle(center, cell * 0.12f, Palette.SawHub * (0.7f + 0.3f * MathF.Sin(time * 9f)));
        }

        foreach (var bug in s.Bugs)
        {
            if (!bug.Alive) continue;
            var center = world.CellCenter(Slide(bug.Previous, bug.Cell, alpha));
            var forward = BoardRenderer.DirVec(bug.Heading);
            var side = new Vector2(-forward.Y, forward.X);
            c.FillCircle(center + forward * (cell * 0.34f) + side * (cell * 0.1f), cell * 0.06f, Palette.Bug);
            c.FillCircle(center + forward * (cell * 0.34f) - side * (cell * 0.1f), cell * 0.06f, Palette.Bug);
        }

        if (s.Rival is { Alive: true } rival) c.DrawGlow(world.CellCenter(rival.Head), cell * 0.9f, Palette.RivalHead * 0.45f);
    }
}
