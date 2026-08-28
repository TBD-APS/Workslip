(() => {
  const root = document.querySelector('[data-module-builder]');
  if (!root) return;

  const cards = Array.from(root.querySelectorAll('[data-module]'));
  const core = root.querySelector('[data-module-core]');
  const capabilities = Array.from(root.querySelectorAll('[data-capability]'));
  const countNode = root.querySelector('[data-module-count]');
  const progressBar = root.querySelector('[data-progress-bar]');
  const progressMessage = root.querySelector('[data-progress-message]');
  const resetButton = root.querySelector('[data-reset-modules]');
  const detail = root.querySelector('.module-detail');
  const detailIcon = root.querySelector('[data-detail-icon]');
  const detailEyebrow = root.querySelector('[data-detail-eyebrow]');
  const detailTitle = root.querySelector('[data-detail-title]');
  const detailDescription = root.querySelector('[data-detail-description]');
  const detailList = root.querySelector('[data-detail-list]');
  const coreLevel = root.querySelector('[data-core-level]');
  const connectionStatus = root.querySelector('[data-connection-status]');

  const enabled = new Set();
  let activeDetail = null;
  let activationTimer;

  const moduleCopy = {
    kls: {
      icon: '✓',
      label: 'KLS',
      eyebrow: 'KLS & kvalitetssikring',
      title: 'Dokumentation bliver en del af selve arbejdet.',
      description: 'KLS kobles direkte på sagen, så kontrolpunkter, billeder og godkendelse følger samme workflow som resten af opgaven.',
      bullets: ['Kvalitetssikring på mobilen', 'Billeder samlet på sagen', 'Tydeligt godkendelsesflow']
    },
    inventory: {
      icon: '◇',
      label: 'Lager',
      eyebrow: 'Lager & materialer',
      title: 'Materialerne kobles på jobbet i stedet for et separat regneark.',
      description: 'Lager-modulet gør QR, beholdning og materialeforbrug til en del af Workslips fælles datagrundlag.',
      bullets: ['QR-baseret registrering', 'Materialeforbrug på sagen', 'Bedre lageroverblik']
    },
    time: {
      icon: '◷',
      label: 'Timer',
      eyebrow: 'Timer',
      title: 'Tiden registreres dér, hvor arbejdet allerede sker.',
      description: 'Timer bliver koblet direkte til opgaven og kan bruges videre i godkendelse, rapportering og økonomisk opfølgning.',
      bullets: ['Tid direkte på sagen', 'Mindre dobbeltregistrering', 'Bedre grundlag for opfølgning']
    },
    insights: {
      icon: '▥',
      label: 'Rapportering',
      eyebrow: 'Rapportering & indsigt',
      title: 'Når data hænger sammen, bliver Workslip markant klogere.',
      description: 'Rapportering samler signalerne fra arbejdet og gør dem til et operationelt overblik for ledelsen.',
      bullets: ['Live driftsblik', 'Sammenhæng mellem moduler', 'Bedre beslutningsgrundlag']
    }
  };

  const baseCopy = {
    icon: 'W',
    eyebrow: 'Workslip Core',
    title: 'Start enkelt. Byg videre efter behov.',
    description: 'Core samler det grundlæggende workflow. Tilføj et modul for at se, hvordan nye capabilities kobles på uden at ændre fundamentet.',
    bullets: ['Sager og kunder samlet', 'Samme arbejdsgang på tværs', 'Moduler kan kobles på løbende']
  };

  const levelLabels = ['Core', 'Core + 1', 'Forbundet', 'Avanceret', 'Komplet'];
  const progressMessages = [
    'Din kerne er klar.',
    'Første capability er koblet på.',
    'Modulerne begynder at arbejde sammen.',
    'Platformen er blevet markant stærkere.',
    'Din Workslip-løsning er fuldt bygget.'
  ];

  const setDetail = (moduleName) => {
    const copy = moduleName && moduleCopy[moduleName] ? moduleCopy[moduleName] : baseCopy;
    activeDetail = moduleName || null;
    detailIcon.textContent = copy.icon;
    detailEyebrow.textContent = copy.eyebrow;
    detailTitle.textContent = copy.title;
    detailDescription.textContent = copy.description;
    detailList.replaceChildren(...copy.bullets.map((text) => {
      const item = document.createElement('li');
      item.textContent = text;
      return item;
    }));
    detail.classList.toggle('has-selection', Boolean(moduleName));
  };

  const setConnectionStatus = (message) => {
    connectionStatus.textContent = message;
  };

  const pulseCore = (moduleName) => {
    const capability = capabilities.find((item) => item.dataset.capability === moduleName);

    window.clearTimeout(activationTimer);
    core.classList.remove('is-activating');
    capability?.classList.remove('is-new');

    window.requestAnimationFrame(() => {
      core.classList.add('is-activating');
      capability?.classList.add('is-new');

      if (enabled.size > 1) {
        core.classList.add('is-flowing');
      }
    });

    activationTimer = window.setTimeout(() => {
      core.classList.remove('is-activating', 'is-flowing');
      capability?.classList.remove('is-new');
    }, 920);
  };

  const render = () => {
    const count = enabled.size;

    cards.forEach((card) => {
      const moduleName = card.dataset.module;
      const isEnabled = enabled.has(moduleName);
      card.classList.toggle('is-active', isEnabled);
      card.setAttribute('aria-pressed', String(isEnabled));
    });

    capabilities.forEach((capability) => {
      const isEnabled = enabled.has(capability.dataset.capability);
      capability.setAttribute('aria-hidden', String(!isEnabled));
    });

    core.dataset.level = String(count);
    core.classList.toggle('has-modules', count > 0);
    countNode.textContent = String(count);
    coreLevel.textContent = levelLabels[count];
    progressMessage.textContent = progressMessages[count];
    progressBar.style.width = `${(count / cards.length) * 100}%`;
    resetButton.disabled = count === 0;

    if (activeDetail && !enabled.has(activeDetail)) {
      const lastEnabled = Array.from(enabled).at(-1);
      setDetail(lastEnabled || null);
    }
  };

  const addModule = (moduleName) => {
    if (!moduleCopy[moduleName]) return;
    if (enabled.has(moduleName)) {
      setDetail(moduleName);
      setConnectionStatus(`${moduleCopy[moduleName].label} er allerede koblet på`);
      return;
    }

    enabled.add(moduleName);
    setDetail(moduleName);
    render();
    setConnectionStatus(`${moduleCopy[moduleName].label} er koblet på`);
    pulseCore(moduleName);
  };

  const toggleModule = (moduleName) => {
    if (!moduleCopy[moduleName]) return;
    if (enabled.has(moduleName)) {
      enabled.delete(moduleName);
      setDetail(Array.from(enabled).at(-1) || null);
      render();
      setConnectionStatus(`${moduleCopy[moduleName].label} er fjernet`);
      pulseCore();
    } else {
      addModule(moduleName);
    }
  };

  cards.forEach((card) => {
    const moduleName = card.dataset.module;

    card.addEventListener('click', () => toggleModule(moduleName));

    card.addEventListener('dragstart', (event) => {
      card.classList.add('is-dragging');
      event.dataTransfer.effectAllowed = 'copy';
      event.dataTransfer.setData('text/workslip-module', moduleName);
      event.dataTransfer.setData('text/plain', moduleName);
    });

    card.addEventListener('dragend', () => {
      card.classList.remove('is-dragging');
      core.classList.remove('is-drop-target');
    });
  });

  core.addEventListener('dragenter', (event) => {
    event.preventDefault();
    core.classList.add('is-drop-target');
  });

  core.addEventListener('dragover', (event) => {
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
    core.classList.add('is-drop-target');
  });

  core.addEventListener('dragleave', (event) => {
    if (!core.contains(event.relatedTarget)) {
      core.classList.remove('is-drop-target');
    }
  });

  core.addEventListener('drop', (event) => {
    event.preventDefault();
    core.classList.remove('is-drop-target');
    const moduleName = event.dataTransfer.getData('text/workslip-module') || event.dataTransfer.getData('text/plain');
    addModule(moduleName);
  });

  core.addEventListener('keydown', (event) => {
    if (event.key !== 'Enter' && event.key !== ' ') return;
    event.preventDefault();
    const nextCard = cards.find((card) => !enabled.has(card.dataset.module));
    if (nextCard) addModule(nextCard.dataset.module);
  });

  resetButton.addEventListener('click', () => {
    enabled.clear();
    setDetail(null);
    render();
    setConnectionStatus('Kernen er klar til nye moduler');
    pulseCore();
  });

  setDetail(null);
  render();
})();
