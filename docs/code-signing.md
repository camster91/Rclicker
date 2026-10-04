# Code signing

Signing `rclicker.exe` tells Windows where it came from. That stops the "unknown publisher" warnings, and many company PCs require it before a program may run at all.

There are two ways. The build supports both, but **set up only one**.

| | Free: SignPath Foundation | Paid: Azure Artifact Signing |
| --- | --- | --- |
| Cost | $0 | about US$9.99/month |
| Publisher name shown by Windows | **SignPath Foundation** | **your verified name** |
| Who can use it | open-source projects they approve | individuals (Canada/US) or organizations |
| Each release | a person approves it in SignPath (CI waits up to 1 hour) | automatic |
| Setup | apply, wait for approval, then about 15 minutes | about 30 minutes plus Microsoft's ID check |

Until one of them is set up, the signing steps in CI are skipped and releases are unsigned.

## Free: SignPath Foundation

[SignPath Foundation](https://signpath.org) signs open-source projects for free. Their certificate is issued to "SignPath Foundation", and they check that each signed file was built by GitHub Actions from this repository's public source.

### Their requirements and where rclicker stands

| Requirement | Status |
| --- | --- |
| OSI-approved open-source license, with no paid dual license | **To do: add a `LICENSE` file** (your choice of license) |
| Public source repository | Done (`camster91/Rclicker`) |
| Already released, and the download page describes what it does | Done (GitHub Releases, README, clicker.rotmanav.ca) |
| Built automatically from source | Done (GitHub Actions, `.github/workflows/ci.yml`) |
| File metadata (product name and version) enforced | Done: the exe says `rclicker` / `0.2.2`, and `.signpath/artifact-configuration.xml` only signs a file that matches |
| Privacy policy, since the app connects to a server | Done: [docs/privacy.md](privacy.md) |
| "Code signing policy" section with their exact attribution, team roles and a privacy link | Done: the [README](../README.md#code-signing-policy) |
| No system changes without warning; easy to uninstall | Done: portable exe, no installer, changes nothing; delete it to uninstall |
| No hacking tools, malware or unwanted features | Done: it can only press five presentation keys |
| Multi-factor sign-in on GitHub and SignPath for everyone in the team | **To do: turn on 2FA on GitHub** (and on SignPath once you have an account) |
| A person approves each signing request | Built in: CI waits for your approval |

### What you do

1. **Add a license.** Ask me to add one. MIT is the usual choice for small tools: anyone may use, change and share the code, but must keep your copyright notice.
2. **Turn on 2FA for GitHub:** https://github.com/settings/security → Two-factor authentication.
3. **Apply** at https://signpath.org/apply. Use these answers:
   - Project: rclicker, `https://github.com/camster91/Rclicker`
   - What it does: a phone-controlled presentation remote for PowerPoint on Windows
   - Build system: GitHub Actions
   - Files to sign: `rclicker.exe` (Windows, Authenticode)
   - Code signing policy: `https://github.com/camster91/Rclicker#code-signing-policy`
   - Privacy policy: `https://github.com/camster91/Rclicker/blob/main/docs/privacy.md`
4. **Wait for approval.** It's free, and they review each project, so this can take a while.

### After approval (about 15 minutes)

In SignPath (https://app.signpath.io):

1. Turn on 2FA for your SignPath account.
2. Create a **project** with the slug `rclicker`, and link it to the repository `camster91/Rclicker`.
3. **Trusted build system:** add or link **GitHub.com** to the project. This is what proves each file was built by GitHub Actions.
4. **Artifact configuration:** paste in [`.signpath/artifact-configuration.xml`](../.signpath/artifact-configuration.xml) and make it the default.
5. **Signing policy:** use the release-signing policy SignPath Foundation set up for you (for example `release-signing`). Make yourself the approver.
6. Create a **CI user** (or API token) that may submit signing requests, and copy its token.

In GitHub, open https://github.com/camster91/Rclicker → **Settings → Secrets and variables → Actions**:

| Kind | Name | Value |
| --- | --- | --- |
| Variable | `SIGNPATH_ORGANIZATION_ID` | your SignPath organization ID |
| Variable | `SIGNPATH_PROJECT_SLUG` | `rclicker` |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | the signing policy's slug, e.g. `release-signing` |
| **Secret** | `SIGNPATH_API_TOKEN` | the CI user's API token (keep it secret; only you add this) |

That's it. Each push to `main` then:

1. builds `rclicker.exe` (unsigned) as before;
2. sends that exact file to SignPath and waits up to **1 hour** for you to approve it (SignPath emails you);
3. checks the signature and publishes the **signed** file to the release behind https://clicker.rotmanav.ca/download.

If you don't approve in time, or you reject it, nothing is published. Approve, then re-run the failed `sign` job in GitHub Actions.

## Paid: Azure Artifact Signing

Use this instead if you want **your own name** on the signature or no per-release approval. It costs about US$9.99/month (Basic: up to 5,000 signatures, 1 certificate profile). Nothing secret is stored in GitHub: it signs in to Azure with a short-lived token (OIDC).

### 1. Azure subscription
1. Go to https://portal.azure.com and sign in (or create an account; a credit card is required).
2. If signing as an **individual**, check **Cost Management + Billing → Billing profile**. The account type must be **Individual**, and the legal name and address must match your government ID exactly.

### 2. Turn on the service
1. In the portal, open **Subscriptions** → your subscription → **Resource providers**.
2. Find **Microsoft.CodeSigning** and click **Register**.

### 3. Create the signing account
1. Search for **Artifact Signing Accounts** → **Create**.
2. Resource group: **Create new** → `rclicker-signing`.
3. Account name: for example `rclickersigning` (letters and numbers, globally unique).
4. Region: **East US** (endpoint `https://eus.codesigning.azure.net`). Any listed region works; note its endpoint.
5. Pricing: **Basic** → **Review + Create**.

### 4. Verify your identity
1. Open the new account → **Access control (IAM)** → **Add role assignment**. Give **yourself** the role **Artifact Signing Identity Verifier**.
2. In the account, go to **Identity validations** → choose **Individual** (or Organization) → **New identity** → **Public**.
3. Check the details and click **Create**. When the status says **Action Required**, click your name and follow the link. It walks you through an ID check with your phone and the Microsoft Authenticator app.
4. Wait for **Completed**. Microsoft says this takes from a few minutes up to 20 business days.

### 5. Create the certificate profile
1. In the account, go to **Certificate profiles** → **Create** → **Public Trust**.
2. Name: `rclicker`. Under **Verified CN and O**, pick your identity validation → **Create**.

### 6. Let GitHub sign (no passwords stored)
1. Go to **Microsoft Entra ID → App registrations → New registration**, name it `rclicker-github-signing` and register it.
2. Note the **Application (client) ID** and **Directory (tenant) ID**.
3. In the app, open **Certificates & secrets → Federated credentials → Add credential**:
   - Scenario: **GitHub Actions deploying Azure resources**
   - Organization: `camster91`, Repository: `Rclicker`, Entity type: **Branch**, Branch: `main`
4. Back in the signing account, go to **Access control (IAM) → Add role assignment** and give the app `rclicker-github-signing` the role **Artifact Signing Certificate Profile Signer**.

### 7. Tell GitHub
In https://github.com/camster91/Rclicker → **Settings → Secrets and variables → Actions → Variables** tab, add these **repository variables** (they're IDs, not secrets):

| Name | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | Application (client) ID from step 6 |
| `AZURE_TENANT_ID` | Directory (tenant) ID from step 6 |
| `AZURE_SUBSCRIPTION_ID` | Your subscription ID |
| `AZURE_SIGNING_ENDPOINT` | e.g. `https://eus.codesigning.azure.net` |
| `AZURE_SIGNING_ACCOUNT` | e.g. `rclickersigning` |
| `AZURE_SIGNING_PROFILE` | `rclicker` |

The next build on `main` signs `rclicker.exe`, checks the signature, and publishes it to the release behind https://clicker.rotmanav.ca/download.

## How to check a download is signed

Right-click `rclicker.exe` → **Properties** → **Digital Signatures** tab. It lists "SignPath Foundation" (free option) or your name (Azure).

## Notes

- A newly signed app may still get a SmartScreen prompt for a short while, until enough people have downloaded it to build a reputation. Signing is what makes that possible.
- Signing doesn't change how rclicker works; the same build is just stamped with a verified signature.
