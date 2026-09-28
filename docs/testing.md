# Automated verification

Run the suite from the repository root:

```powershell
dotnet test IngaCal.Tests/IngaCal.Tests.csproj
```

The suite uses xUnit v2/VSTest and bUnit on .NET 10. Service, persistence, and page-integration tests use SQL Server 2025 through `Testcontainers.MsSql`. One collection fixture starts an independent container with a random host port; each test creates a uniquely named database through the checked-in migration and deletes only that database afterward. Tests never use the application's connection string or Compose database. Identity model options match runtime and design-time configuration.

Docker Desktop must be running with Linux containers, and the SQL Server image must be available or downloadable. Missing Docker/image access fails database tests with a prerequisite message; there is no SQLite fallback or silent skip. Compose does not need to be running. To run only the pure unit/component tests without Docker:

```powershell
dotnet test IngaCal.Tests/IngaCal.Tests.csproj --filter 'Category!=Database'
```

Full verification includes Release build, the complete .NET suite, Node chart tests, and Release publish. Current SQL Server verification results are recorded below; earlier UI evidence is explicitly historical.

Component tests check native `disabled` attributes before dispatching click or submit events. This matters because bUnit can invoke a disabled control's callback even though a real browser prevents it. Page integration tests also render the actual parent pages and inspect calendar/chart JavaScript payloads, catching accidental literal strings in Razor component parameters.

## Requirement evidence

Names below identify methods in `IngaCal.Tests`. Parameterized methods include the relevant success, rejection, and boundary cases.

