(() => {
  const trigger = document.querySelector('.snapshot-today');
  if (!trigger) return;
  trigger.setAttribute('role', 'button');
  trigger.setAttribute('tabindex', '0');
  trigger.setAttribute('aria-haspopup', 'dialog');
  trigger.setAttribute('aria-label', 'Afficher les colis des 30 derniers jours');
  const dialog = document.createElement('dialog');
  dialog.id = 'edi-history-dialog';
  dialog.setAttribute('aria-labelledby', 'edi-history-title');
  dialog.innerHTML = `<div class="section-heading"><div><h2 id="edi-history-title">Colis · 30 derniers jours</h2></div><button type="button" id="edi-history-close">Fermer</button></div>
    <p id="edi-history-status" role="status"></p><div id="edi-history-plot"></div>
    <p>Journées de 4 h à 4 h. La dernière journée est incluse; si elle est en cours, son volume est partiel (en bleu). Survolez une bande ou sélectionnez-la au clavier pour lire son volume.</p>`;
  document.body.append(dialog);
  const $ = id => document.getElementById(id);
  const number = new Intl.NumberFormat('fr-CA');
  let context = null, cached = null, request = 0, controller = null;
  const escape = text => String(text).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;');
  function draw(data) {
    const width = 1260, height = 450, left = 80, top = 30, bottom = 80;
    const plotHeight = height - top - bottom, plotWidth = width - left - 20;
    const max = Math.max(1, ...data.days.map(day => day.parcels));
    const step = plotWidth / data.days.length;
    let svg = `<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="Nombre de colis créés par journée, sur 30 jours">`;
    for (let i = 0; i <= 4; i++) {
      const y = top + plotHeight * (1 - i / 4);
      svg += `<line x1="${left}" y1="${y}" x2="${width - 20}" y2="${y}" stroke="#334856"/><text x="${left - 10}" y="${y + 4}" text-anchor="end" fill="#a5baca" font-size="12">${number.format(Math.round(max * i / 4))}</text>`;
    }
    data.days.forEach((day, i) => {
      const x = left + i * step + 4, h = day.parcels / max * plotHeight;
      const label = `${day.date} : ${number.format(day.parcels)} colis${day.partial ? ' · journée en cours' : ''}`;
      svg += `<g tabindex="0" data-label="${escape(label)}" aria-label="${escape(label)}"><title>${escape(label)}</title><rect x="${x}" y="${top}" width="${step - 8}" height="${plotHeight}" fill="transparent"/><rect x="${x}" y="${top + plotHeight - h}" width="${step - 8}" height="${h}" fill="${day.partial ? '#51a7ff' : '#38dc9a'}"/><text transform="translate(${x + step / 2 - 4},${height - bottom + 18}) rotate(-55)" text-anchor="end" fill="#c2d2dd" font-size="12">${day.date.slice(8)}/${day.date.slice(5, 7)}</text></g>`;
    });
    $('edi-history-plot').innerHTML = svg + '</svg>';
    const status = `${data.days[0].date} au ${data.date} · ${number.format(data.days.reduce((sum, day) => sum + day.parcels, 0))} colis · relevé ${new Date(data.asOf).toLocaleString('fr-CA', { timeZone: 'America/Toronto' })}`;
    $('edi-history-status').textContent = status;
    $('edi-history-plot').querySelectorAll('[data-label]').forEach(bar => {
      const show = () => { $('edi-history-status').textContent = bar.dataset.label; };
      bar.addEventListener('mouseenter', show); bar.addEventListener('focus', show);
      bar.addEventListener('mouseleave', () => { $('edi-history-status').textContent = status; });
      bar.addEventListener('blur', () => { $('edi-history-status').textContent = status; });
    });
  }
  async function load() {
    const version = ++request;
    controller?.abort();
    if (!context) { $('edi-history-status').textContent = 'Chargement des données EDI…'; return; }
    const key = `${context.date}/${context.asOf}`;
    if (cached?.key === key && Date.now() - cached.loadedAt < 60_000) { draw(cached.data); return; }
    controller = new AbortController();
    $('edi-history-status').textContent = 'Chargement des 30 derniers jours…';
    $('edi-history-plot').replaceChildren();
    try {
      const response = await fetch('/api/edi/history?' + new URLSearchParams({ date: context.date }), { signal: controller.signal, cache: 'no-store' });
      if (!response.ok) throw new Error('Historique indisponible');
      const data = await response.json();
      if (version !== request || !dialog.open) return;
      if (data.date !== context.date || data.days?.length !== 30) throw new Error('Historique incomplet');
      draw(data);
      cached = { key, data, loadedAt: Date.now() };
    } catch (error) {
      if (version === request && dialog.open && error.name !== 'AbortError') $('edi-history-status').textContent = 'Historique indisponible. Fermez puis rouvrez pour réessayer.';
    }
  }
  globalThis.updateEdiHistoryContext = next => {
    const changed = context?.date !== next.date || context?.asOf !== next.asOf;
    context = next;
    if (dialog.open && changed) load();
  };
  globalThis.resetEdiHistoryContext = date => { if (context && context.date !== date) { dialog.close(); context = null; } };
  const open = () => { dialog.showModal(); load(); };
  trigger.addEventListener('click', open);
  trigger.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); open(); } });
  $('edi-history-close').addEventListener('click', () => dialog.close());
  dialog.addEventListener('close', () => { ++request; controller?.abort(); trigger.focus(); });
  dialog.addEventListener('click', event => { if (event.target === dialog) {
    const box = dialog.getBoundingClientRect();
    if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) dialog.close();
  } });
})();
