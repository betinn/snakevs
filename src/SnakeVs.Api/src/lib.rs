//! Snake VS online ranking API. The HTTP layer (main.rs) turns each request into a [`Request`] and sends back the
//! [`Response`] from [`App::handle`], so everything here is testable without sockets.
//!
//! Routes (JSON, same contract as `LeaderboardJson` in SnakeVs.Core):
//!   GET    /api/scores/{endless|levels}/{easy|normal|hard}?take=10   top N (max 50)
//!   POST   /api/scores                                               {mode, difficulty, name, score, apples, level, steps}
//!   DELETE /api/scores/{id}                                          moderation, header X-Admin-Key
//!   GET    /api/health                                               "ok"

pub mod rules;

use std::collections::HashMap;
use std::net::IpAddr;
use std::sync::{Mutex, MutexGuard};
use std::time::{Duration, Instant};

use rusqlite::{Connection, params};
use serde::{Deserialize, Serialize};

use rules::{Difficulty, Mode, Submission};

/// Largest request body accepted (a submission is about 120 bytes).
pub const MAX_BODY: usize = 4096;

pub struct Config {
    pub submits_per_minute: u32,
    pub reads_per_minute: u32,
    /// Enables DELETE when set.
    pub admin_key: Option<String>,
    /// Sites allowed to call the API from another origin (game hosted elsewhere). Empty = same site only.
    pub allowed_origins: Vec<String>,
}

impl Default for Config {
    fn default() -> Self {
        Config {
            submits_per_minute: 6,
            reads_per_minute: 120,
            admin_key: None,
            allowed_origins: Vec::new(),
        }
    }
}

pub struct Request<'a> {
    pub method: &'a str,
    /// Path plus optional query, e.g. "/api/scores/endless/normal?take=5".
    pub url: &'a str,
    pub client: IpAddr,
    pub origin: Option<&'a str>,
    pub admin_key: Option<&'a str>,
    pub body: &'a [u8],
}

#[derive(Debug)]
pub struct Response {
    pub status: u16,
    pub body: String,
    pub headers: Vec<(&'static str, String)>,
}

impl Response {
    fn json(status: u16, body: String) -> Response {
        Response {
            status,
            body,
            headers: vec![("Content-Type", "application/json; charset=utf-8".into())],
        }
    }

    fn error(status: u16, message: &str) -> Response {
        Response::json(status, serde_json::json!({ "error": message }).to_string())
    }

    fn empty(status: u16) -> Response {
        Response {
            status,
            body: String::new(),
            headers: Vec::new(),
        }
    }
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct SubmissionDto {
    mode: String,
    difficulty: String,
    name: Option<String>,
    score: i32,
    apples: i32,
    level: i32,
    steps: i64,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ScoreEntry {
    id: i64,
    rank: i64,
    name: String,
    score: i64,
    apples: i64,
    level: i64,
    created_utc: String,
}

#[derive(Serialize)]
struct SubmitResult {
    id: i64,
    rank: i64,
    total: i64,
}

#[derive(Clone, Copy, PartialEq, Eq, Hash)]
enum Kind {
    Read,
    Submit,
}

/// Fixed one-minute window per client address and kind of request.
struct Limiter {
    windows: HashMap<(IpAddr, Kind), (Instant, u32)>,
    last_prune: Instant,
}

impl Limiter {
    const WINDOW: Duration = Duration::from_secs(60);

    fn allow(&mut self, ip: IpAddr, kind: Kind, limit: u32) -> bool {
        let now = Instant::now();
        if now.duration_since(self.last_prune) >= Self::WINDOW {
            self.windows
                .retain(|_, (start, _)| now.duration_since(*start) < Self::WINDOW);
            self.last_prune = now;
        }

        let entry = self.windows.entry((ip, kind)).or_insert((now, 0));
        if now.duration_since(entry.0) >= Self::WINDOW {
            *entry = (now, 0);
        }
        if entry.1 >= limit {
            return false;
        }
        entry.1 += 1;
        true
    }
}

pub struct App {
    db: Mutex<Connection>,
    limiter: Mutex<Limiter>,
    config: Config,
}

fn lock<T>(m: &Mutex<T>) -> MutexGuard<'_, T> {
    // A panic while holding the lock leaves the data usable (SQLite rolls back unfinished transactions).
    m.lock().unwrap_or_else(|e| e.into_inner())
}

impl App {
    /// Opens (or creates) the database file and the table.
    pub fn open(path: &str, config: Config) -> rusqlite::Result<App> {
        Self::with_connection(Connection::open(path)?, config)
    }

