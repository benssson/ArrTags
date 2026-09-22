#!/usr/bin/env bash
set -euo pipefail

# Deterministic ArrTags restore/build/test/package entry point.
#
# Usage:
#   ./build.sh [restore|build|test|package|all]
#
# Prerequisites:
#   .NET SDK 10.0.x (pinned by global.json).

configuration="${CONFIGURATION:-Release}"
solution="ArrTags.slnx"
plugin_project="src/ArrTags/ArrTags.csproj"
package_version="$(grep -oP '^version:\s*"\K[^"]+' build.yaml)"
package_output="artifacts/ArrTags_${package_version}.zip"
package_staging="artifacts/staging"
package_tool="scripts/pack-release.cs"

restore() {
    dotnet restore "$solution" --locked-mode
}

build() {
    dotnet build "$solution" --configuration "$configuration" --no-restore
}

test() {
    dotnet test "$solution" --configuration "$configuration" --no-build
}

package() {
    # The MSBuild PackagePlugin target stages the exact release files; the
    # deterministic packer then writes the archive with sorted entries and a
    # fixed timestamp so the artifact has a stable SHA-256 across clean builds.
    rm -rf "$package_staging" "$package_output"
    dotnet build "$plugin_project" \
        --configuration "$configuration" \
        --no-restore \
        -p:PackagePlugin=true
    dotnet run "$package_tool" -- \
        --source "$package_staging" \
        --output "$package_output"
    rm -rf "$package_staging"
}

case "${1:-all}" in
    restore) restore ;;
    build) build ;;
    test) test ;;
    package) package ;;
    all)
        restore
        build
        test
        package
        ;;
    *)
        echo "Unknown target: $1" >&2
        exit 1
        ;;
esac