| Requirement | Evidence |
| --- | --- |
| “Fresh-database creation, repeat migration with Identity data, restart persistence” | `PersistenceTests.Migrate_FreshDatabase_CreatesJournalAndSurvivesReopening`; `PersistenceTests.Migrate_RepeatedMigration_PreservesAccountCredentialsClaimsTokensAndPasskeys` checks password/security stamps, two-factor keys, claims, and passkey data. |
| “Two accounts cannot read or modify each other’s activities, tags, or reports, including through forged identifiers.” | `ActivityServiceTests.Crud_IsolatedByAccount_EvenWithForgedIdentifiers`; `ActivityServiceTests.Save_ForeignOrMissingTags_RejectsEntireWrite`; `TagServiceTests.Save_StaleAndForeignEditsRejected_PreserveLatestTag`; `ReportServiceTests.Get_AccountIsolationAndForeignFilter_DoNotExposeOtherUsersTime`. |
| “Adjacent intervals succeed; overlaps fail, including simultaneous writes from separate tabs.” | `ActivityServiceTests.Save_OverlapBoundaries_AllowOnlyAdjacentIntervals`; `ActivityServiceTests.Save_ConcurrentConflictingWrites_OnlyOneSucceeds`; `ActivityServiceTests.Save_CrossMidnightConflict_RejectsOverlapAndKeepsExactMinuteDuration`. |
| “Stale edits produce a recoverable conflict.” | `ActivityServiceTests.SaveAndDelete_StaleVersion_PreserveLatestActivity`; `TagServiceTests.Save_StaleAndForeignEditsRejected_PreserveLatestTag`; `ActivityServiceTests.Save_ConflictingMove_PreservesOriginalTimeTextTagsAndVersion`. |
| “Allow a title, description, or both; require at least one.” | `ActivityServiceTests.Save_TitleOrDescriptionAlone_AndMaximumLengthsAreAccepted`; `ActivityServiceTests.Save_InvalidInput_IsRejectedWithoutPersisting`; `CalendarComponentTests.Editor_ManualExactMinuteEntry_ValidatesTextAndSendsUtcCrossMidnightTimes`. |
| “Manual entry supports exact minutes” and “Support cross-midnight activities and explicit start/end dates.” | `CalendarComponentTests.Editor_ManualExactMinuteEntry_ValidatesTextAndSendsUtcCrossMidnightTimes`; `ActivityServiceTests.Save_CrossMidnightConflict_RejectsOverlapAndKeepsExactMinuteDuration`. |
| “Include a searchable multi-tag picker, inline tag creation” | `CalendarComponentTests.TagPicker_SearchAndSelection_KeepSelectedArchivedTagsButHideUnselectedArchivedTags`; `CalendarComponentTests.TagPicker_InlineCreation_SelectsNewTagAndClearsDraftOnlyOnSuccess`; `ActivityServiceTests.Save_UpdatesTagSet_TrimsTextAndNormalizesUtc`. |
| “Duplication opens an editable draft.” | `CalendarComponentTests.Editor_CopyCreatesEditableTomorrowDraft_WithoutUpdatingOriginalOrReusingArchivedTags`. |
| “Deletion with confirmation” | `CalendarComponentTests.Editor_DeleteRequiresConfirmation_AndKeepItCancels`. |
| “Editor buttons are enabled idle/disabled busy” | `CalendarComponentTests.Editor_ActionsAreEnabledWhenIdle_DisabledWhileBusy_AndEnabledAfterCompletion` checks close, copy, delete, cancel, save, and the delete confirmation across state transitions. |
| “Calendar initialize payload view/timeZone uses actual values” | `PageIntegrationTests.Home_InitializesCalendarWithActualViewAndDeviceZone_AndUpdatesViewFromToolbar` verifies `timeGridDay`, the device's IANA zone, and the updated `timeGridWeek` payload. |
| “Reports Theme/TimeZone similarly” | `PageIntegrationTests.Reports_PassesActualDeviceZoneToFiltersAndDarkThemeToCharts` verifies the filter zone and dark chart options through the real page. |
| “Support renaming, recoloring, archiving, and restoring. Archived tags remain on historical activities and in reports.” | `TagServiceTests.Save_ArchiveRenameRecolorRestore_UpdatesHistoricalActivities`; `ActivityServiceTests.Save_ArchivedTags_CanRetainOrRemoveButCannotNewlyAttach`; `ReportServiceTests.Get_MultipleTagsClippingAndCrossMidnightSplits_CountsOverallOnceAndEachTagFully`. |
| “A normalized unique-per-account name” | `TagServiceTests.Save_NormalizesUniqueNamePerAccount_IncludingArchivedNames`; `TagServiceTests.Save_RenameToDuplicateName_RejectsAndPreservesOriginalTag`. |
| “Credit each assigned tag with the full clipped duration. Overall logged time counts each matching activity once.” | `ReportServiceTests.Get_MultipleTagsClippingAndCrossMidnightSplits_CountsOverallOnceAndEachTagFully` asserts 120 overall minutes, 90 minutes for each of two tags, 30 untagged minutes, and percentages totaling 175%. |
| “Timeframe presets, custom inclusive date ranges, tag filters with Any/All matching, and an Untagged filter.” | `ReportComponentTests.Filters_DefaultToSevenInclusiveDays_AndLastMonthHandlesYearRollover`; `ReportComponentTests.Filters_CustomRangeTagsAndUntaggedMode_ProduceExplicitFilterAndClearSelection`; `ReportServiceTests.Get_AnyAllAndUntaggedFilters_SelectMatchingActivitiesAndTagTotals`; `ReportServiceTests.Get_InclusiveDates_UsesSelectedZoneAndIncludesEndDate`. |
| “Daily/weekly trend lines; and sortable tag totals” and “Tables expose the values shown in charts.” | `ReportServiceTests.Get_WeeklyGrouping_UsesMondayBucketsIncludingPartialAndEmptyWeeks`; `ReportServiceTests.Get_EmptyRange_ReturnsZeroMetricsAndEveryCalendarDay`; `ReportComponentTests.Tables_SortTagTotalsAndExposeExactTrendValues`; `ReportComponentTests.Charts_UseSameReportValuesAsTables_ConvertingMinutesToHours`. |
| “Detect the device’s IANA time zone on connection and recheck when the browser regains focus” and “Offer manual selection if detection fails.” | `BrowserContextTests.InitializeAndFocusChange_UseDeviceZoneAndNotifySubscribers`; `BrowserContextTests.InvalidDeviceZone_BlocksReadyUntilManualSelection`; `BrowserContextTests.TimeZonePicker_InvalidThenValidSelection_ShowsErrorThenRecoversAndCanFollowDevice`. The browser focus listener itself is checked in the browser, not simulated by bUnit. |
| “Reject nonexistent daylight-saving times; let users distinguish repeated times” and “23-/25-hour days.” | `JournalTimeTests.Resolve_NonexistentSpringTime_RejectsWithExplanation`; `JournalTimeTests.Resolve_RepeatedHour_DistinguishesEarlierAndLaterOccurrence`; `JournalTimeTests.Resolve_SpringBoundary_PreservesActualOneMinuteBetweenLocal0159And0300`; `ReportServiceTests.Get_DaylightSavingDay_MeasuresActualElapsedMinutes`. |
| “Initialize JavaScript after rendering and dispose subscriptions and references.” | `BrowserContextTests.InitializeAndFocusChange_UseDeviceZoneAndNotifySubscribers`; `BrowserContextTests.Dispose_RemovesBrowserSubscriptions`; chart/editor component tests exercise their render-time initialization. Actual browser listeners and interactions need the browser checks below. |

