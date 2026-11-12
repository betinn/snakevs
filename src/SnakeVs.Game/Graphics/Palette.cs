using Microsoft.Xna.Framework;
using SnakeVs.Core;

namespace SnakeVs.Game.Graphics;

/// <summary>The visual identity of the game. See docs/STYLE.md. Never hard-code colors elsewhere.</summary>
public static class Palette
{
    public static readonly Color Background = Hex(0x0D1021);
    public static readonly Color BoardBackground = Hex(0x141933);
    public static readonly Color BoardCellAlt = Hex(0x171D3A);
    public static readonly Color BoardBorder = Hex(0x2A3260);

    public static readonly Color PlayerHead = Hex(0x6CF08A);
    public static readonly Color PlayerTail = Hex(0x1E9E55);
    public static readonly Color RivalHead = Hex(0xFF6B8A);
    public static readonly Color RivalTail = Hex(0xB02A5A);
    public static readonly Color Eye = Hex(0xF7F9FF);
    public static readonly Color Pupil = Hex(0x0D1021);

    public static readonly Color Apple = Hex(0xFF4D5E);
    public static readonly Color AppleShine = Hex(0xFFB3BB);
    public static readonly Color Leaf = Hex(0x4CD964);
    public static readonly Color Stem = Hex(0x7A4B2A);
    public static readonly Color Golden = Hex(0xFFD23F);
    public static readonly Color GoldenShine = Hex(0xFFF3C4);

    public static readonly Color Obstacle = Hex(0x39406B);
    public static readonly Color ObstacleTop = Hex(0x4D5791);
    public static readonly Color ObstacleShadow = Hex(0x252A4A);

    public static readonly Color Text = Hex(0xF2F4FF);
    public static readonly Color TextDim = Hex(0x8C93B8);
    public static readonly Color TextShadow = Hex(0x05060F);
    public static readonly Color Accent = PlayerHead;
    public static readonly Color Danger = Hex(0xFF6B6B);
    public static readonly Color Gold = Golden;

    public static readonly Color Panel = Hex(0x1A2042);
    public static readonly Color PanelBorder = Hex(0x323B70);
    public static readonly Color Button = Hex(0x232B57);
    public static readonly Color ButtonPressed = Hex(0x2E3870);
    public static readonly Color ButtonPrimary = Hex(0x2ECC71);
    public static readonly Color ButtonPrimaryPressed = Hex(0x27AE60);
    public static readonly Color ButtonPrimaryText = Hex(0x0B2615);
    public static readonly Color Overlay = new(0x07, 0x09, 0x16, 0xC8);

    public static readonly Color Slow = Hex(0x4FC3F7);
    public static readonly Color Ghost = Hex(0xB388FF);
    public static readonly Color Double = Hex(0xFFD23F);
    public static readonly Color Shrink = Hex(0xFF9F43);
    public static readonly Color Shield = Hex(0x5C8DFF);

    // Journey mode.
    public static readonly Color Portal = Hex(0x5CE1E6);
    public static readonly Color PortalDoor = Hex(0x5A3A52);
    public static readonly Color PortalLock = Hex(0xFF6B6B);
    public static readonly Color Rail = Hex(0x2F3868);
    public static readonly Color SawBlade = Hex(0xD5DBF5);
    public static readonly Color SawHub = Hex(0xFF4D5E);
    public static readonly Color Laser = Hex(0xFF3B6B);
    public static readonly Color LaserCore = Hex(0xFFE3EA);
    public static readonly Color Emitter = Hex(0x2B2F55);
    public static readonly Color PhaseBlock = Hex(0x7A5CFF);
    public static readonly Color PhaseBlockTop = Hex(0xA590FF);
    public static readonly Color Lava = Hex(0xFF5A1F);
    public static readonly Color LavaGlow = Hex(0xFFB02E);
    public static readonly Color LavaDeep = Hex(0xB3260D);
    public static readonly Color Meteor = Hex(0xFF9F43);
    public static readonly Color Ice = Hex(0x9EE6FF);
    public static readonly Color TeleportA = Hex(0x4FC3F7);
    public static readonly Color TeleportB = Hex(0xFF7AD9);
    public static readonly Color Bug = Hex(0xE0524D);
    public static readonly Color BugShell = Hex(0x8F2B33);
    public static readonly Color Specter = Hex(0xE8E6FF);
    public static readonly Color SpecterStunned = Hex(0x5C8DFF);
    public static readonly Color Darkness = Hex(0x04050C);

    public static Color Teleport(int pair) => pair % 2 == 0 ? TeleportA : TeleportB;

    public static Color Hazard(HazardKind kind) => kind switch
    {
        HazardKind.Saw => SawBlade,
        HazardKind.Laser => Laser,
        HazardKind.Phase => PhaseBlock,
        HazardKind.Lava => Lava,
        HazardKind.Meteor => Meteor,
        _ => Text,
    };

    public static Color PowerUp(PowerUpKind kind) => kind switch
    {
        PowerUpKind.Slow => Slow,
        PowerUpKind.Ghost => Ghost,
        PowerUpKind.Double => Double,
        PowerUpKind.Shrink => Shrink,
        PowerUpKind.Shield => Shield,
        _ => Text,
    };

    public static (Color Head, Color Tail) SnakeColors(int snakeId) =>
        snakeId == GameSession.PlayerId ? (PlayerHead, PlayerTail) : (RivalHead, RivalTail);

    public static Color Hex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255);
}
