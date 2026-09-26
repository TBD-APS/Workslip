param(
  [string]$Repo = ".",
  [switch]$Start
)

$ErrorActionPreference = "Stop"
$repoPath = (Resolve-Path $Repo).Path
$tsxPath = Join-Path $repoPath "src\FE\src\features\jobs\routes\AdminCompletedJobReport.tsx"
$cssPath = Join-Path $repoPath "src\FE\src\features\jobs\routes\AdminCompletedJobReport.css"

if (-not (Test-Path $tsxPath)) { throw "Kan ikke finde $tsxPath. Kør fra Workslip-repoets rod eller angiv -Repo." }
if (-not (Test-Path $cssPath)) { throw "Kan ikke finde $cssPath." }

$tsx = Get-Content $tsxPath -Raw
$css = Get-Content $cssPath -Raw

$oldStart = "function ActionSuccessDialog({"
$oldEnd = "function InfoRow({ icon, label, value }: { icon: ReactNode; label: string; value: string }) {"
$startIndex = $tsx.IndexOf($oldStart)
$endIndex = $tsx.IndexOf($oldEnd)

if ($startIndex -lt 0 -or $endIndex -lt 0 -or $endIndex -le $startIndex) {
  throw "Kunne ikke finde ActionSuccessDialog-blokken i den nuværende checkout. Repoet kan have ændret sig."
}

