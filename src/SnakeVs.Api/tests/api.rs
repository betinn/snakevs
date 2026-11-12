//! The HTTP routes, run against an in-memory SQLite database (no sockets).

use std::net::{IpAddr, Ipv4Addr};

use serde_json::{Value, json};
use snakevs_api::{App, Config, Request, Response};

const CLIENT: IpAddr = IpAddr::V4(Ipv4Addr::new(203, 0, 113, 7));

fn app() -> App {
    App::open_in_memory(Config {
        submits_per_minute: 1000,
        reads_per_minute: 1000,
        admin_key: Some("secret".into()),
        allowed_origins: vec!["https://game.example".into()],
    })
    .unwrap()
}

fn call(app: &App, method: &str, url: &str, body: &str) -> Response {
    app.handle(&Request {
        method,
        url,
        client: CLIENT,
        origin: None,
        admin_key: None,
        body: body.as_bytes(),
    })
}

fn run(name: &str, score: i64, mode: &str, difficulty: &str) -> String {
    json!({ "mode": mode, "difficulty": difficulty, "name": name, "score": score, "apples": 20, "level": 1, "steps": 2000 }).to_string()
}

fn post(app: &App, name: &str, score: i64) -> Value {
    let r = call(
        app,
        "POST",
        "/api/scores",
        &run(name, score, "Endless", "Normal"),
    );
    assert_eq!(r.status, 201, "{}", r.body);
    serde_json::from_str(&r.body).unwrap()
}

fn top(app: &App, url: &str) -> Vec<Value> {
    let r = call(app, "GET", url, "");
    assert_eq!(r.status, 200, "{}", r.body);
    serde_json::from_str::<Vec<Value>>(&r.body).unwrap()
}

#[test]
fn submit_returns_rank_and_top_is_ordered() {
    let app = app();
    assert_eq!(post(&app, "ana", 100)["rank"], 1);
    assert_eq!(post(&app, "beto", 300)["rank"], 1);
    let third = post(&app, "caio", 100);
    assert_eq!(third["rank"], 3); // ties go to whoever got there first
    assert_eq!(third["total"], 3);

    let list = top(&app, "/api/scores/endless/normal");
    let names: Vec<_> = list.iter().map(|e| e["name"].as_str().unwrap()).collect();
    assert_eq!(names, ["BETO", "ANA", "CAIO"]);
    let ranks: Vec<_> = list.iter().map(|e| e["rank"].as_i64().unwrap()).collect();
    assert_eq!(ranks, [1, 2, 3]);
    // Same JSON contract as the game's LeaderboardJson (camelCase, ISO date).
    let e = &list[0];
    for key in [
        "id",
        "rank",
        "name",
        "score",
        "apples",
        "level",
        "createdUtc",
    ] {
        assert!(e.get(key).is_some(), "missing {key}");
    }
    assert!(e["createdUtc"].as_str().unwrap().ends_with('Z'));
}

#[test]
fn boards_are_separated_by_mode_and_difficulty() {
    let app = app();
    assert_eq!(
        call(
            &app,
            "POST",
            "/api/scores",
            &run("ana", 100, "Endless", "Easy")
        )
        .status,
        201
    );
    assert_eq!(
        call(
            &app,
            "POST",
            "/api/scores/",
            &run("beto", 100, "endless", "hard")
        )
        .status,
        201
    );
    assert_eq!(top(&app, "/api/scores/endless/easy").len(), 1);
    assert_eq!(top(&app, "/api/scores/ENDLESS/HARD").len(), 1);
    assert!(top(&app, "/api/scores/levels/easy").is_empty());
}

#[test]
fn journey_has_its_own_ranking() {
    let app = app();
    // Stage 4 reached with 20 apples: within the rules. The same run in stage 1 would hold too many apples.
    let stage4 = json!({ "mode": "Journey", "difficulty": "Normal", "name": "ana", "score": 700, "apples": 20, "level": 4, "steps": 2000 }).to_string();
    assert_eq!(call(&app, "POST", "/api/scores", &stage4).status, 201);
    assert_eq!(
        call(
            &app,
            "POST",
            "/api/scores",
            &run("beto", 100, "Journey", "Normal")
        )
        .status,
        400
    );
    let list = top(&app, "/api/scores/journey/normal");
    assert_eq!(list.len(), 1);
    assert_eq!(list[0]["level"], 4);
    assert!(top(&app, "/api/scores/endless/normal").is_empty());
}

#[test]
fn take_is_clamped() {
    let app = app();
    for i in 0..12 {
        post(&app, &format!("p{i}"), 10 + i);
    }
    assert_eq!(top(&app, "/api/scores/endless/normal").len(), 10);
    assert_eq!(top(&app, "/api/scores/endless/normal?take=3").len(), 3);
    assert_eq!(top(&app, "/api/scores/endless/normal?take=999").len(), 12);
    assert_eq!(top(&app, "/api/scores/endless/normal?take=abc").len(), 10);
}

