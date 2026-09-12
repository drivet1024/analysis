const REFRESH_SECONDS = 60;
const EDI_DAY_START_HOUR = 4;
const $ = (id) => document.getElementById(id);
const number = new Intl.NumberFormat('fr-CA');
const decimal = new Intl.NumberFormat('fr-CA', { maximumFractionDigits: 1 });
const time = new Intl.DateTimeFormat('fr-CA', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const hourMinute = new Intl.DateTimeFormat('fr-CA', { hour: '2-digit', minute: '2-digit' });
const shortDate = new Intl.DateTimeFormat('fr-CA', { day: '2-digit', month: 'short', year: 'numeric' });
const columnDate = new Intl.DateTimeFormat('fr-CA', { day: '2-digit', month: 'short' });
let countdown = REFRESH_SECONDS;
let requestVersion = 0;
let clientRows = [];
let clientSortKey = 'currentParcels';
let clientSortDirection = 'desc';

function parseDate(value) {
  return new Date(`${value}T12:00:00`);
}

function isoLocalDate(date = new Date()) {
  const pad = (part) => String(part).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function currentEdiDate() {
  const date = new Date();
  if (date.getHours() < EDI_DAY_START_HOUR) date.setDate(date.getDate() - 1);
  return isoLocalDate(date);
}

function validIsoDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value || '')) return false;
  const parsed = parseDate(value);
  return !Number.isNaN(parsed.getTime()) && isoLocalDate(parsed) === value;
}

function initialAnalysisDate() {
  const requested = new URLSearchParams(window.location.search).get('date');
  const today = currentEdiDate();
  return validIsoDate(requested) && requested <= today ? requested : today;
}

let selectedAnalysisDate = initialAnalysisDate();

function syncDateSelector() {
  const today = currentEdiDate();
  $('analysis-date').max = today;
  $('analysis-date').value = selectedAnalysisDate;
  $('next-date').disabled = selectedAnalysisDate >= today;
}

function selectAnalysisDate(value) {
  if (!validIsoDate(value)) return;
  selectedAnalysisDate = value > currentEdiDate() ? currentEdiDate() : value;
  syncDateSelector();
  const url = new URL(window.location.href);
  url.searchParams.set('date', selectedAnalysisDate);
  window.history.replaceState({}, '', url);
  countdown = REFRESH_SECONDS;
  $('countdown').textContent = countdown;
  load();
}

function moveAnalysisDate(dayOffset) {
  const date = parseDate(selectedAnalysisDate);
  date.setDate(date.getDate() + dayOffset);
  selectAnalysisDate(isoLocalDate(date));
}

function formatDate(value) {
  return value ? shortDate.format(parseDate(value)) : '—';
}

function formatTime(value) {
  return value ? time.format(new Date(value)) : '—';
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>'"]/g, (character) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
  })[character]);
}

function setConnection(state, label) {
  $('live-dot').className = `live-dot ${state}`;
  $('live-label').textContent = label;
}

function totalsForRegions(regions) {
  return regions.reduce((totals, row) => ({
    parcelsToday: totals.parcelsToday + Number(row.parcelsToday || 0),
    palletsToday: totals.palletsToday + Number(row.palletsToday || 0),
    parcelsYesterday: totals.parcelsYesterday + Number(row.parcelsYesterday || 0),
    palletsYesterday: totals.palletsYesterday + Number(row.palletsYesterday || 0),
    parcelsLastWeek: totals.parcelsLastWeek + Number(row.parcelsLastWeek || 0),
    palletsLastWeek: totals.palletsLastWeek + Number(row.palletsLastWeek || 0)
  }), { parcelsToday: 0, palletsToday: 0, parcelsYesterday: 0, palletsYesterday: 0, parcelsLastWeek: 0, palletsLastWeek: 0 });
}

