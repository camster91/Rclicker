# rclicker

Turn your phone into a PowerPoint clicker. No app to install on the phone, and it works **even when the phone and the computer can't see each other on the network**, like on office, school and guest Wi-Fi, or with the phone on mobile data.

1. Run `rclicker.exe` on the presentation computer.
2. Scan the QR code it shows with your phone's camera.
3. Tap **Next**, **Previous**, **Start**, **Black** or **End**.

## How it works

```
Phone (mobile data or any Wi-Fi)  ──HTTPS──▶  rclicker relay (Cloudflare)  ◀──HTTPS──  PC running rclicker.exe
```

Both the phone and the PC make **outgoing** HTTPS connections (port 443) to a small relay on Cloudflare. Nothing connects *into* the PC, so there's no firewall prompt and no need for the two devices to share a network. Commands are **end-to-end encrypted**: the relay passes them along but can't read or fake them.

Website and relay: **https://clicker.rotmanav.ca** (built into the app). Download: **https://clicker.rotmanav.ca/download**. The older addresses (`rotmanav.ca/clicker`, `rclicker.cameron-rotman.workers.dev`) redirect there, and apps already installed keep connecting through them.

## How to run

1. Start **rclicker** (`rclicker.exe`). The window shows **Ready — scan the QR code**.
2. Open your presentation in **PowerPoint**.
3. **Scan the QR code** with your phone camera and open the link. The phone shows **● Connected**.
4. **Click once on the PowerPoint window** so it's the active window, then use the phone:

| Phone button | What PowerPoint gets | Effect |
| --- | --- | --- |
| Next | Right Arrow | Next slide / animation |
| Previous | Left Arrow | Previous slide / animation |
| Start | F5 | Start the slideshow from the beginning |
| Black | B | Black screen on/off (slideshow only) |
| End | Esc | Leave the slideshow |

Closing the rclicker window ends the session immediately. The phone shows "rclicker closed".

**Screens stay on while presenting.**
- **Phone:** stays awake while connected (the page shows "Screen stays on while connected"). On some phones this starts after your first tap.
- **PC:** won't dim or go to sleep while a phone is connected.

### Desktop window

- **QR code + link**: what the phone opens. The QR code hides itself while a phone is connected, so the audience can't scan it off the projector (click **Show QR code** to see it again).
- **Status**: `Connecting…`, `Ready`, `Connected — iPhone`, `Reconnecting…`, or `Offline` with the reason.
- **Only send keys when PowerPoint is the active window**: on by default. Turn it off to use the remote with another app (a PDF viewer, a browser slideshow).
- **New session**: makes a new QR code. The old one stops working and any connected phone is disconnected.
- **Copy**, **Help**, **Quit**.

### Command-line options

```
rclicker.exe [--relay https://<relay address>] [--console]
```

- `--relay`: use a different relay (also settable with the `RCLICKER_RELAY` environment variable).
- `--console`: open a console window with diagnostic logs (the session key is never logged in full).

## Requirements

- Windows 10 (1809) or later, or Windows 11. x64 (an ARM64 build is also produced).
- Microsoft PowerPoint (desktop app).
- **Internet access on the PC.** Outgoing HTTPS to `clicker.rotmanav.ca` must be allowed.
- A phone with a modern browser (iPhone Safari, Android Chrome) and any internet connection.
- Nothing else to install. The `.exe` contains the .NET runtime.

## Company networks

rclicker is built to get through typical corporate networks:

- **Outgoing only.** Port 443, the same as web browsing. No inbound ports and no firewall changes.
- **Proxies.** It uses the Windows proxy settings (including PAC/WPAD auto-config) and signs in to the proxy with your Windows account, like a browser does.
- **TLS inspection.** It trusts the certificates installed on the PC, so proxies that inspect HTTPS work.

What IT may need to allow:

- `https://clicker.rotmanav.ca`.
- **WebSockets** to that address. Some proxies allow web pages but block WebSocket upgrades.
- Running `rclicker.exe` itself. Some companies only allow signed or approved programs. Code signing is being set up (see [docs/code-signing.md](docs/code-signing.md)).

