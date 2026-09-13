(() => {
  const $ = id => document.getElementById(id);
  const dialog = $('depot-clients-dialog');
  if (!dialog) return;
  const number = new Intl.NumberFormat('fr-CA');
  const percent = new Intl.NumberFormat('fr-CA', { maximumFractionDigits: 1 });
  let context = null, selected = null, data = null, version = 0;
  const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
  function render() {
    const rows = $('depot-clients-all').checked ? data.clients : data.clients.slice(0, 10);
    const subtotal = rows.reduce((sum, row) => sum + row.parcelsToday, 0);
    $('depot-clients-status').textContent = `${rows.length} client(s) affiché(s) sur ${data.clients.length} · ${number.format(data.total)} colis destinés à ce dépôt`;
    $('depot-clients-body').innerHTML = rows.length ? rows.map(row => `<tr><td>${row.customerId > 0 ? number.format(row.customerId) + ' · ' : ''}${escape(row.customerName)}</td><td>${number.format(row.parcelsToday)}</td><td>${data.total ? percent.format(100 * row.parcelsToday / data.total) + ' %' : '—'}</td></tr>`).join('')
      : '<tr><td colspan="3" class="empty-cell">Aucun client avec des colis pour cette journée.</td></tr>';
    $('depot-clients-foot').innerHTML = `<tr><td>Total affiché</td><td>${number.format(subtotal)}</td><td>${data.total ? percent.format(100 * subtotal / data.total) + ' %' : '—'}</td></tr>`;
  }
  async function load() {
    const request = ++version;
    data = null;
    $('depot-clients-status').textContent = 'Chargement des clients…';
    $('depot-clients-body').replaceChildren();
    $('depot-clients-foot').replaceChildren();
    $('depot-clients-period').textContent = context.date;
    try {
      const response = await fetch(`/api/edi/depots/${selected}/clients?${new URLSearchParams({ date: context.date })}`, { cache: 'no-store' });
      if (!response.ok) throw new Error('Chargement impossible');
      const result = await response.json();
      if (request !== version || !dialog.open) return;
      data = result;
      $('depot-clients-title').textContent = `Clients · ${result.depotName}`;
      $('depot-clients-period').textContent = `${new Date(result.date + 'T12:00:00').toLocaleDateString('fr-CA')} · journée dès 4 h · relevé à ${new Date(result.asOf).toLocaleString('fr-CA')}`;
      render();
    } catch {
      if (request === version && dialog.open) $('depot-clients-status').textContent = 'Clients indisponibles. Fermez puis rouvrez le dépôt pour réessayer.';
    }
  }
  globalThis.updateDepotClientContext = next => {
    const changed = context?.date !== next.date || context?.asOf !== next.asOf;
    context = next;
    if (dialog.open && changed) {
      if (!next.depots.some(row => row.depotId === selected)) dialog.close();
      else load();
    }
  };
  globalThis.resetDepotClientContext = date => {
    if (context && context.date !== date) { dialog.close(); context = null; ++version; }
  };
  $('depot-body').addEventListener('click', event => {
    const button = event.target.closest('[data-depot-clients]');
    if (!button || !context) return;
    selected = Number(button.dataset.depotClients);
    const depot = context.depots.find(row => row.depotId === selected);
    if (!depot) return;
    $('depot-clients-title').textContent = `Clients · ${depot.depotName}`;
    $('depot-clients-all').checked = false;
    dialog.showModal();
    load();
  });
  $('depot-clients-all').addEventListener('change', () => { if (data) render(); });
  $('depot-clients-close').addEventListener('click', () => dialog.close());
  dialog.addEventListener('close', () => { ++version; data = null; });
  dialog.addEventListener('click', event => { if (event.target === dialog) {
    const box = dialog.getBoundingClientRect();
    if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) dialog.close();
  } });
})();
