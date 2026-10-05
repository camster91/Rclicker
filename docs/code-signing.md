# Code signing (Azure Artifact Signing)

Signing `rclicker.exe` tells Windows who made it. That stops the "unknown publisher" warnings, and many company PCs require it before a program may run at all.

We use **Azure Artifact Signing** (formerly "Trusted Signing"), Microsoft's own signing service:

- **Cost:** about US$9.99/month (Basic: up to 5,000 signatures, 1 certificate profile).
- **Who can use it:** individual developers in Canada or the US, or organizations in many countries.
- **How it fits:** GitHub signs every build from `main` automatically. Nothing secret is stored in GitHub; it signs in to Azure with a short-lived token (OIDC).

Until the setup below is done, the signing steps in CI are simply skipped.

## What you do (once, about 30 minutes plus Microsoft's ID check)

### 1. Azure subscription
1. Go to https://portal.azure.com and sign in (or create a free account; a credit card is required).
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

That's it. The next build on `main` signs `rclicker.exe`, checks the signature, and publishes it to the release behind https://clicker.rotmanav.ca/download.

To check the connection before publishing, open **Actions → CI → Run workflow**, select `main`, and run it. Once the variables are configured, this signs and verifies the downloadable `rclicker-win-x64` workflow artifact. A manual run does not update the public release; a push to `main` does.

## How to check a download is signed

Right-click `rclicker.exe` → **Properties** → **Digital Signatures** tab. It should list your name.

## Notes

- A brand-new certificate may still see a SmartScreen prompt for a short while, until the file has been downloaded enough times to build up a reputation. Signing is what makes that possible.
- Signing doesn't change how rclicker works; the same build is just stamped with your verified identity.
