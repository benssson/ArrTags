#!/usr/bin/env bash
#
# ArrTags release orchestrator.
#
# Builds/tests/packages the plugin, generates the Jellyfin plugin-repository
# manifest.json, commits and tags it, and creates the GitHub release with the
# packaged asset.
#
# Usage:
#   scripts/publish-release.sh [--dry-run | --prepare-only | --release-only]
#                              [--skip-build] [--skip-tests] [--no-push]
#                              [--force] [--tag <tag>] [--repo <owner/name>]
#
# Modes (default: prepare the manifest/tag and publish the GitHub release):
#   --dry-run       Build/test/package, compute checksums, write manifest.json and
#                   print the intended actions. No commit, tag, push, or GitHub call.
#   --prepare-only  Generate manifest.json, commit it, create the annotated tag and
#                   push. No GitHub call.
#   --release-only  Push the existing branch/tag (unless --no-push), then create/
#                   refresh the GitHub release and upload the asset. No build,
#                   manifest, commit, or tag; the tag must already exist locally.
#
# Flags:
#   --skip-build    Skip ./build.sh restore/build/package.
#   --skip-tests    Skip ./build.sh test.
#   --no-push       Do not push the current branch or the tag.
#   --force         Move an existing tag to HEAD.
#   --tag <tag>     Override the derived tag (default: v<version without one trailing .0>).
#   --repo <o/n>    GitHub repository (default: benssson/ArrTags).
#
# Exit codes: 0 success, 1 error, 2 usage error.

set -euo pipefail

REPO="benssson/ArrTags"
DRY_RUN=0
PREPARE_ONLY=0
RELEASE_ONLY=0
SKIP_BUILD=0
SKIP_TESTS=0
NO_PUSH=0
FORCE=0
TAG_OVERRIDE=""

usage() {
    cat <<'EOF'
Usage:
  scripts/publish-release.sh [--dry-run | --prepare-only | --release-only]
                             [--skip-build] [--skip-tests] [--no-push]
                             [--force] [--tag <tag>] [--repo <owner/name>]

Modes (default: prepare the manifest/tag and publish the GitHub release):
  --dry-run       Build/test/package, compute checksums, write manifest.json and
                  print the intended actions. No commit, tag, push, or GitHub call.
  --prepare-only  Generate manifest.json, commit it, create the annotated tag and
                  push. No GitHub call.
  --release-only  Push the existing branch/tag (unless --no-push), then create/
                  refresh the GitHub release and upload the asset. No build,
                  manifest, commit, or tag; the tag must already exist locally.

Flags:
  --skip-build    Skip ./build.sh restore/build/package.
  --skip-tests    Skip ./build.sh test.
  --no-push       Do not push the current branch or the tag.
  --force         Move an existing tag to HEAD.
  --tag <tag>     Override the derived tag.
  --repo <o/n>    GitHub repository (default: benssson/ArrTags).
EOF
}

