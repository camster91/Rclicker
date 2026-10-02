# Testing

## Automated tests

```bash
dotnet test PresentationRemote.sln -c Release
```

About 190 xUnit tests in `tests/PresentationRemote.Tests` (many are data-driven cases). They never press real keys: the presentation controller and key sender are fakes, and the integration tests run a real Kestrel server on `127.0.0.1` with a random port.

| Requirement | Tests |
| --- | --- |
| Tokens are cryptographically generated (≥128 bits, unique, balanced bits, URL-safe) | `SessionTokenTests` |
| Old tokens become invalid after regeneration | `SessionManagerTests.Regenerate_*`, `WebSocketControllerTests.RegeneratingSession_*`, `HttpServerTests.SessionStatusEndpoint_*` |
| Missing / invalid / expired tokens rejected | `SessionManagerTests`, `WebSocketControllerTests.MissingToken_*`, `InvalidToken_*`, `CommandsWithExpiredSession_*` |
| Valid token establishes a controller session | `WebSocketControllerTests.ValidToken_*`, `AllFiveCommands_*` |
| Unsupported commands rejected (`press-key`, `send-text`, `execute-command`, `run-process`, case variants) | `PresentationCommandsTests`, `ControllerProtocolTests`, `WebSocketControllerTests.UnsupportedAndMalformed_*` |
| Commands map to the right actions and keys (Right, Left, F5, B, Esc) | `CommandRouterTests`, `KeyboardPresentationControllerTests` |
| Malformed JSON / binary / oversized / invalid UTF-8 never crash the server | `ControllerProtocolTests`, `WebSocketControllerTests.UnsupportedAndMalformed_*`, `InvalidUtf8_*` |
| Rate limiting / debounce: one tap gives one action, flooding is capped | `CommandRateLimiterTests`, `CommandRouterTests`, `WebSocketControllerTests.RapidDoubleTap_*`, `MessageFlood_*` |
| Local URL generation | `ControllerUrlTests` |
| QR contains the full URL (decoded with ZXing), has a quiet zone, changes with the session | `QrCodeMatrixTests` |
| Private IPv4 selection against representative adapters (Wi-Fi, Ethernet, Hyper-V, WSL, VMware, WireGuard, Tailscale, Bluetooth, loopback, link-local, down, none) | `LanAddressSelectorTests` |
| Disconnect / reconnect, grace period, busy second phone, same-device takeover, shutdown | `ControllerHubTests`, `WebSocketControllerTests.Dropped*`, `AfterGracePeriod_*`, `SecondPhone_*`, `SameBrowserNewTab_*`, `StoppingServer_*` |
| PowerPoint focus policy | `PowerPointTargetPolicyTests`, `KeyboardPresentationControllerTests` |
| Static file whitelist, path traversal, DNS rebinding, cross-origin, security headers, port fallback | `HttpServerTests`, `WebSocketControllerTests.CrossOrigin*`, `SmallServerPiecesTests` |

CI (`.github/workflows/ci.yml`) runs restore, the Release build, tests and the `win-x64` publish on `windows-latest`.

## Manual QA performed for 0.1.0

Performed in a Linux cloud container (no Windows, PowerPoint or physical phones available).

**Phone web UI.** The real server ran with a fake key sender that logs keys, driven by headless Chromium (Playwright) with device emulation:

- Layout at 375×667 (iPhone SE), 390×844 (iPhone 14), 430×932 (iPhone 14 Pro Max), 412 px (Pixel 7), 768×1024 (tablet), 1440×900 (desktop) and 844×390 (landscape). All buttons ≥ 44 px, nothing overflows, Next is the largest control.
- One tap of Next → exactly one Right Arrow. Five taps within 300 ms → one key. Nine taps 340 ms apart → nine keys.
- Previous/Start/Black/End → Left/F5/B/Esc. Arrow keys on a keyboard work. Visible focus ring on Tab. Accessible names are present.
- "PowerPoint not active" and "Black in editor" messages appear on the phone.
- Second phone → "Another phone is in control" and can't send. Same browser, new tab → old tab told "Opened somewhere else".
- Offline blip → reconnects. New session → "Session ended". Invalid/missing token → correct message. Receiver quit → "Presentation Remote closed".

**Published `PresentationRemote.exe` (win-x64), run under Wine 9 with a virtual display.** This is a smoke test, not a substitute for Windows:

- Launches, shows the window, starts the server (the phone page returned HTTP 200).
- Wine exposed no network adapters, so the window correctly showed **No network** (no QR code). The QR rendering in the desktop window was therefore **not** seen.
- Help dialog opens. New session doesn't crash. Quit stops the server (port closed) and the process exits.
- A second launch shows "already running" and exits.
- Port fallback was exercised by accident: with 8765 taken, the app moved to 8766.

## Not yet verified (needs a real Windows PC + PowerPoint + phones)

Status: **Completed but awaiting verification**.

- [ ] Windows 11: launch, firewall prompt, QR rendering and scanning from 1–3 m on a monitor/projector, adapter dropdown with real adapters, Copy, network change (switch Wi-Fi) updates the QR code.
- [ ] PowerPoint: Next → exactly one slide, Previous → exactly one back, Start → slideshow from slide 1, Black toggles, End exits. Also with Presenter View on two monitors.
- [ ] Foreground check against real PowerPoint window classes (`PPTFrameClass`, `screenClass`, `PodiumParent`) across PowerPoint versions (M365, 2021, 2019).
- [ ] Rapid tapping during a real slideshow with animations.
- [ ] Physical iPhone Safari and Android Chrome: scan, tap, lock phone for 1+ min and unlock (reconnect), Wi-Fi off/on, airplane mode.
- [ ] Microsoft Edge and Chrome on another PC as the controller.
- [ ] Closing the window while a phone is connected (phone shows "closed").
- [ ] `win-arm64` build on ARM Windows.

### Suggested manual script (15 minutes)

1. Run `PresentationRemote.exe`. Allow on Private networks.
2. Open a 10-slide deck. Scan the QR code. Status shows `Connected — iPhone`, and the QR code hides.
3. Click PowerPoint. Tap **Start** (slide 1 full screen), **Next** ×3 (slide 4), **Previous** (slide 3), **Black** (black), **Black** (back), **End** (editor).
4. Tap Next 5× as fast as possible: expect at most 2 slide changes (fast double taps collapse).
5. Lock the phone for 60 s, unlock: `Reconnecting…` then `Connected`, and Next works.
6. Scan with a second phone: "Another phone is in control".
7. Click **New session**: the phone shows "Session ended", and the new QR code works.
8. Click Notepad, tap Next: the phone says "PowerPoint is not the active window". Nothing is typed in Notepad.
9. Quit: the phone shows "Presentation Remote closed".
