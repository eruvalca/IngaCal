# Automated verification

Run the suite from the repository root:

```powershell
dotnet test IngaCal.Tests/IngaCal.Tests.csproj
```

The suite uses xUnit and bUnit on .NET 10. Service and migration tests use a separate, uniquely named file-backed SQLite database for every test. They never open the application's database. Identity schema configuration matches the application. Each operation gets a fresh context, and tests remove their temporary files after disposing connections.

Verified on September 28, 2026 with .NET SDK 10.0.401:

```text
dotnet test IngaCal.Tests\IngaCal.Tests.csproj --no-restore --verbosity minimal
Passed!  - Failed: 0, Passed: 93, Skipped: 0, Total: 93
```

`dotnet test IngaCal.sln --no-build --no-restore --list-tests --verbosity quiet` also discovers all 93 cases through the solution.

Component tests check native `disabled` attributes before dispatching click or submit events. This matters because bUnit can invoke a disabled control's callback even though a real browser prevents it. Page integration tests also render the actual parent pages and inspect calendar/chart JavaScript payloads, catching accidental literal strings in Razor component parameters.

## Requirement evidence

Names below identify methods in `IngaCal.Tests`. Parameterized methods include the relevant success, rejection, and boundary cases.

| Requirement | Evidence |
| --- | --- |
| “Fresh-database creation, migration of an existing Identity database, restart persistence” | `PersistenceTests.Migrate_FreshDatabase_CreatesJournalAndSurvivesReopening`; `PersistenceTests.Migrate_ExistingIdentityDatabase_PreservesAccountCredentialsClaimsAndTokens` checks password/security stamps, two-factor keys, claims, and passkey data. |
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

## Browser and deployment checks

### Implementation verification — September 28, 2026

Verified in the Codex browser against an isolated SQLite database, then repeated startup/save checks using the published Production build:

- Registration immediately signed in without confirmation; session and saved activities survived a host restart with persisted keys.
- Keyboard entry saved an exact 09:07–10:22 activity (75 minutes). Inline creation selected two tags. Reports showed 75 minutes overall and 75 minutes for each tag.
- Pointer movement preserved duration; both top and bottom resize handles changed duration by 15 minutes. Dragging empty space opened a draft.
- Day/week/month navigation rendered correctly. Copy opened an editable next-day draft. An overlapping copy was rejected; an overnight 23:45–00:15 copy saved and appeared in week view. Confirmed deletion removed only that test copy.
- A stale drag from a second tab reverted to its previous position, displayed a conflict, and Retry loaded the latest saved duration. A drag into another activity also reverted and displayed the overlap explanation.
- Tag archive/restore, light/dark themes, chart rendering, and repeated Calendar/Reports/Tags navigation worked. At a 390px phone viewport, the day view, manual dialog, and resized report charts fit without horizontal overflow.
- Release solution build completed with zero warnings/errors. Release tests: 93 passed, zero failed/skipped. Published output contained no database, sidecars, backup, or key files.
- Published `--migrate` upgraded a copy of the existing Identity database from 118,784 to 155,648 bytes and retained a pre-migration backup. Published `--backup` produced a 155,648-byte snapshot. The original local database's Git object hash still matched the original tracked blob exactly.

Hardware passkey/2FA enrollment, deliberate OS time-zone switching, a transient network disconnect that retains the same circuit, and actual hosting deployment were not exercised. Identity's existing handlers and reconnect UI are retained; the automated tests cover time-zone changes/fallback and DST calculations. Production App Service storage suitability still requires assessment on the actual host.

Automated component tests do not operate a real FullCalendar canvas, WebAuthn authenticator, browser reconnect, or file publishing process. Verify these separately when changing their implementation:

- On desktop: select an empty range; move an activity; resize both edges; verify 15-minute snapping and visible rollback after overlap rejection. Confirm day/week/month navigation.
- On phone: create and edit via the manual form, inspect readable calendar and reports, and use both light/dark themes. Check keyboard focus, dialog dismissal, and focus restoration.
- Reconnect after a brief network interruption; navigate between calendar and reports repeatedly; check for duplicate handlers, charts, or browser errors.
- Exercise registration/login, password changes, and enabled two-factor/passkey features with a disposable account.
- Publish to a temporary output directory and confirm no live database, sidecar, backup, or data-protection key was copied. Run migration and backup commands against a temporary configured database and verify restart persistence before real deployment.

Time-zone tests depend on the host's maintained time-zone database. The fixtures use supported current transitions; obsolete historical civil-time changes may differ between Windows and IANA data.