The remaining tests cover current-user authentication claims, tag validation, cancellation, UTC rehydration, list boundaries/order, local-time conversions including fractional-hour zones, midnight clock changes, account-deletion cascades, and display formatting.

## SQL Server regression evidence

| Requirement | Evidence |
| --- | --- |
| Fresh/repeat migration, snapshot consistency, UTC and restart persistence | `PersistenceTests.Migrate_FreshDatabase_CreatesJournalAndSurvivesReopening` |
| Identity credentials, claims, tokens, and passkey data survive repeat migration | `PersistenceTests.Migrate_RepeatedMigration_PreservesAccountCredentialsClaimsTokensAndPasskeys` |
| Passkey JSON and long WebAuthn credential IDs | `PersistenceTests.Save_Passkey_RoundTripsCredentialAndJsonData` (32 and 1023 bytes) |
| Database duration constraint and account deletion | `PersistenceTests.Save_InvalidDuration_DatabaseConstraintRejectsDirectWrite`; `PersistenceTests.DeleteAccount_CascadesOwnedJournalDataAndKeepsOtherAccount` |
| Concurrent overlapping/adjacent saves | `ActivityServiceTests.Save_ConcurrentConflictingWrites_OnlyOneSucceeds` with read-committed snapshot both on (Azure SQL default) and off; `ActivityServiceTests.Save_ConcurrentAdjacentWrites_BothSucceed` |
| Account-scoped lock, timeout rollback and later recovery | `ActivityServiceTests.Save_LockedAccount_TimesOutWithoutWritingAndOtherAccountCanSave` |
| Cancellation while waiting releases the transaction without writing | `ActivityServiceTests.Save_CancelledWhileWaitingForLock_DoesNotWriteAndReleasesTransaction` |
| Duplicate-name races and Unicode distinctions | `TagServiceTests.Save_ConcurrentDuplicateNames_OneSucceedsAndOneReportsDuplicate`; `TagServiceTests.Save_DistinctUnicodeNormalizedNames_RemainDistinct` |
| Foreign-key errors are not mislabeled duplicates | `TagServiceTests.Save_MissingAccount_DoesNotMisreportForeignKeyViolationAsDuplicate` |
| Required SQL connection | `SqlServerConnectionStringTests.Validate_InvalidConfiguration_RejectsWithoutLeakingCredentials` |
| Host-managed keys, optional directory override, and SQL health endpoint | Published-host smoke checks recorded in the Azure hosting verification below |

The existing service/report tests continue to cover account isolation, stale edits, archived tags, DST, UTC round trips, interval clipping, and report denominators against SQL Server.

## Azure hosting preparation verification — September 28, 2026

- Removed the custom Data Protection directory helper and its two obsolete tests. Platform defaults now select key storage; `Storage__DataProtectionPath` remains an optional override. `DataProtection__ApplicationName` can isolate environments sharing a key store.
- `dotnet build IngaCal.sln -c Release --no-restore`: zero warnings/errors.
- `dotnet test IngaCal.Tests/IngaCal.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Save_ConcurrentConflictingWrites_OnlyOneSucceeds' --verbosity minimal`: **2 passed**. The test asserts the selected RCSI setting and verifies one successful save, one overlap error, and one stored activity under each setting.
- `dotnet test IngaCal.Tests/IngaCal.Tests.csproj -c Release --no-build --no-restore --verbosity minimal`: **112 passed, 0 failed, 0 skipped**.
- `dotnet publish IngaCal/IngaCal.csproj -c Release --no-build --no-restore -o artifacts/publish`: succeeded.
- EF's Release `has-pending-model-changes` check with a passwordless managed-identity connection string reported no model changes. This checks configuration/model construction, not Azure authentication or network access.
- A published Production host without a directory override reported the Windows profile key repository and DPAPI encryption; `/Account/Login` returned HTTP 200. Another host run with an explicit relative override created its key outside the publish directory and served the login page successfully.
- `/health` returned **200/Healthy** against an isolated migrated SQL Server container, **503/Unhealthy** after that container stopped, and **200/Healthy** after it restarted. Error responses contained no connection details. The endpoint uses EF's read-only connectivity check, not migrations.
- Smoke hosts, the disposable SQL container/volume, credentials, and isolated key directory were cleaned up. The platform key store and existing application data were left intact. Windows Event Log logging was disabled only for the smoke hosts.

