# Security

rclicker 0.2 moves slides from a phone through a relay on the internet. This page covers what it protects against, how, and what it doesn't.

## Threat model in one paragraph

Anyone on the internet can reach the relay. The **session key** in the QR code (256 bits, in the URL fragment) is the only thing that grants control, and it never leaves the phone and the PC. The relay is treated as **untrusted for content**: it can see that a room exists and when messages flow, but it can't read commands, forge them, or replay them. Whoever has the key, and holds the single phone seat, can do exactly five things: next, previous, start slideshow, black screen, end slideshow, and by default only while PowerPoint is the active window.

## Controls

| Area | What we do | Where |
| --- | --- | --- |
| Key strength | 256 bits from the OS CSPRNG, base64url. | `SessionToken` |
| Key never sent to servers | It lives in the QR URL **fragment** (`#k=…`), which browsers don't send. The relay gets a room id and a hash, both one-way derived (HMAC-SHA-256). | `RelayKeys`, `app.js` |
| End-to-end encryption | AES-256-GCM with a fresh random nonce per message. Additional data binds each message to its room and direction, so it can't be moved to another room or reflected back. | `RelayCipher`, `app.js` |
| Replay protection | The PC sends a random challenge (nonce) in its hello. Every command must carry it plus a strictly increasing id. Old or repeated messages are dropped. | `RelayHostClient.CheckFreshness` |
| Joining a room | The phone shows a token derived from the key; the relay compares its SHA-256 (constant time) to the hash the PC registered. A wrong key gets close 4401. | `room.ts` `onAuth` |
| Owning a room | The PC registers a private host key (hash stored). Another client can't take over the PC side of a room (4403). | `room.ts` `onClaim` |
| Command whitelist | Exact match on 5 names. No "press key", "send text", "run" or similar exists anywhere. | `PresentationCommands`, `ControllerProtocol` |
| Keyboard surface | Only Right, Left, F5, B and Esc can ever be pressed. | `KeyboardPresentationController`, `Win32KeySender` |
| Wrong-window input | Keys only go to the foreground window when it's `POWERPNT` (default on). "B" is refused in the PowerPoint editor. | `PowerPointTargetPolicy` |
| One controller | First phone wins; others get "busy" (4409). The QR code hides itself on the PC once a phone connects. | `room.ts`, `MainForm` |
| Session lifetime | Key in memory only. **New session** ends the room at the relay (old QR codes report "ended"). Quit ends it too. 12 h session / 24 h room hard limit. | `SessionManager`, `room.ts` |
| Malformed input | Relay: max 2 KB per message, JSON-only, field formats validated, unknown types ignored. PC: decryption must succeed, plaintext ≤ 512 bytes, JSON depth ≤ 4. | `room.ts`, `ControllerProtocol` |
| Flooding | Relay: at most 60 new connections per minute per IP address (then 429); 20 messages/s per socket (then close 1008); unauthenticated sockets closed after 10 s. PC: duplicate filter plus token bucket. | `index.ts`, `room.ts`, `CommandRateLimiter` |
| Cross-site use | rclicker has its own origin, `clicker.rotmanav.ca`, so no other site's pages share its storage or scripts. Pages on the old addresses only redirect there. Phone WebSockets must come from the relay's own origin (403 otherwise). | `index.ts` |
| Phone page hardening | Strict CSP (`default-src 'none'`, self-only scripts and connections), `X-Frame-Options: DENY`, `nosniff`, `no-referrer`, HSTS, no external resources. Only 3 whitelisted paths are served; everything else is 404. | `assets.ts` |
| PC network exposure | **None inbound.** The PC only makes an outbound HTTPS connection; nothing listens on the PC. | `RelayHostClient` |
| Logs | Only a redacted key prefix (`AbCd…`) is ever logged. | `SessionToken.Redact` |

## Known limitations

1. **The QR code is the key.** Anyone who scans it before your phone does can control the slides. Scan it before projecting; once your phone is connected, others are refused and the QR code hides itself. **New session** revokes everything.
2. **The relay sees metadata.** Cloudflare (and anyone operating the relay account) can see room ids, connection times, rough message counts and phone/PC IP addresses, but not commands.
3. **The relay can deny service.** Whoever runs the relay can drop or delay messages. It can't fake them.
4. **Trust in the phone page.** The phone runs JavaScript served by the relay. Someone who controls the relay deployment could serve a modified page that leaks the key. This is the main trust assumption: only deploy the relay from this repository, and protect the Cloudflare account (use 2FA).
5. **Seat reservation uses a non-secret client id.** Someone who has the QR key could imitate your browser's client id (not visible to the network; TLS hides it) to take over the seat. New session recovers.
6. **Foreground check is a heuristic.** It trusts the process name and window class. When turned off, keys go to whatever window is active, like a USB clicker.
7. **Elevation (UIPI).** If PowerPoint runs as administrator, Windows silently drops key presses.
8. **Unsigned binary.** 0.2 isn't code-signed; SmartScreen or app-control policies may block it.
9. **Corporate TLS inspection** can see the (still end-to-end encrypted) payloads and metadata, the same as any HTTPS traffic.

## Review checklist (0.2)

- [x] No inbound ports on the PC; outbound 443 only.
- [x] Key never in URLs sent to servers, logs or storage; relay holds only hashes and derived ids.
- [x] Relay can't read, forge or replay commands (AEAD with room/direction binding, nonce + counter).
- [x] Arbitrary command execution or keyboard input impossible (whitelists on both ends).
- [x] Phone page: static whitelist, strict CSP, no third-party content.
- [x] DoS: per-IP connection limit, size, rate and auth-timeout limits at the relay; rate limiter at the PC.
- [x] Build supply chain: GitHub Actions pinned to exact commits; Dependabot proposes updates.
- [x] Stale sessions: explicit end, expiry, tombstones.
- [x] Cross-implementation crypto vectors (Python ↔ C# ↔ WebCrypto) tested.
