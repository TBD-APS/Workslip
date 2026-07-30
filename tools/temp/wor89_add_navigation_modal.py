from pathlib import Path

path = Path('src/FE/src/features/jobs/routes/CompletedJobReport.tsx')
text = path.read_text(encoding='utf-8')

replacements = [
    (
        "import { useEffect, useMemo, useRef, useState } from 'react';\n",
        "import { useEffect, useMemo, useRef, useState } from 'react';\nimport { createPortal } from 'react-dom';\n",
    ),
    (
        "  const [confirmAction, setConfirmAction] = useState<'approve' | 'reject' | 'undo-reject' | null>(null);\n  const [isLoadingPreview, setIsLoadingPreview] = useState(false);\n",
        "  const [confirmAction, setConfirmAction] = useState<'approve' | 'reject' | 'undo-reject' | null>(null);\n  const [undoRejectionCompleted, setUndoRejectionCompleted] = useState(false);\n  const [isLoadingPreview, setIsLoadingPreview] = useState(false);\n",
    ),
    (
        "      notify.success(message);\n      setConfirmAction(null);\n      navigate(from);\n",
        "      setConfirmAction(null);\n\n      if (confirmAction === 'undo-reject') {\n        setUndoRejectionCompleted(true);\n        return;\n      }\n\n      notify.success(message);\n      navigate(from);\n",
    ),
    (
        "      {confirmAction && (\n        <ConfirmActionDialog\n          action={confirmAction}\n          reportNumber={details.form.reportNumber}\n          isPending={statusMutation.isPending}\n          onConfirm={(note) => void executeConfirmAction(note)}\n          onClose={() => setConfirmAction(null)}\n        />\n      )}\n    </div>\n  );\n}\n",
        "      {confirmAction && (\n        <ConfirmActionDialog\n          action={confirmAction}\n          reportNumber={details.form.reportNumber}\n          isPending={statusMutation.isPending}\n          onConfirm={(note) => void executeConfirmAction(note)}\n          onClose={() => setConfirmAction(null)}\n        />\n      )}\n\n      {undoRejectionCompleted && (\n        <UndoRejectionSuccessDialog\n          reportNumber={formatReportNumber(job)}\n          onGoToJobList={() => navigate('/app', { replace: true })}\n          onGoToJob={() => navigate(`/app/job/${job.id}`, { replace: true, state: { from: '/app' } })}\n        />\n      )}\n    </div>\n  );\n}\n\nfunction UndoRejectionSuccessDialog({\n  reportNumber,\n  onGoToJobList,\n  onGoToJob,\n}: {\n  reportNumber: string;\n  onGoToJobList: () => void;\n  onGoToJob: () => void;\n}) {\n  return createPortal(\n    <div className=\"modal-backdrop\" role=\"dialog\" aria-modal=\"true\" aria-labelledby=\"undo-rejection-success-title\">\n      <div className=\"modal-card\">\n        <h3 id=\"undo-rejection-success-title\">Afvisningen er fortrudt</h3>\n        <p>Sagen <strong>{reportNumber}</strong> er sendt til gennemgang igen.</p>\n        <div className=\"modal-actions modal-actions--double\">\n          <button className=\"btn btn-secondary\" type=\"button\" onClick={onGoToJobList}>\n            Til sagslisten\n          </button>\n          <button className=\"btn btn-primary\" type=\"button\" onClick={onGoToJob}>\n            Til sagen\n          </button>\n        </div>\n      </div>\n    </div>,\n    document.body,\n  );\n}\n",
    ),
]

for old, new in replacements:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'Expected exactly one match, found {count}: {old[:80]!r}')
    text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
