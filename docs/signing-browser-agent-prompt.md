# Browser agent prompt: code signing for all my Windows apps

Copy everything below the line into a browser agent (for example Claude in Chrome). Sign in to GitHub (as camster91) and to the Azure portal first.

---

You're helping me, camster91 on GitHub, set up automatic code signing for every Windows app on my GitHub profile. I already have Azure Artifact Signing (Microsoft's signing service) set up. A shared, reusable GitHub Actions workflow already exists at `camster91/Rclicker/.github/workflows/sign-windows.yml`. Your job: collect my Azure IDs, put them in that one file, let each Windows app repo sign through Azure, and connect each repo's build to the shared workflow.

Reference guide (read it first): https://github.com/camster91/Rclicker/blob/main/docs/code-signing.md. If it isn't on `main` yet, use the same file on branch `claude/adoring-franklin-gix1q0`.

## Rules (follow these every time)

- **Never spend money, change billing or plans, or create paid resources.** If a page asks for payment or an upgrade, stop and ask me.
- **Never create, copy, paste or store secrets:** no client secrets, passwords, API keys or certificates. The six Azure values listed below are plain IDs, not secrets. Sign-in uses OIDC federated credentials only. If any step seems to need a secret, stop and ask me.
- **Never delete** repositories, branches, files, workflows, Azure resources or credentials.
- **Never push straight to a `main` branch, and never merge a pull request.** Make every code change on a new branch with a pull request, and leave each PR open for me.
- **Never change** repository visibility, branch protection, collaborators or Actions secrets.
- If a sign-in, 2-step check or CAPTCHA appears, stop and let me do it.
- **Before Phase 3, show me your list of repos and wait for my OK.**
- If something doesn't match these instructions, stop and tell me what you see instead of guessing.

## Phase 1: collect the six Azure IDs (read only)

In https://portal.azure.com:

1. **Microsoft Entra ID → App registrations → All applications.** Find the app used for GitHub signing (probably named `github-code-signing` or `rclicker-github-signing`). Write down:
   - `AZURE_CLIENT_ID` = Application (client) ID
   - `AZURE_TENANT_ID` = Directory (tenant) ID
2. **Subscriptions.** Write down `AZURE_SUBSCRIPTION_ID` for the subscription that holds the signing account.
3. Search for **Artifact Signing Accounts** (it may also be called "Trusted Signing Accounts") and open my account. Write down:
   - `AZURE_SIGNING_ACCOUNT` = the account name
   - `AZURE_SIGNING_ENDPOINT` = the account URI, e.g. `https://eus.codesigning.azure.net`
4. In that account, open **Certificate profiles**. Write down `AZURE_SIGNING_PROFILE` = the profile name. Its status must be **Active**. Also check under **Identity validations** that my identity says **Completed**.
5. In the signing account, open **Access control (IAM) → Role assignments**. Confirm that the app from step 1 has the role **Artifact Signing Certificate Profile Signer** (it may be called "Trusted Signing Certificate Profile Signer"). If it's missing, add it: **Add → Add role assignment**, pick that role, and choose the app as the member.

If there's no app registration, no signing account or no certificate profile, stop and tell me. The full setup is in the guide, steps 1–6.

## Phase 2: find my Windows apps (read only)

Go to https://github.com/camster91?tab=repositories and look at every repository that isn't a fork or archived. A repo is a **Windows app** if any of these is true:
- a `.csproj` with `<OutputType>WinExe</OutputType>` or `Exe`, or with a `TargetFramework` ending in `-windows`, or using WinForms or WPF;
- an Electron, Tauri, Flutter, Go, Rust or C++ project whose workflow builds for Windows (`windows-latest` runner, `--win`, `x86_64-pc-windows-msvc`, `.exe`, `.msi`, `.msix`);
- its releases include `.exe`, `.msi` or `.msix` files.

For each Windows app, note:
- the repo name and its default branch;
- the workflow file in `.github/workflows/` that builds the Windows files;
- the job that builds them, and whether it already uploads them with `actions/upload-artifact`. If so, note the artifact name.

**Show me the list and wait for my OK.**

## Phase 3: put the IDs in the shared workflow (one pull request)

In `camster91/Rclicker`, open `.github/workflows/sign-windows.yml`:
- if PR "Shared Azure code signing workflow for all repositories" is still open, open the file on branch `claude/adoring-franklin-gix1q0`;
- otherwise open it on `main`.

Click the pencil (Edit). In the `env:` block, fill in the six values from Phase 1 between the quotes. Change nothing else:

