# Optional MSIX package

rclicker keeps its portable, self-contained `rclicker.exe` as the primary
download. The repository also contains an MSIX packaging path for managed
Windows presentation PCs. The package is an optional installation format; it
does not replace the portable file or change how the receiver talks to the
relay.

The package uses one stable identity and the same verified publisher identity
as the Windows release:

- Identity name: `CameronAshley.Rclicker`
- Publisher: `CN=Cameron Ashley, O=Cameron Ashley, L=Scarborough, S=on, C=CA`
- Package version: the three-part product version plus a fourth component, for
  example `0.2.4.0`
- Desktop entry point: `windows.fullTrustApplication`
- Required capability: `runFullTrust`

Full trust is required because the receiver is a Win32 WinForms program. It
uses the limited process-query API, the foreground-window checks, and
`SendInput` to send the five presentation shortcuts. The existing default
continues to fail closed when the foreground process is unknown or is not
PowerPoint. Packaging does not grant the phone or relay additional access to
the PC.

## Build and test on Windows

The Windows SDK supplies `makeappx.exe` and `signtool.exe`. From the repository
root, the unsigned package can be built with:

```powershell
.\tools\build-msix.ps1 -RuntimeIdentifier win-x64
```

The isolated lifecycle check creates a disposable code-signing certificate,
trusts it only on the isolated test machine, signs two temporary packages,
installs the first package, launches it through the Windows AppsFolder,
installs the higher-version package as an in-place update, and uninstalls the
package. It refuses to remove an existing installation with the same stable
identity:

```powershell
.\tools\test-msix.ps1
```

The test removes its certificate and package in `finally` cleanup. It needs an
elevated Windows test account because package trust is installed in the local
machine `TrustedPeople` store. Use a clean Windows test account or VM; it is
deliberately isolated from a user's normal installation. CI runs the same check
on `windows-latest` and also keeps the portable EXE package validation.

## Signing and updates

An unsigned MSIX cannot be installed. Local and CI lifecycle checks use a
disposable self-signed certificate only for testing. A distributed package
must be signed by the configured production publisher, and the certificate's
subject must exactly match the manifest `Publisher` value. On main pushes and
manual runs from main, CI sends the unsigned MSIX artifact through the shared
Azure OIDC signing workflow and uploads the verified result as a separate
`rclicker-msix-win-x64-unsigned-signed` artifact. It is retained for production
validation and is not included in the EXE release publication. Production
distribution remains a release-owner gate.

Keep the identity name and publisher unchanged for updates. Increase the
four-part package version; Windows uses the higher version to update the
existing package in place. Changing the identity or publisher creates a
different package family and is not a compatible update path.

These rules follow Microsoft's [package identity guidance](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/package-identity-overview),
[MSIX signing guide](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide),
[full-trust capability guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations),
and [package update guidance](https://learn.microsoft.com/en-us/windows/msix/app-package-updates).

## DPI and accessibility review

The WinForms project already opts into `PerMonitorV2` DPI, uses DPI autoscaling,
keeps the QR control's quiet zone and whole-pixel modules, and exposes the QR
code and phone link through accessibility names. The MSIX manifest supplies
the required launcher assets and the app retains its normal keyboard focus
order.

The remaining presentation checks are visual and assistive-technology QA on a
real Windows display: verify 100%, 150%, and 200% scaling, screen-reader
announcements for status changes, and keyboard traversal through the link,
checkbox, and buttons. These checks are not inferred from package installation
or CI unit tests.
