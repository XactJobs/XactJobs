#!/usr/bin/env bash
# Builds the XactJobs and XactJobs.UI packages in Release, at the version in src/Package.props, and pushes them to nuget.org.
# Prompts for the API key; it's never written to disk. The symbols (.snupkg) next to each package are pushed with it.
set -euo pipefail

cd "$(dirname "$0")"

version=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' src/Package.props)
[[ -n "$version" ]] || { echo "No <Version> in src/Package.props" >&2; exit 1; }

if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
    echo "The working tree has changes: commit or stash them first, so the packages match a commit." >&2
    exit 1
fi

tag="v$version"
if [[ "$(git rev-parse -q --verify "$tag^{commit}" || true)" != "$(git rev-parse HEAD)" ]]; then
    echo "Warning: HEAD isn't tagged $tag."
    read -rp "Publish $version anyway? [y/N] " answer
    [[ "$answer" == [yY] ]] || exit 1
fi

packages=(
    "src/XactJobs/bin/Release/XactJobs.$version.nupkg"
    "src/XactJobs.UI/bin/Release/XactJobs.UI.$version.nupkg"
)

# GeneratePackageOnBuild: the build creates the packages (dotnet pack wouldn't build). Old ones are removed first, so a failed
# build can't leave a stale package to push
rm -f "${packages[@]}" "${packages[@]/%.nupkg/.snupkg}"

dotnet build -c Release --disable-build-servers src/XactJobs/XactJobs.csproj
dotnet build -c Release --disable-build-servers src/XactJobs.UI/XactJobs.UI.csproj

for package in "${packages[@]}"; do
    [[ -f "$package" ]] || { echo "Missing $package" >&2; exit 1; }
done

read -rsp "nuget.org API key for $version: " api_key
echo
[[ -n "$api_key" ]] || { echo "No API key: nothing pushed." >&2; exit 1; }

for package in "${packages[@]}"; do
    dotnet nuget push "$package" --api-key "$api_key" --source https://api.nuget.org/v3/index.json --skip-duplicate
done

unset api_key

echo "Pushed XactJobs and XactJobs.UI $version."
