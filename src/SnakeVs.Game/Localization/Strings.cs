using System.Globalization;
using SnakeVs.Core;

namespace SnakeVs.Game.Localization;

/// <summary>All user facing text. Portuguese (Brazil) for "pt" cultures, English otherwise.</summary>
public sealed class Strings
{
    public required string Classic { get; init; }
    public required string Versus { get; init; }
    public required string Options { get; init; }
    public required string HowToPlay { get; init; }
    public required string Back { get; init; }
    public required string Resume { get; init; }
    public required string Restart { get; init; }
    public required string Menu { get; init; }
    public required string PlayAgain { get; init; }
    public required string Rematch { get; init; }
    public required string Paused { get; init; }
    public required string GameOver { get; init; }
    public required string NewRecord { get; init; }
    public required string Score { get; init; }
    public required string Best { get; init; }
    public required string Level { get; init; }
    public required string You { get; init; }
    public required string Cpu { get; init; }
    public required string Goal { get; init; }
    public required string YouWin { get; init; }
    public required string CpuWins { get; init; }
    public required string Draw { get; init; }
    public required string SwipeToStart { get; init; }
    public required string ArrowToStart { get; init; }
    public required string LevelCleared { get; init; }
    public required string Bonus { get; init; }
    public required string Difficulty { get; init; }
    public required string Easy { get; init; }
    public required string Normal { get; init; }
    public required string Hard { get; init; }
    public required string Sound { get; init; }
    public required string Vibration { get; init; }
    public required string Controls { get; init; }
    public required string ControlSwipe { get; init; }
    public required string ControlButtons { get; init; }
    public required string On { get; init; }
    public required string Off { get; init; }
    public required string Record { get; init; }
    public required string WinsLosses { get; init; }
    public required string Tagline { get; init; }
    public required string Apples { get; init; }
    public required string ShieldSaved { get; init; }
    public required string[] HelpBasics { get; init; }
    public required string[] HelpVersus { get; init; }
    public required string[] PowerUpNames { get; init; }
    public required string[] PowerUpHelp { get; init; }
    public required string GoldenName { get; init; }
    public required string GoldenHelp { get; init; }
    public required string ObstacleName { get; init; }
    public required string ObstacleHelp { get; init; }

    public string DifficultyName(Core.Difficulty d) => d switch
    {
        Core.Difficulty.Easy => Easy,
        Core.Difficulty.Hard => Hard,
        _ => Normal,
    };

    public string PowerUpName(PowerUpKind kind) => PowerUpNames[(int)kind];

    public static Strings ForCulture(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase) ? Portuguese : English;

    public static readonly Strings Portuguese = new()
    {
        Classic = "CLÁSSICO",
        Versus = "VS CPU",
        Options = "OPÇÕES",
        HowToPlay = "COMO JOGAR",
        Back = "VOLTAR",
        Resume = "CONTINUAR",
        Restart = "REINICIAR",
        Menu = "MENU",
        PlayAgain = "JOGAR DE NOVO",
        Rematch = "REVANCHE",
        Paused = "PAUSA",
        GameOver = "FIM DE JOGO",
        NewRecord = "NOVO RECORDE!",
        Score = "PONTOS",
        Best = "RECORDE",
        Level = "NÍVEL",
        You = "VOCÊ",
        Cpu = "CPU",
        Goal = "META",
        YouWin = "VOCÊ VENCEU!",
        CpuWins = "A CPU VENCEU",
        Draw = "EMPATE",
        SwipeToStart = "DESLIZE PARA\nCOMEÇAR",
        ArrowToStart = "TOQUE NUMA SETA\nPARA COMEÇAR",
        LevelCleared = "NÍVEL COMPLETO!",
        Bonus = "BÔNUS",
        Difficulty = "DIFICULDADE",
        Easy = "FÁCIL",
        Normal = "NORMAL",
        Hard = "DIFÍCIL",
        Sound = "SOM",
        Vibration = "VIBRAÇÃO",
        Controls = "CONTROLE",
        ControlSwipe = "DESLIZAR",
        ControlButtons = "BOTÕES",
        On = "LIGADO",
        Off = "DESLIGADO",
        Record = "RECORDE",
        WinsLosses = "V/D",
        Tagline = "CLÁSSICO COM OBSTÁCULOS E PODERES",
        Apples = "MAÇÃS",
        ShieldSaved = "ESCUDO!",
        HelpBasics =
        [
            "DESLIZE O DEDO PARA VIRAR.",
            "COMA MAÇÃS PARA CRESCER.",
            "NÃO BATA NAS PAREDES, NOS",
            "BLOCOS NEM NO PRÓPRIO CORPO.",
            "8 MAÇÃS = PRÓXIMO NÍVEL.",
        ],
        HelpVersus =
        [
            "VS CPU: CHEGUE A 15 MAÇÃS",
            "ANTES DA CPU OU FAÇA ELA BATER.",
        ],
        PowerUpNames = ["LENTO", "FANTASMA", "PONTOS X2", "TESOURA", "ESCUDO"],
        PowerUpHelp =
        [
            "DEIXA O JOGO MAIS LENTO",
            "ATRAVESSA TUDO POR 6S",
            "DOBRA OS PONTOS POR 10S",
            "CORTA 4 PEDAÇOS DA CAUDA",
            "SALVA DE UMA BATIDA",
        ],
        GoldenName = "MAÇÃ DOURADA",
        GoldenHelp = "+50 PONTOS. SOME RÁPIDO!",
        ObstacleName = "BLOCO",
        ObstacleHelp = "OBSTÁCULO. NÃO BATA!",
    };