function renderRegions(regions) {
  const body = $('regions-body');
  body.replaceChildren();
  if (!regions.length) {
    body.innerHTML = '<tr><td colspan="8" class="empty-cell">Aucun volume EDI trouvé pour la période.</td></tr>';
  } else {
    regions.forEach((region) => {
      const row = document.createElement('tr');
      row.innerHTML = `
        <td class="region-name">${escapeHtml(region.region)}</td>
        <td class="depots-cell">${escapeHtml(region.depots)}</td>
        <td><strong>${number.format(region.parcelsToday)}</strong></td>
        <td>${number.format(region.palletsToday)}</td>
        <td><strong>${number.format(region.parcelsYesterday)}</strong></td>
        <td>${number.format(region.palletsYesterday)}</td>
        <td><strong>${number.format(region.parcelsLastWeek)}</strong></td>
        <td>${number.format(region.palletsLastWeek)}</td>`;
      body.append(row);
    });
  }

  const totals = totalsForRegions(regions);
  $('regions-foot').innerHTML = `
    <tr><td colspan="2">Total</td>
      <td>${number.format(totals.parcelsToday)}</td><td>${number.format(totals.palletsToday)}</td>
      <td>${number.format(totals.parcelsYesterday)}</td><td>${number.format(totals.palletsYesterday)}</td>
      <td>${number.format(totals.parcelsLastWeek)}</td><td>${number.format(totals.palletsLastWeek)}</td></tr>`;
  $('parcels-today').textContent = number.format(totals.parcelsToday);
  $('pallets-today').textContent = number.format(totals.palletsToday);
}

function trendBadge(direction, percent) {
  if (direction === 'new') return '<span class="trend-pill new">Nouveau</span>';
  if (direction === 'stable') return '<span class="trend-pill stable">→ Stable</span>';
  const safeDirection = direction === 'up' ? 'up' : 'down';
  const arrow = safeDirection === 'up' ? '↑' : '↓';
  const sign = safeDirection === 'up' ? '+' : '−';
  return `<span class="trend-pill ${safeDirection}">${arrow} ${sign}${decimal.format(Math.abs(Number(percent) || 0))} %</span>`;
}