    pub fn open_in_memory(config: Config) -> rusqlite::Result<App> {
        Self::with_connection(Connection::open_in_memory()?, config)
    }

    fn with_connection(db: Connection, config: Config) -> rusqlite::Result<App> {
        db.busy_timeout(Duration::from_secs(5))?;
        // Same schema as the former .NET server, so an existing scores.db keeps working.
        db.execute_batch(
            "PRAGMA journal_mode = WAL;
             PRAGMA synchronous = NORMAL;
             CREATE TABLE IF NOT EXISTS scores (
                 id          INTEGER PRIMARY KEY AUTOINCREMENT,
                 mode        INTEGER NOT NULL,
                 difficulty  INTEGER NOT NULL,
                 name        TEXT    NOT NULL,
                 score       INTEGER NOT NULL,
                 apples      INTEGER NOT NULL,
                 level       INTEGER NOT NULL,
                 steps       INTEGER NOT NULL,
                 created_utc TEXT    NOT NULL
             );
             CREATE INDEX IF NOT EXISTS ix_scores_board ON scores (mode, difficulty, score DESC, id);",
        )?;
        Ok(App {
            db: Mutex::new(db),
            limiter: Mutex::new(Limiter {
                windows: HashMap::new(),
                last_prune: Instant::now(),
            }),
            config,
        })
    }

    pub fn handle(&self, req: &Request) -> Response {
        let (path, query) = req.url.split_once('?').unwrap_or((req.url, ""));
        let path = path.trim_end_matches('/');
        let segments: Vec<&str> = path.split('/').filter(|s| !s.is_empty()).collect();

        let mut response = match (req.method, segments.as_slice()) {
            ("OPTIONS", ["api", ..]) => Response::empty(204),
            ("GET", ["api", "health"]) => Response {
                status: 200,
                body: "ok".into(),
                headers: vec![("Content-Type", "text/plain".into())],
            },
            ("GET", ["api", "scores", mode, difficulty]) => {
                self.limited(req, Kind::Read, || self.top(mode, difficulty, query))
            }
            ("POST", ["api", "scores"]) => {
                self.limited(req, Kind::Submit, || self.submit(req.body))
            }
            ("DELETE", ["api", "scores", id]) => self.delete(id, req.admin_key),
            _ => Response::error(404, "not found"),
        };

        response.headers.push(("Cache-Control", "no-store".into()));
        response
            .headers
            .push(("X-Content-Type-Options", "nosniff".into()));
        self.add_cors(req, &mut response);
        response
    }

    fn limited(&self, req: &Request, kind: Kind, run: impl FnOnce() -> Response) -> Response {
        let limit = match kind {
            Kind::Read => self.config.reads_per_minute,
            Kind::Submit => self.config.submits_per_minute,
        };
        if !lock(&self.limiter).allow(req.client, kind, limit) {
            return Response::error(429, "too many requests");
        }
        run()
    }

    fn top(&self, mode: &str, difficulty: &str, query: &str) -> Response {
        let (Some(mode), Some(difficulty)) = (Mode::parse(mode), Difficulty::parse(difficulty))
        else {
            return Response::error(404, "unknown ranking");
        };
        if !rules::is_ranked(mode) {
            return Response::error(404, "unknown ranking");
        }

        let take = query
            .split('&')
            .find_map(|p| p.strip_prefix("take="))
            .and_then(|v| v.parse::<i64>().ok())
            .unwrap_or(rules::DEFAULT_TAKE)
            .clamp(1, rules::MAX_TAKE);

        let db = lock(&self.db);
        let result = (|| -> rusqlite::Result<Vec<ScoreEntry>> {
            let mut stmt = db.prepare_cached(
                "SELECT id, name, score, apples, level, created_utc FROM scores
                 WHERE mode = ?1 AND difficulty = ?2 ORDER BY score DESC, id LIMIT ?3",
            )?;
            let rows = stmt.query_map(params![mode as i64, difficulty as i64, take], |r| {
                Ok(ScoreEntry {
                    id: r.get(0)?,
                    rank: 0,
                    name: r.get(1)?,
                    score: r.get(2)?,
                    apples: r.get(3)?,
                    level: r.get(4)?,
                    created_utc: r.get(5)?,
                })
            })?;
            let mut list = rows.collect::<rusqlite::Result<Vec<_>>>()?;
            for (i, e) in list.iter_mut().enumerate() {
                e.rank = i as i64 + 1;
            }
            Ok(list)
        })();

        match result {
            Ok(list) => Response::json(
                200,
                serde_json::to_string(&list).unwrap_or_else(|_| "[]".into()),
            ),
            Err(e) => db_error(e),
        }
    }