$newBlock = @'
function ActionSuccessDialog({
  action,
  reportNumber,
  onGoToJobList,
  onGoToJob,
}: {
  action: JobAction;
  reportNumber: string;
  onGoToJobList: () => void;
  onGoToJob: () => void;
}) {
  const primaryButtonRef = useRef<HTMLButtonElement>(null);
  const [approvalAnimationKey, setApprovalAnimationKey] = useState(0);
  const [approvalSettled, setApprovalSettled] = useState(false);
  const isUndoReject = action === 'undo-reject';
  const isApprove = action === 'approve';
  const isReopen = action === 'reopen';

  useEffect(() => {
    if (!isApprove) return;

    const reducedMotion = typeof window !== 'undefined'
      && typeof window.matchMedia === 'function'
      && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    if (reducedMotion) {
      setApprovalSettled(true);
      return;
    }

    setApprovalSettled(false);
    const timer = window.setTimeout(() => setApprovalSettled(true), 1050);
    return () => window.clearTimeout(timer);
  }, [approvalAnimationKey, isApprove]);

  const dialogRef = useModalAccessibility<HTMLDivElement>({
    open: true,
    onClose: onGoToJobList,
    initialFocusRef: primaryButtonRef,
    closeOnEscape: false,
  });

  const title = isUndoReject
    ? 'Afvisningen er fortrudt'
    : isApprove
      ? 'Sagen er godkendt'
      : isReopen
        ? 'Sagen er genåbnet'
        : 'Sagen er afvist';

  if (isApprove) {
    return createPortal(
      <div className="modal-backdrop approval-celebration-backdrop">
        <div
          id="job-approval-celebration"
          ref={dialogRef}
          className="approval-celebration-card"
          role="dialog"
          aria-modal="true"
          aria-label={title}
          tabIndex={-1}
        >
          <p className="approval-celebration-kicker">VATER MIKROANIMATION</p>
          <h3>Få sagen i vater</h3>

          <div
            key={approvalAnimationKey}
            className={`approval-level ${approvalSettled ? 'approval-level--settled' : ''}`}
            aria-hidden="true"
          >
            <div className="approval-level__body">
              <span className="approval-level__guide approval-level__guide--left" />
              <span className="approval-level__guide approval-level__guide--right" />
              <span className="approval-level__window">
                <span className="approval-level__bubble" />
              </span>
            </div>
          </div>

          <div className="approval-celebration-copy" aria-live="polite">
            <strong>{approvalSettled ? 'Så er den i vater!' : 'Ikke helt i vater endnu'}</strong>
            <span>
              {approvalSettled
                ? <>Sagen <b>{reportNumber}</b> er godkendt, dokumenteret og klar.</>
                : 'Workslip retter sagen ind og låser godkendelsen.'}
            </span>
          </div>

          <div className="approval-celebration-actions">
            <button
              id="job-approval-go-list"
              className="btn btn-secondary approval-celebration-button"
              type="button"
              onClick={onGoToJobList}
            >
              Til sagslisten
            </button>
            <button
              id="job-approval-go-job"
              ref={primaryButtonRef}
              className="btn btn-primary approval-celebration-button"
              type="button"
              onClick={onGoToJob}
            >
              Til sagen
            </button>
          </div>

          <button
            id="job-approval-replay"
            className="approval-celebration-replay"
            type="button"
            onClick={() => setApprovalAnimationKey((current) => current + 1)}
          >
            Se animationen igen
          </button>
        </div>
      </div>,
      document.body,
    );
  }

  const body = isUndoReject
    ? <>Sagen <strong>{reportNumber}</strong> er sendt til gennemgang igen.</>
    : isReopen
      ? <>Sagen <strong>{reportNumber}</strong> er genåbnet og kan nu rettes. Årsagen er gemt i historikken.</>
      : <>Sagen <strong>{reportNumber}</strong> er afvist.</>;

  return createPortal(
    <div className="modal-backdrop">
      <div
        ref={dialogRef}
        className="modal-card"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
      >
        <h3>{title}</h3>
        <p>{body}</p>
        <div className="modal-actions modal-actions--double">
          <button className="btn btn-secondary" type="button" onClick={onGoToJobList}>
            Til sagslisten
          </button>
          <button ref={primaryButtonRef} className="btn btn-primary" type="button" onClick={onGoToJob}>
            Til sagen
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

'@

$tsx = $tsx.Substring(0, $startIndex) + $newBlock + $tsx.Substring($endIndex)
Set-Content -Path $tsxPath -Value $tsx -Encoding utf8

$marker = "/* WOR-449 approval water gamification */"
if (-not $css.Contains($marker)) {
  $cssAddition = @'

/* WOR-449 approval water gamification */
.approval-celebration-backdrop {
  padding: max(1rem, env(safe-area-inset-top)) max(1rem, env(safe-area-inset-right))
    max(1rem, env(safe-area-inset-bottom)) max(1rem, env(safe-area-inset-left));
}

.approval-celebration-card {
  background: #f6f2e7;
  border: 1px solid color-mix(in srgb, var(--color-primary) 15%, transparent);
  border-radius: clamp(1.7rem, 4vw, 3rem);
  box-shadow: 0 24px 80px rgb(0 0 0 / 0.28);
  color: #104e42;
  display: grid;
  gap: 1rem;
  margin: auto;
  max-height: min(92dvh, 820px);
  max-width: 700px;
  overflow: auto;
  padding: clamp(1.35rem, 4vw, 2.6rem);
  text-align: center;
  width: min(100%, 700px);
}

.approval-celebration-kicker {
  color: #75827b;
  font-size: clamp(0.72rem, 2.2vw, 0.92rem);
  letter-spacing: 0.35em;
  margin: 0;
  text-transform: uppercase;
}

.approval-celebration-card h3 {
  color: #104e42;
  font-size: clamp(1.55rem, 4vw, 2.15rem);
  line-height: 1.1;
  margin: 0;
}

.approval-level {
  margin: clamp(0.35rem, 2vw, 1rem) auto;
  max-width: 550px;
  padding: 1rem 0.4rem;
  transform: rotate(-6deg);
  transform-origin: center;
  width: 100%;
  animation: approval-level-straighten 900ms cubic-bezier(.22,.9,.27,1) forwards;
}

.approval-level__body {
  background: #d8f080;
  border: clamp(7px, 1.6vw, 12px) solid #104e42;
  border-radius: 2.6rem;
  height: clamp(105px, 19vw, 145px);
  overflow: hidden;
  position: relative;
}

.approval-level__guide {
  background: rgb(16 78 66 / 0.33);
  bottom: 0;
  position: absolute;
  top: 0;
  width: 4px;
}

.approval-level__guide--left { left: 38%; }
.approval-level__guide--right { right: 38%; }

.approval-level__window {
  background: rgb(246 242 231 / 0.38);
  border: 6px solid #104e42;
  border-radius: 2rem;
  inset: 16% 28%;
  position: absolute;
}

.approval-level__bubble {
  background: #f6f2e7;
  border: 6px solid #104e42;
  border-radius: 999px;
  height: clamp(54px, 9vw, 68px);
  left: 28%;
  position: absolute;
  top: 50%;
  transform: translate(-50%, -50%);
  width: clamp(54px, 9vw, 68px);
  animation: approval-bubble-center 900ms cubic-bezier(.22,.9,.27,1) forwards;
}

.approval-celebration-copy {
  display: grid;
  gap: 0.35rem;
  min-height: 4.2rem;
}

.approval-celebration-copy strong {
  color: #104e42;
  font-size: clamp(1.45rem, 4vw, 2.15rem);
  line-height: 1.15;
}

.approval-celebration-copy span {
  color: #75827b;
  font-size: clamp(1rem, 2.8vw, 1.25rem);
  line-height: 1.45;
}

.approval-celebration-actions {
  display: grid;
  gap: 0.8rem;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin-top: 0.25rem;
}

.approval-celebration-button {
  border-radius: 999px;
  font-size: clamp(1rem, 3vw, 1.25rem);
  min-height: 3.5rem;
}

.approval-celebration-card .btn-primary {
  background: #104e42;
  border-color: #104e42;
  color: #fff;
}

.approval-celebration-card .btn-secondary {
  background: transparent;
  border-color: #104e42;
  color: #104e42;
}

.approval-celebration-replay {
  appearance: none;
  background: transparent;
  border: 0;
  color: #104e42;
  cursor: pointer;
  font: inherit;
  font-size: 0.9rem;
  justify-self: center;
  padding: 0.45rem 0.65rem;
  text-decoration: underline;
  text-underline-offset: 0.2em;
}

.approval-celebration-replay:focus-visible,
.approval-celebration-button:focus-visible {
  outline: 3px solid var(--focus-ring);
  outline-offset: 3px;
}

@keyframes approval-level-straighten {
  0% { transform: rotate(-6deg); }
  65% { transform: rotate(1.2deg); }
  100% { transform: rotate(0deg); }
}

@keyframes approval-bubble-center {
  0% { left: 28%; }
  65% { left: 53%; }
  100% { left: 50%; }
}

@media (max-width: 560px) {
  .approval-celebration-backdrop {
    align-items: flex-end;
    padding: 0.75rem;
  }

  .approval-celebration-card {
    border-radius: 2rem;
    gap: 0.85rem;
    max-height: 94dvh;
    padding: 1.35rem 1rem 1.15rem;
  }

  .approval-level { padding-block: 0.35rem; }
  .approval-level__body { border-width: 7px; border-radius: 2rem; }
  .approval-level__window { border-width: 5px; inset: 17% 28%; }
  .approval-level__bubble { border-width: 5px; }
  .approval-celebration-actions { grid-template-columns: 1fr; }
  .approval-celebration-button { width: 100%; }
}

@media (prefers-reduced-motion: reduce) {
  .approval-level,
  .approval-level__bubble { animation: none; }
  .approval-level { transform: none; }
  .approval-level__bubble { left: 50%; }
}
'@
  Add-Content -Path $cssPath -Value $cssAddition -Encoding utf8
}

Write-Host "WOR-449: WATER-gamification er anvendt i den lokale checkout." -ForegroundColor Green
Write-Host "Ændret: src/FE/src/features/jobs/routes/AdminCompletedJobReport.tsx"
Write-Host "Ændret: src/FE/src/features/jobs/routes/AdminCompletedJobReport.css"

if ($Start) {
  $demoScript = Join-Path $repoPath "scripts\demo.ps1"
  if (-not (Test-Path $demoScript)) { throw "Kan ikke finde $demoScript" }
  Write-Host "Starter Workslip via det vedligeholdte Docker-script..." -ForegroundColor Cyan
  & powershell -ExecutionPolicy Bypass -File $demoScript
}
