# rclicker

Turn your phone into a PowerPoint clicker. No app to install on the phone.

1. Run `rclicker.exe` on the presentation computer.
2. Scan the QR code it shows with your phone's camera.
3. Tap **Next**, **Previous**, **Start**, **Black** or **End**.

Version 0.1.0 works on the **local network only**: the phone and the computer must be on the same Wi-Fi or LAN. There is no cloud service, no account and no data leaves your network.

## How to run

1. Start **rclicker** (`rclicker.exe`). A small window opens with a QR code.
   - The first time, Windows Firewall may ask whether to allow it. Allow it on **Private networks** (see [Firewall](#firewall)).
2. Open your presentation in **PowerPoint**.
3. **Scan the QR code** with your phone camera and open the link. The page shows **● Connected**.
4. **Click once on the PowerPoint window** so it is the active window, then use the phone:

| Phone button | What PowerPoint gets | Effect |
| --- | --- | --- |
| Next | Right Arrow | Next slide / animation |
| Previous | Left Arrow | Previous slide / animation |
| Start | F5 | Start the slideshow from the beginning |
| Black | B | Black screen on/off (slideshow only) |
| End | Esc | Leave the slideshow |

Closing the rclicker window ends the session immediately. The phone shows "rclicker closed".

### Desktop window

- **QR code + address**: what the phone opens. The QR code hides itself while a phone is connected so the audience can't scan it off the projector (click **Show QR code** to see it again).
- **Status**: `Ready`, `Connected — iPhone`, `Reconnecting…`, `No network`, or `Not running` with the reason.
- **Network**: choose a different address if the computer has several (Wi-Fi + Ethernet, VPN, virtual adapters).
- **Only send keys when PowerPoint is the active window**: on by default. Turn it off to use the remote with another app (a PDF viewer, a browser slideshow). Keys always go to whatever window is active.
- **New session**: makes a new QR code. The old one stops working and any connected phone is disconnected.
- **Copy**, **Help**, **Quit**.

### Command-line options

```
rclicker.exe [--port <1024-65535>] [--console]
```

- `--port` use a specific port. By default it uses **8765**, and if that is busy, the next free port up to 8775.
- `--console` open a console window with diagnostic logs (the session token is never logged in full).

## Requirements

- Windows 10 (1809) or later, or Windows 11. x64 (an ARM64 build is also produced).
- Microsoft PowerPoint (desktop app).
- A phone (iPhone or Android) with a modern browser that can reach the computer over the local network.
- Nothing else to install. The `.exe` contains the .NET runtime.

## Networking

The phone talks directly to the computer over your local network (`http://<computer IP>:8765`). This only works when the phone can reach the computer.

**Often blocked:** guest Wi-Fi, hotel Wi-Fi, university and corporate networks frequently use "client isolation", which stops devices on the same Wi-Fi from talking to each other. If that's the case, there's nothing the app can do. Workarounds: use a phone hotspot and connect the computer to it, or a small travel router.

Mobile data won't work. The phone must be on Wi-Fi, on the same network as the computer.

## Firewall

rclicker listens for incoming connections, so Windows Defender Firewall must allow it. The app **never changes firewall settings itself**.

- On first launch Windows usually shows **"Windows Defender Firewall has blocked some features of this app"**. Tick **Private networks** and click **Allow access** (needs admin rights).
- If your Wi-Fi is set to **Public**, Windows blocks it there. Either set the network to Private (*Settings → Network & internet → Wi-Fi → your network → Network profile type → Private*), or allow the app for Public networks too.
- If you clicked Cancel earlier: *Windows Security → Firewall & network protection → Allow an app through firewall → Change settings* → find **rclicker** and tick **Private**. If it isn't listed, use **Allow another app…** and pick `rclicker.exe`.
- Third-party antivirus or firewalls need the same allowance for `rclicker.exe` (TCP port 8765).

## Security

- Each session has a **random 256-bit token** inside the QR code. Without it the computer refuses connections.
- The token **lives only in memory** while the app runs. It's never saved to disk. **New session** or closing the app makes it invalid at once. Sessions also expire after 12 hours.
- Only **one phone controls at a time**. A second phone that scans the code is told "Another phone is in control". Nobody can silently take over.
- The phone can only send five fixed commands (next, previous, start, black, end). It **can't** press arbitrary keys, type text or run anything.
- The connection is **plain HTTP on your local network: it is not encrypted**. Someone else on the same Wi-Fi could, with effort, watch the traffic and see the token. Use it on networks you trust.

Details: [docs/security.md](docs/security.md).

## Troubleshooting

**QR opens but the page doesn't load**
- Check the phone is on the **same Wi-Fi** as the computer, not mobile data.
- Check Windows Firewall ([Firewall](#firewall)). This is the most common cause.
- Guest, school and office Wi-Fi often block device-to-device traffic. Try a phone hotspot.

**PC and phone are on different networks**
- Typical with dual-band or "guest" networks, or when the PC is on Ethernet in a different VLAN. Put both on the same network.

**Windows Firewall blocks the connection**
- Allow `rclicker.exe` on Private networks, and make sure your Wi-Fi profile is Private. See [Firewall](#firewall).

**VPN interferes**
- VPNs can route local traffic away or block it. Disconnect the VPN, or enable "allow LAN access" in the VPN client. Make sure the **Network** dropdown shows your Wi-Fi/Ethernet address, not the VPN's.

**Wrong network adapter selected**
- Pick another address in the **Network** dropdown (for example the `192.168.x.x` one marked Wi-Fi). The QR code updates instantly. Hyper-V, WSL, VMware, Docker and VPN adapters are marked "(virtual/VPN)" and ranked last.

**PowerPoint doesn't respond**
- PowerPoint must be the **active window**. Click on it once. The phone says "PowerPoint is not the active window" when it isn't.
- **Black** only works during a slideshow. Tap **Start** first.
- If PowerPoint runs **as administrator**, Windows blocks key presses from normal apps. Run rclicker as administrator too, or run PowerPoint normally.
- Some localized PowerPoint versions use a different black-screen key. In that case the "." (period) key also blackens the screen from the PC keyboard.

**Port already in use**
- The app automatically tries ports 8765–8775. If all are busy it says so. Start it with `--port 9000` (or any free port), then scan the new QR code.

**Phone shows "Reconnecting…"**
- Normal after the phone screen locks or Wi-Fi blips. It reconnects by itself when you open it again. Your seat is kept for 30 seconds, so another phone can't grab control meanwhile.

**Phone shows "Session ended"**
- The QR code was replaced (New session) or the app was restarted. Scan the QR code on the computer again.

**Windows SmartScreen warns about the app**
- Version 0.1 isn't code-signed. Click **More info → Run anyway** if you trust where you got it.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) (LTS).

```bash
dotnet build RClicker.sln -c Release
dotnet test RClicker.sln -c Release
dotnet publish src/RClicker/RClicker.csproj -c Release -r win-x64 -o artifacts/publish/win-x64
```

The result is a single self-contained `artifacts/publish/win-x64/rclicker.exe` (~65 MB). Use `-r win-arm64` for ARM64 Windows. The code builds and its tests run on Windows, macOS and Linux. The app itself only runs on Windows.

Project layout:

```
src/RClicker.Core/   platform-neutral: sessions, protocol, rate limiting, LAN address
                               selection, QR matrix, Kestrel/WebSocket server, phone UI (wwwroot/)
src/RClicker/        Windows app: WinForms window, SendInput keyboard, foreground check
tests/RClicker.Tests xUnit tests (unit + real HTTP/WebSocket integration tests)
docs/                          architecture, security, testing
```

See [docs/architecture.md](docs/architecture.md) and [docs/testing.md](docs/testing.md).

## Roadmap (not in 0.1)

Ideas for later versions, deliberately **not** implemented now:

- Secure cloud relay for cross-network control (phone on mobile data, isolated Wi-Fi)
- PowerPoint-native control (COM / API) instead of key presses
- Google Slides and other presentation apps
- Presenter notes, slide preview, timer
- Laser pointer / pointer mode
- Managed classroom deployment
