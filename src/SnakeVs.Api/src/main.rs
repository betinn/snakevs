//! snakevs-api: the online ranking server. Meant to run behind nginx (or another web server that also serves the
//! game files) on the same machine. Configuration through environment variables:
//!
//!   SNAKEVS_LISTEN              address to listen on            (default 127.0.0.1:5090)
//!   SNAKEVS_DB                  SQLite file                     (default ./scores.db)
//!   SNAKEVS_ADMIN_KEY           enables DELETE /api/scores/{id} (default: off)
//!   SNAKEVS_ALLOWED_ORIGINS     comma separated sites allowed to call from another origin (default: none)
//!   SNAKEVS_SUBMITS_PER_MINUTE  per client address              (default 6)
//!   SNAKEVS_READS_PER_MINUTE    per client address              (default 120)
//!   SNAKEVS_THREADS             connections served at once      (default 4)
//!
//! HTTP is the minimum the game and a reverse proxy need: HTTP/1.1, one request per connection, Content-Length
//! bodies. Small and predictable: no async runtime, a fixed number of threads, a few MB of memory.

use std::io::{Read, Write};
use std::net::{IpAddr, Ipv4Addr, TcpListener, TcpStream};
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::Arc;
use std::time::Duration;

use snakevs_api::{App, Config, MAX_BODY, Request, Response};

const MAX_HEAD: usize = 8 * 1024;
const IO_TIMEOUT: Duration = Duration::from_secs(5);

fn env(name: &str) -> Option<String> {
    std::env::var(name)
        .ok()
        .map(|v| v.trim().to_string())
        .filter(|v| !v.is_empty())
}

fn env_num(name: &str, default: u32) -> u32 {
    env(name).and_then(|v| v.parse().ok()).unwrap_or(default)
}

fn main() {
    let listen = env("SNAKEVS_LISTEN").unwrap_or_else(|| "127.0.0.1:5090".into());
    let db = env("SNAKEVS_DB").unwrap_or_else(|| "scores.db".into());
    let config = Config {
        submits_per_minute: env_num("SNAKEVS_SUBMITS_PER_MINUTE", 6),
        reads_per_minute: env_num("SNAKEVS_READS_PER_MINUTE", 120),
        admin_key: env("SNAKEVS_ADMIN_KEY"),
        allowed_origins: env("SNAKEVS_ALLOWED_ORIGINS")
            .map(|v| {
                v.split(',')
                    .map(|s| s.trim().trim_end_matches('/').to_string())
                    .filter(|s| !s.is_empty())
                    .collect()
            })
            .unwrap_or_default(),
    };
    let threads = env_num("SNAKEVS_THREADS", 4).clamp(1, 32);

    let app = match App::open(&db, config) {
        Ok(app) => Arc::new(app),
        Err(e) => {
            eprintln!("cannot open database {db}: {e}");
            std::process::exit(1);
        }
    };
    let listener = match TcpListener::bind(&listen) {
        Ok(l) => l,
        Err(e) => {
            eprintln!("cannot listen on {listen}: {e}");
            std::process::exit(1);
        }
    };
    eprintln!(
        "snakevs-api {} listening on {listen}, database {db}",
        env!("CARGO_PKG_VERSION")
    );

    let workers: Vec<_> = (0..threads)
        .map(|_| {
            let listener = listener.try_clone().expect("listener clone");
            let app = app.clone();
            std::thread::spawn(move || {
                loop {
                    if let Ok((stream, _)) = listener.accept() {
                        serve(&app, stream);
                    }
                }
            })
        })
        .collect();
    for w in workers {
        let _ = w.join();
    }
}

struct Head {
    method: String,
    url: String,
    content_length: usize,
    chunked: bool,
    expect_continue: bool,
    origin: Option<String>,
    admin_key: Option<String>,
    forwarded_for: Option<String>,
}

fn parse_head(text: &str) -> Option<Head> {
    let mut lines = text.split("\r\n");
    let mut first = lines.next()?.split(' ');
    let (method, url, version) = (first.next()?, first.next()?, first.next()?);
    if !version.starts_with("HTTP/1.") || !url.starts_with('/') {
        return None;
    }

    let mut head = Head {
        method: method.to_string(),
        url: url.to_string(),
        content_length: 0,
        chunked: false,
        expect_continue: false,
        origin: None,
        admin_key: None,
        forwarded_for: None,
    };
    for line in lines {
        let Some((name, value)) = line.split_once(':') else {
            continue;
        };
        let value = value.trim();
        match name.trim().to_ascii_lowercase().as_str() {
            "content-length" => head.content_length = value.parse().ok()?,
            "transfer-encoding" => head.chunked = value.to_ascii_lowercase().contains("chunked"),
            "expect" => head.expect_continue = value.eq_ignore_ascii_case("100-continue"),
            "origin" => head.origin = Some(value.to_string()),
            "x-admin-key" => head.admin_key = Some(value.to_string()),
            "x-forwarded-for" => head.forwarded_for = Some(value.to_string()),
            _ => {}
        }
    }
    Some(head)
}

