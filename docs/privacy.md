# Privacy policy

rclicker is a presentation remote: a Windows app (`rclicker.exe`) and a phone web page. This page says what data it handles and where that data goes.

## Short version

- No accounts, no ads, no analytics, no tracking cookies.
- Your slide commands are **end-to-end encrypted** between your phone and your PC. The relay that passes them along can't read them.
- The relay keeps only what it needs to connect one phone to one PC, and deletes it within about two days at most.

## What leaves your devices

rclicker only connects to its relay, `clicker.rotmanav.ca`, which runs on Cloudflare. It connects there only while the app is open, or while the phone page is open.

| Data | Sent by | Why | Kept |
| --- | --- | --- | --- |
| Room id: a random code derived from the QR code's key (not the key itself) | PC and phone | To put your phone and your PC in the same room | While the room exists (see below) |
| SHA-256 hashes of the PC's private host key and of the phone's join token | PC | So that only your PC can own the room and only your phone can join it | While the room exists |
| Encrypted commands and replies (for example "next slide") | PC and phone | To control your presentation | Not stored. Forwarded immediately. |
| Phone type ("iPhone", "Android", …), worked out from the browser's User-Agent | Phone's browser | To show "Connected: iPhone" on the PC | While the room exists |
| A random browser id (kept in the phone browser's local storage) | Phone | So the same phone can reconnect after a short drop | 30 seconds after the phone disconnects |
| IP address and standard request details | Both (as with any website) | Needed to deliver internet traffic; handled by Cloudflare | Under [Cloudflare's privacy policy](https://www.cloudflare.com/privacypolicy/) |

**The QR code's key never reaches any server.** It sits after the `#` in the link, which browsers don't send.

**How long rooms last.** A room ends when you press **New session** or quit the app, 2 hours after the PC goes offline, or after 24 hours at most. After it ends, a small marker that just says "ended" is kept for up to 24 hours, so old QR codes can say "Session ended". Then it's deleted.

## What stays on your devices

- **PC:** rclicker doesn't install anything, change system settings or write files. Its session key exists only in memory and is gone when you quit. To uninstall, delete `rclicker.exe`.
- **Phone:** the browser keeps the random browser id described above. Clear the site's data to remove it.

## The download

`clicker.rotmanav.ca/download` and the README link point to GitHub Releases. GitHub's [privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement) applies to downloads from there.

## Questions

Open an issue at https://github.com/camster91/Rclicker/issues.
