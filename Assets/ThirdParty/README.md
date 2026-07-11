# ThirdParty

Import every Unity Asset Store package (`.unitypackage`) into this folder. Its
contents are git-ignored (see root `.gitignore`) so paid/licensed assets never
get committed — only this README and the folder itself are tracked.

## Adding a package

1. Import it from the Asset Store / Package Manager "My Assets" tab, choosing
   `Assets/ThirdParty/<PackageName>/` as the import root if the importer lets
   you pick one.
2. Add an entry below with its name, source, and version so teammates can
   re-import the same thing.

## Re-importing on a fresh clone

Unity won't have these files after `git clone` — open the Package Manager's
"My Assets" tab and re-import each package listed below.

## Caveat: GUID stability

Files in this folder get fresh `.meta` GUIDs on every import, and those GUIDs
aren't shared through git. **Don't reference ThirdParty assets by GUID from
tracked Scenes/Prefabs/ScriptableObjects** (e.g. don't drag a ThirdParty
prefab into a tracked scene) unless every teammate imports the exact same
package version in the exact same order — otherwise references can silently
break for other teammates. Prefer wrapping/copying what you need into your
own tracked assets, or keep ThirdParty content limited to editor tooling that
isn't referenced by scene data.

## Installed packages

| Package | Source | Version | Notes |
|---------|--------|---------|-------|
| _(none yet)_ | | | |