Azure infrastructure is not provisioned or tested here. Managed identity, App Service key storage, HTTPS forwarding, slots/scale-out, and Azure SQL backup/recovery must be exercised in Azure staging as specified in `docs/azure-deployment.md`. The existing sign-out issue below remains a pre-launch item.

## Earlier configuration cleanup verification — September 28, 2026

- Removed the combined database/key-storage record. Runtime startup and EF tooling use the shared SQL connection-string validator; Data Protection resolves and checks its own key directory.
- `dotnet build IngaCal.sln -c Release --no-restore`: zero warnings/errors.
- `dotnet test IngaCal.Tests/IngaCal.Tests.csproj -c Release --no-build --no-restore --verbosity minimal`: **113 passed, 0 failed, 0 skipped**.
- `dotnet ef migrations has-pending-model-changes --project IngaCal/IngaCal.csproj --configuration Release --no-build -- --ConnectionStrings:DefaultConnection 'Server=tcp:127.0.0.1,14333;Database=IngaCalConfigurationCheck;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'`: no model changes.
- A Production startup probe returned HTTP 200 for `/Account/Login` and persisted a Data Protection key to an isolated configured directory. Windows Event Log logging was disabled for the probe because the sandbox cannot write to that log. The temporary host and key directory were removed afterward.

## SQL Server implementation verification — September 28, 2026

- .NET SDK 10.0.401: Release solution build succeeded with zero warnings/errors.
- `dotnet test IngaCal.Tests/IngaCal.Tests.csproj -c Release --no-build --no-restore --verbosity minimal`: **113 passed, 0 failed, 0 skipped**, including real SQL Server Testcontainers tests.
- `node --test scripts/tests/report-chart-labels.test.mjs`: **3 passed, 0 failed**.
- Release publish succeeded; output inspection found no database, backup, key, or dotenv files. EF's Release `has-pending-model-changes` check reported none, and idempotent migration SQL generation succeeded.
- Compose started an isolated SQL Server 2025 container and reached healthy status. Browser registration signed in; a tagged 09:07–10:22 activity appeared as 75 minutes in reports.
- Recreated the SQL Server container while retaining its named volume. Published Production `--migrate` exited successfully with no pending changes. The existing browser session, account, activity, and tag persisted. Password login worked after signing out; editing the activity to 10:27 updated the report to 80 minutes.
- A separate published Production startup probe pointed at a missing database. The host started successfully and SQL Server confirmed no database was created, proving ordinary startup does not migrate.
- Native `BACKUP DATABASE ... WITH COPY_ONLY, CHECKSUM`, `RESTORE VERIFYONLY`, and restore to a separate database succeeded. The restored account, 80-minute activity, and tag association were verified.
- The retired `--backup` command returned a nonzero exit with SQL Server guidance and created no backup file.

An unrelated existing issue was observed: Sign out clears the cookie but the unchanged logout endpoint constructs `~//` from the navigation form's `/` return URL, causing a local-redirect exception. Navigating to Sign in and logging in succeeds. This provider migration does not alter that endpoint. Hardware passkey/2FA ceremonies and actual production infrastructure were not exercised; passkey persistence is covered by the automated SQL Server tests.

## Browser and deployment checks

### Pie chart extension

The reports pie chart is covered by `ReportComponentTests.Charts_UseSameReportValuesAsTables_ConvertingMinutesToHours` and `ReportComponentTests.PieChart_MultiTagAndUntaggedTotals_UpdateWithFiltersAndTheme`. These verify slice hours, tag colors, the duration/percentage legend, percentage hover labels, multi-tag counting, untagged-only updates, and theme changes without reinitializing the chart. The multi-tag case distinguishes percentages of combined tag totals (37.5%, 37.5%, 25%) from percentages of unique logged time (60%, 60%, 40%). `ReportComponentTests.PieChart_ZeroTotal_ShowsZeroPercentWithoutInvalidValues` checks the zero-total guard.

