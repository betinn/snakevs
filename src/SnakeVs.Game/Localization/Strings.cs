using System.Globalization;
using SnakeVs.Core;

namespace SnakeVs.Game.Localization;

/// <summary>All user facing text. Portuguese (Brazil) for "pt" cultures, English otherwise.</summary>
public sealed class Strings
{
    public required string Journey { get; init; }
    public required string Stage { get; init; }
    public required string PortalOpen { get; init; }
    public required string Chaos { get; init; }
    public required string TailCut { get; init; }
    public required string AppleStolen { get; init; }
    public required string RivalDown { get; init; }

    /// <summary>Names of the stage elements, indexed by <see cref="StageElement"/>.</summary>
    public required string[] StageNames { get; init; }

    /// <summary>One tip per stage element, shown when a stage starts. Indexed by <see cref="StageElement"/>.</summary>
    public required string[] StageTips { get; init; }
    public required string[] HelpJourney { get; init; }
    public required string Classic { get; init; }
    public required string Levels { get; init; }
    public required string Speed { get; init; }
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
    public required string Ranking { get; init; }
    public required string YourScore { get; init; }
    public required string Position { get; init; }
    public required string Of { get; init; }
    public required string Sending { get; init; }
    public required string SaveFailed { get; init; }
    public required string Loading { get; init; }
    public required string RankingEmpty { get; init; }
    public required string RankingOffline { get; init; }
    public required string Retry { get; init; }

    /// <summary>Name dialog (drawn by the platform, not the pixel font: mixed case allowed).</summary>
    public required string NameTitle { get; init; }
    public required string NamePoints { get; init; }
    public required string NamePrompt { get; init; }
    public required string NameSave { get; init; }
    public required string NameCancel { get; init; }
    public required string[] HelpBasics { get; init; }
    public required string[] HelpModes { get; init; }
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

    /// <summary>Name of a journey stage: its element, two of them joined, or "chaos" when there are more.</summary>
    public string StageName(IReadOnlyList<StageElement> elements) => elements.Count switch
    {
        0 => string.Empty,
        1 => StageNames[(int)elements[0]],
        2 => $"{StageNames[(int)elements[0]]} + {StageNames[(int)elements[1]]}",
        _ => Chaos,
    };

    /// <summary>Tip for a stage with a single element; mixed stages have been explained before.</summary>
    public string StageTip(IReadOnlyList<StageElement> elements) => elements.Count == 1 ? StageTips[(int)elements[0]] : string.Empty;

    public static Strings ForCulture(CultureInfo culture) => ForLanguage(culture.TwoLetterISOLanguageName);

