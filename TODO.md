# rclicker to-do

Last updated: 2026-10-05

## Now: free code signing (SignPath Foundation)

- [ ] **Pick a license.** Tell Claude A, B or C, and Claude adds the `LICENSE` file.
  - A. MIT (recommended)
  - B. Apache 2.0
  - C. GPL 3.0
- [ ] **Turn on 2-step login on GitHub:** https://github.com/settings/security
- [ ] **Merge PR #12** (signing setup, privacy policy, code signing policy). Just say "merge".
- [ ] **Apply** at https://signpath.org/apply. Answers to copy are in [docs/code-signing.md](docs/code-signing.md#what-you-do).
- [ ] **Wait for approval.** This can take a while.

## After SignPath approves

- [ ] In SignPath, follow ["After approval"](docs/code-signing.md#after-approval-about-15-minutes): turn on 2FA, create the project `rclicker`, link GitHub, paste the artifact configuration, and set yourself as approver.
- [ ] In GitHub (Settings → Secrets and variables → Actions), add:
  - variable `SIGNPATH_ORGANIZATION_ID`
  - variable `SIGNPATH_PROJECT_SLUG` = `rclicker`
  - variable `SIGNPATH_SIGNING_POLICY_SLUG`
  - **secret** `SIGNPATH_API_TOKEN` (only you add this)
- [ ] Push or merge anything to `main`, then **approve the signing request** that SignPath emails you within 1 hour.
- [ ] Check the download: right-click `rclicker.exe` → Properties → Digital Signatures should show "SignPath Foundation".

## rclicker website

- [ ] **Unblock rclicker** when you're ready. The rotmanav.ca login lock blocks phones and the PC app right now. Say "unblock rclicker", and Claude opens only clicker.rotmanav.ca while the rest stays locked.
- [ ] Until then, share the direct download: https://github.com/camster91/Rclicker/releases/latest/download/rclicker.exe

## Testing

- [ ] Try it on a real work PC with PowerPoint, on the work network:
  - start the slideshow, next, previous, black screen, end
  - the phone on mobile data
  - the PC behind the company proxy

## Account safety

- [ ] Turn on 2-step login on **Cloudflare**. Anyone who gets into that account could change the phone page.
- [ ] Optional: install the Claude GitHub App so Claude gets alerts about pull requests automatically: https://github.com/apps/claude/installations/select_target

## Ideas for later

- [ ] "Present in the browser" mode: pick a PDF on the PC and control it from the phone. Nothing to install.
- [ ] Ideas from the README roadmap: proxies that block WebSockets, native PowerPoint control, Google Slides, presenter notes and timer.
