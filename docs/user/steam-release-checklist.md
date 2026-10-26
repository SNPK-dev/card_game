# Steam release checklist

This repository can prepare a preview depot, but publishing requires the real Steamworks App ID, Depot ID, authorized Steam account, store assets, legal approvals, and Valve review.

1. Build and test the Windows package with `scripts/release/build-windows-release.ps1` and `scripts/release/test-windows-release.ps1`.
2. Inspect `BUILD-MANIFEST.json`, `SHA256SUMS.txt`, and both packaged E2E reports.
3. Run `scripts/release/prepare-steam-depot.ps1 -AppId <id> -DepotId <id> -Version <version>`.
4. Review the generated VDF files under `artifacts/steam/<version>`. They use SteamCMD preview mode and do not set a live branch.
5. Configure Steamworks launch option `PLAY.bat`, supported Windows versions, redistributables, cloud-save paths, achievements, controller support, and privacy disclosures as applicable.
6. Complete code-signing, malware scanning, crash-reporting/privacy validation, hardware-matrix testing, accessibility checks, store-page review, and manual pointer/keyboard acceptance.
7. Upload through an authenticated SteamCMD session. Never store Steam credentials or guard codes in this repository.

The step-by-step Russian manual acceptance procedure is documented in [`manual-release-test.ru.md`](manual-release-test.ru.md).
