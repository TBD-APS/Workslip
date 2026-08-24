(() => {
  const app = document.querySelector('[data-demo-app]');
  if (!app) return;

  const views = {
    overview: { eyebrow: 'Overblik', title: 'Godmorgen, Anders' },
    tasks: { eyebrow: 'Opgaver', title: 'Dagens opgaver' },
    approvals: { eyebrow: 'Godkendelser', title: 'Afventer dit svar' },
  };
  const defaultState = { view: 'overview', awaiting: 2, task: 'Montering', hours: '1.5', approved: false };
  let state = { ...defaultState };
  let toastTimer;

  const panels = [...app.querySelectorAll('[data-demo-panel]')];
  const viewControls = [...app.querySelectorAll('[data-demo-view]')];
  const navControls = [...app.querySelectorAll('.demo-nav [data-demo-view]')];
  const awaitingNodes = [...app.querySelectorAll('[data-demo-awaiting]')];
  const toast = app.querySelector('[data-demo-toast]');
  const title = app.querySelector('[data-demo-title]');
  const eyebrow = app.querySelector('[data-demo-eyebrow]');
  const taskLabel = app.querySelector('[data-demo-task-label]');
  const taskRows = [...app.querySelectorAll('[data-demo-task]')];
  const approvalStatus = app.querySelector('[data-demo-slip-status]');
  const approvalButton = app.querySelector('[data-demo-action="approve"]');
  const approvalCard = app.querySelector('[data-demo-slip]');
  const timeInput = app.querySelector('[data-demo-time]');

  function showToast(message) {
    window.clearTimeout(toastTimer);
    toast.textContent = message;
    toast.classList.add('is-visible');
    toastTimer = window.setTimeout(() => toast.classList.remove('is-visible'), 4200);
  }

  function render() {
    const currentView = views[state.view];
    panels.forEach((panel) => {
      const active = panel.dataset.demoPanel === state.view;
      panel.hidden = !active;
      panel.classList.toggle('is-active', active);
    });
    viewControls.forEach((control) => {
      control.classList.toggle('is-active', control.dataset.demoView === state.view);
    });
    navControls.forEach((control) => {
      control.setAttribute('aria-pressed', String(control.dataset.demoView === state.view));
    });
    eyebrow.textContent = currentView.eyebrow;
    title.textContent = currentView.title;
    awaitingNodes.forEach((node) => {
      node.textContent = node.closest('.demo-section-heading') ? `${state.awaiting} arbejdssedler` : state.awaiting;
    });
    taskRows.forEach((row) => row.classList.toggle('is-selected', row.dataset.demoTask === state.task));
    taskLabel.textContent = `${state.task} · ${state.task === 'Montering' ? 'Nordic Byg' : state.task === 'Dokumentation' ? 'Vestergaard ApS' : 'Hansen & Søn'}`;
    timeInput.value = state.hours;

    if (state.approved) {
      approvalCard.classList.add('is-approved');
      approvalStatus.textContent = 'Godkendt';
      approvalStatus.className = 'task-status done';
      approvalButton.disabled = true;
      approvalButton.innerHTML = 'Godkendt <span aria-hidden="true">✓</span>';
    } else {
      approvalCard.classList.remove('is-approved');
      approvalStatus.textContent = 'Afventer';
      approvalStatus.className = 'task-status now';
      approvalButton.disabled = false;
      approvalButton.innerHTML = 'Godkend arbejdsseddel <span aria-hidden="true">✓</span>';
    }
  }

  app.addEventListener('click', (event) => {
    const viewControl = event.target.closest('[data-demo-view]');
    if (viewControl) {
      event.preventDefault();
      state.view = viewControl.dataset.demoView;
      render();
      return;
    }

    const taskRow = event.target.closest('[data-demo-task]');
    if (taskRow) {
      state.task = taskRow.dataset.demoTask;
      render();
      showToast(`${state.task} er valgt i demoen.`);
      return;
    }

    const action = event.target.closest('[data-demo-action]')?.dataset.demoAction;
    if (!action) return;

    if (action === 'reset') {
      state = { ...defaultState };
      render();
      showToast('Demoen er nulstillet.');
    }

    if (action === 'approve' && !state.approved) {
      state.approved = true;
      state.awaiting = 1;
      render();
      showToast('Arbejdsseddel #1842 er godkendt i demoen.');
    }

    if (action === 'revision') {
      showToast('I den rigtige løsning kan du sende en rettelse direkte til medarbejderen.');
    }

    if (action === 'save-time') {
      if (timeInput.value === '' || !timeInput.checkValidity()) {
        timeInput.reportValidity();
        showToast('Indtast mellem 0,5 og 12 timer i halve timer.');
        return;
      }
      state.hours = timeInput.value;
      showToast(`${state.hours.replace('.', ',')} time(r) er registreret i demoen – intet er gemt.`);
    }
  });

  render();
})();