    /// <summary>Picks the strings for a language tag such as "pt", "pt-BR" or "en-US".</summary>
    public static Strings ForLanguage(string tag) =>
        tag.Equals("pt", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("pt-", StringComparison.OrdinalIgnoreCase)
            ? Portuguese
            : English;

    public static readonly Strings Portuguese = new()
    {
        Journey = "JORNADA",
        Stage = "FASE",
        PortalOpen = "PORTAL ABERTO!",
        Chaos = "CAOS",
        TailCut = "CORTE!",
        AppleStolen = "ROUBADA!",
        RivalDown = "RIVAL FORA!",
        StageNames =
        [
            "LARGADA", "BLOCOS", "SERRAS", "LASERS", "BESOUROS", "PISCA-PISCA", "LAVA",
            "TELEPORTES", "ESPECTRO", "APAGÃO", "GELO", "METEOROS", "COBRA RIVAL", "TORRE LASER",
        ],
        StageTips =
        [
            "COMA A MAÇÃ E SUBA PELO PORTAL",
            "DESVIE DOS BLOCOS",
            "NA CABEÇA MATA, NA CAUDA CORTA",
            "PASSE QUANDO O RAIO APAGAR",
            "NÃO DEIXE MORDER A CABEÇA",
            "ELES SOMEM E VOLTAM",
            "A LAVA SOBE. CORRA!",
            "ENTRE NUM ANEL, SAIA NO OUTRO",
            "COMA PARA ATORDOAR O ESPECTRO",
            "SIGA A LUZ",
            "NO GELO NÃO DÁ PARA VIRAR",
            "FUJA DAS MIRAS",
            "FAÇA ELA BATER EM VOCÊ",
            "A TORRE GIRA. ACOMPANHE!",
        ],
        HelpJourney =
        [
            "JORNADA: FASES SEM PARADA. COMA AS",
            "MAÇÃS, SUBA PELO PORTAL E O MAPA ANDA.",
            "PERIGO NA CABEÇA MATA, NA CAUDA CORTA.",
        ],
        Classic = "CLÁSSICO",
        Levels = "NÍVEIS",
        Speed = "VELOCIDADE",
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
        Tagline = "JORNADA, CLÁSSICO, NÍVEIS E DUELO",
        Apples = "MAÇÃS",
        ShieldSaved = "ESCUDO!",
        Ranking = "RANKING",
        YourScore = "SUA PONTUAÇÃO",
        Position = "POSIÇÃO",
        Of = "DE",
        Sending = "ENVIANDO...",
        SaveFailed = "ERRO. TENTAR DE NOVO",
        Loading = "CARREGANDO...",
        RankingEmpty = "NINGUÉM AINDA. SEJA O PRIMEIRO!",
        RankingOffline = "RANKING INDISPONÍVEL",
        Retry = "TENTAR DE NOVO",
        NameTitle = "Você entrou no top 10!",
        NamePoints = "pontos",
        NamePrompt = "Digite seu apelido",
        NameSave = "OK",
        NameCancel = "Pular",
        HelpBasics =
        [
            "DESLIZE O DEDO PARA VIRAR.",
            "COMA MAÇÃS PARA CRESCER.",
            "NÃO BATA NAS PAREDES, NOS",
            "BLOCOS NEM NO PRÓPRIO CORPO.",
        ],
        HelpModes =
        [
            "CLÁSSICO: SEM FIM. A COBRA CRESCE",
            "E O JOGO ACELERA A CADA MAÇÃ.",
            "NÍVEIS: 8 MAÇÃS = PRÓXIMO NÍVEL.",
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
        Journey = "JOURNEY",
        Stage = "STAGE",
        PortalOpen = "PORTAL OPEN!",
        Chaos = "CHAOS",
        TailCut = "CUT!",
        AppleStolen = "STOLEN!",
        RivalDown = "RIVAL OUT!",
        StageNames =
        [
            "KICK-OFF", "BLOCKS", "SAWS", "LASERS", "BEETLES", "BLINKERS", "LAVA",
            "TELEPORTS", "SPECTER", "BLACKOUT", "ICE", "METEORS", "RIVAL SNAKE", "LASER TOWER",
        ],
        StageTips =
        [
            "EAT THE APPLE, THEN UP THE PORTAL",
            "MIND THE BLOCKS",
            "HEAD: DEAD. TAIL: CUT",
            "CROSS WHEN THE BEAM IS OFF",
            "DO NOT LET THEM BITE YOUR HEAD",
            "THEY COME AND GO",
            "THE LAVA RISES. RUN!",
            "IN ONE RING, OUT THE OTHER",
            "EAT TO STUN THE SPECTER",
            "FOLLOW THE LIGHT",
            "NO TURNING ON ICE",
            "GET OFF THE CROSSHAIRS",
            "MAKE IT CRASH INTO YOU",
            "THE TOWER TURNS. KEEP UP!",
        ],
        HelpJourney =
        [
            "JOURNEY: STAGES WITH NO STOPS. EAT THE",
            "APPLES, GO UP THE PORTAL, THE MAP MOVES ON.",
            "HAZARDS KILL THE HEAD AND CUT THE TAIL.",
        ],
        Classic = "CLASSIC",
        Levels = "LEVELS",
        Speed = "SPEED",
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
        Tagline = "JOURNEY, CLASSIC, LEVELS AND DUEL",
        Apples = "APPLES",
        ShieldSaved = "SHIELD!",
        Ranking = "RANKING",
        YourScore = "YOUR SCORE",
        Position = "POSITION",
        Of = "OF",
        Sending = "SENDING...",
        SaveFailed = "ERROR. TRY AGAIN",
        Loading = "LOADING...",
        RankingEmpty = "NO SCORES YET. BE THE FIRST!",
        RankingOffline = "RANKING UNAVAILABLE",
        Retry = "TRY AGAIN",
        NameTitle = "You made the top 10!",
        NamePoints = "points",
        NamePrompt = "Type your nickname",
        NameSave = "OK",
        NameCancel = "Skip",
        HelpBasics =
        [
            "SWIPE TO TURN.",
            "EAT APPLES TO GROW.",
            "DO NOT HIT THE WALLS, THE",
            "BLOCKS OR YOUR OWN BODY.",
        ],
        HelpModes =
        [
            "CLASSIC: NO END. THE SNAKE GROWS",
            "AND THE GAME SPEEDS UP WITH EVERY APPLE.",
            "LEVELS: 8 APPLES = NEXT LEVEL.",
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
