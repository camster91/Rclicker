# Security

rclicker 0.1 is a **local-network tool** for moving slides. This page describes what it protects against, how, and what it does not protect against.

## Threat model in one paragraph

Anyone on the same network can reach the receiver's port. The **session token** in the QR code is the only credential. Whoever has a valid token, and gets the single controller seat, can do exactly five things: next, previous, start slideshow, toggle black screen, end slideshow, and only while PowerPoint is the active window (by default). There is no way to send other keys, type text, read files or run programs.

## Controls in place

| Area | What we do | Where |
| --- | --- | --- |
| Token strength | 256 bits from the OS CSPRNG (`RandomNumberGenerator`), base64url. Not a short code. | `SessionToken` |
| Token comparison | Constant-time (`CryptographicOperations.FixedTimeEquals`) after a shape check. | `SessionManager.Validate` |
| Token lifetime | Memory only, never written to disk. Invalid after **New session**, after 12 h, or when the app closes. Re-checked on **every command**, not just at connect. | `SessionManager`, `ControllerEndpoint` |
| Authentication | WebSocket upgrade is refused with HTTP 401 before any socket exists for missing, invalid or expired tokens. | `ControllerEndpoint.HandleAsync` |
| Command whitelist | Exact, case-sensitive match on 5 names. Everything else gets `unsupported_command` and nothing runs. No "press key", "send text", "execute" or "run" exists anywhere. | `PresentationCommands`, `ControllerProtocol` |
| Keyboard surface | `PresentationKey` enum has 5 members (Right, Left, F5, B, Esc). `Win32KeySender` can't press anything else. | `KeyboardPresentationController`, `Win32KeySender` |
| Wrong-window input | Keys are only sent when `POWERPNT` is the foreground process (default on). "B" is refused in the PowerPoint editor. | `PowerPointTargetPolicy` |
| One controller | First phone wins. Others are told "busy" (close 4409) and can't act. | `ControllerHub` |
| Malformed input | Messages > 512 bytes are discarded unparsed. JSON depth ≤ 4. Binary frames, invalid UTF-8, non-objects and wrong types are rejected without exceptions escaping. | `ControllerEndpoint`, `ControllerProtocol` |
| Flooding / DoS | Per-connection cap 20 messages/s (then disconnect). Command duplicate filter 200 ms plus token bucket (burst 6, 3/s). Kestrel limits: 64 connections, 8 WebSockets, 4 KB bodies, 16 KB headers. | `ControllerEndpoint`, `CommandRateLimiter`, `RemoteServer` |
| Static files | Three files embedded in the assembly, served from a fixed path → content map. No file system access, so no path traversal. | `StaticAssets` |
| Cross-origin | WebSocket and API requests with an `Origin` that isn't the server's own are refused (403). No CORS headers are sent. | `RequestGuard` |
| DNS rebinding | Requests whose `Host` isn't an IP literal (or `localhost`) are refused (400). The QR always uses an IP. | `RequestGuard` |
| Browser hardening | `Content-Security-Policy: default-src 'none'; script-src 'self'; …; connect-src 'self' ws://<host>; frame-ancestors 'none'`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store`. No `Server` header. No external scripts, fonts or images. | `RequestGuard`, `wwwroot/` |
| Token leakage in logs | ASP.NET Core request logging (which prints full URLs) is filtered to Warning. Our logs only print a redacted prefix (`AbCd…`). | `RemoteServer.Build`, `SessionToken.Redact` |
| Token in the session-check API | `GET /api/session` reads the token from the `X-Session-Token` header only, not the URL. | `RemoteServer.DispatchAsync` |
| Firewall | Never modified by the app. The user decides (see README). | — |
| Single instance | A per-user mutex prevents two receivers from both sending keys. | `Program` |

## Known limitations (be aware)

1. **Not encrypted.** The connection is plain HTTP/WebSocket. Being on the LAN does **not** mean the traffic is encrypted. Someone on the same Wi-Fi who can capture traffic (an open network, a compromised router, ARP spoofing) can see the token and the commands. They could then try to connect, but would still be refused while your phone holds the seat. Use trusted networks. We deliberately did not add a self-signed HTTPS setup, because it makes phones show scary certificate warnings and breaks onboarding.
2. **The QR code is a key.** If the window with the QR code is shown on the projector, the audience can scan it. Mitigations: only one phone can control at a time, and the QR code hides itself once a phone connects. If in doubt, click **New session**, which disconnects everyone and invalidates the old code.
3. **Seat reservation uses a non-secret client id.** The "same phone may reconnect / take over its own tab" rule trusts a random id stored in the phone browser's `localStorage`. An attacker who has the token **and** sniffs that id from the network (see 1) could take over the seat. Pressing **New session** recovers.
4. **Token in the URL.** The token is part of the page URL, so it appears in the phone's browser history. It's useless after the session ends.
5. **Listens on all interfaces.** The server binds `0.0.0.0`, including VPN or virtual adapters and, if the PC has a public IP with no firewall, the internet. The token is still required. Keep Windows Firewall on and allow the app on **Private** networks only.
6. **Foreground check is a heuristic.** It trusts the process name `POWERPNT` and well-known window classes. If the user disables the check, keys go to whatever window is active, just like a USB clicker.
7. **Elevation (UIPI).** If PowerPoint runs as administrator, Windows silently drops the key presses. The app can't detect this reliably. It's documented in Troubleshooting.
8. **Unsigned binary.** The 0.1 `.exe` isn't code-signed, so SmartScreen may warn. Only run copies from a source you trust.
9. **No brute-force lockout.** Not needed at 256 bits, and Kestrel connection limits bound the request rate.

## Security review checklist (0.1)

Reviewed against the implementation before release:

- [x] Arbitrary command execution: none. No process launching, shell, scripting host or COM.
- [x] Arbitrary keyboard input: impossible from the browser (enum of 5 keys, whitelist of 5 commands).
- [x] Path traversal / unsafe static files: embedded whitelist only; tests try `..`, encoded `..`, case variants and DLL names.
- [x] Token leakage: no request URL logging; redacted logs; header-based session check; `no-referrer`; nothing persisted.
- [x] Cross-origin: Origin check, no CORS, CSP, frame denial.
- [x] WebSocket auth: checked before upgrade; re-checked per command; regeneration closes live sockets.
- [x] Predictable tokens: CSPRNG, 256 bits; tested for uniqueness and bit balance.
- [x] Stale sessions: regenerate, 12 h expiry, process exit.
- [x] LAN exposure: documented above (limitations 1 and 5).
- [x] DoS via rapid commands: per-connection message cap, rate limiter, Kestrel limits; tested.