If IT can only allow a company-owned domain, the relay can also run on your own domain (see [Running your own relay](#running-your-own-relay)).

## Security

- Each session has a **random 256-bit key**. It's in the QR code link (`…/clicker/remote#k=…`) after the `#`, which browsers never send to any server, so the relay never sees it.
- From that key, the phone and the PC derive an **encryption key** (AES-256-GCM) that only they have. Every command and reply is encrypted. Commands also carry a one-time challenge and a counter, so recorded messages can't be replayed.
- The relay only gets a room number and a **hash** of a phone pass, so it can tell which phone may join without being able to join itself.
- Only **one phone controls at a time**. A second phone is told "Another phone is in control".
- The phone can only send five fixed commands. It **can't** press other keys, type text or run anything.
- Keys exist only in memory. **New session**, closing the app, or 12 hours makes the old QR code useless.

Details: [docs/security.md](docs/security.md).

## Troubleshooting

**Window says "Offline"**
- Check the PC has internet (open any website).
- The message says why: proxy sign-in needed, address blocked, WebSocket blocked, and so on. Send it to IT along with the relay address above.
- rclicker keeps retrying on its own (up to every 30 seconds).

**QR opens but the page doesn't load**
- The phone needs internet. Try mobile data instead of guest Wi-Fi.

**Phone says "Waiting for computer…"**
- The phone is connected, but the PC isn't right now (network drop, laptop asleep). It continues as soon as the PC is back.

**PowerPoint doesn't respond**
- PowerPoint must be the **active window**. Click on it once. The phone says "PowerPoint is not the active window" when it isn't.
- **Black** only works during a slideshow. Tap **Start** first.
- If PowerPoint runs **as administrator**, Windows blocks key presses from normal apps. Run rclicker as administrator too, or run PowerPoint normally.
- Some localized PowerPoint versions use a different black-screen key. The "." (period) key on the PC keyboard also blackens the screen.

**Phone shows "Reconnecting…"**
- Normal after the phone screen locks or the signal drops. It reconnects by itself when you open it again. Your seat is kept for 30 seconds, so another phone can't take control meanwhile. While connected, the page keeps the phone screen on. If the phone says "Tap any button to keep the screen on", tap once. On very old phones, turn off auto-lock instead.

**Phone shows "Session ended"**
- The QR code was replaced (New session), expired, or the app was restarted. Scan the QR code on the computer again.

**Windows SmartScreen warns about the app**
- Version 0.2.x isn't code-signed. Click **More info → Run anyway** if you trust where you got it.

## Building from source

Windows app (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)):

```bash
dotnet test RClicker.sln -c Release
dotnet publish src/RClicker/RClicker.csproj -c Release -r win-x64 -o artifacts/publish/win-x64
```

This produces one self-contained `rclicker.exe` (about 60 MB). Use `-r win-arm64` for ARM64 Windows. Add `-p:RelayUrl=https://<your relay>` to build in a different relay.

Relay (needs Node.js 20+):

```bash
cd relay
npm ci
npm test          # runs inside the real Workers runtime (Miniflare)
npm run dev       # local relay on http://localhost:8787; run rclicker.exe --relay http://localhost:8787
```

Project layout:

```
src/RClicker.Core/       platform-neutral: sessions, relay client, end-to-end crypto, protocol,
                         rate limiting, QR matrix
src/RClicker/            Windows app: WinForms window, SendInput keyboard, foreground check
tests/RClicker.Tests     xUnit tests (unit + relay client against a fake relay)
relay/                   Cloudflare Worker + Durable Object relay, and the phone page (relay/public)
docs/                    architecture, security, testing
```

## Running your own relay

The relay is free on Cloudflare's Workers Free plan (100,000 requests a day; a one-hour talk uses a few hundred).

```bash
cd relay
npx wrangler login      # or set CLOUDFLARE_API_TOKEN
npx wrangler deploy     # prints https://rclicker.<your-subdomain>.workers.dev
```

Then build the app with `-p:RelayUrl=<that address>`, or run it with `--relay <that address>`. For a company domain, add a [Custom Domain](https://developers.cloudflare.com/workers/configuration/routing/custom-domains/) to the Worker in the Cloudflare dashboard.

## Roadmap (not in 0.2)

- Long-polling fallback for proxies that block WebSockets
- PowerPoint-native control (COM / API) instead of key presses
- Google Slides and other presentation apps
- Presenter notes, slide preview, timer
- Laser pointer / pointer mode
- Managed classroom deployment
