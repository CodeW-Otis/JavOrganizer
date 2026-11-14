#!/usr/bin/env bash
#
# Builds the JavOrganizer plugin on macOS and Linux (the bash twin of
# build.ps1). Produces one publish folder per build under ./dist/ plus one
# release zip per Jellyfin version line, each with the .meta sidecar and a
# .sha256 checksum — identical layout to the PowerShell script.
#
#   ./build.sh                 # build both lines (10.8 and 10.9+)
#   ./build.sh 10.9            # build only the 10.9-and-later build
#   ./build.sh 10.9 Debug      # ...in Debug configuration
#
# Requires: the .NET SDK (6.0 for the 10.8 line, 8.0 for 10.9+), zip, and
# sha256sum (Linux) or shasum (macOS).

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dist="$root/dist"
csproj="$root/Jellyfin.Plugin.JavOrganizer.csproj"

plugin_guid='e9e8bfe2-5e0f-4d1a-9a3c-7b7ea4e7581c'
plugin_name='JavOrganizer'
plugin_version='1.5.5.0'

requested="${1:-}"
configuration="${2:-Release}"

if [[ -n "$requested" && "$requested" != "10.8" && "$requested" != "10.9" ]]; then
    echo "Usage: ./build.sh [10.8|10.9] [Release|Debug]" >&2
    exit 2
fi
if [[ "$configuration" != "Release" && "$configuration" != "Debug" ]]; then
    echo "Configuration must be Release or Debug." >&2
    exit 2
fi

# Every Jellyfin version line, the build that serves it and the targetAbi
# that build advertises for the line: slug|abi|build.
lines='108|10.8.13.0|10.8
109|10.9.11.0|10.9
1010|10.10.7.0|10.9
1011|10.11.11.0|10.9
120|12.0.0.0|10.9
121|12.1.0.0|10.9
122|12.2.0.0|10.9
123|12.3.0.0|10.9
124|12.4.0.0|10.9'

command -v dotnet >/dev/null 2>&1 || { echo "dotnet SDK not found on PATH." >&2; exit 1; }
command -v zip >/dev/null 2>&1 || { echo "zip not found on PATH (install it: apt install zip / brew install zip)." >&2; exit 1; }

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{print $1}'
    else
        shasum -a 256 "$1" | awk '{print $1}'
    fi
}

# write_meta <file> <targetAbi>
write_meta() {
    local now
    now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf '{"guid":"%s","name":"%s","version":"%s","targetAbi":"%s","source":"%s","status":"Active","autoUpdate":false,"installDate":"%s"}\n' \
        "$plugin_guid" "$plugin_name" "$plugin_version" "$2" "$plugin_name" "$now" > "$1"
}

build_target() {
    local v="$1"
    local out="$dist/$plugin_name-$v"
    echo ""
    echo "=== Building JavOrganizer for Jellyfin $v ==="

    dotnet publish "$csproj" -c "$configuration" -p:JellyfinVersion="$v" -o "$out"

    # Meta sidecar for the publish folder itself (first line of this build).
    local first_abi
    first_abi="$(printf '%s\n' "$lines" | awk -F'|' -v b="$v" '$3==b {print $2; exit}')"
    write_meta "$out/Jellyfin.Plugin.JavOrganizer.dll.meta" "$first_abi"

    # One release zip per version line this build serves.
    while IFS='|' read -r slug abi build; do
        [[ "$build" == "$v" ]] || continue
        local stage="$dist/_stage-$slug"
        rm -rf "$stage"
        mkdir -p "$stage"
        cp -R "$out/." "$stage/"
        write_meta "$stage/Jellyfin.Plugin.JavOrganizer.dll.meta" "$abi"

        local zip="$dist/$plugin_name-$slug-v$plugin_version.zip"
        rm -f "$zip"
        (cd "$stage" && zip -q -r "$zip" .)
        rm -rf "$stage"

        local hash
        hash="$(sha256_of "$zip")"
        printf '%s\n' "$hash" > "$zip.sha256"
        echo "OK   $out"
        echo "ZIP  $zip  (targetAbi $abi)"
        echo "SHA256 $hash"
    done <<< "$lines"
}

if [[ -n "$requested" ]]; then
    targets="$requested"
else
    targets='10.8
10.9'
fi

while IFS= read -r v; do
    [[ -n "$v" ]] || continue
    build_target "$v"
done <<< "$targets"

echo ""
echo "All requested targets built into $dist"