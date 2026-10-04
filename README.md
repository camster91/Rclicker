# rclicker

Turn your phone into a PowerPoint clicker. Nothing to install on the phone, and it works **even when the phone and the computer can't see each other on the network**: office, school and guest Wi-Fi, or a phone on mobile data.

1. Run `rclicker.exe` on the presentation computer.
2. Scan the QR code it shows with your phone's camera.
3. Tap **Next**, **Previous**, **Start**, **Black** or **End**.

**Download:** [rclicker.exe](https://github.com/camster91/Rclicker/releases/latest/download/rclicker.exe) (latest release, Windows 10/11). All releases: [Releases](https://github.com/camster91/Rclicker/releases).

Originally built for presenters working with the Rotman AV team (University of Toronto), where guest devices and room PCs usually sit on different networks.

## How it works

```
Phone (mobile data or any Wi-Fi)  --HTTPS-->  rclicker relay (Cloudflare)  <--HTTPS--  PC running rclicker.exe
```

Both the phone and the PC make **outgoing** HTTPS connections (port 443) to a small relay running as a Cloudflare Worker with a Durable Object per room. Nothing connects *into* the PC, so there is no firewall prompt and no need for the two devices to share a network. Commands are **end-to-end encrypted**: the relay passes them along but can't read or forge them.

## Key features

- **No phone app**: the QR code opens a small web page served by the relay
- **End-to-end encryption**: a random 256-bit session key lives only in the QR link fragment (never sent to a server); phone and PC derive an AES-256-GCM key from it, with a challenge and counter on every command so recorded messages can't be replayed
- **Locked-down control**: exactly five commands (next, previous, start, black screen, end), one phone in control at a time, and by default keys are only sent when PowerPoint is the active window
- **Presentation-friendly**: the QR code hides itself once a phone connects so the audience can't scan it off the projector; phone and PC screens stay awake while connected
- **Works on corporate networks**: uses the Windows proxy settings (including PAC/WPAD) with Windows sign-in, trusts the PC's installed certificates for TLS inspection, and reconnects on its own with clear "Offline" reasons to pass to IT
- **Single self-contained `.exe`** (about 60 MB, .NET runtime included), x64 and ARM64 builds

| Phone button | What PowerPoint gets | Effect |
| --- | --- | --- |
| Next | Right Arrow | Next slide / animation |
| Previous | Left Arrow | Previous slide / animation |
| Start | F5 | Start the slideshow from the beginning |
| Black | B | Black screen on/off (slideshow only) |
| End | Esc | Leave the slideshow |

Security details and the threat model: [docs/security.md](docs/security.md). Architecture: [docs/architecture.md](docs/architecture.md).

## Tech stack

- **Windows app**: C# on .NET 10, WinForms, Win32 `SendInput` and foreground-window checks, a built-in QR code generator, xUnit tests
- **Relay**: TypeScript on Cloudflare Workers with Durable Objects, Workers rate limiting, Vitest running inside the Workers runtime
- **Phone page**: plain HTML, CSS and JavaScript using WebCrypto, strict CSP and no third-party resources
- **CI**: GitHub Actions (build, test, typecheck, Windows publish and GitHub Releases), actions pinned to commits, Dependabot

## Using it

1. Start `rclicker.exe`. The window shows **Ready**.
2. Open your presentation in PowerPoint.
3. Scan the QR code with your phone and open the link. The phone shows **Connected**.
4. Click once on the PowerPoint window so it's active, then use the phone.

Closing the rclicker window ends the session immediately. **New session** makes a new QR code and disconnects any phone using the old one.

### Command-line options

```
rclicker.exe [--relay https://<relay address>] [--console]
```

- `--relay`: use a different relay (also settable with the `RCLICKER_RELAY` environment variable)
- `--console`: open a console window with diagnostic logs (the session key is never logged in full)

### Requirements

- Windows 10 (1809) or later, or Windows 11
- Microsoft PowerPoint (desktop app)
- Internet access on the PC (outgoing HTTPS and WebSockets to the relay)
- A phone with a modern browser (iPhone Safari, Android Chrome)

The release `.exe` is not code-signed yet, so Windows SmartScreen may warn on first run. See [docs/code-signing.md](docs/code-signing.md) for the signing setup.

## Building from source

Windows app (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)):

```bash
dotnet test RClicker.sln -c Release
dotnet publish src/RClicker/RClicker.csproj -c Release -r win-x64 -o artifacts/publish/win-x64
```

Use `-r win-arm64` for ARM64 Windows. Add `-p:RelayUrl=https://<your relay>` to build in a different relay.

Relay (needs Node.js 20+):

```bash
cd relay
npm ci
npm test            # runs inside the Workers runtime
npm run typecheck
npm run dev         # local relay on http://localhost:8787
```

Then run `rclicker.exe --relay http://localhost:8787`. More on tests: [docs/testing.md](docs/testing.md).

### Running your own relay

The relay fits Cloudflare's Workers Free plan. Point the routes in `relay/wrangler.jsonc` at your own domain (or use workers.dev), then:

```bash
cd relay
npx wrangler login
npx wrangler deploy
```

Build the app with `-p:RelayUrl=<that address>`, or run it with `--relay <that address>`.

## Project structure

```
src/RClicker.Core/    platform-neutral: sessions, relay client, end-to-end crypto,
                      protocol, rate limiting, QR matrix
src/RClicker/         Windows app: WinForms window, SendInput keyboard, foreground check
tests/RClicker.Tests  xUnit tests (unit + relay client against a fake relay)
relay/                Cloudflare Worker + Durable Object relay, and the phone page (relay/public)
docs/                 architecture, security, testing, code signing
```
