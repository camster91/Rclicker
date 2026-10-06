# Code signing

Signing `rclicker.exe` tells Windows who made it. That stops the "unknown publisher" warnings, and many company PCs require it before a program may run at all.

rclicker uses **Azure Artifact Signing** (Microsoft, about US$9.99/month for up to 5,000 signatures). One Azure account signs builds from **all** your repositories through one shared workflow:

- [`.github/workflows/sign-windows.yml`](../.github/workflows/sign-windows.yml) holds the signing steps and the six Azure IDs.
- Each repository calls it with a few lines (below).
- Nothing secret is stored anywhere. GitHub signs in to Azure with a short-lived token (OIDC), and Azure decides which repositories may sign.

**Status:** set up. The IDs are filled in, and rclicker releases from 0.2.3 are signed by Cameron Ashley. If signing is ever unavailable, CI never replaces an already-published release with an unsigned build.

## One-time Azure setup (about 30 minutes, plus Microsoft's ID check)

### 1. Azure subscription
1. Go to https://portal.azure.com and sign in, or create an account. A credit card is required.
2. If you're signing as an **individual**, check **Cost Management + Billing → Billing profile**. The account type must be **Individual**, and the legal name and address must match your government ID exactly.

### 2. Turn on the service
1. In the portal, open **Subscriptions** → your subscription → **Resource providers**.
2. Find **Microsoft.CodeSigning** and click **Register**.

### 3. Create the signing account
1. Search for **Artifact Signing Accounts** → **Create**.
2. Resource group: **Create new** → `code-signing`.
3. Account name: for example `camsterSigning` (letters and numbers, globally unique).
4. Region: for example **East US**, whose endpoint is `https://eus.codesigning.azure.net`. Note the endpoint of the region you pick.
5. Pricing: **Basic** → **Review + Create**.

### 4. Verify your identity
1. Open the new account → **Access control (IAM)** → **Add role assignment**. Give **yourself** the role **Artifact Signing Identity Verifier**.
2. In the account, go to **Identity validations** → **Individual** (or Organization) → **New identity** → **Public**.
3. Click **Create**. When the status says **Action Required**, click your name and follow the link. It walks you through an ID check with your phone and the Microsoft Authenticator app.
4. Wait for **Completed**. Microsoft says this takes from a few minutes up to 20 business days.

### 5. Create the certificate profile
1. In the account, go to **Certificate profiles** → **Create** → **Public Trust**.
2. Name: for example `camster`. Under **Verified CN and O**, pick your identity validation → **Create**.

### 6. Let GitHub sign (no passwords stored)
1. Go to **Microsoft Entra ID → App registrations → New registration**. Name it `github-code-signing` and register it.
2. Note the **Application (client) ID** and the **Directory (tenant) ID**.
3. In the signing account, go to **Access control (IAM) → Add role assignment**. Give the app `github-code-signing` the role **Artifact Signing Certificate Profile Signer**.
4. Allow each repository that should sign; see [Allow a repository](#allow-a-repository). Start with `Rclicker`.

### 7. Fill in the six IDs (once, for all repositories)
In [`.github/workflows/sign-windows.yml`](../.github/workflows/sign-windows.yml), fill in the `env:` block. These are IDs, not secrets:

| Name | Value |
| --- | --- |
| `AZURE_TENANT_ID` | Directory (tenant) ID, from step 6 |
| `AZURE_CLIENT_ID` | Application (client) ID, from step 6 |
| `AZURE_SUBSCRIPTION_ID` | your subscription ID |
| `AZURE_SIGNING_ENDPOINT` | e.g. `https://eus.codesigning.azure.net` |
| `AZURE_SIGNING_ACCOUNT` | e.g. `camsterSigning` |
| `AZURE_SIGNING_PROFILE` | e.g. `camster` |

Or send them to Claude to fill in. The next build of rclicker's `main` branch then signs `rclicker.exe`, checks the signature, and publishes it.

## Allow a repository

Each repository (and branch) that signs needs one entry in Azure. This is what stops other people's repositories from using your certificate.

1. Open **Microsoft Entra ID → App registrations → `github-code-signing` → Certificates & secrets → Federated credentials → Add credential**.
2. Scenario: **GitHub Actions deploying Azure resources**.
3. Organization: `camster91`. Repository: for example `Rclicker`. Entity type: **Branch**. Branch: `main`.
4. Name it after the repository, then click **Add**.

An app registration can hold up to 20 of these.

## Use it in another repository

1. Allow the repository in Azure (above).
2. In that repository's workflow, have the build job upload its unsigned files as an artifact. Then add a job like this:

```yaml
  sign:
    needs: build
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    permissions:
      contents: read
      id-token: write
    uses: camster91/Rclicker/.github/workflows/sign-windows.yml@main
    with:
      artifact: my-app-win-x64   # the artifact your build job uploaded
      # file-types: exe,dll,msi,msix   (the default)
```

3. Later jobs download the signed files from the artifact `my-app-win-x64-signed`. `needs.sign.outputs.signed` is `'true'` when they were signed.

## Let a browser agent do it

[signing-browser-agent-prompt.md](signing-browser-agent-prompt.md) is a ready-made prompt for a browser agent (for example Claude in Chrome). It:

- collects the six Azure IDs and fills them in;
- allows each of your Windows repos in Azure;
- opens one pull request per repo that connects its build to this workflow.

It never handles secrets or money, and it never merges anything.

## How to check a download is signed

Right-click the `.exe` → **Properties** → **Digital Signatures** tab. It should list your verified name.

## Notes

- A newly signed app may still get a SmartScreen prompt for a short while, until enough people have downloaded it to build a reputation. Signing is what makes that possible.
- Signing doesn't change how the program works. The same build is just stamped with your verified identity.
- A free alternative for open-source projects is [SignPath Foundation](https://signpath.org). The publisher then shows as "SignPath Foundation", and each release needs a manual approval. Its setup was in PR #12 and was removed when Azure was chosen; ask Claude if you want it back.
