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

dotnet test Bismillah.sln --configuration Release --verbosity quiet
echo "Tagging v$VERSION..."
git push origin main
git tag "v$VERSION"
git push origin "v$VERSION"

echo "Waiting for GitHub to build the release..."
sleep 10
RUN=$(gh run list --repo "$REPO" --workflow release.yml --limit 1 --json databaseId -q '.[0].databaseId')
gh run watch "$RUN" --repo "$REPO" --exit-status >/dev/null || { echo "The release build failed: gh run view $RUN --repo $REPO --log"; exit 1; }

WORK=$(mktemp -d)
gh release download "v$VERSION" --repo "$REPO" --pattern '*.zip' --dir "$WORK"
ZIP="$WORK/Bismillah-$VERSION-win-x64.zip"
[ -f "$ZIP" ] || { echo "The zip isn't on the draft release."; exit 1; }
swift tools/update_key.swift sign "$KEY" "$ZIP" > "$ZIP.sig"
SHA=$(shasum -a 256 "$ZIP" | awk '{print $1}')
sed "s/{{SHA256}}/$SHA/g" "$NOTES" > "$WORK/notes.md"
gh release upload "v$VERSION" "$ZIP.sig" --repo "$REPO"
gh release edit "v$VERSION" --repo "$REPO" --notes-file "$WORK/notes.md" --draft=false --latest
echo "Done: https://github.com/$REPO/releases/tag/v$VERSION"
