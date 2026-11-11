//! Ranking rules. Port of `src/SnakeVs.Core/Leaderboard.cs` and the numbers of `Rules.cs`.
//! Both sides are checked against the same cases in `tests/fixtures/leaderboard.json`; change the C# rules,
//! regenerate the fixture (see the C# `LeaderboardFixtureTests`), then update this file until `cargo test` passes.

use unicode_normalization::UnicodeNormalization;

pub const BOARD_WIDTH: i64 = 20;
pub const BOARD_HEIGHT: i64 = 30;
pub const APPLES_PER_LEVEL: i64 = 8;
pub const ENDLESS_APPLES_PER_SPEED_STEP: i64 = 5;
pub const APPLE_SCORE: i64 = 10;
pub const APPLE_SCORE_PER_LEVEL: i64 = 2;
pub const GOLDEN_SCORE: i64 = 50;
pub const POWER_UP_SCORE: i64 = 5;
pub const LEVEL_BONUS: i64 = 100;
pub const NAME_MAX_LENGTH: usize = 12;
pub const DEFAULT_TAKE: i64 = 10;
pub const MAX_TAKE: i64 = 50;

/// Accented capitals the pixel font can draw.
const ACCENTED_LETTERS: &str = "ÁÀÂÃÉÊÍÓÔÕÚÜÇÑ";

/// Same numbers as the C# `GameMode` enum (stored in the database).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Mode {
    Levels = 0,
    Endless = 1,
    Versus = 2,
}

/// Same numbers as the C# `Difficulty` enum (stored in the database).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Difficulty {
    Easy = 0,
    Normal = 1,
    Hard = 2,
}

impl Mode {
    pub fn parse(s: &str) -> Option<Mode> {
        match s.to_ascii_lowercase().as_str() {
            "levels" => Some(Mode::Levels),
            "endless" => Some(Mode::Endless),
            "versus" => Some(Mode::Versus),
            _ => None,
        }
    }

    pub fn name(self) -> &'static str {
        match self {
            Mode::Levels => "Levels",
            Mode::Endless => "Endless",
            Mode::Versus => "Versus",
        }
    }
}

impl Difficulty {
    pub fn parse(s: &str) -> Option<Difficulty> {
        match s.to_ascii_lowercase().as_str() {
            "easy" => Some(Difficulty::Easy),
            "normal" => Some(Difficulty::Normal),
            "hard" => Some(Difficulty::Hard),
            _ => None,
        }
    }

    pub fn name(self) -> &'static str {
        match self {
            Difficulty::Easy => "Easy",
            Difficulty::Normal => "Normal",
            Difficulty::Hard => "Hard",
        }
    }
}

/// A finished run as sent by the game.
#[derive(Clone, Debug)]
pub struct Submission {
    pub mode: Mode,
    pub difficulty: Difficulty,
    pub name: String,
    pub score: i32,
    pub apples: i32,
    pub level: i32,
    pub steps: i64,
}

/// Versus ends in a win or a loss, not a score worth ranking.
pub fn is_ranked(mode: Mode) -> bool {
    matches!(mode, Mode::Endless | Mode::Levels)
}

pub fn is_name_char(c: char) -> bool {
    c.is_ascii_uppercase()
        || c.is_ascii_digit()
        || matches!(c, '.' | '-' | '_')
        || ACCENTED_LETTERS.contains(c)
}

/// Upper case, letters/digits/space/.-_ only, single spaces, at most 12 characters; None when nothing is left.
pub fn normalize_name(raw: &str) -> Option<String> {
    if raw.trim().is_empty() {
        return None;
    }

    let mut out = String::new();
    let mut len = 0usize;
    let mut pending_space = false;
    for ch in raw.nfc() {
        let c = upper(ch);
        if c.is_whitespace() {
            pending_space = len > 0;
            continue;
        }

        if !is_name_char(c) {
            continue;
        }

        if pending_space {
            if len + 1 >= NAME_MAX_LENGTH {
                break;
            }
            out.push(' ');
            len += 1;
            pending_space = false;
        }

        if len >= NAME_MAX_LENGTH {
            break;
        }
        out.push(c);
        len += 1;
    }

    (len > 0).then_some(out)
}

/// One-to-one upper case like .NET `char.ToUpperInvariant` ("ß" stays "ß" instead of becoming "SS").
fn upper(c: char) -> char {
    let mut u = c.to_uppercase();
    match (u.next(), u.next()) {
        (Some(a), None) => a,
        _ => c,
    }
}

pub fn apple_value(level: i64) -> i64 {
    APPLE_SCORE + (level - 1) * APPLE_SCORE_PER_LEVEL
}

/// Rejects numbers the game cannot produce. Bonuses only spawn when an apple is eaten, so each apple brings at most
/// one golden apple and one power-up, all doubled at best.
pub fn is_plausible(s: &Submission) -> bool {
    if !is_ranked(s.mode) {
        return false;
    }
    if s.score <= 0 || s.apples < 0 || s.level < 1 || s.steps < 1 {
        return false;
    }

    let apples = s.apples as i64;
    let level = s.level as i64;
    if apples > s.steps {
        return false;
    }

    let (top_level, level_bonus): (i64, i128) = match s.mode {
        Mode::Endless => {
            if apples > BOARD_WIDTH * BOARD_HEIGHT || level != 1 {
                return false;
            }
            (1 + apples / ENDLESS_APPLES_PER_SPEED_STEP, 0)
        }
        _ => {
            // Levels: 8 apples per level; clearing level l pays LEVEL_BONUS * l.
            if level > apples / APPLES_PER_LEVEL + 1 {
                return false;
            }
            (
                level,
                LEVEL_BONUS as i128 * (level as i128 - 1) * level as i128 / 2,
            )
        }
    };

    let per_apple = 2 * (apple_value(top_level) + GOLDEN_SCORE + POWER_UP_SCORE) as i128;
    (s.score as i128) <= apples as i128 * per_apple + level_bonus
}
