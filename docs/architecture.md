# Architecture

rclicker 0.1 is one Windows process. It contains a small web server, and the phone uses an ordinary browser. Nothing else is involved: no cloud, database or phone app.

```
┌─────────────┐
│    Phone    │
│ Web Browser │   index.html + app.js + styles.css (vanilla, no build step)
└──────┬──────┘
       │
       │ HTTP (page) + WebSocket (commands)
       │ LAN only, http://<PC IPv4>:8765/?session=<token>
       ▼
┌──────────────────────────────────────────────┐
│ rclicker.exe                       │
│                                              │
│  Web Server        Kestrel (RemoteServer)    │
│   ├ RequestGuard   IP-literal Host, Origin,  │
│   │                security headers          │
│   ├ StaticAssets   3 embedded files, fixed   │
│   │                whitelist                 │
│   └ /ws            ControllerEndpoint        │
│  Session Manager   SessionManager (token)    │
│  Controller seat   ControllerHub (1 phone)   │
│  Command Router    ControllerProtocol →      │
│                    CommandRouter (+ rate     │
│                    limiter)                  │
│  Presentation API  IPresentationController   │
│                    └ KeyboardPresentation-   │
│                      Controller              │
│  UI                WinForms MainForm + QR    │
└─────────┬────────────────────────────────────┘
          │
          │ Windows SendInput (Right, Left, F5, B, Esc only)
          ▼
┌────────────────────┐
│    PowerPoint      │  (must be the foreground window)
└────────────────────┘
```

## Projects

| Project | Target | Contents |
| --- | --- | --- |
| `src/RClicker.Core` | `net10.0` | Everything that does not need Windows: sessions, wire protocol, command whitelist, rate limiting, `IPresentationController`, keyboard mapping and PowerPoint focus policy, LAN address ranking, QR matrix, the Kestrel server, and the phone UI as embedded resources. |
| `src/RClicker` | `net10.0-windows10.0.17763.0`, WinForms | `Program` (entry point, single-instance mutex, logging), `MainForm`, `QrCodeView`, Win32 `SendInput` key sender, foreground-window reader. |
| `tests/RClicker.Tests` | `net10.0`, xUnit | Unit tests and real localhost HTTP/WebSocket integration tests against Core. |

Why two projects instead of one: the Windows-specific code (WinForms, `user32.dll`) is kept to a thin shell. All logic can then be unit-tested and integration-tested on any OS, including Linux CI, without ever pressing real keys.

## Flow of one tap

1. The phone sends `{"type":"presentation.next","id":12}` over its WebSocket.
2. `ControllerEndpoint` checks message size (≤ 512 bytes) and a per-connection message rate (≤ 20/s), then decodes UTF-8.
3. `ControllerProtocol.Parse` accepts only a JSON object whose `type` is exactly one of the five whitelisted names (or `ping`).
4. The endpoint re-validates the session token (it may have been regenerated or expired) and checks this connection is still the active controller.
5. `CommandRouter` applies `CommandRateLimiter` (duplicate within 200 ms → dropped; token bucket of 6 refilling at 3/s), then calls `IPresentationController.Next()`.
6. `KeyboardPresentationController` asks `PowerPointTargetPolicy` whether it is safe (PowerPoint is the foreground app; "Black" not in the editor). Then it sends one key press via `IKeySender` → `SendInput`.
7. The phone gets `{"type":"ack","id":12,"status":"ok"}`, or `rate_limited` / `rejected` / `failed` with a short message to show.

## Why semantic commands are separate from the keyboard

The phone only knows *intent* (`presentation.next`), never *how* it is done (Right Arrow):

- **Security.** The browser cannot express "press any key" or "type text", so a stolen token can only move slides. There is no generic key or text API to abuse.
- **Swappable back ends.** `IPresentationController` has `Next/Previous/Start/ToggleBlack/End`. A future PowerPoint COM controller, a Google Slides or Keynote controller, or a browser-presentation controller can replace `KeyboardPresentationController` without changing the phone UI or the protocol.
- **Testability.** Tests use a fake controller and a fake key sender. No real keyboard input in tests.

