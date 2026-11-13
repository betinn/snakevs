using Microsoft.Xna.Framework;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;

namespace SnakeVs.Game.Scenes;

/// <summary>
/// The journey: stages one above the other and no stops between them. The board is a window that scrolls to the next
/// stage while the snake keeps moving; a banner names the stage for a moment and the run goes on.
/// </summary>
public sealed class JourneyScene : RunScene
{
    private const float BannerSeconds = 2.3f;

    /// <summary>Time the camera takes to settle on a new stage (smooth start and stop).</summary>
    private const float CameraSmoothTime = 0.2f;

    /// <summary>Rows the camera moves ahead while the head is inside the portal, so the next stage starts to show.</summary>
    private const float PortalPeekRows = 5f;

    private readonly JourneySession _session;
    private float _camera;
    private float _cameraSpeed;
    private float _bannerTime;
    private float _zapCooldown;

    public JourneyScene(SnakeGame game, bool autopilot = false, int startStage = 1) : base(game, GameMode.Journey, autopilot)
    {
        _session = new JourneySession(game.Save.Difficulty, Environment.TickCount, startStage);
        if (autopilot) _session.Player.IsAiControlled = true;
        _camera = _session.Current.Top;
    }

    public JourneySession Session => _session;

    protected override Snake Player => _session.Player;

    protected override Difficulty RunDifficulty => _session.Difficulty;

    protected override string ReadyTitle => Game.Text.Journey;

    protected override Color ReadyColor => Palette.Portal;

    protected override int BoardColumns => Stage.Width;

    protected override int BoardRows => Stage.Height;

    protected override string ResultDetail => $"{Game.Text.Stage} {_session.StageNumber}";

    protected override Vector2 ReadyHintAnchor => MapPoint(_session.Player.Head) + CameraOffset;

    /// <summary>Particles and floating texts live in map space; this brings them to the screen.</summary>
    private Vector2 CameraOffset => new(0, -_camera * View.Cell);

    protected override void Simulate(float dt)
    {
        _session.Update(dt * 1000.0);
        ProcessEvents();
        if (_session.State == SessionState.GameOver) EndRun();
    }

    protected override void KillPlayer() => _session.KillPlayer();

    protected override ScoreSubmission BuildSubmission(string name)
    {
        var p = _session.Player;
        return new ScoreSubmission(Mode, _session.Difficulty, name, p.Score, p.ApplesEaten, _session.StageNumber, _session.StepCount);
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        _zapCooldown -= dt;
        if (Phase is RunPhase.Playing or RunPhase.Dying) _bannerTime += dt;

        // The camera follows the stage in play, and leans ahead while the head is going through the portal.
        var stage = _session.Current;
        var target = (float)stage.Top;
        if (stage.GateOpen && _session.Player.Alive && _session.Player.Head.Y <= stage.Top) target -= PortalPeekRows;
        if (Phase != RunPhase.Paused) _camera = SmoothDamp(_camera, target, ref _cameraSpeed, CameraSmoothTime, dt);
    }

    private void ProcessEvents()
    {
        var t = Game.Text;
        var cell = View.Cell;
        foreach (var e in _session.Events)
        {
            var pos = MapPoint(e.Cell);
            switch (e.Type)
            {
                case GameEventType.GateOpened:
                    Game.PlaySound(Sfx.Portal);
                    var gate = pos + new Vector2(-cell / 2f, 0);
                    Particles.Burst(gate, Palette.Portal, 36, cell * 8f, cell * 0.2f, 0.9f);
                    Texts.Add(t.PortalOpen, gate + new Vector2(0, cell * 2.6f), Palette.Portal, 3.5f);
                    Game.Vibrate(40);
                    break;

                case GameEventType.StageEntered:
                    Game.PlaySound(Sfx.Stage);
                    Particles.Burst(pos, Palette.Portal, 30, cell * 8f, cell * 0.2f, 0.9f);
                    Texts.Add($"+{e.Value}", pos - new Vector2(0, cell * 1.5f), Palette.Gold, 4f);
                    _bannerTime = 0f;
                    Game.Vibrate(30);
                    break;

                case GameEventType.SnakeCut:
                    Game.PlaySound(Sfx.Cut);
                    Particles.Burst(pos, Palette.PlayerTail, 18, cell * 6f, cell * 0.2f, 0.7f);
                    Particles.Burst(pos, Color.White, 8, cell * 4f, cell * 0.1f, 0.4f);
                    Texts.Add($"{t.TailCut} -{e.Value}", pos - new Vector2(0, cell), Palette.Danger, 3f);
                    Shake = MathF.Max(Shake, 4f);
                    Game.Vibrate(40);
                    break;

                case GameEventType.LaserFired:
                    if (_zapCooldown <= 0f)
                    {
                        Game.PlaySound(Sfx.Zap, 0.5f);
                        _zapCooldown = 0.12f;
                    }

                    break;

                case GameEventType.MeteorImpact:
                    Game.PlaySound(Sfx.Boom, 0.55f);
                    Particles.Burst(pos, Palette.Meteor, 22, cell * 7f, cell * 0.2f, 0.7f);
                    Shake = MathF.Max(Shake, 3f);
                    break;

                case GameEventType.EnemyDied when e.SnakeId == JourneySession.RivalId:
                    Game.PlaySound(Sfx.Golden);
                    if (_session.Rival is { } fallen)
                    {
                        foreach (var segment in fallen.Body) Particles.Burst(MapPoint(segment), Palette.RivalHead, 6, cell * 5f, cell * 0.18f, 0.7f);
                    }

                    Texts.Add($"{t.RivalDown} +{e.Value}", pos - new Vector2(0, cell), Palette.Gold, 3f);
                    Game.Vibrate(60);
                    break;

                case GameEventType.EnemyDied:
                    Particles.Burst(pos, Palette.Bug, 16, cell * 5f, cell * 0.16f, 0.6f);
                    break;

                case GameEventType.AppleStolen:
                    Game.PlaySound(Sfx.Eat, 0.5f, -0.4f);
                    Particles.Burst(pos, Palette.RivalHead, 12, cell * 5f, cell * 0.16f);
                    Texts.Add(t.AppleStolen, pos - new Vector2(0, cell), Palette.RivalHead, 3f);
                    break;

                case GameEventType.Teleported:
                    Game.PlaySound(Sfx.Warp, 0.8f);
                    Particles.Burst(pos, Palette.TeleportA, 18, cell * 6f, cell * 0.16f, 0.6f);
                    Game.Vibrate(20);
                    break;

                case GameEventType.SpecterStunned:
                    Game.PlaySound(Sfx.Stun, 0.7f);
                    Particles.Burst(pos, Palette.SpecterStunned, 16, cell * 5f, cell * 0.16f, 0.6f);
                    break;

                default:
                    Feedback(e, pos);
                    break;
            }
        }

        _session.ClearEvents();
    }

