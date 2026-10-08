# rclicker guidance for IT teams

rclicker is a Windows presentation remote. The person presenting starts one
self-contained `rclicker.exe`, then opens the phone page by scanning the QR code
shown by the program.

## What the desktop program does

- It makes one outbound WebSocket connection over HTTPS to the configured relay
  on port 443. The exact hostname is the one in the QR link and the phone page;
  it can also be supplied with `--relay` for an organisation's own relay.
- It has no inbound listener, service, driver, scheduled task, or firewall rule.
- The phone can send only five fixed presentation commands: Right Arrow, Left
  Arrow, F5, B, and Escape. By default the desktop program sends them only when
  PowerPoint is the foreground application. If Windows cannot identify the
  foreground process, it fails closed and sends no key.
- The phone and PC exchange end-to-end encrypted commands. The relay coordinates
  the room but does not receive the session key or readable commands.

The browser version at `/present` is available when installing a Windows program
is not permitted. It presents a locally selected PDF and does not require the
desktop executable.

## What to allow

Allow outbound HTTPS and WebSocket upgrades to the exact relay hostname shown by
the application, on port 443. No inbound connection to the presentation PC is
needed. A proxy that permits ordinary HTTPS but blocks WebSockets can still stop
the desktop connection.

For the desktop file, inspect the actual release before creating a rule:

1. Open `rclicker.exe` properties and confirm the Digital Signatures tab reports
   a valid signature and the verified publisher **Cameron Ashley**.
2. Use the signer or publisher identity in the organisation's approved endpoint
   policy or application-control review. Product-specific exclusion names and
   approval requirements vary by tenant, so the endpoint-security team should
   follow its vendor and internal policy documentation.
3. Keep the file hash and path as supporting details. They change between
   releases, while the verified publisher is the identity the signed artifact
   carries.

The exact policy names and approval requirements vary by tenant and product
version. A valid signature identifies the publisher; it does not guarantee that
every antivirus or endpoint policy will approve the file automatically.
