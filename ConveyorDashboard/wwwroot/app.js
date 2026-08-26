const $ = selector => document.querySelector(selector);
const number = new Intl.NumberFormat('fr-CA');
const percent = new Intl.NumberFormat('fr-CA', { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 });
const decimal = new Intl.NumberFormat('fr-CA', { maximumFractionDigits: 2 });
const dateTime = new Intl.DateTimeFormat('fr-CA', { dateStyle: 'short', timeStyle: 'short' });
const palette = ['#1463ff', '#f28c28', '#7357d8', '#16845b', '#d05776'];

const state = { daily: null, customers: null };
const els = {
  date: $('#analysis-date'), conveyor: $('#conveyor'), refresh: $('#refresh'), error: $('#error'),
  dbStatus: $('#db-status'), scopeStatus: $('#scope-status'), metricGroups: $('#metric-groups'),
  dailyBody: $('#daily-body'), weightGrid: $('#weight-grid'), hourlyChart: $('#hourly-chart'), hourlyLegend: $('#hourly-legend'),
  repeatChart: $('#repeat-chart'), repeatLegend: $('#repeat-legend'), repeatPeaks: $('#repeat-peaks'),
  minVolume: $('#min-volume'), sort: $('#customer-sort'), search: $('#customer-search'), loadCustomers: $('#load-customers'),
  customerSummary: $('#customer-summary'), customerBody: $('#customer-body'), customerChart: $('#customer-chart'),
  loadException: $('#load-exception'), exceptionContent: $('#exception-content'), dialog: $('#detail-dialog'),
  analyzeAi: $('#analyze-ai'), aiOutput: $('#ai-output'),
  detailTitle: $('#detail-title'), detailSummary: $('#detail-summary'), detailBody: $('#detail-body'), closeDetail: $('#close-detail'),
};

