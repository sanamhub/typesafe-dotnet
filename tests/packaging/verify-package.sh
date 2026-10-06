#!/usr/bin/env bash
# Consumes the packed packages the way a user would, from projects with no reference to this
# repository's source: NativeAOT on Linux and macOS (AC-4.3), C# 7.3 on .NET Framework on
# Windows (AC-4.4), and an ASP.NET Core app with AddTypeSafe under NativeAOT. The two console
# consumers make one call each to a loopback stub with a fake key.
#
#   tests/packaging/verify-package.sh                       pack, then run the consumer for this OS
#   tests/packaging/verify-package.sh --packages <dir>      use the .nupkg files already in <dir>
#   tests/packaging/verify-package.sh --consumer Aot        run one consumer regardless of OS
#
# Consumers: Aot (NativeAOT console, one call), AotWeb (ASP.NET Core with AddTypeSafe, publish
# only), NetFx (.NET Framework 4.8.1 at C# 7.3, one call).
set -euo pipefail

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
PACKAGES=""
CONSUMERS=()

while [ $# -gt 0 ]; do
  case "$1" in
    --packages) PACKAGES="$2"; shift 2 ;;
    --consumer) CONSUMERS+=("$2"); shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [ ${#CONSUMERS[@]} -eq 0 ]; then
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) CONSUMERS=(NetFx) ;;
    *) CONSUMERS=(Aot AotWeb) ;;
  esac
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Under Git Bash, .NET reads an MSYS path like /tmp/x as C:\tmp\x. Anything handed to a .NET
# tool needs the native form.
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else echo "$1"; fi; }

if [ -z "$PACKAGES" ]; then
  # A version nuget.org will never have, so a cached copy of a real release cannot stand in.
  PACKAGES="$WORK/packages"
  dotnet pack "$(native "$REPO/TypeSafeSharp.slnx")" -c Release -o "$(native "$PACKAGES")" \
    -p:Version="0.0.0-ci.${GITHUB_RUN_NUMBER:-local}" --verbosity quiet
fi
PACKAGES="$(cd "$PACKAGES" && pwd)"

# Glob and index, with no pipe for head to close early under pipefail.
FOUND=("$PACKAGES"/TypeSafeSharp.[0-9]*.nupkg)
[ -f "${FOUND[0]}" ] || { echo "no TypeSafeSharp package in $PACKAGES" >&2; exit 1; }
VERSION="$(basename "${FOUND[0]}" .nupkg)"
VERSION="${VERSION#TypeSafeSharp.}"
echo "consuming TypeSafeSharp $VERSION from $(native "$PACKAGES")"

# A fresh package cache, so a stale TypeSafeSharp from an earlier run can never be used.
export NUGET_PACKAGES="$(native "$WORK/nuget-cache")"

# The consumers are copied out of the repository so nothing from its root applies:
# Directory.Build.props, central versions, NuGet.Config, .editorconfig, the signing key.
cp -R "$REPO/tests/packaging/consumers" "$WORK/consumers"

# TypeSafeSharp may only come from the folder under test; everything else only from nuget.org.
cat > "$WORK/consumers/NuGet.Config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$(native "$PACKAGES")" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="TypeSafeSharp" />
      <package pattern="TypeSafeSharp.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
XML

# Out of the repository, so its global.json does not pick the SDK for the consumers either.
cd "$WORK/consumers"

for consumer in "${CONSUMERS[@]}"; do
  project="$(native "$WORK/consumers/$consumer/$consumer.csproj")"
  out="$WORK/out/$consumer"
  echo "== $consumer"
  case "$consumer" in
    Aot)
      dotnet publish "$project" -c Release -o "$(native "$out")" -p:TypeSafeSharpVersion="$VERSION" --verbosity quiet
      "$out/Aot"
      ;;
    AotWeb)
      dotnet publish "$project" -c Release -o "$(native "$out")" -p:TypeSafeSharpVersion="$VERSION" --verbosity quiet
      echo "PASS (AotWeb publish)"
      ;;
    NetFx)
      dotnet build "$project" -c Release -o "$(native "$out")" -p:TypeSafeSharpVersion="$VERSION" --verbosity quiet
      "$out/NetFx.exe"
      ;;
    *) echo "unknown consumer: $consumer" >&2; exit 2 ;;
  esac
done