Focused validation: `dotnet test IngaCal.Tests/IngaCal.Tests.csproj --no-restore -c Release --filter "FullyQualifiedName~ReportComponentTests|FullyQualifiedName~PageIntegrationTests" --verbosity minimal` — 8 passed, 0 failed, 0 skipped. Release publish also succeeded. Browser checks against the isolated database verified desktop and 390px phone layouts, light/dark themes, an untagged-only single slice, and chart removal/recreation when switching to an empty period and back.

### Permanent chart labels

`ReportComponentTests.PieChart_MultiTagAndUntaggedTotals_UpdateWithFiltersAndTheme` also verifies that on-chart labels carry the correct duration and percentage for each chart's denominator, refresh with filters/themes, and release the JavaScript module on disposal. The focused .NET command above passes all 8 cases with these checks.

`node --test scripts/tests/report-chart-labels.test.mjs` — 3 passed, 0 failed. The cases are `bar durations and percentages remain visible for short bars, update once, and clean up`, `pie draws all durations and percentages directly inside ordinary slices`, and `small pie slices use nonoverlapping callouts within phone chart bounds and respond to resize`. These run the actual drawing plugin with measured canvas/geometry substitutes. Browser checks additionally verified rendered labels on both charts in light/dark themes, on a 390px phone layout, after an untagged-only filter, and after switching to an empty report and back.

### Historical SQLite verification — before the SQL Server transition

Verified in the Codex browser against an isolated SQLite database, then repeated startup/save checks using the published Production build:

- Registration immediately signed in without confirmation; session and saved activities survived a host restart with persisted keys.
- Keyboard entry saved an exact 09:07–10:22 activity (75 minutes). Inline creation selected two tags. Reports showed 75 minutes overall and 75 minutes for each tag.
- Pointer movement preserved duration; both top and bottom resize handles changed duration by 15 minutes. Dragging empty space opened a draft.
- Day/week/month navigation rendered correctly. Copy opened an editable next-day draft. An overlapping copy was rejected; an overnight 23:45–00:15 copy saved and appeared in week view. Confirmed deletion removed only that test copy.
- A stale drag from a second tab reverted to its previous position, displayed a conflict, and Retry loaded the latest saved duration. A drag into another activity also reverted and displayed the overlap explanation.
- Tag archive/restore, light/dark themes, chart rendering, and repeated Calendar/Reports/Tags navigation worked. At a 390px phone viewport, the day view, manual dialog, and resized report charts fit without horizontal overflow.
- Release solution build completed with zero warnings/errors. Release tests: 93 passed, zero failed/skipped. Published output contained no database, sidecars, backup, or key files.
- Published `--migrate` upgraded a copy of the existing Identity database from 118,784 to 155,648 bytes and retained a pre-migration backup. Published `--backup` produced a 155,648-byte snapshot. The original local database's Git object hash still matched the original tracked blob exactly.

Hardware passkey/2FA enrollment, deliberate OS time-zone switching, a transient network disconnect that retains the same circuit, and actual hosting deployment were not exercised. Identity's existing handlers and reconnect UI are retained; the automated tests cover time-zone changes/fallback and DST calculations. Production SQL Server connectivity, backups, and key persistence still require assessment on the actual host.

Automated component tests do not operate a real FullCalendar canvas, WebAuthn authenticator, browser reconnect, or file publishing process. Verify these separately when changing their implementation:

- On desktop: select an empty range; move an activity; resize both edges; verify 15-minute snapping and visible rollback after overlap rejection. Confirm day/week/month navigation.
- On phone: create and edit via the manual form, inspect readable calendar and reports, and use both light/dark themes. Check keyboard focus, dialog dismissal, and focus restoration.
- Reconnect after a brief network interruption; navigate between calendar and reports repeatedly; check for duplicate handlers, charts, or browser errors.
- Exercise registration/login, password changes, and enabled two-factor/passkey features with a disposable account.
- Publish to a temporary output directory and confirm no live database, sidecar, backup, or data-protection key was copied. Run migrations and native SQL Server backup/restore against an isolated database and verify restart persistence before real deployment. The former application --backup command is no longer supported.

Time-zone tests depend on the host's maintained time-zone database. The fixtures use supported current transitions; obsolete historical civil-time changes may differ between Windows and IANA data.
