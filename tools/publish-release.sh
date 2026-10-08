#!/usr/bin/env bash
set -euo pipefail

: "${GITHUB_REF:?GITHUB_REF is required}"
: "${GITHUB_SHA:?GITHUB_SHA is required}"
: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"
: "${RELEASE_VERSION:?RELEASE_VERSION is required}"

DIST_DIR="${DIST_DIR:-dist}"
SIGNED="${SIGNED:-false}"
MSIX_SIGNED="${MSIX_SIGNED:-false}"
MAIN_REF="refs/heads/main"

if [ ! -f "$DIST_DIR/rclicker.exe" ]; then
  echo "::error::The release executable is missing from $DIST_DIR." >&2
  exit 1
fi

if [ "$GITHUB_REF" = "$MAIN_REF" ]; then
  if [ "$SIGNED" != "true" ]; then
    echo "::error::Main publication requires the signed executable artifact." >&2
    exit 1
  fi
  if [ "$MSIX_SIGNED" != "true" ]; then
    echo "::error::Main publication requires the signed MSIX artifact." >&2
    exit 1
  fi
  if [ ! -f "$DIST_DIR/rclicker-x64.msix" ]; then
    echo "::error::The signed MSIX artifact is missing from $DIST_DIR." >&2
    exit 1
  fi
  mv "$DIST_DIR/rclicker-x64.msix" "$DIST_DIR/rclicker-win-x64.msix"
fi

tag="v$RELEASE_VERSION"
flags=()
if [ "$GITHUB_REF" != "$MAIN_REF" ]; then
  tag="v$RELEASE_VERSION-preview"
  flags=(--prerelease)
fi

if [ "$GITHUB_REF" = "$MAIN_REF" ] && [ "$SIGNED" = "true" ]; then
  signing="Code-signed: right-click → Properties → Digital Signatures shows who signed it."
else
  signing="Not code-signed yet: if Windows warns, choose More info → Run anyway."
fi
notes="Download **rclicker.exe** for the portable app, or **rclicker-win-x64.msix** for managed Windows installation. Scan the QR code with your phone. Works over the internet through the rclicker relay (no shared Wi-Fi needed). $signing"

(cd "$DIST_DIR" && zip -9 -j rclicker-win-x64.zip rclicker.exe)

if [ "$GITHUB_REF" = "$MAIN_REF" ]; then
  (cd "$DIST_DIR" && sha256sum rclicker.exe rclicker-win-x64.zip rclicker-win-x64.msix > SHA256SUMS.txt)
fi

release_json=""
if release_json="$(gh release view "$tag" --json tagName,name,isPrerelease 2>/dev/null)"; then
  release_exists=true
else
  release_exists=false
fi

if [ "$GITHUB_REF" = "$MAIN_REF" ]; then
  tag_ref_json=""
  if tag_ref_json="$(gh api "repos/$GITHUB_REPOSITORY/git/ref/tags/$tag" 2>/dev/null)"; then
    tag_sha="$(jq -r '.object.sha' <<<"$tag_ref_json")"
    tag_type="$(jq -r '.object.type' <<<"$tag_ref_json")"
    if [ "$tag_type" = "tag" ]; then
      tag_sha="$(gh api "repos/$GITHUB_REPOSITORY/git/tags/$tag_sha" --jq '.object.sha')"
    fi
    if [ "$tag_sha" != "$GITHUB_SHA" ]; then
      echo "::error::Stable tag $tag points to $tag_sha, expected source $GITHUB_SHA; refusing to replace it." >&2
      exit 1
    fi
  elif [ "$release_exists" = true ]; then
    echo "::error::Stable release $tag exists but its Git tag cannot be resolved; refusing to replace it." >&2
    exit 1
  fi

  if [ "$release_exists" = true ]; then
    release_tag="$(jq -r '.tagName' <<<"$release_json")"
    release_name="$(jq -r '.name' <<<"$release_json")"
    release_prerelease="$(jq -r '.isPrerelease' <<<"$release_json")"
    if [ "$release_tag" != "$tag" ] || [ "$release_name" != "rclicker $RELEASE_VERSION" ] || [ "$release_prerelease" != "false" ]; then
      echo "::error::Stable release $tag has a mismatched name, tag, or prerelease state; refusing to replace it." >&2
      exit 1
    fi
    echo "::notice::Stable release $tag already matches source $GITHUB_SHA and version $RELEASE_VERSION; leaving its assets unchanged."
    exit 0
  fi

  gh release create "$tag" "$DIST_DIR/rclicker.exe" "$DIST_DIR/rclicker-win-x64.zip" "$DIST_DIR/rclicker-win-x64.msix" "$DIST_DIR/SHA256SUMS.txt" \
    --target "$GITHUB_SHA" --title "rclicker $RELEASE_VERSION" --notes "$notes"
  gh release edit "$tag" --prerelease=false --latest
  exit 0
fi

# claude/* preview releases retain their replaceable pre-release behavior.
if [ "$release_exists" = true ]; then
  gh release upload "$tag" "$DIST_DIR/rclicker.exe" "$DIST_DIR/rclicker-win-x64.zip" --clobber
  gh release edit "$tag" --notes "$notes"
else
  gh release create "$tag" "$DIST_DIR/rclicker.exe" "$DIST_DIR/rclicker-win-x64.zip" \
    --target "$GITHUB_SHA" --title "rclicker $RELEASE_VERSION preview" "${flags[@]}" --notes "$notes"
fi
