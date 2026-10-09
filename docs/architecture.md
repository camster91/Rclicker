# Architecture

rclicker 0.2 has three parts: the Windows app, a small relay on Cloudflare, and a phone web page that the relay serves. The phone and the PC never connect to each other directly; both connect **out** to the relay over HTTPS (port 443). That's what makes it work on corporate, school and guest networks, and with the phone on mobile data.

```
┌──────────────────────┐                      ┌──────────────────────┐
│        Phone         │                      │   rclicker.exe (PC)  │
│  Browser, no install │                      │                      │
│  relay/public/app.js │                      │  RelayHostClient     │
└──────────┬───────────┘                      │  CommandRouter       │
           │ wss://…/ws/phone                 │  IPresentationCtrl   │
           │ (mobile data / any Wi-Fi)        └──────────┬───────────┘
           ▼                                             │ wss://…/ws/host
┌──────────────────────────────────────────────┐         │ (outbound 443,
│  Cloudflare Worker  rclicker                 │◀────────┘  system proxy)
│   GET /, /app.js, /styles.css  phone page    │
│   /ws/host, /ws/phone  ──▶  Durable Object   │
│                             "Room" per key   │
│   forwards opaque AES-GCM payloads only      │
└──────────────────────────────────────────────┘
                                                         │ Windows SendInput
                                                         ▼
                                              ┌──────────────────────┐
                                              │      PowerPoint      │
                                              └──────────────────────┘
```

## Where it runs

The Cloudflare Worker `rclicker` lives on its own origin, a Workers custom domain. Its addresses are Worker settings in the Cloudflare dashboard, not part of this repository: `HOME_ORIGIN` is the home address, and `LEGACY_HOSTS` lists older addresses (zone routes and workers.dev) with the path the relay was mounted under. On those, `/ws/host` and `/ws/phone` keep working for apps already installed, and every page redirects (301) to the same path on `HOME_ORIGIN`, keeping the `#k=` key.

On the home address:

| Path | What |
| --- | --- |
| `/` | Marketing page (`public/site.html`). Old QR links with `#k=` are forwarded to `/remote`. |
| `/remote` | Phone remote (`public/remote.html`, `app.js`, `styles.css`) |
| `/present` | Present a PDF from a browser, no install (`public/present.html`, `present.js`, `present.css`). The page plays the PC's part: same keys, encryption and freshness checks as `RelayHostClient`, and it shows the PDF with pdf.js instead of pressing keys in PowerPoint. |
| `/vendor/…` | Third-party browser files for `/present` (pdf.js, a QR encoder). The Worker fetches each from a public npm mirror (jsDelivr, then unpkg) on first use, checks its SHA-384 against `src/vendor-manifest.ts` (made by `scripts/vendor-manifest.mjs`), and serves and caches it from this origin. A file that doesn't match is never served. |
| `/download` | 302 to the latest GitHub release `rclicker.exe` |
| `/ws/host`, `/ws/phone` | Relay WebSockets → Durable Object `Room` |

## Components

| Part | Where | What it does |
| --- | --- | --- |
| Windows app | `src/RClicker` (WinForms, `net10.0-windows`) | Window with QR code and status; `Win32KeySender` (`SendInput`) and `Win32ForegroundWindowProvider`. |
| Core | `src/RClicker.Core` (`net10.0`) | `SessionManager` (key), `RelayKeys`/`RelayCipher` (derivation + AES-GCM), `RelayHostClient` (outbound WebSocket, reconnect, freshness checks), `ControllerProtocol` (command whitelist), `CommandRouter` + `CommandRateLimiter`, `KeyboardPresentationController`, `PowerPointTargetPolicy`, `QrCodeMatrix`. No Windows APIs, so it's fully testable on any OS. |
| Relay | `relay/` (Cloudflare Worker + SQLite-backed Durable Object) | Serves the phone page; pairs one PC with one phone per room; forwards encrypted payloads; enforces one phone at a time, seat reservation, rate and size limits, room expiry. |
| Phone page | `relay/public/` (vanilla HTML/CSS/JS, bundled into the Worker) | Derives keys with WebCrypto, connects, encrypts commands, shows status, reconnects, keeps the screen awake. |

## Keys

The QR code is `https://<relay>/#k=<K>` where K is 32 random bytes (base64url). The part after `#` (the fragment) is never sent to any server. Both sides derive, with HMAC-SHA-256(K, label):

