(() => {
  const toggles = Array.from(document.querySelectorAll('[data-pricing-toggle]'));
  if (!toggles.length) return;

  const totalValue = document.getElementById('pricing-total-value');
  const totalRow = document.getElementById('pricing-total-row');
  const summaryLines = document.getElementById('pricing-summary-lines');
  const emptyState = document.getElementById('pricing-summary-empty');
  const primaryCta = document.getElementById('pricing-primary-cta');

  const formatDkk = (value) => new Intl.NumberFormat('da-DK', {
    maximumFractionDigits: 0,
  }).format(value);

  const render = () => {
    const selected = toggles
      .filter((toggle) => toggle.checked)
      .map((toggle) => {
        const module = toggle.closest('[data-module]');
        return {
          id: module.dataset.module,
          label: module.dataset.label,
          price: Number(module.dataset.price || 0),
        };
      });

    const total = selected.reduce((sum, item) => sum + item.price, 0);
    totalValue.textContent = formatDkk(total);
    totalRow.textContent = `${formatDkk(total)} kr./md.`;

    summaryLines.querySelectorAll('.pricing-summary-line').forEach((line) => line.remove());
    emptyState.hidden = selected.length > 0;

    selected.forEach((item) => {
      const line = document.createElement('div');
      line.className = 'pricing-summary-line';

      const label = document.createElement('strong');
      label.textContent = item.label;

      const price = document.createElement('span');
      price.textContent = `${formatDkk(item.price)} kr.`;

      line.append(label, price);
      summaryLines.appendChild(line);
    });

    const params = new URLSearchParams();
    if (selected.length) params.set('modules', selected.map((item) => item.id).join(','));
    params.set('monthly', String(total));
    primaryCta.href = `${primaryCta.dataset.baseHref || primaryCta.getAttribute('href').split('?')[0]}?${params.toString()}`;
  };

  primaryCta.dataset.baseHref = primaryCta.getAttribute('href').split('?')[0];
  toggles.forEach((toggle) => toggle.addEventListener('change', render));
  render();
})();