function yesterday() {
  const d = new Date(); d.setDate(d.getDate() - 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function setStatus(element, ok, okText, failText) {
  element.textContent = ok ? okText : failText;
  element.className = `status ${ok ? 'status-ok' : 'status-error'}`;
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>'"]/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[char]);
}

function apiError(payload, fallback) { return payload?.detail || payload?.error || fallback; }
function rate(value) { return value == null ? '<span class="na">N/A</span>' : percent.format(value); }
function countRate(count, value) { return value == null ? '<span class="na">N/A</span>' : `<strong>${percent.format(value)}</strong><small>${number.format(count)} colis</small>`; }

async function init() {
  els.date.value = yesterday();
  try {
    const response = await fetch('/api/status'); const status = await response.json();
    setStatus(els.dbStatus, status.databaseConnected, 'Base de données · connectée', 'Base de données · erreur');
  } catch { setStatus(els.dbStatus, false, '', 'Base de données · non vérifiée'); }
  await loadDaily();
}

async function loadDaily() {
  hideError();
  els.refresh.disabled = true;
  els.scopeStatus.textContent = 'Calcul des KPI…'; els.scopeStatus.className = 'status status-waiting';
  els.metricGroups.innerHTML = '<div class="loading-card">Lecture des passages et regroupement par colis…</div>';
  try {
    const params = new URLSearchParams({ date: els.date.value, conveyor: els.conveyor.value });
    const response = await fetch(`/api/daily?${params}`); const payload = await response.json();
    if (!response.ok) throw new Error(apiError(payload, 'Analyse indisponible.'));
    state.daily = payload;
    renderDaily(payload);
    setStatus(els.scopeStatus, true, `Journée · ${els.date.value}`, '');
  } catch (error) {
    showError(error.message); els.metricGroups.innerHTML = '';
    setStatus(els.scopeStatus, false, '', 'Calcul impossible');
  } finally { els.refresh.disabled = false; }
}

function renderDaily(data) {
  els.metricGroups.innerHTML = data.metrics.map(metric => `
    <article class="conveyor-block">
      <div class="conveyor-heading"><div><span>${escapeHtml(metric.site)}</span><h3>${escapeHtml(metric.conveyorName)}</h3></div><span class="volume-pill">${number.format(metric.uniqueParcels)} colis · ${number.format(metric.passages)} passages</span></div>
      <div class="metric-grid metric-grid-six">
        ${metricCard('Colis uniques', number.format(metric.uniqueParcels), 'total', metric, 'Dénominateur des taux')}
        ${metricCard('Recirculés', rate(metric.recirculationRate), 'recirculated', metric, `${number.format(metric.recirculated)} colis`)}
        ${metricCard('Chute 98', rate(metric.chute98Rate), 'chute98', metric, `${number.format(metric.chute98)} colis`)}
        ${metricCard('Même chute répétée', rate(metric.sameChuteRepeatedRate), 'sameChute', metric, `${number.format(metric.sameChuteRepeated)} colis · chute 98 exclue`)}
        ${metricCard('Sans poids', rate(metric.noWeightRate), 'noWeight', metric, metric.noWeight == null ? 'Non mesuré sur ce site' : `${number.format(metric.noWeight)} colis`)}
        ${metricCard('Sans dimensions', rate(metric.noDimensionsRate), 'noDimensions', metric, metric.noDimensions == null ? 'Non mesuré sur ce site' : `${number.format(metric.noDimensions)} colis`)}
      </div>
    </article>`).join('');

  els.dailyBody.innerHTML = data.metrics.map(m => `<tr>
    <td>${escapeHtml(m.conveyorName)}</td><td>${number.format(m.uniqueParcels)}</td><td>${number.format(m.passages)}</td>
    <td>${countRate(m.recirculated, m.recirculationRate)}</td><td>${countRate(m.chute98, m.chute98Rate)}</td><td>${countRate(m.sameChuteRepeated, m.sameChuteRepeatedRate)}</td>
    <td>${countRate(m.noWeight, m.noWeightRate)}</td><td>${countRate(m.noDimensions, m.noDimensionsRate)}</td></tr>`).join('');
  renderWeights(data.metrics);
  drawHourly(data.hourly, data.metrics);
  drawRepeatHourly(data.hourly, data.metrics);
}

function metricCard(label, value, metricName, metric, context) {
  const disabled = value.includes('N/A');
  return `<button class="metric-card detail-trigger" type="button" data-metric="${metricName}" data-conveyor="${metric.conveyorKey}" ${disabled ? 'disabled' : ''}>
    <span class="metric-label">${label}</span><strong class="metric-value">${value}</strong><span class="metric-context">${escapeHtml(context)}</span><span class="metric-action">${disabled ? 'Non applicable' : 'Voir les colis →'}</span></button>`;
}

function renderWeights(metrics) {
  const bins = [['under1', '< 1 lb'], ['from1To3', '1 à 3 lb'], ['over3To5', '> 3 à 5 lb'], ['over5To10', '> 5 à 10 lb'], ['over10', '> 10 lb']];
  els.weightGrid.innerHTML = metrics.map(m => {
    if (!m.supportsMeasurements) return `<article class="weight-card"><h3>${escapeHtml(m.conveyorName)}</h3><div class="empty-state compact">Non applicable à Gilmore.</div></article>`;
    const total = bins.reduce((sum, [key]) => sum + (m[key] || 0), 0);
    return `<article class="weight-card"><h3>${escapeHtml(m.conveyorName)}</h3><p>${number.format(total)} colis avec un dernier poids valide</p>
      <div class="stacked-bar" aria-label="Distribution des poids">${bins.map(([key], index) => `<i style="width:${total ? (m[key] / total * 100) : 0}%;background:${palette[index]}" title="${bins[index][1]}"></i>`).join('')}</div>
      <div class="weight-list">${bins.map(([key, label], index) => `<div><span><i style="background:${palette[index]}"></i>${label}</span><strong>${total ? percent.format(m[key] / total) : '0 %'}</strong><small>${number.format(m[key] || 0)}</small></div>`).join('')}</div></article>`;
  }).join('');
}

function drawHourly(points, metrics) {
  const canvas = els.hourlyChart; const ctx = canvas.getContext('2d');
  const rect = canvas.getBoundingClientRect(); const dpr = window.devicePixelRatio || 1;
  canvas.width = Math.max(640, rect.width) * dpr; canvas.height = Math.max(320, rect.height) * dpr; ctx.scale(dpr, dpr);
  const width = canvas.width / dpr, height = canvas.height / dpr, pad = { l: 60, r: 24, t: 24, b: 48 };
  ctx.clearRect(0, 0, width, height);
  if (!points.length) { ctx.fillStyle = '#617087'; ctx.fillText('Aucun passage dans cette période.', pad.l, 50); return; }
  const maxHour = Math.max(...points.map(p => p.operationalHour)); const maxY = Math.max(...points.map(p => p.passages), 1) * 1.1;
  ctx.font = '12px system-ui'; ctx.strokeStyle = '#dfe6ef'; ctx.fillStyle = '#617087'; ctx.lineWidth = 1;
  for (let i = 0; i <= 4; i++) { const y = pad.t + (height - pad.t - pad.b) * i / 4; ctx.beginPath(); ctx.moveTo(pad.l, y); ctx.lineTo(width - pad.r, y); ctx.stroke(); ctx.textAlign = 'right'; ctx.fillText(number.format(Math.round(maxY * (1 - i / 4))), pad.l - 9, y + 4); }
  const x = h => pad.l + (width - pad.l - pad.r) * h / Math.max(maxHour, 1); const y = v => height - pad.b - (height - pad.t - pad.b) * v / maxY;
  for (let h = 0; h <= maxHour; h += 2) { ctx.textAlign = 'center'; ctx.fillText(`H+${h}`, x(h), height - 20); }
  metrics.forEach((metric, index) => {
    const series = points.filter(p => p.conveyorKey === metric.conveyorKey).sort((a, b) => a.operationalHour - b.operationalHour);
    ctx.strokeStyle = palette[index % palette.length]; ctx.fillStyle = palette[index % palette.length]; ctx.lineWidth = 2.5; ctx.beginPath();
    series.forEach((p, i) => { if (i) ctx.lineTo(x(p.operationalHour), y(p.passages)); else ctx.moveTo(x(p.operationalHour), y(p.passages)); }); ctx.stroke();
    series.forEach(p => { ctx.beginPath(); ctx.arc(x(p.operationalHour), y(p.passages), 3, 0, Math.PI * 2); ctx.fill(); });
  });
  els.hourlyLegend.innerHTML = metrics.map((m, i) => `<span><i style="background:${palette[i % palette.length]}"></i>${escapeHtml(m.conveyorName)}</span>`).join('');
}

function drawRepeatHourly(points, metrics) {
  const canvas = els.repeatChart; const ctx = canvas.getContext('2d');
  const rect = canvas.getBoundingClientRect(); const dpr = window.devicePixelRatio || 1;
  canvas.width = Math.max(680, rect.width) * dpr; canvas.height = Math.max(320, rect.height) * dpr; ctx.scale(dpr, dpr);
  const width = canvas.width / dpr, height = canvas.height / dpr, pad = { l: 58, r: 24, t: 24, b: 58 };
  ctx.clearRect(0, 0, width, height);
  const buckets = [...new Set(points.map(p => p.bucketStart))].sort((a, b) => new Date(a) - new Date(b));
  const maxY = Math.max(...points.map(p => p.sameChuteRepeated || 0), 1);

  const peakCards = metrics.map((metric, metricIndex) => {
    const series = points.filter(p => p.conveyorKey === metric.conveyorKey);
    const peak = series.reduce((best, point) => !best || point.sameChuteRepeated > best.sameChuteRepeated ? point : best, null);
    const peakCount = peak?.sameChuteRepeated || 0;
    const share = metric.sameChuteRepeated ? peakCount / metric.sameChuteRepeated : 0;
    return `<article><i style="background:${palette[metricIndex % palette.length]}"></i><span>${escapeHtml(metric.conveyorName)}</span><strong>${peakCount ? formatHour(peak.bucketStart) : 'Aucune répétition'}</strong><small>${peakCount ? `${number.format(peakCount)} colis · ${percent.format(peak.sameChuteRepeatedRate)} des colis actifs à cette heure · ${percent.format(share)} du total quotidien` : '0 colis pour la journée'}</small></article>`;
  });
  els.repeatPeaks.innerHTML = peakCards.join('');
  els.repeatLegend.innerHTML = metrics.map((m, i) => `<span><i style="background:${palette[i % palette.length]}"></i>${escapeHtml(m.conveyorName)}</span>`).join('');

  ctx.font = '12px system-ui'; ctx.strokeStyle = '#dfe6ef'; ctx.fillStyle = '#617087'; ctx.lineWidth = 1;
  for (let i = 0; i <= 4; i++) {
    const y = pad.t + (height - pad.t - pad.b) * i / 4;
    ctx.beginPath(); ctx.moveTo(pad.l, y); ctx.lineTo(width - pad.r, y); ctx.stroke();
    ctx.textAlign = 'right'; ctx.fillText(number.format(Math.round(maxY * (1 - i / 4))), pad.l - 9, y + 4);
  }
  if (!buckets.length) { ctx.textAlign = 'left'; ctx.fillText('Aucune donnée horaire.', pad.l, 50); return; }
  const plotWidth = width - pad.l - pad.r;
  const groupWidth = plotWidth / buckets.length;
  const usableWidth = groupWidth * .74;
  const barWidth = Math.max(2, usableWidth / metrics.length);
  const y = value => height - pad.b - (height - pad.t - pad.b) * value / maxY;
  buckets.forEach((bucket, bucketIndex) => {
    const center = pad.l + groupWidth * (bucketIndex + .5);
    metrics.forEach((metric, metricIndex) => {
      const point = points.find(p => p.conveyorKey === metric.conveyorKey && p.bucketStart === bucket);
      const value = point?.sameChuteRepeated || 0;
      const x = center - usableWidth / 2 + metricIndex * barWidth;
      ctx.fillStyle = palette[metricIndex % palette.length];
      ctx.fillRect(x, y(value), Math.max(1, barWidth - 1), height - pad.b - y(value));
    });
    if (bucketIndex % Math.max(1, Math.ceil(buckets.length / 10)) === 0) {
      ctx.save(); ctx.translate(center, height - pad.b + 14); ctx.rotate(-Math.PI / 5); ctx.textAlign = 'right'; ctx.fillStyle = '#617087'; ctx.fillText(formatHour(bucket), 0, 0); ctx.restore();
    }
  });
}

function formatHour(value) {
  const d = new Date(value); const selected = new Date(`${els.date.value}T00:00:00`);
  const nextDay = d.getDate() !== selected.getDate();
  return `${String(d.getHours()).padStart(2, '0')} h${nextDay ? ' (+1)' : ''}`;
}

async function loadCustomers() {
  hideError(); els.loadCustomers.disabled = true; els.customerBody.innerHTML = '<tr><td colspan="14">Calcul du classement client…</td></tr>';
  try {
    const params = new URLSearchParams({ date: els.date.value, conveyor: els.conveyor.value, minVolume: els.minVolume.value || '100', sort: els.sort.value, search: els.search.value });
    const response = await fetch(`/api/customers?${params}`); const payload = await response.json();
    if (!response.ok) throw new Error(apiError(payload, 'Analyse client indisponible.'));
    state.customers = payload; renderCustomers(payload);
  } catch (error) { showError(error.message); els.customerBody.innerHTML = `<tr><td colspan="14">${escapeHtml(error.message)}</td></tr>`; }
  finally { els.loadCustomers.disabled = false; }
}

function renderCustomers(data) {
  const total = data.conveyorParcelsWithCustomer + data.conveyorParcelsWithoutCustomer;
  els.customerSummary.innerHTML = [
    ['Clients affichés', number.format(data.customers.length)], ['Colis avec client', number.format(data.conveyorParcelsWithCustomer)],
    ['Colis sans client', number.format(data.conveyorParcelsWithoutCustomer)], ['Couverture client', total ? percent.format(data.conveyorParcelsWithCustomer / total) : '0 %']
  ].map(([label, value]) => `<article class="customer-summary-card"><span>${label}</span><strong>${value}</strong></article>`).join('');
  els.customerBody.innerHTML = data.customers.length ? data.customers.map(c => `<tr>
    <td><span class="customer-name">${escapeHtml(c.customerName)}</span><small>#${number.format(c.customerId)}</small></td>
    <td>${number.format(c.totalParcelsGiven)}</td><td>${number.format(c.conveyorParcels)}<small>${percent.format(c.conveyorCoverageRate)} du total</small></td>
    <td><strong>${percent.format(c.problemVsTotalRate)}</strong><small>${number.format(c.problemParcels)} colis</small></td><td>${percent.format(c.problemOnConveyorRate)}</td>
    <td>${percent.format(c.recirculationRate)}</td><td>${percent.format(c.chute98Rate)}</td><td>${percent.format(c.sameChuteRepeatedRate)}</td>
    <td>${rate(c.noWeightRate)}</td><td>${rate(c.noDimensionsRate)}</td><td>${percent.format(c.veryLightRate)}</td><td>${percent.format(c.verySmallFormatRate)}</td><td>${percent.format(c.atypicalFormatRate)}</td>
    <td><button class="link-button customer-detail" type="button" data-customer="${c.customerId}">Voir</button></td></tr>`).join('') : '<tr><td colspan="14">Aucun client ne satisfait les filtres.</td></tr>';
  drawCustomerChart(data.customers.slice(0, 10));
}

function drawCustomerChart(customers) {
  const canvas = els.customerChart, ctx = canvas.getContext('2d'), rect = canvas.getBoundingClientRect(), dpr = window.devicePixelRatio || 1;
  canvas.width = Math.max(700, rect.width) * dpr; canvas.height = Math.max(380, rect.height) * dpr; ctx.scale(dpr, dpr);
  const width = canvas.width / dpr, height = canvas.height / dpr; ctx.clearRect(0, 0, width, height);
  if (!customers.length) return;
  const pad = { l: Math.min(240, width * .32), r: 70, t: 16, b: 36 }, max = Math.max(...customers.map(c => c.problemVsTotalRate), .01), row = (height - pad.t - pad.b) / customers.length;
  ctx.font = '12px system-ui';
  customers.forEach((c, i) => { const y = pad.t + i * row + row * .2, h = row * .56, w = (width - pad.l - pad.r) * c.problemVsTotalRate / max;
    ctx.textAlign = 'right'; ctx.fillStyle = '#14233a'; ctx.fillText(c.customerName.slice(0, 30), pad.l - 10, y + h * .7);
    ctx.fillStyle = '#1463ff'; ctx.fillRect(pad.l, y, w, h); ctx.textAlign = 'left'; ctx.fillStyle = '#14233a'; ctx.font = '700 12px system-ui'; ctx.fillText(percent.format(c.problemVsTotalRate), pad.l + w + 8, y + h * .7); ctx.font = '12px system-ui';
  });
}

async function loadException25() {
  els.loadException.disabled = true; els.exceptionContent.innerHTML = '<div class="loading-card">Analyse des sept journées et rapprochement avec l’exception 25…</div>';
  try {
    const params = new URLSearchParams({ endDate: els.date.value, conveyor: els.conveyor.value });
    const response = await fetch(`/api/exception25?${params}`); const data = await response.json();
    if (!response.ok) throw new Error(apiError(data, 'Corrélation indisponible.'));
    const g = data.global;
    els.exceptionContent.innerHTML = `<div class="correlation-grid">
      <article><span>Avec exception 25</span><strong>${percent.format(g.rateWith)}</strong><small>${number.format(g.withException25Problems)} problèmes / ${number.format(g.withException25)} colis</small></article>
      <article><span>Sans exception 25</span><strong>${percent.format(g.rateWithout)}</strong><small>${number.format(g.withoutException25Problems)} problèmes / ${number.format(g.withoutException25)} colis</small></article>
      <article><span>Écart</span><strong>${decimal.format(g.differencePoints)} pts</strong><small>Ratio ${g.rateRatio == null ? 'N/A' : `${decimal.format(g.rateRatio)}×`}</small></article>
      <article class="classification"><span>Conclusion statistique</span><strong>${escapeHtml(g.classification)}</strong><small>Association, pas causalité</small></article></div>
      <div class="table-wrap"><table><thead><tr><th>Client</th><th>Colis convoyeur</th><th>Problèmes</th><th>Exception 25</th><th>Exception 25 + problème</th></tr></thead><tbody>${data.customers.slice(0, 25).map(c => `<tr><td>${escapeHtml(c.customerName)}</td><td>${number.format(c.conveyorParcels)}</td><td>${number.format(c.problemParcels)}</td><td>${number.format(c.exception25Parcels)}</td><td>${number.format(c.exception25AndProblemParcels)}</td></tr>`).join('')}</tbody></table></div>`;
  } catch (error) { els.exceptionContent.textContent = error.message; }
  finally { els.loadException.disabled = false; }
}

async function analyzeAi() {
  els.analyzeAi.disabled = true; els.aiOutput.textContent = 'OpenAI analyse les agrégats et prépare les vérifications terrain…';
  try {
    const response = await fetch('/api/ai-analysis', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ date: els.date.value, conveyor: els.conveyor.value, minVolume: Number(els.minVolume.value || 100) }) });
    const data = await response.json(); if (!response.ok) throw new Error(apiError(data, 'Analyse OpenAI indisponible.'));
    els.aiOutput.textContent = data.analysis;
  } catch (error) { els.aiOutput.textContent = `Analyse indisponible : ${error.message}`; }
  finally { els.analyzeAi.disabled = false; }
}