while [ $# -gt 0 ]; do
    case "$1" in
        --dry-run) DRY_RUN=1 ;;
        --prepare-only) PREPARE_ONLY=1 ;;
        --release-only) RELEASE_ONLY=1 ;;
        --skip-build) SKIP_BUILD=1 ;;
        --skip-tests) SKIP_TESTS=1 ;;
        --no-push) NO_PUSH=1 ;;
        --force) FORCE=1 ;;
        --tag)
            [ $# -ge 2 ] || { echo "ERROR: --tag requires a value" >&2; exit 2; }
            TAG_OVERRIDE="$2"; shift ;;
        --repo)
            [ $# -ge 2 ] || { echo "ERROR: --repo requires a value" >&2; exit 2; }
            REPO="$2"; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "ERROR: unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
    shift
done

if [ $((DRY_RUN + PREPARE_ONLY + RELEASE_ONLY)) -gt 1 ]; then
    echo "ERROR: choose at most one of --dry-run, --prepare-only, --release-only" >&2
    exit 2
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
cd "$repo_root"

if [ -f /config/arrtags-env.sh ]; then
    # shellcheck source=/dev/null
    . /config/arrtags-env.sh
fi

yaml_scalar() {
    sed -n "s/^$1:[[:space:]]*\"\(.*\)\"[[:space:]]*\$/\1/p" build.yaml | head -n1
}

version="$(yaml_scalar version)"
target_abi="$(yaml_scalar targetAbi)"
guid="$(yaml_scalar guid)"
if [ -z "$version" ] || [ -z "$target_abi" ] || [ -z "$guid" ]; then
    echo "ERROR: could not read version, targetAbi, or guid from build.yaml" >&2
    exit 1
fi

default_tag="v${version%.0}"
tag="${TAG_OVERRIDE:-$default_tag}"
asset_name="ArrTags_${version}.zip"
artifact="artifacts/${asset_name}"
source_url="https://github.com/${REPO}/releases/download/${tag}/${asset_name}"
repository_url="https://raw.githubusercontent.com/${REPO}/main/manifest.json"

do_prepare=1
do_release=1
if [ "$DRY_RUN" -eq 1 ]; then
    do_release=0
elif [ "$PREPARE_ONLY" -eq 1 ]; then
    do_release=0
elif [ "$RELEASE_ONLY" -eq 1 ]; then
    do_prepare=0
fi

echo "==> ArrTags release: version ${version}, targetAbi ${target_abi}, tag ${tag}"
echo "    repository: ${REPO}"

md5=""
sha256=""

if [ "$do_prepare" -eq 1 ]; then
    if [ "$SKIP_BUILD" -eq 0 ]; then
        echo "==> Restoring"
        ./build.sh restore
        echo "==> Building"
        ./build.sh build
    fi

    if [ "$SKIP_TESTS" -eq 0 ]; then
        echo "==> Testing"
        ./build.sh test
    fi

    if [ "$SKIP_BUILD" -eq 0 ]; then
        echo "==> Packaging"
        ./build.sh package
    fi

    if [ ! -f "$artifact" ]; then
        echo "ERROR: release artifact not found: $artifact" >&2
        echo "       Run without --skip-build to produce it." >&2
        exit 1
    fi

    sha256="$(sha256sum "$artifact" | awk '{print $1}')"
    md5="$(md5sum "$artifact" | awk '{print $1}')"
    timestamp="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

    echo "==> Artifact: ${artifact}"
    echo "    sha256: ${sha256}"
    echo "    md5:    ${md5}"

    echo "==> Writing manifest.json"
    dotnet run scripts/write-manifest.cs -- \
        --build-yaml build.yaml \
        --manifest manifest.json \
        --checksum "$md5" \
        --source-url "$source_url" \
        --timestamp "$timestamp" \
        --output manifest.json
fi

if [ "$do_prepare" -eq 1 ] && [ "$DRY_RUN" -eq 0 ]; then
    echo "==> Committing manifest.json"
    git add manifest.json
    if git diff --cached --quiet -- manifest.json; then
        echo "    manifest.json is unchanged; nothing to commit"
    else
        git commit -m "Publish ArrTags ${version} repository manifest" -- manifest.json
    fi

    echo "==> Annotated tag ${tag}"
    if git rev-parse -q --verify "refs/tags/${tag}" >/dev/null 2>&1; then
        tag_commit="$(git rev-list -n 1 "$tag")"
        head_commit="$(git rev-parse HEAD)"
        if [ "$tag_commit" = "$head_commit" ]; then
            echo "    tag ${tag} already exists at HEAD"
        elif [ "$FORCE" -eq 1 ]; then
            echo "    moving existing tag ${tag} to HEAD (--force)"
            git tag -f -a "$tag" -m "ArrTags ${version}"
        else
            echo "ERROR: tag ${tag} already exists at ${tag_commit} but HEAD is ${head_commit}" >&2
            echo "       Re-run with --force to move the tag." >&2
            exit 1
        fi
    else
        git tag -a "$tag" -m "ArrTags ${version}"
    fi

    if [ "$NO_PUSH" -eq 0 ]; then
        branch="$(git rev-parse --abbrev-ref HEAD)"
        echo "==> Pushing ${branch} and ${tag}"
        git push origin "$branch"
        git push origin "$tag"
    else
        echo "==> --no-push: skipping git push"
    fi
fi

if [ "$DRY_RUN" -eq 1 ]; then
    echo
    echo "==> Dry run complete; no commit, tag, push, or GitHub call was made."
    echo "    Would commit manifest.json"
    echo "    Would create annotated tag ${tag}"
    if [ "$NO_PUSH" -eq 1 ]; then
        echo "    Would NOT push (--no-push)"
    else
        echo "    Would push the current branch and ${tag}"
    fi
    echo "    Would create/refresh the GitHub release ${tag} and upload ${asset_name}"
    echo "    Repository URL: ${repository_url}"
    exit 0
fi

if [ "$do_release" -eq 1 ]; then
    if [ ! -f manifest.json ]; then
        echo "ERROR: manifest.json not found; run --prepare-only (or the default mode) first." >&2
        exit 1
    fi

    extract_checksum() {
        if command -v jq >/dev/null 2>&1; then
            jq -r --arg guid "$guid" --arg version "$version" \
                '.[] | select(.guid==$guid) | .versions[] | select(.version==$version) | .checksum' \
                manifest.json | head -n1
        else
            sed -n 's/.*"checksum":[[:space:]]*"\([^"]*\)".*/\1/p' manifest.json | head -n1
        fi
    }

    manifest_checksum="$(extract_checksum)"
    if [ -z "$manifest_checksum" ]; then
        echo "ERROR: manifest.json has no checksum for version ${version}." >&2
        exit 1
    fi

    changelog="$(dotnet run scripts/write-manifest.cs -- --print-changelog --build-yaml build.yaml)"

    tmp_dir="$(mktemp -d)"
    trap 'rm -rf "$tmp_dir"' EXIT
    downloaded="$tmp_dir/$asset_name"

    # In release-only mode the branch and tag must already exist locally (created
    # by the prepare step), so push them before publishing the GitHub release;
    # otherwise the release would point at a commit the remote does not have.
    if [ "$RELEASE_ONLY" -eq 1 ]; then
        if ! git rev-parse -q --verify "refs/tags/${tag}" >/dev/null 2>&1; then
            echo "ERROR: tag ${tag} does not exist locally; create it (for example with --prepare-only) before --release-only." >&2
            exit 1
        fi

        if [ "$NO_PUSH" -eq 0 ]; then
            branch="$(git rev-parse --abbrev-ref HEAD)"
            echo "==> Pushing ${branch} and existing tag ${tag} before publishing the release"
            git push origin "$branch"
            git push origin "$tag"
        else
            echo "==> --no-push: skipping push of the existing branch/tag before the release"
        fi
    fi

    if command -v gh >/dev/null 2>&1; then
        if gh release view "$tag" --repo "$REPO" >/dev/null 2>&1; then
            echo "==> GitHub release ${tag} exists; refreshing title and notes"
            gh release edit "$tag" --repo "$REPO" --title "ArrTags ${version}" --notes "$changelog"
        else
            echo "==> Creating GitHub release ${tag}"
            gh release create "$tag" --repo "$REPO" --title "ArrTags ${version}" --notes "$changelog"
        fi

        echo "==> Uploading ${asset_name}"
        gh release upload "$tag" "$artifact" --repo "$REPO" --clobber

        echo "==> Verifying uploaded asset checksum"
        gh release download "$tag" --repo "$REPO" --pattern "$asset_name" --dir "$tmp_dir" --clobber
    elif [ -n "${GITHUB_TOKEN:-}${GH_TOKEN:-}" ]; then
        token="${GITHUB_TOKEN:-${GH_TOKEN:-}}"

        if ! command -v jq >/dev/null 2>&1; then
            echo "ERROR: the GitHub REST API fallback requires 'jq'." >&2
            exit 1
        fi

        api_request() {
            local method="$1"
            local url="$2"
            local data="${3:-}"
            if [ -n "$data" ]; then
                curl -fsSL -X "$method" \
                    -H "Authorization: Bearer ${token}" \
                    -H "Accept: application/vnd.github+json" \
                    -H "X-GitHub-Api-Version: 2022-11-28" \
                    -d "$data" "$url"
            else
                curl -fsSL -X "$method" \
                    -H "Authorization: Bearer ${token}" \
                    -H "Accept: application/vnd.github+json" \
                    -H "X-GitHub-Api-Version: 2022-11-28" \
                    "$url"
            fi
        }

        release_payload="$(jq -n \
            --arg tag "$tag" \
            --arg name "ArrTags ${version}" \
            --arg body "$changelog" \
            '{tag_name: $tag, name: $name, body: $body, draft: false, prerelease: false}')"

        release_json="$(api_request GET "https://api.github.com/repos/${REPO}/releases/tags/${tag}" || true)"
        if [ -n "$release_json" ]; then
            release_id="$(printf '%s' "$release_json" | jq -r '.id')"
            echo "==> GitHub release ${tag} exists; refreshing title and notes"
            api_request PATCH "https://api.github.com/repos/${REPO}/releases/${release_id}" "$release_payload" >/dev/null
        else
            echo "==> Creating GitHub release ${tag}"
            release_json="$(api_request POST "https://api.github.com/repos/${REPO}/releases" "$release_payload")"
            release_id="$(printf '%s' "$release_json" | jq -r '.id')"
        fi

        if [ -z "$release_id" ] || [ "$release_id" = "null" ]; then
            echo "ERROR: could not determine the GitHub release id for ${tag}." >&2
            exit 1
        fi

        echo "==> Uploading ${asset_name}"
        curl -fsSL -X POST \
            -H "Authorization: Bearer ${token}" \
            -H "Content-Type: application/zip" \
            --data-binary "@${artifact}" \
            "https://uploads.github.com/repos/${REPO}/releases/${release_id}/assets?name=${asset_name}" >/dev/null

        echo "==> Verifying uploaded asset checksum"
        release_json="$(api_request GET "https://api.github.com/repos/${REPO}/releases/${release_id}")"
        asset_url="$(printf '%s' "$release_json" | jq -r --arg n "$asset_name" '.assets[] | select(.name==$n) | .url')"
        if [ -z "$asset_url" ] || [ "$asset_url" = "null" ]; then
            echo "ERROR: uploaded asset ${asset_name} was not found on release ${tag}." >&2
            exit 1
        fi
        curl -fsSL \
            -H "Authorization: Bearer ${token}" \
            -H "Accept: application/octet-stream" \
            -o "$downloaded" \
            "$asset_url"
    else
        echo "ERROR: cannot publish the GitHub release: neither the 'gh' CLI nor GITHUB_TOKEN/GH_TOKEN is available." >&2
        echo "       The GitHub release and asset upload are your manual step:" >&2
        echo "       create release ${tag} in ${REPO} and upload ${artifact} as ${asset_name}." >&2
        exit 1
    fi

    downloaded_md5="$(md5sum "$downloaded" | awk '{print $1}')"
    if [ "$downloaded_md5" != "$manifest_checksum" ]; then
        echo "ERROR: uploaded asset MD5 ${downloaded_md5} does not match the manifest checksum ${manifest_checksum}." >&2
        exit 1
    fi
    echo "    verified md5: ${downloaded_md5}"

    echo
    echo "==> Release complete"
    echo "    tag:        ${tag}"
    echo "    asset:      ${asset_name}"
    echo "    md5:        ${manifest_checksum}"
    echo "    repository: ${repository_url}"
fi