function normalizedClientName(value) {
  return String(value || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('fr-CA');
}

function clientSortValue(client) {
  if (clientSortKey === 'trendPercent') {
    if (client.trendDirection === 'new') return Number.POSITIVE_INFINITY;
    return Number(client.trendPercent || 0);
  }
  return client[clientSortKey];
}

function sortedFilteredClients() {
  const filter = normalizedClientName($('client-filter').value.trim());
  const filtered = filter
    ? clientRows.filter((client) => normalizedClientName(client.customerName).includes(filter))
    : [...clientRows];

  return filtered.sort((left, right) => {
    const leftValue = clientSortValue(left);
    const rightValue = clientSortValue(right);
    const leftMissing = leftValue === null || leftValue === undefined;
    const rightMissing = rightValue === null || rightValue === undefined;
    if (leftMissing !== rightMissing) return leftMissing ? 1 : -1;

    let result = clientSortKey === 'customerName'
      ? String(leftValue || '').localeCompare(String(rightValue || ''), 'fr-CA', { sensitivity: 'base', numeric: true })
      : Number(leftValue || 0) - Number(rightValue || 0);
    if (clientSortDirection === 'desc') result *= -1;
    return result || String(left.customerName || '').localeCompare(String(right.customerName || ''), 'fr-CA', { sensitivity: 'base', numeric: true });
  });
}

function syncClientSortHeaders() {
  document.querySelectorAll('[data-client-sort]').forEach((button) => {
    const active = button.dataset.clientSort === clientSortKey;
    const header = button.closest('th');
    const indicator = button.querySelector('.sort-indicator');
    header.setAttribute('aria-sort', active ? (clientSortDirection === 'asc' ? 'ascending' : 'descending') : 'none');
    indicator.textContent = active ? (clientSortDirection === 'asc' ? '↑' : '↓') : '↕';
  });
}

function renderClientRows() {
  const clients = sortedFilteredClients();
  const body = $('clients-body');
  body.replaceChildren();
  if (!clients.length) {
    const message = clientRows.length ? 'Aucun client ne correspond à ce filtre.' : 'Aucun volume client trouvé pour cette période.';
    body.innerHTML = `<tr><td colspan="4" class="empty-cell">${message}</td></tr>`;
    syncClientSortHeaders();
    return;
  }

  clients.forEach((client) => {
    const row = document.createElement('tr');
    row.innerHTML = `
      <td class="client-name">${escapeHtml(client.customerName)}<small>Client ${number.format(client.customerId)}</small></td>
      <td><strong>${number.format(client.currentParcels)}</strong></td>
      <td class="client-average">${decimal.format(client.historicalAverage)}</td>
      <td>${trendBadge(client.trendDirection, client.trendPercent)}</td>`;
    body.append(row);
  });
  syncClientSortHeaders();
}

function renderClients(clients, analysisDate, databaseNow) {
  const isToday = analysisDate === currentEdiDate();
  $('client-date-current').textContent = isToday ? 'Aujourd’hui' : columnDate.format(parseDate(analysisDate));
  $('clients-trend-context').textContent = `Top ${number.format(clients.length)} selon le volume du jour · ${isToday ? `de 4 h à ${hourMinute.format(new Date(databaseNow))}` : 'journée complète de 4 h à 4 h'} · tendance comparée à la moyenne des quatre semaines`;
  clientRows = clients;
  renderClientRows();
}

function renderWeek(days, weeklyBudget, analysisDate) {
  const body = $('week-body');
  const operationalToday = currentEdiDate();
  body.replaceChildren();
  days.forEach((day) => {
    const budget = Number(day.budget || 0);
    const parcels = Number(day.parcels || 0);
    const isFuture = day.date > operationalToday;
    const difference = parcels - budget;
    const attainment = budget ? 100 * parcels / budget : 0;
    const row = document.createElement('tr');
    if (day.date === analysisDate) row.classList.add('today-row');
    if (isFuture) row.classList.add('future-row');
    row.innerHTML = `
      <td class="day-name">${escapeHtml(day.dayName)}</td>
      <td>${formatDate(day.date)}</td>
      <td><strong>${number.format(parcels)}</strong></td>
      <td>${number.format(budget)}</td>
      <td class="${isFuture ? '' : difference >= 0 ? 'delta-positive' : 'delta-negative'}">${isFuture ? 'À venir' : `${difference >= 0 ? '+' : '−'}${number.format(Math.abs(difference))}`}</td>
      <td>${isFuture ? '—' : `<span class="attainment"><span class="attainment-track"><i style="width:${Math.min(100, attainment)}%"></i></span>${attainment.toLocaleString('fr-CA', { maximumFractionDigits: 1 })} %</span>`}</td>`;
    body.append(row);
  });

  const weeklyParcels = days.reduce((sum, day) => sum + Number(day.parcels || 0), 0);
  const weeklyDifference = weeklyParcels - weeklyBudget;
  const weeklyAttainment = weeklyBudget ? 100 * weeklyParcels / weeklyBudget : 0;
  $('week-foot').innerHTML = `<tr><td colspan="2">Total semaine</td><td>${number.format(weeklyParcels)}</td><td>${number.format(weeklyBudget)}</td><td class="${weeklyDifference >= 0 ? 'delta-positive' : 'delta-negative'}">${weeklyDifference >= 0 ? '+' : '−'}${number.format(Math.abs(weeklyDifference))}</td><td>${weeklyAttainment.toLocaleString('fr-CA', { maximumFractionDigits: 1 })} %</td></tr>`;
  $('weekly-parcels').textContent = number.format(weeklyParcels);
  $('weekly-budget').textContent = number.format(weeklyBudget);
  $('weekly-progress').textContent = `${weeklyAttainment.toLocaleString('fr-CA', { maximumFractionDigits: 1 })} % du budget reçu`;

  const maximum = Math.max(1, ...days.flatMap((day) => [Number(day.parcels || 0), Number(day.budget || 0)]));
  const chart = $('week-chart');
  chart.replaceChildren();
  days.forEach((day) => {
    const parcels = Number(day.parcels || 0);
    const budget = Number(day.budget || 0);
    const column = document.createElement('div');
    column.className = `day-column${day.date > operationalToday ? ' future' : ''}`;
    column.innerHTML = `
      <span class="day-value">${number.format(parcels)}</span>
      <div class="day-track" title="${number.format(parcels)} colis · budget ${number.format(budget)}">
        <i class="day-bar" style="height:${100 * parcels / maximum}%"></i>
        <i class="budget-marker" style="bottom:${100 * budget / maximum}%"></i>
      </div>
      <small>${escapeHtml(day.dayName.slice(0, 3))}</small>`;
    chart.append(column);
  });
}

function render(data) {
  const regions = data.regions || [];
  const days = data.days || [];
  const clients = data.clients || [];
  selectedAnalysisDate = data.executionDate || selectedAnalysisDate;
  syncDateSelector();
  const isToday = selectedAnalysisDate === currentEdiDate();
  const selectedDateLabel = formatDate(selectedAnalysisDate);

  $('snapshot-today-label').textContent = isToday ? 'Colis aujourd’hui' : `Colis · ${selectedDateLabel}`;
  $('snapshot-today-context').textContent = isToday ? 'Depuis 4 h jusqu’à maintenant' : 'Journée complète · 4 h à 4 h';
  $('snapshot-d7-context').textContent = isToday ? 'Même période et même heure' : 'Même journée, sept jours plus tôt';
  $('linehaul-parcels-label').textContent = isToday ? 'Colis linehaul aujourd’hui' : `Colis linehaul · ${selectedDateLabel}`;
  $('linehaul-pallets-label').textContent = isToday ? 'Palettes linehaul aujourd’hui' : `Palettes linehaul · ${selectedDateLabel}`;
  $('linehaul-parcels-context').textContent = isToday ? 'Expéditions par région jusqu’à maintenant' : 'Expéditions par région pour la journée';
  $('regions-period-label').textContent = isToday
    ? 'Aujourd’hui, hier et même période la semaine dernière'
    : `${selectedDateLabel}, veille et même journée la semaine précédente`;
  $('regions-current-label').textContent = isToday ? 'Aujourd’hui' : selectedDateLabel;

  renderRegions(regions);
  renderClients(clients, selectedAnalysisDate, data.databaseNow);
  renderWeek(days, Number(data.weeklyBudget || 0), selectedAnalysisDate);
  $('snapshot-parcels-today').textContent = number.format(data.parcelsTodaySnapshot || 0);
  $('snapshot-parcels-d7').textContent = number.format(data.parcelsLastWeekSameTime || 0);
  $('week-range').textContent = `${formatDate(data.weekStart)} au ${formatDate(data.weekEnd)}`;
  $('database-time').textContent = formatTime(data.databaseNow);
  $('last-refresh').textContent = `Actualisé à ${formatTime(data.databaseNow)}`;
}

async function load() {
  const version = ++requestVersion;
  $('refresh-button').disabled = true;
  $('error-banner').hidden = true;
  setConnection('waiting', 'Actualisation…');
  try {
    const query = new URLSearchParams({ date: selectedAnalysisDate, t: Date.now().toString() });
    const response = await fetch(`/api/edi?${query}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`Réponse ${response.status}`);
    const data = await response.json();
    if (version !== requestVersion) return;
    render(data);
    setConnection('ok', 'Données en direct');
    countdown = REFRESH_SECONDS;
  } catch (error) {
    if (version !== requestVersion) return;
    setConnection('error', 'Connexion interrompue');
    $('error-banner').textContent = 'Impossible de charger les données EDI. Une nouvelle tentative sera faite automatiquement.';
    $('error-banner').hidden = false;
  } finally {
    if (version === requestVersion) $('refresh-button').disabled = false;
  }
}

$('refresh-button').addEventListener('click', () => { countdown = REFRESH_SECONDS; load(); });
$('previous-date').addEventListener('click', () => moveAnalysisDate(-1));
$('next-date').addEventListener('click', () => moveAnalysisDate(1));
$('analysis-date').addEventListener('change', (event) => selectAnalysisDate(event.target.value));
$('client-filter').addEventListener('input', renderClientRows);
document.querySelectorAll('[data-client-sort]').forEach((button) => {
  button.addEventListener('click', () => {
    const key = button.dataset.clientSort;
    if (clientSortKey === key) {
      clientSortDirection = clientSortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      clientSortKey = key;
      clientSortDirection = key === 'customerName' ? 'asc' : 'desc';
    }
    renderClientRows();
  });
});
setInterval(() => {
  countdown -= 1;
  if (countdown <= 0) {
    countdown = REFRESH_SECONDS;
    load();
  }
  $('countdown').textContent = countdown;
}, 1000);

syncDateSelector();
load();
