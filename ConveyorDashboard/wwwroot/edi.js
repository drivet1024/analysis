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

function offsetDate(value, days) {
  const date = parseDate(value);
  date.setDate(date.getDate() + days);
  return isoLocalDate(date);
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
  $('live-dot').className = state;
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

function calculateClientTrend(currentParcels, history) {
  const historicalAverage = history.reduce((sum, value) => sum + value, 0) / history.length;
  if (historicalAverage === 0) {
    return { historicalAverage, trendPercent: null, trendDirection: currentParcels > 0 ? 'new' : 'stable' };
  }
  const trendPercent = (currentParcels - historicalAverage) * 100 / historicalAverage;
  return {
    historicalAverage,
    trendPercent,
    trendDirection: currentParcels > historicalAverage ? 'up' : currentParcels < historicalAverage ? 'down' : 'stable'
  };
}

function trendBadge(direction, percent) {
  if (direction === 'new') return '<span class="trend-pill new">Nouveau</span>';
  if (direction === 'stable') return '<span class="trend-pill stable">→ Stable</span>';
  const safeDirection = direction === 'up' ? 'up' : 'down';
  const arrow = safeDirection === 'up' ? '↑' : '↓';
  const sign = safeDirection === 'up' ? '+' : '−';
  return `<span class="trend-pill ${safeDirection}">${arrow} ${sign}${decimal.format(Math.abs(Number(percent) || 0))} %</span>`;
}

function renderClients(clients, analysisDate, databaseNow) {
  const isToday = analysisDate === currentEdiDate();
  $('client-date-current').textContent = isToday ? 'Aujourd’hui' : columnDate.format(parseDate(analysisDate));
  for (let week = 1; week <= 4; week += 1) {
    $(`client-date-week${week}`).textContent = columnDate.format(parseDate(offsetDate(analysisDate, -7 * week)));
  }
  $('clients-trend-context').textContent = `${number.format(clients.length)} client${clients.length === 1 ? '' : 's'} · ${isToday ? `de 4 h à ${hourMinute.format(new Date(databaseNow))}` : 'journées complètes de 4 h à 4 h'} · écart par rapport à la moyenne des quatre semaines`;

  const body = $('clients-body');
  body.replaceChildren();
  if (!clients.length) {
    body.innerHTML = '<tr><td colspan="8" class="empty-cell">Aucun volume client trouvé pour ces cinq dates.</td></tr>';
    $('clients-foot').replaceChildren();
    return;
  }

  clients.forEach((client) => {
    const row = document.createElement('tr');
    row.innerHTML = `
      <td class="client-name">${escapeHtml(client.customerName)}<small>Client ${number.format(client.customerId)}</small></td>
      <td><strong>${number.format(client.currentParcels)}</strong></td>
      <td>${number.format(client.week1Parcels)}</td>
      <td>${number.format(client.week2Parcels)}</td>
      <td>${number.format(client.week3Parcels)}</td>
      <td>${number.format(client.week4Parcels)}</td>
      <td class="client-average">${decimal.format(client.historicalAverage)}</td>
      <td>${trendBadge(client.trendDirection, client.trendPercent)}</td>`;
    body.append(row);
  });

  const totals = clients.reduce((sum, client) => ({
    current: sum.current + Number(client.currentParcels || 0),
    week1: sum.week1 + Number(client.week1Parcels || 0),
    week2: sum.week2 + Number(client.week2Parcels || 0),
    week3: sum.week3 + Number(client.week3Parcels || 0),
    week4: sum.week4 + Number(client.week4Parcels || 0)
  }), { current: 0, week1: 0, week2: 0, week3: 0, week4: 0 });
  const totalTrend = calculateClientTrend(totals.current, [totals.week1, totals.week2, totals.week3, totals.week4]);
  $('clients-foot').innerHTML = `<tr><td>Total</td><td>${number.format(totals.current)}</td><td>${number.format(totals.week1)}</td><td>${number.format(totals.week2)}</td><td>${number.format(totals.week3)}</td><td>${number.format(totals.week4)}</td><td>${decimal.format(totalTrend.historicalAverage)}</td><td>${trendBadge(totalTrend.trendDirection, totalTrend.trendPercent)}</td></tr>`;
}

function renderWeek(days, weeklyBudget, analysisDate) {
  const body = $('week-body');
  body.replaceChildren();
  days.forEach((day) => {
    const budget = Number(day.budget || 0);
    const parcels = Number(day.parcels || 0);
    const isFuture = day.date > analysisDate;
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
    column.className = `day-column${day.date > analysisDate ? ' future' : ''}`;
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
