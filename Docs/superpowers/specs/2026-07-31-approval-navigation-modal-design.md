# WOR-240: After approval/rejection, ask admin to go to list or the case

## Status

Proposed.

## Problem

When an admin approves or rejects a job on the completed-job report page, the
application immediately navigates back to the job list (`navigate(from)` in
`CompletedJobReport.tsx`) and shows a toast. The admin has no choice about where
to go next.

The undo-rejection flow already shows a success dialog with "Til sagslisten" /
"Til sagen" buttons. The approve/reject flow should offer the same choice.

## Desired behavior

After a successful approve or reject, a modal appears on the case page:

- approve: title "Sagen er godkendt", text "Sagen `<reportNumber>` er godkendt."
- reject: title "Sagen er afvist", text "Sagen `<reportNumber>` er afvist."

The modal has two buttons:

- **Til sagslisten** -> `navigate('/app')`
- **Til sagen** -> `navigate('/app/completed/:id')` (the case, now showing its new status)

The modal is not dismissible via backdrop click or Escape, so the admin must
choose where to go. This matches the existing success dialogs
(`UndoRejectionSuccessDialog` in `CompletedJobReport.tsx` and
`CreateSuccessDialog` in `SimpleJobCreate.tsx`).

The toast for approve/reject is removed because the modal conveys the outcome.
The undo-reject flow keeps its existing dialog text and uses the same two
buttons; "Til sagen" navigates to the case for consistency.

## Implementation

Frontend only (`src/FE`).

In `src/FE/src/features/jobs/routes/CompletedJobReport.tsx`:

1. Generalize the inline `UndoRejectionSuccessDialog` into an
   `ActionSuccessDialog` that takes an action: `'approve' | 'reject' |
   'undo-reject'`, the report number, `onGoToJobList`, and `onGoToJob`.
   - Titles/texts as described above.
   - Reuses existing `modal-backdrop`, `modal-card`, `modal-actions
     modal-actions--double`, `btn btn-secondary`, `btn btn-primary` styles.
   - Portals to `document.body` like the current dialog.
2. Replace the `undoRejectionCompleted` boolean state with a
   `completedAction` state of type `'approve' | 'reject' | 'undo-reject' |
   null`.
3. In `executeConfirmAction`:
   - approve/reject success: set `completedAction` instead of
     `notify.success(...)` and `navigate(from)`.
   - undo-reject success: set `completedAction` instead of
     `setUndoRejectionCompleted(true)`.
   - Keep the existing error handling and `setConfirmAction(null)`.
4. Render `ActionSuccessDialog` when `completedAction` is set:
   - "Til sagslisten" -> `navigate('/app')`.
   - "Til sagen" -> `navigate(\`/app/completed/${job.id}\`)`.
5. `from` is still used by the `LinkedJobs` component; keep it.

No backend, API contract, schema, or generated-artifact changes.

## Testing

- Add a focused vitest + testing-library unit test for the new dialog behavior
  in `CompletedJobReport`: after a successful approve, the modal appears with
  both buttons; "Til sagslisten" navigates to `/app`; "Til sagen" navigates to
  `/app/completed/:id`. Mirror the setup used by existing tests such as
  `SimpleJobCreate.navigation.test.tsx`.
- Run frontend lint, TypeScript check, and production build.
- Playwright: the repository has no application Playwright harness and no
  test identity. This change is reported as "implemented but
  Playwright-unvalidated" with the exact missing prerequisite per
  `Docs/agents/VALIDATION.md` unless a running app and identity become
  available.

## Out of scope

- Changes to the confirm dialog before the action (`ConfirmActionDialog`).
- Changes to backend authorization or status transitions.
- Changing the undo-reject success text.