async function openDetails(metric, conveyor, customerId = null) {
  els.dialog.showModal(); els.detailTitle.textContent = 'Chargement du détail…'; els.detailBody.innerHTML = '<tr><td colspan="9">Calcul…</td></tr>';
  try {
    const params = new URLSearchParams({ date: els.date.value, conveyor, metric, page: '1', pageSize: '100' });
    if (customerId) params.set('customerId', customerId);
    const response = await fetch(`/api/details?${params}`); const data = await response.json();
    if (!response.ok) throw new Error(apiError(data, 'Détail indisponible.'));
    els.detailTitle.textContent = `Détail — ${metricLabel(metric)}`; els.detailSummary.textContent = `${number.format(data.totalRows)} colis trouvés; les 100 premiers sont affichés.`;
    els.detailBody.innerHTML = data.rows.map(r => `<tr><td>${number.format(r.parcelId)}</td><td>${escapeHtml(r.customerName)}${r.customerId ? `<small>#${r.customerId}</small>` : ''}</td><td>${escapeHtml(r.conveyorName)}</td><td>${number.format(r.passages)}</td><td>${dateTime.format(new Date(r.firstPassage))}</td><td>${dateTime.format(new Date(r.lastPassage))}</td><td>${escapeHtml(r.chutes)}</td><td>${r.latestValidWeight == null ? '—' : decimal.format(r.latestValidWeight)}</td><td>${[r.latestValidLength, r.latestValidWidth, r.latestValidHeight].every(v => v != null) ? `${decimal.format(r.latestValidLength)} × ${decimal.format(r.latestValidWidth)} × ${decimal.format(r.latestValidHeight)}` : '—'}</td></tr>`).join('') || '<tr><td colspan="9">Aucun colis.</td></tr>';
  } catch (error) { els.detailTitle.textContent = 'Détail indisponible'; els.detailBody.innerHTML = `<tr><td colspan="9">${escapeHtml(error.message)}</td></tr>`; }
}

function metricLabel(metric) { return ({ total: 'tous les colis', recirculated: 'recirculés', chute98: 'chute 98', sameChute: 'même chute répétée', noWeight: 'sans poids', noDimensions: 'sans dimensions' })[metric] || metric; }
function showError(message) { els.error.textContent = message; els.error.hidden = false; }
function hideError() { els.error.hidden = true; els.error.textContent = ''; }

document.addEventListener('click', event => {
  const tab = event.target.closest('.tab'); if (tab) { document.querySelectorAll('.tab').forEach(x => x.classList.toggle('is-active', x === tab)); document.querySelectorAll('.page').forEach(x => x.classList.toggle('is-active', x.id === `${tab.dataset.tab}-page`)); if (tab.dataset.tab === 'customers' && !state.customers) loadCustomers(); }
  const detail = event.target.closest('.detail-trigger'); if (detail) openDetails(detail.dataset.metric, detail.dataset.conveyor);
  const customer = event.target.closest('.customer-detail'); if (customer) openDetails('total', els.conveyor.value, customer.dataset.customer);
});
els.refresh.addEventListener('click', async () => { state.customers = null; await loadDaily(); if ($('#customers-page').classList.contains('is-active')) await loadCustomers(); });
els.loadCustomers.addEventListener('click', loadCustomers); els.loadException.addEventListener('click', loadException25);
els.analyzeAi.addEventListener('click', analyzeAi);
els.closeDetail.addEventListener('click', () => els.dialog.close());
window.addEventListener('resize', () => { if (state.daily) { drawHourly(state.daily.hourly, state.daily.metrics); drawRepeatHourly(state.daily.hourly, state.daily.metrics); } if (state.customers) drawCustomerChart(state.customers.customers.slice(0, 10)); });

init();