    fn submit(&self, body: &[u8]) -> Response {
        let Ok(dto) = serde_json::from_slice::<SubmissionDto>(body) else {
            return Response::error(400, "invalid body");
        };
        let (Some(mode), Some(difficulty)) =
            (Mode::parse(&dto.mode), Difficulty::parse(&dto.difficulty))
        else {
            return Response::error(400, "invalid score");
        };
        let Some(name) = dto.name.as_deref().and_then(rules::normalize_name) else {
            return Response::error(400, "invalid name");
        };

        let s = Submission {
            mode,
            difficulty,
            name,
            score: dto.score,
            apples: dto.apples,
            level: dto.level,
            steps: dto.steps,
        };
        if !rules::is_plausible(&s) {
            return Response::error(400, "invalid score");
        }

        let mut db = lock(&self.db);
        let result = (|| -> rusqlite::Result<SubmitResult> {
            let tx = db.transaction()?;
            tx.execute(
                "INSERT INTO scores (mode, difficulty, name, score, apples, level, steps, created_utc)
                 VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))",
                params![s.mode as i64, s.difficulty as i64, s.name, s.score, s.apples, s.level, s.steps],
            )?;
            let id = tx.last_insert_rowid();
            // Ties go to whoever got there first (lower id).
            let (rank, total) = tx.query_row(
                "SELECT
                    (SELECT COUNT(*) FROM scores WHERE mode = ?1 AND difficulty = ?2
                        AND (score > ?3 OR (score = ?3 AND id < ?4))) + 1,
                    (SELECT COUNT(*) FROM scores WHERE mode = ?1 AND difficulty = ?2)",
                params![s.mode as i64, s.difficulty as i64, s.score, id],
                |r| Ok((r.get(0)?, r.get(1)?)),
            )?;
            tx.commit()?;
            Ok(SubmitResult { id, rank, total })
        })();

        match result {
            Ok(r) => {
                let mut response =
                    Response::json(201, serde_json::to_string(&r).unwrap_or_default());
                response.headers.push((
                    "Location",
                    format!("/api/scores/{}/{}", s.mode.name(), s.difficulty.name()),
                ));
                response
            }
            Err(e) => db_error(e),
        }
    }

    fn delete(&self, id: &str, given_key: Option<&str>) -> Response {
        let Some(key) = self.config.admin_key.as_deref().filter(|k| !k.is_empty()) else {
            return Response::error(404, "not found");
        };
        if !constant_time_eq(given_key.unwrap_or("").as_bytes(), key.as_bytes()) {
            return Response::error(401, "unauthorized");
        }
        let Ok(id) = id.parse::<i64>() else {
            return Response::error(404, "not found");
        };

        match lock(&self.db).execute("DELETE FROM scores WHERE id = ?1", params![id]) {
            Ok(0) => Response::error(404, "not found"),
            Ok(_) => Response::empty(204),
            Err(e) => db_error(e),
        }
    }

    fn add_cors(&self, req: &Request, response: &mut Response) {
        let Some(origin) = req.origin else { return };
        if !self
            .config
            .allowed_origins
            .iter()
            .any(|o| o.eq_ignore_ascii_case(origin))
        {
            return;
        }
        response
            .headers
            .push(("Access-Control-Allow-Origin", origin.to_string()));
        response.headers.push(("Vary", "Origin".into()));
        if req.method == "OPTIONS" {
            response
                .headers
                .push(("Access-Control-Allow-Methods", "GET, POST".into()));
            response
                .headers
                .push(("Access-Control-Allow-Headers", "Content-Type".into()));
            response
                .headers
                .push(("Access-Control-Max-Age", "86400".into()));
        }
    }
}

fn db_error(e: rusqlite::Error) -> Response {
    eprintln!("database error: {e}");
    Response::error(500, "storage error")
}

fn constant_time_eq(a: &[u8], b: &[u8]) -> bool {
    if a.len() != b.len() {
        return false;
    }
    a.iter().zip(b).fold(0u8, |acc, (x, y)| acc | (x ^ y)) == 0
}
