# API Rate Limiter — Provn Challenge #101

A proof-of-concept rate-limiting middleware for a B2B SaaS API, built in response to a real incident where one client consumed 40% of API capacity over three hours.

**Stack:** ASP.NET Core 8 (C#) · Angular 19

---

## Quick Start

### 1 — Backend

```bash
cd RateLimiterApi
dotnet run
# API listens on http://localhost:5257
```

### 2 — Frontend

```bash
cd rate-limiter-ui
npm install
ng serve
# UI available at http://localhost:4200
```

---

## Architecture

### Algorithm: Sliding Window Counter

Each unique `(clientId, key)` pair is tracked by a `WindowTracker` — a thread-safe wrapper around a `Queue<long>` of UTC timestamps (in milliseconds).

On every request:
1. Expired timestamps older than `WindowSeconds` are purged from the front of the queue.
2. If the queue length is at or above the configured limit, the request is rejected (HTTP 429).
3. Otherwise, the current timestamp is enqueued and the request proceeds.

This avoids the "burst at window boundary" problem that plagues fixed-window counters, while remaining far simpler to implement in-memory than a true sliding-window log with distributed storage.

### Middleware Chain

```
Incoming Request
    │
    ▼
RateLimitingMiddleware          ← registered via app.UseMiddleware<>()
    ├── Extract X-Client-Id header (falls back to "anonymous")
    ├── Extract X-Client-Tier header (falls back to "standard")
    ├── Resolve endpoint key by prefix match (e.g. DELETE:/api/data/42 → DELETE:/api/data)
    │
    ├── [Endpoint limit check]  ← stricter, write-operation focused
    │       └── 429 + Retry-After if exceeded
    │
    ├── [Client tier limit check]
    │       └── 429 + Retry-After if exceeded
    │
    └── Record request + inject X-RateLimit-* headers → next middleware
```

All state lives in a `ConcurrentDictionary<string, WindowTracker>` — no Redis, no database, no external process.

---

## Configuration

Limits are driven entirely by `appsettings.json`. No code change is needed to adjust thresholds:

```json
"RateLimiting": {
  "WindowSeconds": 60,
  "DefaultLimit": 100,
  "Clients": {
    "standard":   10,
    "premium":    50,
    "enterprise": 200
  },
  "Endpoints": {
    "POST:/api/data":   5,
    "PUT:/api/data":    5,
    "DELETE:/api/data": 3
  }
}
```

**How it works:**
- `Clients` maps a tier name (sent via `X-Client-Tier` header) to a per-window request budget.
- `Endpoints` maps `METHOD:/path-prefix` to a tighter budget for write operations.
- Endpoint limits are checked first; a request must clear *both* checks to succeed.
- `DefaultLimit` applies when an unrecognised tier is presented.

**To add a new tier:** add one line under `Clients`. **To tighten a write route:** add or update one line under `Endpoints`. Restart is required (file is read at startup), but no recompilation.

---

## API Response Headers

Every allowed response includes:

| Header | Description |
|---|---|
| `X-RateLimit-Limit` | Maximum requests allowed in the window |
| `X-RateLimit-Remaining` | Requests left in the current window |
| `X-RateLimit-Reset` | Unix epoch (seconds) when the window resets |

Every 429 response additionally includes:

| Header | Description |
|---|---|
| `Retry-After` | Seconds until the client may retry |

Example 429 body:
```json
{
  "error": "Too Many Requests",
  "detail": "Client limit exceeded for tier 'standard'",
  "retryAfterSeconds": 47,
  "limit": 10
}
```

---

## Known Limitations (By Design)

These are not bugs — they are the expected trade-offs of an in-memory, single-node PoC:

| Limitation | Impact | Production path |
|---|---|---|
| **Counters reset on restart** | Clients get a full budget immediately after a redeploy | Use Redis or a distributed cache with TTL-backed keys |
| **No cross-instance sync** | Running two replicas behind a load balancer gives each client 2× the intended budget | Redis + Lua atomic `INCR`/`EXPIRE`, or a sticky-session LB policy |
| **Memory growth** | One `WindowTracker` entry per unique `(client, endpoint)` pair is created and never evicted | Add a background cleanup task that removes trackers idle for > 1 window |
| **Header spoofing** | `X-Client-Id` and `X-Client-Tier` are trusted from the caller | In production, resolve client identity from a validated JWT or mTLS certificate, not a raw header |

---

## Project Structure

```
Assessment/
├── RateLimiterApi/
│   ├── Configuration/
│   │   └── RateLimitConfig.cs       ← typed config POCO
│   ├── Middleware/
│   │   └── RateLimitingMiddleware.cs ← all rate-limit logic + WindowTracker
│   ├── Controllers/
│   │   └── DataController.cs        ← sample GET/POST/PUT/DELETE endpoints
│   ├── Program.cs                   ← wire-up: CORS, middleware, controllers
│   └── appsettings.json             ← all limits live here
│
├── rate-limiter-ui/
│   └── src/app/
│       ├── services/api.service.ts  ← HTTP calls with client headers
│       ├── app.ts                   ← component logic + signals
│       ├── app.html                 ← demo UI template
│       └── app.css                  ← dark-theme styles
│
└── README.md
```

---

## Testing the Limits Manually

```http
### Standard client — hits limit after 10 requests in 60 s
GET http://localhost:5257/api/data
X-Client-Id: client-001
X-Client-Tier: standard

### Premium client — 50 req / 60 s
GET http://localhost:5257/api/data
X-Client-Id: client-002
X-Client-Tier: premium

### Write endpoint — POST limit is 5 req / 60 s regardless of tier
POST http://localhost:5257/api/data
X-Client-Id: client-001
X-Client-Tier: premium
Content-Type: application/json

{}
```

Or use the Angular UI at `http://localhost:4200` — the **Burst × 15** button fires 15 rapid requests and the log shows exactly where the 429s begin.


### Loom Video Explanation:
https://www.loom.com/share/d34f4c54164a4f8abc7ccbd5dc90ba0c