#[test]
fn unknown_routes_and_boards_are_404() {
    let app = app();
    for url in [
        "/api/scores/versus/normal",
        "/api/scores/endless/impossible",
        "/api/scores/1/2",
        "/api/nothing",
        "/",
    ] {
        assert_eq!(call(&app, "GET", url, "").status, 404, "{url}");
    }
}

#[test]
fn invalid_submissions_are_rejected() {
    let app = app();
    for body in [
        run("@@@", 100, "Endless", "Normal"),
        run("", 100, "Endless", "Normal"),
        run("ana", 9_999_999, "Endless", "Normal"),
        run("ana", 10, "Versus", "Normal"),
        run("ana", 10, "Endless", "Nightmare"),
        "not json".into(),
        json!({ "mode": "Endless", "difficulty": "Normal", "name": "ana", "score": 99_999_999_999i64, "apples": 1, "level": 1, "steps": 10 }).to_string(),
    ] {
        assert_eq!(call(&app, "POST", "/api/scores", &body).status, 400, "{body}");
    }
    assert!(top(&app, "/api/scores/endless/normal").is_empty());
}

#[test]
fn stored_names_are_normalized() {
    let app = app();
    post(&app, "  <b>zé</b>   lagarto ", 50);
    assert_eq!(
        top(&app, "/api/scores/endless/normal")[0]["name"],
        "BZÉB LAGARTO"
    );
}

#[test]
fn delete_needs_the_admin_key() {
    let app = app();
    let id = post(&app, "ana", 100)["id"].as_i64().unwrap();
    let url = format!("/api/scores/{id}");
    let delete = |key: Option<&str>| {
        app.handle(&Request {
            method: "DELETE",
            url: &url,
            client: CLIENT,
            origin: None,
            admin_key: key,
            body: b"",
        })
        .status
    };
    assert_eq!(delete(None), 401);
    assert_eq!(delete(Some("wrong")), 401);
    assert_eq!(delete(Some("secret")), 204);
    assert_eq!(delete(Some("secret")), 404);
    assert!(top(&app, "/api/scores/endless/normal").is_empty());
}

#[test]
fn delete_is_off_without_a_key() {
    let app = App::open_in_memory(Config::default()).unwrap();
    let r = app.handle(&Request {
        method: "DELETE",
        url: "/api/scores/1",
        client: CLIENT,
        origin: None,
        admin_key: Some(""),
        body: b"",
    });
    assert_eq!(r.status, 404);
}

#[test]
fn submissions_are_rate_limited_per_client() {
    let app = App::open_in_memory(Config {
        submits_per_minute: 2,
        ..Config::default()
    })
    .unwrap();
    let body = run("ana", 10, "Endless", "Normal");
    let send = |ip: [u8; 4]| {
        app.handle(&Request {
            method: "POST",
            url: "/api/scores",
            client: IpAddr::from(ip),
            origin: None,
            admin_key: None,
            body: body.as_bytes(),
        })
        .status
    };
    assert_eq!(
        [send([1, 1, 1, 1]), send([1, 1, 1, 1]), send([1, 1, 1, 1])],
        [201, 201, 429]
    );
    assert_eq!(send([2, 2, 2, 2]), 201); // another player is not affected
}

#[test]
fn cors_only_for_allowed_origins() {
    let app = app();
    let preflight = |origin| {
        app.handle(&Request {
            method: "OPTIONS",
            url: "/api/scores",
            client: CLIENT,
            origin: Some(origin),
            admin_key: None,
            body: b"",
        })
    };
    let ok = preflight("https://game.example");
    assert_eq!(ok.status, 204);
    assert!(
        ok.headers
            .iter()
            .any(|(k, v)| *k == "Access-Control-Allow-Origin" && v == "https://game.example")
    );
    let other = preflight("https://evil.example");
    assert!(
        !other
            .headers
            .iter()
            .any(|(k, _)| k.starts_with("Access-Control"))
    );
}

#[test]
fn reopening_the_database_keeps_scores() {
    let path = std::env::temp_dir().join(format!("snakevs-api-test-{}.db", std::process::id()));
    let path = path.to_str().unwrap();
    {
        let app = App::open(path, Config::default()).unwrap();
        post(&app, "ana", 100);
    }
    {
        let app = App::open(path, Config::default()).unwrap();
        assert_eq!(top(&app, "/api/scores/endless/normal").len(), 1);
    }
    for suffix in ["", "-wal", "-shm"] {
        let _ = std::fs::remove_file(format!("{path}{suffix}"));
    }
}
