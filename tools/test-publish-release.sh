#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
publisher="$repository_root/tools/publish-release.sh"
workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT

fake_bin="$workdir/bin"
mkdir -p "$fake_bin"
cat > "$fake_bin/gh" <<'FAKE_GH'
#!/usr/bin/env bash
set -euo pipefail

printf '%s\n' "$*" >> "$FAKE_GH_LOG"
case "${1:-} ${2:-}" in
  "release view")
    if [ "${FAKE_GH_MODE:-}" = "stable-mismatch" ] || [ "${FAKE_GH_MODE:-}" = "preview-existing" ]; then
      if [ "${FAKE_GH_MODE:-}" = "stable-mismatch" ]; then
        printf '%s\n' '{"tagName":"v0.2.5","name":"rclicker 0.2.4","isPrerelease":false}'
      else
        printf '%s\n' '{"tagName":"v0.2.5-preview","name":"rclicker 0.2.5 preview","isPrerelease":true}'
      fi
      exit 0
    fi
    exit 1
    ;;
  api*)
    if [ "${FAKE_GH_MODE:-}" = "stable-mismatch" ]; then
      printf '%s\n' '{"object":{"sha":"new-source-sha","type":"commit"}}'
      exit 0
    fi
    exit 1
    ;;
  release*)
    exit 0
    ;;
  *)
    exit 1
    ;;
esac
FAKE_GH
chmod +x "$fake_bin/gh"

run_publisher() {
  PATH="$fake_bin:$PATH" \
    FAKE_GH_LOG="$FAKE_GH_LOG" \
    FAKE_GH_MODE="${FAKE_GH_MODE:-}" \
    GITHUB_REF="$GITHUB_REF" \
    GITHUB_SHA="$GITHUB_SHA" \
    GITHUB_REPOSITORY="camster91/Rclicker" \
    RELEASE_VERSION="${RELEASE_VERSION:-0.2.5}" \
    SIGNED="${SIGNED:-false}" \
    MSIX_SIGNED="${MSIX_SIGNED:-false}" \
    DIST_DIR="$DIST_DIR" \
    "$publisher"
}

expect_failure() {
  local expected="$1"
  shift
  local output
  if output="$("$@" 2>&1)"; then
    echo "Expected failure containing '$expected'." >&2
    exit 1
  fi
  if [[ "$output" != *"$expected"* ]]; then
    echo "Failure did not contain '$expected': $output" >&2
    exit 1
  fi
}

# An absent signed MSIX must stop the actual publisher before any GitHub write.
DIST_DIR="$workdir/missing-msix"
mkdir -p "$DIST_DIR"
printf 'signed-exe' > "$DIST_DIR/rclicker.exe"
GITHUB_REF='refs/heads/main' GITHUB_SHA='new-source-sha' SIGNED=true MSIX_SIGNED=true \
  FAKE_GH_MODE='' FAKE_GH_LOG="$workdir/missing-msix.log" \
  expect_failure 'signed MSIX artifact is missing' run_publisher

# An existing stable release with the old version is rejected instead of being clobbered.
DIST_DIR="$workdir/old-stable"
mkdir -p "$DIST_DIR"
printf 'signed-exe' > "$DIST_DIR/rclicker.exe"
printf 'signed-msix' > "$DIST_DIR/rclicker-x64.msix"
GITHUB_REF='refs/heads/main' GITHUB_SHA='new-source-sha' SIGNED=true MSIX_SIGNED=true \
  FAKE_GH_MODE='stable-mismatch' FAKE_GH_LOG="$workdir/old-stable.log" \
  expect_failure 'mismatched name, tag, or prerelease state' run_publisher

# Preview creation remains portable-only.
DIST_DIR="$workdir/preview-create"
mkdir -p "$DIST_DIR"
printf 'preview-exe' > "$DIST_DIR/rclicker.exe"
GITHUB_REF='refs/heads/claude/example' GITHUB_SHA='preview-sha' SIGNED=false MSIX_SIGNED=false \
  FAKE_GH_MODE='' FAKE_GH_LOG="$workdir/preview-create.log" \
  run_publisher
grep -Fq -- 'release create v0.2.5-preview' "$workdir/preview-create.log"
grep -Fq -- '--prerelease' "$workdir/preview-create.log"

# Preview re-runs retain their replaceable assets.
DIST_DIR="$workdir/preview-existing"
mkdir -p "$DIST_DIR"
printf 'preview-exe' > "$DIST_DIR/rclicker.exe"
GITHUB_REF='refs/heads/claude/example' GITHUB_SHA='preview-sha-2' SIGNED=false MSIX_SIGNED=false \
  FAKE_GH_MODE='preview-existing' FAKE_GH_LOG="$workdir/preview-existing.log" \
  run_publisher
grep -Fq -- 'release upload v0.2.5-preview' "$workdir/preview-existing.log"
grep -Fq -- '--clobber' "$workdir/preview-existing.log"

echo 'Release publisher guard scenarios passed: missing MSIX, stable mismatch rejection, preview create, preview replacement.'
