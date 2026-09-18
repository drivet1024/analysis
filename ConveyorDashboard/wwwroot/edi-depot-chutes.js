(() => {
  const $ = id => document.getElementById(id);
  const dialog = $('depot-chutes-dialog');
  if (!dialog) return;
  const number = new Intl.NumberFormat('fr-CA');
  const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
  let context = null, selected = null, configuration = null, version = 0;
  async function load() {
    const request = ++version;
    $('depot-chutes-status').textContent = 'Chargement de la répartition…';
    $('depot-chutes-body').replaceChildren();
    $('depot-chutes-foot').replaceChildren();
    const query = new URLSearchParams({ date: context.date });
    if (configuration != null) query.set('configurationId', configuration);
    try {
      const response = await fetch(`/api/edi/depots/${selected}/chutes?${query}`, { cache: 'no-store' });
      if (!response.ok) throw new Error('Chutes indisponibles');
      const result = await response.json();
      if (request !== version || !dialog.open) return;
      configuration = result.configurationId;
      $('depot-chutes-title').textContent = `Chutes · ${result.depotName}`;
      $('depot-chutes-period').textContent = `${result.date} · journée dès 4 h · relevé à ${new Date(result.asOf).toLocaleString('fr-CA')}`;
      $('depot-chutes-configuration').innerHTML = '<option value="">Tous les convoyeurs · configurations actives</option>' + result.configurations.map(item => `<option value="${item.id}">${escape(item.name)}${item.active ? ' (active)' : ''}</option>`).join('');
      $('depot-chutes-configuration').value = configuration == null ? '' : String(configuration);
      $('depot-chutes-configuration').disabled = !result.configurations.length;
      $('depot-chutes-status').textContent = `${number.format(result.total)} colis uniques · ${number.format(result.totalPassages)} passages réels · ${configuration == null ? (result.configurations.length ? 'Tous les convoyeurs; destinations selon les configurations actives.' : 'Aucune configuration disponible pour ce dépôt.') : 'Destinations selon la configuration actuelle sélectionnée.'}`;
      $('depot-chutes-body').innerHTML = result.rows.map(row => `<tr><td>${escape(row.conveyorName || '—')}</td><td>${row.chute == null ? 'Chute non renseignée' : number.format(row.chute)}</td><td>${row.destinations.length ? row.destinations.map(escape).join('<br>') : 'Destination non déterminée'}</td><td>${row.localFsas.length ? row.localFsas.map(escape).join(', ') : '—'}</td><td>${number.format(row.parcels)}</td><td>${number.format(row.passages)}</td><td>${number.format(row.unidentifiedPassages)}</td></tr>`).join('') || '<tr><td colspan="7" class="empty-cell">Aucun passage enregistré pour cette journée.</td></tr>';
      $('depot-chutes-foot').innerHTML = `<tr><td colspan="4">Total dédupliqué</td><td>${number.format(result.total)}</td><td>${number.format(result.totalPassages)}</td><td>${number.format(result.rows.reduce((sum, row) => sum + row.unidentifiedPassages, 0))}</td></tr>`;
    } catch {
      if (request === version && dialog.open) $('depot-chutes-status').textContent = 'Répartition indisponible. Fermez puis rouvrez le dépôt pour réessayer.';
    }
  }
  globalThis.updateDepotChuteContext = next => {
    const changed = context?.date !== next.date || context?.asOf !== next.asOf;
    context = next;
    if (dialog.open && changed) {
      if (!next.depots.some(row => row.depotId === selected)) dialog.close();
      else load();
    }
  };
  globalThis.resetDepotChuteContext = date => {
    if (context && context.date !== date) { dialog.close(); context = null; ++version; }
  };
  $('depot-body').addEventListener('click', event => {
    const button = event.target.closest('[data-depot-chutes]');
    if (!button || !context) return;
    const depot = context.depots.find(row => row.depotId === Number(button.dataset.depotChutes));
    if (!depot) return;
    selected = depot.depotId;
    configuration = null;
    $('depot-chutes-title').textContent = `Chutes · ${depot.depotName}`;
    $('depot-chutes-period').textContent = context.date;
    $('depot-chutes-configuration').replaceChildren();
    dialog.showModal();
    load();
  });
  $('depot-chutes-configuration').addEventListener('change', event => {
    configuration = event.target.value ? Number(event.target.value) : null;
    load();
  });
  $('depot-chutes-close').addEventListener('click', () => dialog.close());
  dialog.addEventListener('close', () => { ++version; });
  dialog.addEventListener('click', event => {
    if (event.target !== dialog) return;
    const bounds = dialog.getBoundingClientRect();
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
  });
})();