```yaml
env:
  AZURE_TENANT_ID: '<tenant id>'
  AZURE_CLIENT_ID: '<client id>'
  AZURE_SUBSCRIPTION_ID: '<subscription id>'
  AZURE_SIGNING_ENDPOINT: '<https://xxx.codesigning.azure.net>'
  AZURE_SIGNING_ACCOUNT: '<account name>'
  AZURE_SIGNING_PROFILE: '<profile name>'
```

Commit:
- **On `claude/adoring-franklin-gix1q0`:** commit directly to that branch, with the message "Fill in Azure signing IDs".
- **On `main`:** choose "Create a new branch for this commit and start a pull request", name the branch `signing-ids`, and open the PR without merging it.

## Phase 4: let each Windows repo sign (Azure, one entry per repo)

In Azure: **Microsoft Entra ID → App registrations → (the app from Phase 1) → Certificates & secrets → Federated credentials**.

For `Rclicker` and every repo I approved in Phase 2 that doesn't already have an entry, click **Add credential** and fill in:
- Federated credential scenario: **GitHub Actions deploying Azure resources**
- Organization: `camster91`
- Repository: the repo name, exactly as GitHub shows it (case matters)
- Entity type: **Branch**
- GitHub branch name: the repo's default branch (usually `main`)
- Name: the repo name, e.g. `Rclicker-main`

Save each one. An app can hold at most 20; if you reach that, stop and tell me.

## Phase 5: connect each Windows repo's build (one pull request per repo)

Skip `Rclicker`; it's already connected. For every other approved repo, edit its Windows build workflow in the browser:

1. **Make sure the unsigned files are uploaded.**
   - If the Windows build job has no `actions/upload-artifact` step for the built files, add one at the end of that job:
     ```yaml
           - uses: actions/upload-artifact@v4
             with:
               name: <repo-name>-windows
               path: <folder or file pattern holding the built .exe/.msi files>
               if-no-files-found: error
     ```
   - Work out `path` from the build step's output folder. If you can't tell, stop and ask me.
   - If an upload step already exists, use its artifact name.
2. **Add a signing job** at the same indentation as the other jobs, under `jobs:`:
   ```yaml
     sign:
       needs: <name of the Windows build job>
       if: github.event_name == 'push' && github.ref == 'refs/heads/main'
       permissions:
         contents: read
         id-token: write
       uses: camster91/Rclicker/.github/workflows/sign-windows.yml@main
       with:
         artifact: <the artifact name from step 1>
   ```
   If the repo's default branch isn't `main`, change `refs/heads/main` to match.
3. **If a job publishes a release** from the build artifact (it uses `actions/download-artifact`, `gh release`, `softprops/action-gh-release` or similar):
   - add `sign` to that job's `needs:`;
   - make it download `<artifact>-signed` when `needs.sign.outputs.signed == 'true'`, and the unsigned artifact otherwise. For example:
     ```yaml
         name: ${{ needs.sign.outputs.signed == 'true' && '<artifact>-signed' || '<artifact>' }}
     ```
   - if that job previously ran only when its other `needs` succeeded, add `always() &&` at the start of its `if:`. Then also require `needs.sign.result == 'success' || needs.sign.result == 'skipped'`, so a failed signing never publishes.
   - If the release logic is complicated, don't change it. Only do steps 1–2, and note it in the PR description.
4. **Commit** with "Create a new branch for this commit and start a pull request", using branch `code-signing` and title "Sign Windows builds with Azure Artifact Signing". In the PR description, say what you changed and which artifact is signed. **Don't merge.**
5. **Check the run:** open the PR's **Checks** tab and wait for the run to finish.
   - The `sign` job is skipped on PR branches. That's expected, because it only runs on `main`.
   - If the build is red because of your edit (for example, wrong indentation or a wrong artifact path), fix it on the same branch.

Also check each repo's **Settings → Actions → General → Actions permissions**. It must allow reusable workflows from `camster91/Rclicker`: "Allow all actions and reusable workflows", or an allow-list that includes `camster91/*`. If it doesn't, tell me; don't change it.

## Phase 6: report back

Give me a short table with these columns:
- repo
- Windows app? (yes/no)
- Azure entry added? (yes/already/no)
- PR link
- build status on the PR
- anything I need to do

Also give me the Rclicker PR or commit link from Phase 3.

Finish by reminding me: after I merge each PR, the next build on `main` signs the files. I can check a download by right-clicking the `.exe` → **Properties** → **Digital Signatures**.