    /// <summary>Position of a world cell in map space (the screen position when the camera row is 0).</summary>
    private Vector2 MapPoint(Int2 cell) => View.CellCenter(cell);

    protected override void DrawBoard(Canvas c) => JourneyRenderer.Draw(c, _session, View, _camera, Time, Shake);

    protected override void DrawEffectsLayer(Canvas c)
    {
        Particles.Draw(c, CameraOffset);
        Texts.Draw(c, Font, CameraOffset);
        DrawBanner(c);
    }

    protected override void DrawHud(Canvas c)
    {
        var t = Game.Text;
        DrawScore(c);

        var stage = _session.Current;
        var cx = Hud.Center.X + 10;
        var y = Hud.Y;
        Font.DrawCentered(c, $"{t.Stage} {stage.Number}", new Vector2(cx, y + 16), 3.5f, Palette.Text);

        if (stage.GateOpen)
        {
            var blink = 0.6f + 0.4f * MathF.Sin(Time * 7f);
            Font.DrawCentered(c, t.PortalOpen, new Vector2(cx, y + 50), Font.FitScale(t.PortalOpen, Hud.W * 0.42f, 2.5f), Palette.Portal, alpha: blink);
        }
        else
        {
            const float spacing = 26f;
            var pips = stage.AppleGoal;
            var startX = cx - (pips - 1) * spacing / 2f;
            for (var i = 0; i < pips; i++)
            {
                var filled = i < _session.ApplesThisStage;
                var p = new Vector2(startX + i * spacing, y + 50);
                c.FillCircle(p, 9f, filled ? Palette.Apple : Palette.BoardBorder);
                if (filled) c.FillCircle(p + new Vector2(-3, -3), 3f, Palette.AppleShine * 0.8f);
            }
        }

        DrawEffects(c, new Vector2(cx, y + 84), _session.Player, _session.SlowMs);
    }

    /// <summary>Stage number and name over the top of the board for a moment. The game does not wait for it.</summary>
    private void DrawBanner(Canvas c)
    {
        if (Phase is RunPhase.Ready or RunPhase.Result || _bannerTime >= BannerSeconds) return;
        var t = Game.Text;
        var stage = _session.Current;
        var fade = MathHelper.Clamp(_bannerTime * 6f, 0f, 1f) * MathHelper.Clamp((BannerSeconds - _bannerTime) * 2.5f, 0f, 1f);
        var slide = (1f - Ui.Pop(_bannerTime * 3.5f)) * View.Area.W * 0.5f;

        var name = t.StageName(stage.Elements);
        var tip = t.StageTip(stage.Elements);
        var height = tip.Length > 0 ? 176f : 140f;
        var band = new RectF(View.Area.X, View.Area.Y + View.Area.H * 0.14f, View.Area.W, height);
        c.FillRect(band, Palette.Overlay * (0.8f * fade));
        c.FillRect(band.X, band.Y, band.W, 4, Palette.Portal * (0.8f * fade));
        c.FillRect(band.X, band.Bottom - 4, band.W, 4, Palette.Portal * (0.8f * fade));

        var cx = band.Center.X - slide;
        var title = $"{t.Stage} {stage.Number}";
        Font.DrawCentered(c, title, new Vector2(cx, band.Y + 46), Font.FitScale(title, band.W - 40, 6f), Palette.Portal, alpha: fade);
        Font.DrawCentered(c, name, new Vector2(cx + slide * 2f, band.Y + 102), Font.FitScale(name, band.W - 40, 3.5f), Palette.Text, alpha: fade);
        if (tip.Length > 0) Font.DrawCentered(c, tip, new Vector2(band.Center.X, band.Y + 146), Font.FitScale(tip, band.W - 30, 2.5f), Palette.TextDim, alpha: fade);
    }

    /// <summary>Critically damped spring towards a target: eases in and out without overshooting, even if the target moves.</summary>
    private static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float dt)
    {
        if (dt <= 0f) return current;
        var omega = 2f / smoothTime;
        var x = omega * dt;
        var exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        var change = current - target;
        var temp = (velocity + omega * change) * dt;
        velocity = (velocity - omega * temp) * exp;
        return target + (change + temp) * exp;
    }
}
