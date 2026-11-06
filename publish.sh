#!/bin/bash
# Publishes a release in one go (from the Mac, because the signing key lives there):
#   1. tags the commit; GitHub Actions builds and tests the app on Windows and attaches the zip to a draft release
#   2. downloads that zip, signs it with the private key, and uploads the signature
#   3. publishes the release with the notes
#
#   ./publish.sh <version> <release-notes.md>        e.g. ./publish.sh 1.0.0 release-notes/1.0.0.md
#
# "{{SHA256}}" in the notes file is replaced with the zip's checksum. Commit your changes first.
set -e
cd "$(dirname "$0")"
VERSION="$1"; NOTES="$2"
[ -n "$VERSION" ] && [ -f "$NOTES" ] || { echo "usage: ./publish.sh <version> <release-notes.md>"; exit 1; }
REPO=hafizuddin-a/bismillah-startup
KEY="${BISMILLAH_SIGNING_KEY:-$HOME/.config/bismillah-windows/update-signing.key}"
[ -f "$KEY" ] || { echo "No signing key at $KEY"; exit 1; }
[ -z "$(git status --porcelain --untracked-files=no)" ] || { echo "Commit your changes first."; exit 1; }
grep -q "<Version>$VERSION</Version>" Directory.Build.props || { echo "Directory.Build.props doesn't say Version $VERSION."; exit 1; }

# network calls are retried, and a re-run after an interruption carries on from where it stopped
retry() { local n; for n in 1 2 3 4 5 6; do "$@" && return 0; echo "(network problem, retrying in 10 s)" >&2; sleep 10; done; return 1; }

dotnet test Bismillah.sln --configuration Release --verbosity quiet
if git rev-parse -q --verify "refs/tags/v$VERSION" >/dev/null; then
  echo "v$VERSION is already tagged; carrying on."
  retry git push origin "v$VERSION"
else
  echo "Tagging v$VERSION..."
  retry git push origin main
  git tag "v$VERSION"
  retry git push origin "v$VERSION"
fi

echo "Waiting for GitHub to build the release..."
sleep 10
RUN=""
for n in 1 2 3 4 5 6; do
  RUN=$(gh run list --repo "$REPO" --workflow release.yml --branch "v$VERSION" --limit 1 --json databaseId -q '.[0].databaseId' 2>/dev/null) && [ -n "$RUN" ] && break
  sleep 10
done
[ -n "$RUN" ] || { echo "Couldn't find the release build for v$VERSION."; exit 1; }
for n in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
  STATUS=$(gh run view "$RUN" --repo "$REPO" --json status,conclusion -q '.status+" "+.conclusion' 2>/dev/null) || { sleep 15; continue; }
  case "$STATUS" in
    "completed success") break ;;
    completed*) echo "The release build failed: gh run view $RUN --repo $REPO --log"; exit 1 ;;
  esac
  sleep 15
done
[ "$STATUS" = "completed success" ] || { echo "The release build is still running: gh run watch $RUN --repo $REPO"; exit 1; }

WORK=$(mktemp -d)
retry gh release download "v$VERSION" --repo "$REPO" --pattern '*.zip' --dir "$WORK"
ZIP="$WORK/Bismillah-$VERSION-win-x64.zip"
[ -f "$ZIP" ] || { echo "The zip isn't on the draft release."; exit 1; }
swift tools/update_key.swift sign "$KEY" "$ZIP" > "$ZIP.sig"
SHA=$(shasum -a 256 "$ZIP" | awk '{print $1}')
sed "s/{{SHA256}}/$SHA/g" "$NOTES" > "$WORK/notes.md"
retry gh release upload "v$VERSION" "$ZIP.sig" --repo "$REPO" --clobber
retry gh release edit "v$VERSION" --repo "$REPO" --notes-file "$WORK/notes.md" --draft=false --latest
echo "Done: https://github.com/$REPO/releases/tag/v$VERSION"
