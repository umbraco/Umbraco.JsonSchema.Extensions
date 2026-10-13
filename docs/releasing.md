# Releasing

Versions are calculated by [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) from `version.json` and the commit height, so no version number is ever typed by hand. A build only publishes when it is a public release, and `publicReleaseRefSpec` limits that to `release-*` tags, so the tag is what makes a build publishable.

## Branches

- `develop` is the development branch and the default branch of the repository. When an older major needs to stay supported after development moves on, it gets a `vX/dev` development branch (none exist yet).
- `main` holds the released code of the newest major, and `vX/main` holds it for an older one. They exist so a patch can be branched from what was actually released rather than from whatever has since landed on a development branch. `main` is moved by **Merge release** each time a stable release is published, and a `vX/main` is created automatically when a new major takes over `main`.
- `release/X.Y.Z` is cut when preparing a release and is deleted once it has been merged back. The `release-X.Y.Z` tag still points at its final commit, so it can be restored with `git branch release/X.Y.Z release-X.Y.Z && git push origin release/X.Y.Z`.

A `main` branch only ever points at a commit that was built, tagged and published. It is never a merge commit, and nothing is committed to it directly.

Fixes that apply to several majors are made on the oldest affected branch and merged forward, ending in `develop`.

## Cutting a release

Both of these are GitHub Actions, run from the **Actions** tab. Each one refuses to run on a branch it does not belong on.

**Minor release:** run **Prepare release** on `develop` or `vX/dev` and choose `rc`, `beta` or `stable`. It creates `release/X.Y.0`, moves the development branch on to the next version, and merges the release branch back so that the `version.json` conflict is resolved once rather than at every later merge.

**Patch release:** run **Prepare patch release** on `main` or `vX/main`. It takes no options: it creates `release/X.Y.Z+1` at the stable version, because patches ship without stabilisation. Nothing on a development branch is touched. If a patch does need stabilising, run **Prepare release** with `rc` on the new release branch afterwards.

## Stabilising

Run **Prepare release** again on the `release/X.Y.Z` branch to move its version along:

- the same label continues its numbering, so `rc1` becomes `rc2`
- a different label starts again at 1, so `beta2` becomes `rc1`
- `stable` removes the label, which is the last step before shipping

Only move labels forward. `beta` to `rc` is fine; nbgv rejects going back from a stable version to a prerelease one, and nothing stops `rc2` becoming `beta1`, which would publish a version sorting below what already shipped.

## Publishing

Publish a **GitHub Release** targeting the release branch. That creates the tag, which triggers the build, which packs and publishes.

- The tag is `release-` followed by exactly the version in `version.json` on that branch: `release-0.6.0` for a stable release, `release-0.6.0-rc1` for a prerelease. `publicReleaseRefSpec` only matches the `release-` prefix, so a typo still builds and publishes.
- Tick **Set as a pre-release** for anything with a prerelease label. This also keeps it out of the latest release.
- Untick **Set as the latest release** unless this is the newest major. GitHub would otherwise mark a patch to an older major as latest.
- Building and publishing is Azure Pipelines (`azure-pipelines.yml`), not GitHub Actions. For a public release, its `ReleaseMyGet` stage pushes the package to the MyGet pre-release feed and its `Release` stage pushes it to nuget.org.

## After publishing

Publishing a stable release runs **Merge release**, which sets the major's `main` branch to the released commit, merges the release branch into its development branch, and deletes the release branch. A new major additionally takes over `main`, with the outgoing line preserved as `v{previous}/main`.

It stops without changing anything if the tag is not the tip of its release branch, if `main` is not contained in the tag, or if anything other than `version.json` conflicts. The push is atomic and happens last, so a failed run leaves nothing half-done and can simply be re-run once the cause is fixed. Re-run it from the Actions tab, or start it manually with **Run workflow** and the tag name.

A prerelease is a no-op that says so in the log, whether or not the pre-release box was ticked.

## What is still done by hand

- Ticking and unticking the pre-release and latest boxes when publishing the GitHub Release.
- Merging between majors, both `release/X.Y.Z` into the next major's release branch and `vX/dev` into `develop`. These are judgement calls about timing and they conflict often, so they are deliberately not automated.
- Branching `v{N}/dev` from `develop` when development moves to a new major while the previous one is still supported. This happens before that major is released, so there is no release event to hang it on.
- The first patch release of an older major. **Prepare patch release** runs on `vX/main`, and that branch only comes into existence when a new major takes over `main`, so until then create `release/X.Y.Z+1` by hand from the `release-X.Y.Z` tag.

## Notes

These workflows run from the commit a tag points at, not from the default branch, so **Merge release** only runs for releases cut from branches that carry these files (releases up to and including 0.5.0 were made before they existed).

They push to `develop`, `vX/dev`, `main` and `vX/main` as `github-actions[bot]` using `GITHUB_TOKEN`, so any branch protection on those refs has to permit it.

If release creation is ever automated, it must not use `GITHUB_TOKEN`: releases created with it trigger no workflow at all, so the merge-back would silently never run. Use a personal access token or a GitHub App.