| Label | Result | Who learns it |
| --- | --- | --- |
| `rclicker/v1/room` | room id (first 16 bytes) | relay (it's the routing key) |
| `rclicker/v1/phone` | phone token | relay sees it when the phone joins; stores only its SHA-256 (sent by the PC) |
| `rclicker/v1/enc` | AES-256-GCM key | PC and phone only |

The PC also makes a private random **host key** per session (never in the QR code). The relay stores only its hash, so only that PC can reclaim the room after a network drop.

## Messages

Relay-level JSON (plaintext):

```
PC    → relay  {t:"claim", v:1, hostKey, phoneAuthHash}  then {t:"msg", pid, iv, ct} | {t:"end", reason}
relay → PC     {t:"ready"} | {t:"phone", event:"join"|"leave"|"released", pid, label, held?} | {t:"msg", pid, iv, ct}
phone → relay  {t:"auth", token, client}                 then {t:"msg", iv, ct}
relay → phone  {t:"host", online} | {t:"msg", iv, ct}
either         {"t":"ping"} → {"t":"pong"}  (answered by Cloudflare without waking the room)
```

End-to-end payloads (inside `iv`/`ct`, AES-GCM, additional data `rclicker/v1/p2h/<room>` or `…/h2p/<room>`):

```
PC → phone   {type:"hello", version, commands:[…], nonce}
phone → PC   {type:"presentation.next", id:7, nonce}
PC → phone   {type:"ack", id:7, command, status:"ok"|"rate_limited"|"rejected"|"failed", message?}
```

## Flow of one tap

1. The phone encrypts `{type:"presentation.next", id, nonce}` and sends it to the relay.
2. The relay checks size and rate, then forwards it to the room's PC with the phone's `pid`.
3. `RelayHostClient` decrypts it. Wrong key, wrong room or wrong direction means it's dropped. It then checks freshness: the nonce must be the PC's latest hello challenge, and `id` must be higher than the last one. Replays are dropped silently; a stale nonce gets "tap again" and a fresh hello.
4. `ControllerProtocol.Parse` accepts only the five whitelisted command names.
5. `CommandRouter` applies the rate limiter (a duplicate within 200 ms is dropped; token bucket of 6, refilling at 3 per second), then calls `IPresentationController.Next()`.
6. `KeyboardPresentationController` checks `PowerPointTargetPolicy` (PowerPoint must be in front; "Black" not in the editor), then presses one key via `SendInput`.
7. An encrypted ack goes back the same way. Measured round trip via the live relay: about 100 ms.

## Why semantic commands are separate from the keyboard

The phone only knows *intent* (`presentation.next`), never *how* it's done (Right Arrow):

- **Security.** No message can express "press any key" or "type text". A stolen key can only move slides.
- **Swappable back ends.** `IPresentationController` can be replaced (PowerPoint COM, Google Slides, …) without touching the phone or the relay.
- **Testability.** Tests use fake controllers and key senders; no real keyboard input.

## One phone at a time (enforced by the relay)

- The first phone to join a room gets the seat. Others are closed with **4409** ("Another phone is in control").
- The same browser (random `client` id in `localStorage`, not a secret) may reconnect or take over from its own older tab. The old tab gets **4408**.
- A dropped phone keeps its seat for **30 s**; the PC shows "Reconnecting…".
- **New session**: the PC sends `{t:"end"}`, the phone gets **4401**, and the room is marked ended (old QR codes say "Session ended").
- **Quit**: phone gets **4410** ("rclicker closed").
- PC offline: the phone shows "Waiting for computer…" during a 10-second grace period, then "Computer disconnected" with controls disabled. The same PC can reclaim the session; after 2 hours offline the room ends. The relay's stored alarm drives the notice even if the PC tab cannot send a final message.
- Room hard limit: 24 hours (the PC regenerates sessions every 12 hours anyway).

## PC connection handling

- One outbound `ClientWebSocket` to `wss://<relay>/ws/host`, through `HttpClient.DefaultProxy` (Windows proxy settings, PAC/WPAD) with the user's default credentials.
- WebSocket ping every 15 s; reconnect if no pong within 20 s.
- Reconnect with backoff 1 s → 30 s, reclaiming the same room with the same host key, so the phone stays put.
- Relay closes with 4401/4403 (room expired or not ours): start a fresh session automatically.
- Status is shown in the window: Connecting, Ready, Connected, Reconnecting, Offline (with a plain-English reason).

## Relay cost model

Cloudflare Workers Free plan: 100,000 requests a day, and SQLite-backed Durable Objects are included. The room uses the WebSocket Hibernation API, so it isn't billed while idle, and keep-alive pings are auto-answered. A one-hour talk uses a few hundred requests.

## Packaging

`dotnet publish -c Release -r win-x64` produces one self-contained, uncompressed `rclicker.exe` with the relay address built in (`-p:RelayUrl=…`; CI takes it from the `RELAY_URL` secret). Keeping single-file compression off makes the signed payload easier for endpoint scanners to inspect. The relay deploys with `npx wrangler deploy --keep-vars` from `relay/`.
