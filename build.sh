#!/usr/bin/env bash
# Builds EasyGred inside a throwaway .NET SDK container - nothing is installed on the host.
#
#   ./build.sh            portable builds for Linux and Windows -> dist/
#   ./build.sh linux      Linux x64 only
#   ./build.sh windows    Windows x64 only
#   ./build.sh test       run the unit tests
#
# The build image (easygred-build) is created from docker/build.Dockerfile on first run.
# NuGet packages are cached in the Docker volume "easygred-nuget" (remove with: docker volume rm easygred-nuget).
set -euo pipefail
cd "$(dirname "$0")"

IMAGE=easygred-build
docker image inspect "$IMAGE" >/dev/null 2>&1 || docker build -t "$IMAGE" -f docker/build.Dockerfile docker
TARGET=${1:-all}
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/EasyGred/EasyGred.csproj)

publish() { # $1 = runtime id
  local out="EasyGred-$VERSION-$1"
  echo "rm -rf dist/$out dist/$out.zip && dotnet publish src/EasyGred -c Release -r $1 --self-contained true -o dist/$out && (cd dist && zip -qr $out.zip $out) &&"
}

case "$TARGET" in
  test)    SCRIPT="dotnet test tests/EasyGred.Tests -c Release &&" ;;
  linux)   SCRIPT="$(publish linux-x64)" ;;
  windows) SCRIPT="$(publish win-x64)" ;;
  all)     SCRIPT="$(publish linux-x64) $(publish win-x64)" ;;
  *) echo "usage: $0 [all|linux|windows|test]"; exit 1 ;;
esac

docker run --rm \
  -v "$PWD":/src -w /src \
  -v easygred-nuget:/root/.nuget/packages \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  "$IMAGE" bash -c "$SCRIPT true; rc=\$?; chown -R $(id -u):$(id -g) /src/dist /src/src /src/tests 2>/dev/null; exit \$rc"

if [[ "$TARGET" != test ]]; then
  echo
  echo "Portable builds:"
  ls -sh dist/EasyGred-"$VERSION"-*.zip 2>/dev/null
fi