/// The real client address. Behind a proxy on this machine (peer is loopback) it is the last X-Forwarded-For entry,
/// the one the proxy itself added; a client cannot forge it because only local peers are trusted.
fn client_ip(peer: IpAddr, forwarded_for: Option<&str>) -> IpAddr {
    if !peer.is_loopback() {
        return peer;
    }
    forwarded_for
        .and_then(|v| v.rsplit(',').next())
        .and_then(|v| v.trim().parse().ok())
        .unwrap_or(peer)
}

fn serve(app: &App, mut stream: TcpStream) {
    // Each response goes out in a single write; no Nagle delay waiting for an ACK.
    let _ = stream.set_nodelay(true);
    let _ = stream.set_read_timeout(Some(IO_TIMEOUT));
    let _ = stream.set_write_timeout(Some(IO_TIMEOUT));
    let peer = stream
        .peer_addr()
        .map(|a| a.ip())
        .unwrap_or(IpAddr::V4(Ipv4Addr::UNSPECIFIED));

    let response = match read_request(&mut stream) {
        Ok((head, body)) => {
            let req = Request {
                method: &head.method,
                url: &head.url,
                client: client_ip(peer, head.forwarded_for.as_deref()),
                origin: head.origin.as_deref(),
                admin_key: head.admin_key.as_deref(),
                body: &body,
            };
            catch_unwind(AssertUnwindSafe(|| app.handle(&req))).unwrap_or_else(|_| {
                eprintln!("request failed: {} {}", head.method, head.url);
                plain(500)
            })
        }
        Err(Some(status)) => plain(status),
        Err(None) => return, // connection closed or timed out: nothing to answer
    };

    let _ = write_response(&mut stream, &response);
    let _ = stream.shutdown(std::net::Shutdown::Write);
}

/// Reads one request. Err(Some(status)) for a bad request worth answering, Err(None) for a dead connection.
fn read_request(stream: &mut TcpStream) -> Result<(Head, Vec<u8>), Option<u16>> {
    let mut buf = Vec::with_capacity(1024);
    let mut chunk = [0u8; 1024];
    let head_end = loop {
        if let Some(i) = buf.windows(4).position(|w| w == b"\r\n\r\n") {
            break i;
        }
        if buf.len() > MAX_HEAD {
            return Err(Some(431));
        }
        match stream.read(&mut chunk) {
            Ok(0) | Err(_) => return Err(None),
            Ok(n) => buf.extend_from_slice(&chunk[..n]),
        }
    };

    let text = std::str::from_utf8(&buf[..head_end]).map_err(|_| Some(400))?;
    let head = parse_head(text).ok_or(Some(400))?;
    if head.chunked {
        return Err(Some(411)); // the game and nginx always send Content-Length
    }
    if head.content_length > MAX_BODY {
        return Err(Some(413));
    }

    let mut body = buf[head_end + 4..].to_vec();
    if head.expect_continue && body.len() < head.content_length {
        stream
            .write_all(b"HTTP/1.1 100 Continue\r\n\r\n")
            .map_err(|_| None)?;
    }
    while body.len() < head.content_length {
        match stream.read(&mut chunk) {
            Ok(0) | Err(_) => return Err(None),
            Ok(n) => body.extend_from_slice(&chunk[..n]),
        }
    }
    body.truncate(head.content_length);
    Ok((head, body))
}

fn plain(status: u16) -> Response {
    Response {
        status,
        body: String::new(),
        headers: Vec::new(),
    }
}

fn reason(status: u16) -> &'static str {
    match status {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        411 => "Length Required",
        413 => "Content Too Large",
        429 => "Too Many Requests",
        431 => "Request Header Fields Too Large",
        _ => "Internal Server Error",
    }
}

fn write_response(stream: &mut TcpStream, r: &Response) -> std::io::Result<()> {
    let mut out = format!(
        "HTTP/1.1 {} {}\r\nContent-Length: {}\r\nConnection: close\r\n",
        r.status,
        reason(r.status),
        r.body.len()
    );
    for (name, value) in &r.headers {
        // Header values come from this program, but never let a stray newline split the response.
        if !value.contains(['\r', '\n']) {
            out.push_str(name);
            out.push_str(": ");
            out.push_str(value);
            out.push_str("\r\n");
        }
    }
    out.push_str("\r\n");
    out.push_str(&r.body);
    stream.write_all(out.as_bytes())
}
