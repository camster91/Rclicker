# Testing

## Automated tests

```bash
dotnet test RClicker.sln -c Release      # Windows app logic + relay client (any OS)
cd relay && npm ci && npm test             # relay, inside the real Workers runtime (Miniflare)
```

Neither suite presses real keys: the presentation controller and key sender are fakes.

### C# (`tests/RClicker.Tests`)

| Area | Tests |
| --- | --- |
| Session keys: CSPRNG, 256-bit, unique, URL-safe, redacted in logs; regenerate, expiry | `SessionTokenTests`, `SessionManagerTests` |
| Key derivation and AES-GCM match an **independent Python implementation** (fixed vectors); wrong room, direction or tampering rejected | `RelayCryptoTests` |
| Relay URL rules (https only, http only for localhost), key in fragment, wss address | `RelayUrlsTests` |
| Relay client against a scripted fake relay: claims the right room without revealing the key; decrypts, executes and acks all five commands; **replay dropped**; stale nonce refused with a new hello; unsupported or undecryptable messages ignored; double tap executes once; phone drop → Reconnecting → Ready; New session ends the old room and claims a new one; Quit sends shutdown; relay-ended room → fresh session; connection drop → Offline → reclaims the same room with the same host key; unreachable relay → Offline with a reason | `RelayHostClientTests` |
| Command whitelist, malformed JSON, oversized messages | `PresentationCommandsTests`, `ControllerProtocolTests` |
| Command → action → key mapping; PowerPoint focus policy; rate limiting | `CommandRouterTests`, `KeyboardPresentationControllerTests`, `PowerPointTargetPolicyTests`, `CommandRateLimiterTests` |
| QR decodes (ZXing) to the full relay URL, has a quiet zone, stays ≤ version 6 | `QrCodeMatrixTests` |

### Relay (`relay/test/relay.test.ts`, vitest + `@cloudflare/vitest-pool-workers`)

Phone page served with security headers and a strict path whitelist; WebSocket only, room id format, cross-origin refused; PC + phone pairing and forwarding both ways; wrong token, unclaimed room and foreign PC refused; PC offline/online notices and reclaim; ping auto-response; busy second phone; same-browser takeover; seat held then released (alarm); New session (4401) and Quit (4410); malformed or hostile messages not forwarded; oversized (1009) and flood (1008) disconnects; unauthenticated sockets time out; WebCrypto derivation and decryption match the Python vectors.

CI (`.github/workflows/ci.yml`) runs both suites, plus the `win-x64` publish on `windows-latest`.

## Manual / end-to-end QA performed for 0.2.0

Done in a Linux cloud container. **No Windows PC, no PowerPoint and no physical phones were available.**

1. **Local relay (`wrangler dev`) + real PC client + real phone page in headless Chromium (iPhone 14 / Pixel 7 emulation).** 15/15 checks passed:
   - Phone connects; PC shows "Connected — iPhone".
   - Next → exactly one Right Arrow. 5 rapid taps → 1 key. 9 taps 340 ms apart → 9 keys. Previous/Start/Black/End → Left/F5/B/Esc.
   - "PowerPoint is not the active window" shown on the phone.
   - Second phone told "Another phone is in control".
   - The session key never appears in the WebSocket URL or any frame sent to the relay.
   - Works again after the phone goes offline and back.
   - New session → old phone "Session ended", and the new QR code works. Wrong key → "Session ended".
   - Quit → "rclicker closed". No page errors.
2. **Live relay (`https://rclicker.cameron-rotman.workers.dev`) + real PC client** through this container's outbound HTTPS proxy, with a protocol-identical phone simulator:
   - All five commands acked "ok" and pressed the right keys.
   - **Round trip about 100 ms.**
   - Second phone 4409, wrong key 4401, New session 4401, keep-alive pong.
   - The real phone page could not be loaded in a browser against the live relay from this container: its proxy doesn't support browser WebSockets. The page itself was verified against the identical relay code locally (step 1).
3. Phone page layout (unchanged from 0.1) was checked at 375/390/430/768/1440 px and in landscape.

## Not yet verified (needs real hardware) — "Completed but awaiting verification"

- [ ] Windows 11 + PowerPoint: Next/Previous exactly one slide, Start, Black, End, Presenter View.
- [ ] Real iPhone Safari and Android Chrome on **mobile data**: scan, tap, lock for 1+ min and unlock, airplane mode on/off, screen stays awake while connected.
- [ ] A real corporate network: proxy with sign-in (NTLM/Kerberos), PAC file, TLS inspection, `*.workers.dev` filtering, WebSocket blocking.
- [ ] Latency from the presentation location (tap → slide change should feel instant, under 300 ms).
- [ ] `win-arm64` build on ARM Windows.

### Suggested manual script (15 minutes)

1. Run `rclicker.exe`. The window shows "Ready — scan the QR code" (no firewall prompt expected).
2. Open a 10-slide deck. Turn the phone's Wi-Fi **off** (mobile data only) and scan the QR code. The status shows "Connected — iPhone" and the QR code hides.
3. Click PowerPoint. Tap **Start** (slide 1), **Next** ×3 (slide 4), **Previous** (slide 3), **Black**, **Black**, **End**.
4. Tap Next 5× as fast as possible: expect at most 2 slide changes.
5. Lock the phone for 60 s and unlock: "Reconnecting…" then "Connected", and Next works.
6. Unplug the PC's network for 10 s: the phone shows "Waiting for computer…" and recovers when the network is back.
7. Scan with a second phone: "Another phone is in control".
8. Click **New session**: the first phone shows "Session ended"; the new QR code works.
9. Quit: the phone shows "rclicker closed".
