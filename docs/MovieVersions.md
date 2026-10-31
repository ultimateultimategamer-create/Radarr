# Movie versions

A movie can have several independently managed versions. Each version has a name, a quality profile, and optional required custom formats. Names can describe anything: an edition, a language, a remaster, 3D, resolution, or a personal label. Several versions can share the same quality profile.

## Add or edit a version

Open a movie and choose **Add Version**. Enter a name, choose the quality profile used for downloads and upgrades, and optionally select required custom formats. Names must be unique within the movie, ignoring case. Use the edit button beside a version to change its name, profile, or rules without changing its file assignment.

All selected custom formats must match. For example, two versions named **Theatrical** and **Director's Cut** can use the same quality profile with different custom formats that identify their editions. A version name is a label and does not create a matching rule by itself.

## Search and import

Use a version's search button to choose releases for that version. Its name also appears in general search results and the manual import version selector, including manual grab overrides.

Automatic downloads require the version's matching rules. If versions share a quality profile and cannot be distinguished by their rules, search for a specific version or use manual import. Explicit custom-format rules take precedence over a general version with no rules.

On rescan, untracked files in the shared movie folder are assigned only when their quality and custom formats identify one version. Ambiguous files remain available for manual import. Files already assigned to a version keep that assignment.

Versions share the movie folder. When file names collide, Radarr appends the version's name and, if necessary, a numeric suffix. An upgrade affects only its target version.

## Existing libraries and removal

Upgrading this branch preserves version IDs, file assignments, and paths. Existing versions initially keep their quality profile names as labels; newly added movies start with an **Original** version.

The primary version cannot be removed. Delete an additional version's file before removing that version. Its pending releases are removed too. Downloads already in progress require manual reassignment if their version is removed; they never fall back to the primary version.

## API

Create a version with `POST /api/v3/movieversion`, and edit it with `PUT /api/v3/movieversion/{id}`. Supply `movieId`, `name`, `qualityProfileId`, and `requiredCustomFormatIds` (an array of existing custom-format IDs, or an empty array). The edit request also accepts `id`.

List a movie's versions with `GET /api/v3/movieversion?movieId={movieId}`. Use `movieVersionId` for version-specific release searches, grabs, and manual imports. A grab or import for a removed or unrelated version is rejected.