    public static readonly Strings English = new()
    {
        Classic = "CLASSIC",
        Versus = "VS CPU",
        Options = "OPTIONS",
        HowToPlay = "HOW TO PLAY",
        Back = "BACK",
        Resume = "RESUME",
        Restart = "RESTART",
        Menu = "MENU",
        PlayAgain = "PLAY AGAIN",
        Rematch = "REMATCH",
        Paused = "PAUSED",
        GameOver = "GAME OVER",
        NewRecord = "NEW RECORD!",
        Score = "SCORE",
        Best = "BEST",
        Level = "LEVEL",
        You = "YOU",
        Cpu = "CPU",
        Goal = "GOAL",
        YouWin = "YOU WIN!",
        CpuWins = "CPU WINS",
        Draw = "DRAW",
        SwipeToStart = "SWIPE TO\nSTART",
        ArrowToStart = "TAP AN ARROW\nTO START",
        LevelCleared = "LEVEL CLEAR!",
        Bonus = "BONUS",
        Difficulty = "DIFFICULTY",
        Easy = "EASY",
        Normal = "NORMAL",
        Hard = "HARD",
        Sound = "SOUND",
        Vibration = "VIBRATION",
        Controls = "CONTROLS",
        ControlSwipe = "SWIPE",
        ControlButtons = "BUTTONS",
        On = "ON",
        Off = "OFF",
        Record = "BEST",
        WinsLosses = "W/L",
        Tagline = "CLASSIC WITH OBSTACLES AND POWER-UPS",
        Apples = "APPLES",
        ShieldSaved = "SHIELD!",
        HelpBasics =
        [
            "SWIPE TO TURN.",
            "EAT APPLES TO GROW.",
            "DO NOT HIT THE WALLS, THE",
            "BLOCKS OR YOUR OWN BODY.",
            "8 APPLES = NEXT LEVEL.",
        ],
        HelpVersus =
        [
            "VS CPU: REACH 15 APPLES",
            "FIRST OR MAKE THE CPU CRASH.",
        ],
        PowerUpNames = ["SLOW", "GHOST", "DOUBLE", "SCISSORS", "SHIELD"],
        PowerUpHelp =
        [
            "SLOWS THE GAME DOWN",
            "PASS THROUGH ALL FOR 6S",
            "DOUBLE POINTS FOR 10S",
            "CUTS 4 TAIL PIECES",
            "SAVES YOU FROM ONE CRASH",
        ],
        GoldenName = "GOLDEN APPLE",
        GoldenHelp = "+50 POINTS. VANISHES FAST!",
        ObstacleName = "BLOCK",
        ObstacleHelp = "OBSTACLE. DO NOT HIT IT!",
    };
}