The key mapping lives in exactly one place: `KeyboardPresentationController.KeyFor`.

## Sessions

- `SessionToken.Generate()`: 32 bytes from `RandomNumberGenerator`, base64url (43 chars).
- `SessionManager` holds exactly one current session in memory. `Regenerate()` replaces it (the old token is forgotten, so it can never validate again) and raises `SessionChanged`. Sessions expire after 12 hours. The desktop window checks every 30 s and regenerates automatically.
- Validation is constant-time (`CryptographicOperations.FixedTimeEquals`).

## One phone at a time

`ControllerHub` implements option A (a single active controller):

- The first phone to connect with a valid token gets the seat.
- Another phone gets WebSocket close code **4409** and sees "Another phone is in control".
- The **same browser** is recognised by a random `client` id kept in `localStorage`. It is not a secret, just a way to tell "my phone reconnecting" from "someone else". It may reconnect or take over from its own older tab (the old tab gets **4408**).
- When the phone drops (screen lock, Wi-Fi blip), the seat is held for that phone for **30 s** (`Reconnecting`), then released.
- `New session` closes the controller with **4401**. App shutdown closes it with **4410**. The phone stops reconnecting on these codes and explains why.

## Phone connection handling (`app.js`)

- Connects to `/ws?session=…&client=…`. Failed handshakes are disambiguated with `GET /api/session` (token in the `X-Session-Token` header): a 401 means the session is over, so it stops; a network error means it retries.
- Exponential backoff reconnect (0.4 s → 5 s cap).
- App-level ping every 15 s while visible. A missing pong within 4 s means a dead socket, so it reconnects. On `visibilitychange`/`pageshow`/`online`/`focus` (phone unlocked) it checks immediately.
- A command with no ack within 2.5 s means the socket is presumed dead and it reconnects. Commands are **never queued or retried**, so a late command can't jump slides unexpectedly.
- Per-button 300 ms tap lock on the client, plus the server-side duplicate filter.
- The server pings at the WebSocket level every 10 s and drops connections that miss the pong (`KeepAliveTimeout`).

## Networking

- Kestrel listens on `0.0.0.0:8765` (all IPv4 interfaces), so it keeps working when the IP changes. Ports 8765–8775 are tried in order. `--port` pins one.
- `LanAddressSelector.Rank` picks the address for the QR code. It excludes adapters that are down, loopback, tunnel and link-local (169.254/16) addresses, and prefers RFC 1918 private addresses, adapters with a default gateway, and Ethernet/Wi-Fi. It pushes likely virtual/VPN adapters (Hyper-V, WSL, VMware, VirtualBox, Docker, WireGuard, Tailscale, …) to the bottom. All candidates are listed in the window's Network dropdown.
- `NetworkChange` events (debounced 1.5 s) re-rank and refresh the QR code with the same token.

## Focus behaviour

`SendInput` delivers keys to the foreground window, just like a USB clicker. The app never steals focus. Instead:

- `Win32ForegroundWindowProvider` reads the foreground window's process name and window class (read-only).
- `PowerPointTargetPolicy` (on by default, user-switchable) refuses to send keys unless the foreground process is `POWERPNT`. It also refuses "Black" while PowerPoint's editor window (`PPTFrameClass`) is active, so a stray "b" is never typed into a slide. Slideshow (`screenClass`) and Presenter View (`PodiumParent`) accept all commands.
- The phone shows why a command was refused ("Click on PowerPoint on the computer").

## Packaging

`dotnet publish -c Release -r win-x64` produces one self-contained, compressed, single-file `rclicker.exe` (~65 MB; .NET + ASP.NET Core + Windows Forms runtimes inside). Trimming is off because WinForms does not support it. `win-arm64` publishes the same way.
