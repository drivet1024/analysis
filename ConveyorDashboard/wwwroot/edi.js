const REFRESH_SECONDS = 60;
const TRANSPORT_PAGE = document.body.dataset.ediPage === 'transport';
const CLIENT_PAGE = document.body.dataset.ediPage === 'clients';
const FORECAST_PAGE = document.body.dataset.ediPage === 'forecasts';
const DELIVERY_PAGE = document.body.dataset.ediPage === 'deliveries';
let sectorData = null;
let deliveryRefreshTimer = null;
let forecastRefreshTimer = null;
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
let regionRows = [];
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
  if (!DELIVERY_PAGE && date.getHours() < EDI_DAY_START_HOUR) date.setDate(date.getDate() - 1);
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
let deliveryFollowToday = selectedAnalysisDate === currentEdiDate();

function syncDateSelector() {
  const today = currentEdiDate();
  $('analysis-date').max = today;
  $('analysis-date').value = selectedAnalysisDate;
  $('next-date').disabled = selectedAnalysisDate >= today;
  document.querySelectorAll('[data-edi-navigation]').forEach((link) => {
    const url = new URL(link.href, window.location.origin);
    url.searchParams.set('date', selectedAnalysisDate);
    link.href = url.toString();
  });
}

function selectAnalysisDate(value) {
  if (!validIsoDate(value)) return;
  selectedAnalysisDate = value > currentEdiDate() ? currentEdiDate() : value;
  syncDateSelector();
  const url = new URL(window.location.href);
  url.searchParams.set('date', selectedAnalysisDate);
  window.history.replaceState({}, '', url);
  countdown = REFRESH_SECONDS;
  if (!DELIVERY_PAGE && !FORECAST_PAGE) $('countdown').textContent = countdown;
  deliveryFollowToday = selectedAnalysisDate === currentEdiDate();
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
  regionRows = regions;
  const cubicConversion = $('pallet-unit').value === 'cm' ? 2.54 ** 3 : 1;
  const height = Number($('pallet-height').value || 78);
  const fill = Number($('pallet-fill').value || 0.7);
  const usableVolume = 40 * 48 * height * fill;
  const estimate = (region) => region.parcelsToday === 0 ? 0
    : region.estimatedParcelVolume == null || region.missingProfileParcels > 0 ? null
    : Math.ceil(Number(region.estimatedParcelVolume) / cubicConversion / usableVolume);
  $('pallet-assumptions').textContent = `Palette 40 × 48 × 84 po, base incluse · ${height} po utiles (réserve estimée de ${84 - height} po pour la base) · ${Math.round(fill * 100)} % de remplissage (${Math.round((1 - fill) * 100)} % de vide) · capacité utilisée : ${number.format(usableVolume)} po³. Dimensions des colis en pouces : confirmé.`;
  const body = $('regions-body');
  body.replaceChildren();
  if (!regions.length) {
    body.innerHTML = '<tr><td colspan="9" class="empty-cell">Aucun volume EDI trouvé pour la période.</td></tr>';
  } else {
    regions.forEach((region) => {
      const row = document.createElement('tr');
      const pallets = estimate(region);
      const coverage = region.parcelsToday > 0 ? 100 * Number(region.clientProfileParcels || 0) / region.parcelsToday : 0;
      const explanation = region.parcelsToday === 0 ? 'Aucun colis'
        : `${decimal.format(coverage)} % profil client${region.fallbackProfileParcels ? ` · ${number.format(region.fallbackProfileParcels)} colis : moyenne générale` : ''}${region.missingProfileParcels ? ` · ${number.format(region.missingProfileParcels)} sans profil` : ''}`;
      row.innerHTML = `
        <td class="region-name">${escapeHtml(region.region)}</td>
        <td class="depots-cell">${escapeHtml(region.depots)}</td>
        <td><strong>${number.format(region.parcelsToday)}</strong></td>
        <td>${number.format(region.palletsToday)}</td>
        <td class="pallet-estimate" title="${escapeHtml(explanation)}"><strong>${pallets == null ? '—' : number.format(pallets)}</strong><small>${escapeHtml(explanation)}</small></td>
        <td><strong>${number.format(region.parcelsYesterday)}</strong></td>
        <td>${number.format(region.palletsYesterday)}</td>
        <td><strong>${number.format(region.parcelsLastWeek)}</strong></td>
        <td>${number.format(region.palletsLastWeek)}</td>`;
      body.append(row);
    });
  }

  const totals = totalsForRegions(regions);
  const estimates = regions.map(estimate);
  const palletTotal = estimates.every(value => value != null) ? estimates.reduce((sum, value) => sum + value, 0) : null;
  $('regions-foot').innerHTML = `
    <tr><td colspan="2">Total</td>
      <td>${number.format(totals.parcelsToday)}</td><td>${number.format(totals.palletsToday)}</td>
      <td class="pallet-estimate">${palletTotal == null ? 'Incomplet' : number.format(palletTotal)}</td>
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

function renderForecast(forecast, archive) {
  globalThis.updateEdiForecastChart?.(forecast, archive);
  const body = $('forecast-body');
  body.replaceChildren();
  $('forecast-foot').replaceChildren();
  if (!forecast) {
    $('forecast-context').textContent = 'Prévision indisponible';
    $('forecast-summary').textContent = 'Aucune prévision sauvegardée pour cette date. Le calcul quotidien est effectué en arrière-plan à 6 h.';
    $('forecast-ml-summary').textContent = '';
    $('forecast-schedule').textContent = '';
    $('forecast-holidays').textContent = '';
    $('forecast-seasonality').hidden = true;
    $('forecast-validation').textContent = '';
    return;
  }
  $('forecast-context').textContent = `${formatDate(forecast.days[0].date)} au ${formatDate(forecast.days[6].date)} · référence du ${formatDate(forecast.asOfDate)}`;
  const saved = archive?.snapshot;
  const mlForecast = saved?.mlForecast;
  const mlByDate = new Map((mlForecast?.days || []).map(day => [day.date, day]));
  $('forecast-schedule').textContent = saved
    ? `Prévision sauvegardée le ${new Date(saved.savedAt).toLocaleString('fr-CA', { timeZone: 'America/Toronto' })} (Montréal) · référence ${formatDate(forecast.asOfDate)} · prochain renouvellement à ${new Date(archive.nextRefresh).toLocaleString('fr-CA', { timeZone: 'America/Toronto' })}.`
    : 'Reconstitution non archivée : aucune prévision sauvegardée ne correspond à cette date.';
  if (archive?.refreshPending) $('forecast-schedule').textContent += ' Renouvellement en attente : la dernière version disponible reste affichée.';
  $('forecast-summary').textContent = `Historique consulté : ${formatDate(forecast.historyStart)} au ${formatDate(forecast.historyEnd)} · ${number.format(forecast.observedDays)} jours observés sur ${number.format(forecast.observedDays + forecast.missingDays)}${forecast.missingDays ? ` · ${number.format(forecast.missingDays)} jours sans données, exclus du calcul` : ''}. Estimations, non garanties.`;
  $('forecast-ml-summary').textContent = mlForecast?.modelId
    ? `ML.NET LightGBM : modèle ${mlForecast.modelId}, entraîné jusqu’au ${formatDate(mlForecast.trainedThrough)} sur ${number.format(mlForecast.trainingRows)} journées. Validation chronologique sur les mêmes ${number.format(mlForecast.backtestDays)} journées : LightGBM ${number.format(mlForecast.backtestMae)} colis d’erreur moyenne${mlForecast.backtestWape == null ? '' : `, WAPE ${decimal.format(mlForecast.backtestWape)} %`}${mlForecast.statisticalBacktestMae == null ? '' : ` · statistique ${number.format(mlForecast.statisticalBacktestMae)} colis${mlForecast.statisticalBacktestWape == null ? '' : `, WAPE ${decimal.format(mlForecast.statisticalBacktestWape)} %`}`}.`
    : mlForecast?.status || 'Cette archive précède l’intégration ML.NET; aucune prévision IA n’y est enregistrée.';
  const seasonal = forecast.seasonality;
  $('forecast-seasonality').hidden = !seasonal;
  if (seasonal) {
    $('forecast-cyber').textContent = `Prochain Cyber Monday : ${formatDate(seasonal.cyberMonday)} · ${formatDate(seasonal.previousCyberMonday)} : ${seasonal.previousCyberParcels == null ? 'volume historique indisponible' : `${number.format(seasonal.previousCyberParcels)} colis`}.`;
    const pairs = seasonal.growthPairs || [];
    const currentTotal = pairs.reduce((sum, pair) => sum + pair.parcels, 0);
    const previousTotal = pairs.reduce((sum, pair) => sum + pair.previousParcels, 0);
    $('forecast-growth').textContent = pairs.length >= 21 && previousTotal > 0
      ? `Évolution de l’activité : ${decimal.format(100 * (currentTotal / previousTotal - 1))} % sur ${pairs.length} jours appariés des 8 dernières semaines, hors fériés et période Cyber Monday. Facteur annuel = ${number.format(currentTotal)} ÷ ${number.format(previousTotal)} = ${(currentTotal / previousTotal).toLocaleString('fr-CA', { maximumFractionDigits: 3 })}.`
      : 'Historique apparié insuffisant pour ajuster les volumes de l’an dernier. Les jours ordinaires utilisent seulement la tendance récente; les jours de la période Cyber Monday restent sans estimation.';
    $('forecast-growth-pairs').innerHTML = pairs.map(pair => `<tr><td>${formatDate(pair.date)}</td><td>${number.format(pair.parcels)}</td><td>${formatDate(pair.previousDate)}</td><td>${number.format(pair.previousParcels)}</td></tr>`).join('');
    $('forecast-model-comparison').textContent = seasonal.comparedDays && seasonal.baselineWape != null && seasonal.seasonalComparedWape != null
      ? `Sur les mêmes ${seasonal.comparedDays} journées rétrospectives : erreur absolue cumulée / réel cumulé de ${decimal.format(seasonal.seasonalComparedWape)} % avec la saisonnalité, contre ${decimal.format(seasonal.baselineWape)} % avec le modèle récent seul. Ce test récent ne garantit pas la précision du prochain Cyber Monday.`
      : 'Comparaison des modèles indisponible : historique insuffisant.';
  }
  const excluded = forecast.excludedHolidays || [];
  $('forecast-holidays').textContent = excluded.length
    ? `Jours fériés exclus : ${excluded.map(day => `${formatDate(day.date)} (${number.format(day.parcels)} colis)`).join(' · ')}.`
    : saved?.modelVersion === 'weekday-weighted-v1-with-holidays' ? 'Archive initiale : les jours fériés étaient encore inclus.' : 'Aucun jour férié observé dans cet historique.';
  const comparisons = (archive?.comparisons || []).filter(row => row.snapshotId === saved?.id);
  const byDate = new Map(comparisons.map(row => [row.date, row]));
  const mlByDisplayDate = new Map(mlByDate);
  const previousDay = archive?.previousDay;
  const displayDays = [...forecast.days];
  if (previousDay?.day && !displayDays.some(day => day.date === previousDay.day.date)) {
    displayDays.unshift(previousDay.day);
    byDate.set(previousDay.day.date, previousDay.comparison);
    if (previousDay.mlDay) mlByDisplayDate.set(previousDay.day.date, previousDay.mlDay);
  }
  displayDays.forEach((day) => {
    const comparison = byDate.get(day.date);
    const mlDay = mlByDisplayDate.get(day.date);
    const samples = day.samples || [];
    const weightSum = samples.reduce((sum, sample) => sum + sample.weight, 0);
    const row = document.createElement('tr');
    const annual = day.annual;
    const annualUsed = annual?.adjustedParcels != null && day.parcels != null;
    const explanation = day.holiday ? `${escapeHtml(day.holiday)} : prévision suspendue, faute d’historique de jours fériés comparables.` : annual?.event && day.parcels == null
      ? `${escapeHtml(annual.event)} : référence annuelle insuffisante, aucune estimation ordinaire substituée.` : day.parcels == null
      ? `Historique insuffisant : ${samples.length} observation(s), minimum 4.`
      : annualUsed ? annual.event ? `${escapeHtml(annual.event)} : volume du ${formatDate(annual.referenceDate)}, ajusté à l’évolution de l’activité.`
        : '50 % de tendance récente + 50 % de référence annuelle ajustée.'
      : `Moyenne pondérée des ${samples.length} derniers ${escapeHtml(day.dayName)}s disponibles. Poids de 1 à ${samples.length}, somme des poids : ${weightSum}.${annual ? ' Référence annuelle insuffisante.' : ''}`;
    const recentEstimate = annual ? day.recentEstimate : day.parcels;
    const annualDetails = annual ? `<br>${escapeHtml(annual.note)}${annual.references.length ? `<br>Références annuelles : ${annual.references.map(sample => `${formatDate(sample.date)} : ${number.format(sample.parcels)} colis`).join(' · ')}` : ''}${annualUsed ? `<br>Référence annuelle ajustée : ${number.format(annual.adjustedParcels)} colis (facteur ${Number(annual.growthFactor).toLocaleString('fr-CA', { maximumFractionDigits: 3 })}, ${annual.growthPairs} paires).<br>${annual.event ? '100 % de la référence événementielle ajustée' : `50 % × ${number.format(recentEstimate)} + 50 % × ${number.format(annual.adjustedParcels)}`} ≈ <strong>${number.format(day.parcels)} colis</strong>.` : ''}` : '';
    const isPreviousDay = previousDay?.day?.date === day.date;
    if (isPreviousDay) row.classList.add('forecast-previous-day');
    row.innerHTML = `<td class="day-name">${escapeHtml(day.dayName)}${isPreviousDay ? '<br><small>Veille</small>' : ''}</td><td>${formatDate(day.date)}</td>
      <td><strong>${day.parcels == null ? 'Indisponible' : number.format(day.parcels)}</strong></td>
      <td><strong>${mlDay?.parcels == null ? '' : number.format(mlDay.parcels)}</strong></td>
      <td>${comparison?.actual == null ? '' : number.format(comparison.actual)}</td>
      <td>${comparison?.difference == null ? '—' : `${comparison.difference > 0 ? '+' : ''}${number.format(comparison.difference)}`}</td>
      <td>${comparison?.mlDifference == null ? '' : `${comparison.mlDifference > 0 ? '+' : ''}${number.format(comparison.mlDifference)}`}</td>
      <td>${day.historicalLow == null ? '—' : `${number.format(day.historicalLow)} – ${number.format(day.historicalHigh)}`}</td>
      <td>${explanation}${mlDay?.parcels == null ? '' : `<br><span class="ml-explanation">ML.NET : ${escapeHtml(mlDay.status)}.</span>`}<details><summary>Voir les volumes et le calcul</summary>${samples.map(sample => `${formatDate(sample.date)} : ${number.format(sample.parcels)} colis × ${sample.weight}`).join('<br>')}${recentEstimate == null ? '' : `<br>Tendance récente : somme pondérée ÷ ${weightSum} ≈ ${number.format(recentEstimate)} colis.`}${annualDetails}</details></td>`;
    body.append(row);
  });
  const allActuals = forecast.days.length > 0 && forecast.days.every(day => byDate.get(day.date)?.actual != null);
  const allDifferences = forecast.days.length > 0 && forecast.days.every(day => byDate.get(day.date)?.difference != null);
  const actualTotal = allActuals ? forecast.days.reduce((sum, day) => sum + byDate.get(day.date).actual, 0) : null;
  const differenceTotal = allDifferences ? forecast.days.reduce((sum, day) => sum + byDate.get(day.date).difference, 0) : null;
  const mlTotal = mlForecast?.total ?? null;
  const allMlDifferences = forecast.days.length > 0 && forecast.days.every(day => byDate.get(day.date)?.mlDifference != null);
  const mlDifferenceTotal = allMlDifferences ? forecast.days.reduce((sum, day) => sum + byDate.get(day.date).mlDifference, 0) : null;
  $('forecast-foot').innerHTML = `<tr><td colspan="2">Total sur 7 jours</td><td>${forecast.total == null ? 'Incomplet' : number.format(forecast.total)}</td><td>${mlTotal == null ? '' : number.format(mlTotal)}</td><td>${actualTotal == null ? 'Incomplet' : number.format(actualTotal)}</td><td>${differenceTotal == null ? '—' : (differenceTotal > 0 ? '+' : '') + number.format(differenceTotal)}</td><td>${mlDifferenceTotal == null ? '' : (mlDifferenceTotal > 0 ? '+' : '') + number.format(mlDifferenceTotal)}</td><td colspan="2">Les totaux réels et les écarts attendent les sept journées évaluables.</td></tr>`;
  $('forecast-validation').textContent = forecast.backtestDays
    ? `Test rétrospectif : quatre horizons de 7 jours, sans utiliser les volumes postérieurs à chaque date de calcul, sur ${forecast.backtestDays} jours évaluables sur 28${forecast.excludedHolidays ? ' (jours fériés exclus)' : ''}. Erreur absolue moyenne : ${number.format(forecast.backtestMae)} colis par jour. ${forecast.backtestWape == null ? 'Erreur relative non calculable (volume réel nul).' : `Erreur absolue cumulée / volume réel cumulé : ${decimal.format(forecast.backtestWape)} %.`} Ces erreurs passées ne garantissent pas la précision future.`
    : 'Test rétrospectif indisponible : historique insuffisant pour évaluer les prévisions passées.';
}

function renderForecastVersions(archive) {
  const selector = $('forecast-archive-select');
  const entries = (archive?.comparisons || []).map(row => [row.snapshotId, row]);
  if (archive?.snapshot) entries.push([archive.snapshot.id, { snapshotId: archive.snapshot.id, savedAt: archive.snapshot.savedAt }]);
  const versions = [...new Map(entries).values()]
    .sort((a, b) => new Date(b.savedAt).getTime() - new Date(a.savedAt).getTime());
  selector.innerHTML = versions.map(row => `<option value="${escapeHtml(row.snapshotId)}">${escapeHtml(row.snapshotId)} · ${new Date(row.savedAt).toLocaleString('fr-CA', { timeZone: 'America/Toronto' })}</option>`).join('');
  selector.value = archive?.snapshot?.id || '';
  selector.onchange = () => load(selector.value);
}

function sectorActualLine(row, date) {
  const actual = row.actuals?.find(day => day.date === date);
  if (!actual) return '';
  if (actual.parcels == null) return '<small class="sector-actual">Réel : ' + (actual.status === 'future' ? 'à venir' : 'indisponible') + '</small>';
  const forecast = row.forecast.days.find(day => day.date === date)?.parcels;
  const difference = forecast == null ? '' : ' · ' + (actual.parcels - forecast > 0 ? '+' : '') + number.format(actual.parcels - forecast);
  return '<small class="sector-actual has-value">Réel : ' + number.format(actual.parcels) + difference + '</small>';
}

function renderSectors(data) {
  sectorData = data;
  globalThis.updateSectorForecastCharts?.(data);
  data = { ...data, sectors: data.sectors.map(row => ({ ...row, forecast: {
    ...row.forecast,
    days: row.forecast.days.filter(day => ![0, 6].includes(new Date(`${day.date}T12:00:00`).getDay()))
  } })) };
  const filter = normalizedClientName($('sector-filter').value.trim());
  const sectors = data.sectors.filter(row => ($('sector-show-empty').checked || row.historicalParcels > 0 || row.actuals?.some(day => day.parcels > 0))
    && normalizedClientName(`${row.sectorId} ${row.name}`).includes(filter));
  const dates = data.sectors[0]?.forecast.days || [];
  $('sector-head').innerHTML = `<tr><th>Secteur</th>${dates.map(day => `<th>${escapeHtml(day.dayName)}<br>${formatDate(day.date)}</th>`).join('')}<th>Total période</th><th>Explication</th></tr>`;
  $('sector-body').innerHTML = sectors.map(row => `<tr><td><button type="button" class="sector-chart-trigger" data-sector-chart="${row.sectorId}" aria-label="Afficher le graphique du secteur ${row.sectorId}" aria-haspopup="dialog">${row.sectorId}</button><small>Contact : ${escapeHtml(row.sectorContact || 'Non renseigné')}</small><small>${row.activeRoutes == null ? 'Routes actives : indisponible' : `${number.format(row.activeRoutes)} routes actives`}</small><small>${number.format(row.postalCodes)} codes postaux</small></td>
    ${row.forecast.days.map(day => `<td title="${escapeHtml(day.holiday || day.annual?.event || '')}"><strong>${day.parcels == null ? '—' : number.format(day.parcels)}</strong>${sectorActualLine(row, day.date)}</td>`).join('')}
    <td><strong>${row.forecast.total == null ? 'Incomplet' : number.format(row.forecast.total)}</strong>${row.actuals ? '<small class="sector-actual">Réel cumulé : ' + (row.actuals.some(d => d.parcels != null) ? number.format(row.actuals.reduce((sum, d) => sum + (d.parcels ?? 0), 0)) : '—') + ' (' + row.actuals.filter(d => d.parcels != null).length + '/5 jours)</small>' : ''}</td>
    <td><details><summary>Voir le calcul</summary><p>${number.format(row.historicalParcels)} colis historiques · ${number.format(row.routeFallbackParcels)} par repli route · ${number.format(row.conflictingRouteParcels)} conflits postal/route.</p><p>Erreur rétrospective : ${row.forecast.backtestWape == null ? 'non calculable' : `${decimal.format(row.forecast.backtestWape)} %`} sur ${row.forecast.backtestDays} jours évaluables (erreur absolue cumulée / réel cumulé).</p>
    ${data.weekly && !data.weekly.forecastAvailable ? '<p>Aucune prévision sauvegardée avant cette semaine. Le réel est disponible sans comparaison chiffrée.</p>' : row.forecast.days.map(day => `<p><b>${escapeHtml(day.dayName)} ${formatDate(day.date)}</b> : ${day.holiday ? escapeHtml(day.holiday) : day.parcels == null ? 'Historique insuffisant' : `${number.format(day.parcels)} colis`}.<br>Récent : ${day.recentEstimate == null ? '—' : number.format(day.recentEstimate)} (${day.samples.length} journées). Annuel ajusté : ${day.annual?.adjustedParcels == null ? 'indisponible' : number.format(day.annual.adjustedParcels)}${day.annual?.event ? ` · ${escapeHtml(day.annual.event)}` : ''}.<br>${day.holiday ? 'Règle de calendrier : aucun calcul ordinaire.' : day.annual?.adjustedParcels == null ? 'Tendance récente seule, sauf événement sans référence.' : day.annual.event ? '100 % de la référence événementielle ajustée.' : '50 % récent + 50 % annuel ajusté.'}<br>Références récentes : ${day.samples.map(sample => `${formatDate(sample.date)} : ${number.format(sample.parcels)} × poids ${sample.weight}`).join(' ; ')}.<br>Références annuelles : ${(day.annual?.references || []).map(sample => `${formatDate(sample.date)} : ${number.format(sample.parcels)}`).join(' ; ') || 'aucune'}${day.annual?.growthFactor == null ? '' : ` · facteur ${Number(day.annual.growthFactor).toLocaleString('fr-CA', { maximumFractionDigits: 3 })}`}.</p>`).join('')}</details></td></tr>`).join('')
    || `<tr><td colspan="${dates.length + 3}" class="empty-cell">Aucun secteur ne correspond au filtre.</td></tr>`;
  const totals = dates.map((_, index) => sectors.every(row => row.forecast.days[index].parcels != null)
    ? sectors.reduce((sum, row) => sum + row.forecast.days[index].parcels, 0) : null);
  $('sector-foot').innerHTML = `<tr><td>Total des secteurs affichés</td>${totals.map((total, index) => `<td><strong>${total == null ? 'Incomplet' : number.format(total)}</strong>${data.weekly ? '<small class="sector-actual">Réel : ' + (sectors.length && sectors.every(row => row.actuals?.find(d => d.date === dates[index].date)?.parcels != null) ? number.format(sectors.reduce((sum, row) => sum + row.actuals.find(d => d.date === dates[index].date).parcels, 0)) : '—') + '</small>' : ''}</td>`).join('')}<td>${totals.every(total => total != null) ? number.format(totals.reduce((sum, value) => sum + value, 0)) : 'Incomplet'}</td><td>${sectors.length} secteurs</td></tr>`;
  $('sector-status').textContent = data.weekly
    ? (data.weekly.forecastAvailable ? 'Prévision de la semaine figée le samedi ' + formatDate(data.asOfDate) + ', sauvegardée le ' + new Date(data.savedAt).toLocaleString('fr-CA', { timeZone: 'America/Toronto' }) : 'Aucune prévision sauvegardée le samedi ' + formatDate(data.asOfDate) + ' pour cette semaine : prévisions indisponibles, réel affiché sans reconstitution.')
      + ' · Réel mis à jour le ' + new Date(data.weekly.actualsUpdatedAt).toLocaleString('fr-CA', { timeZone: 'America/Toronto' }) + ' · ' + sectors.length + ' secteurs affichés.'
    : 'Prévisions sauvegardées · ' + sectors.length + ' secteurs affichés.';
  const assigned = data.sectors.reduce((sum, row) => sum + row.historicalParcels, 0);
  $('sector-quality').textContent = `Réconciliation des colis uniques triés à Saint-Hubert (par jour de livraison prévu) : ${number.format(data.networkParcels)} colis = ${number.format(assigned)} dans les secteurs Saint-Hubert + ${number.format(data.outsideDepotParcels)} hors périmètre + ${number.format(data.unmappedParcels)} non rattachés + ${number.format(data.ambiguousPostalParcels)} avec destination ambiguë. Les catégories sont exclusives.`;
}

function renderNowcast(nowcast) {
  $('snapshot-final-label').textContent = nowcast?.status === 'completed' ? 'Total final observé' : 'Finale estimée aujourd’hui';
  $('snapshot-final-value').textContent = nowcast?.estimatedFinal == null ? '—' : number.format(nowcast.estimatedFinal);
  $('snapshot-final-context').textContent = nowcast?.status === 'estimated'
    ? `≈ ${number.format(nowcast.remaining)} colis à venir · à ${hourMinute.format(new Date(nowcast.asOf))}`
    : nowcast?.status === 'completed' ? 'Journée terminée · 4 h à 4 h' : 'Estimation en attente';
  $('nowcast-explanation').textContent = nowcast
    ? `${nowcast.explanation}${nowcast.status === 'estimated' ? ` Calcul : ${number.format(nowcast.created)} ÷ ${decimal.format(nowcast.historicalProgressPercent)} % ≈ ${number.format(nowcast.estimatedFinal)} colis. Journées comparables : ${nowcast.samples.length}.` : ''}`
    : 'Estimation indisponible pour le moment.';
  $('nowcast-samples').innerHTML = nowcast?.samples?.length ? nowcast.samples.map(sample => `<tr>
    <td>${formatDate(sample.date)}</td><td>${number.format(sample.parcelsAtSameTime)}</td><td>${number.format(sample.finalParcels)}</td>
    <td>${decimal.format(100 * sample.parcelsAtSameTime / sample.finalParcels)} %</td><td>${sample.weight}</td></tr>`).join('')
    : '<tr><td colspan="5" class="empty-cell">Aucune journée comparable utilisée.</td></tr>';
}

function renderDepots(data) {
  globalThis.updateDepotClientContext?.(data);
  const rows = data.depots || [];
  const total = rows.reduce((sum, row) => sum + row.parcelsToday, 0);
  const d7 = rows.reduce((sum, row) => sum + row.parcelsD7, 0);
  const difference = value => (value > 0 ? '+' : '') + number.format(value);
  $('depot-current-label').textContent = data.date === currentEdiDate() ? 'Colis aujourd’hui' : 'Colis · ' + formatDate(data.date);
  $('depot-period').textContent = data.date === currentEdiDate()
    ? 'Depuis 4 h jusqu’à ' + formatTime(data.asOf) + ' · D−7 à la même heure'
    : 'Journée complète · 4 h à 4 h · comparaison D−7';
  $('depot-body').innerHTML = rows.length ? rows.map(row => '<tr><td><strong>' +
    '<button type="button" class="sector-chart-trigger" data-depot-clients="' + row.depotId + '" aria-haspopup="dialog">' + (row.depotId > 0 ? number.format(row.depotId) + ' · ' : '') + escapeHtml(row.depotName) + '</button>' +
    '</strong></td><td>' + number.format(row.parcelsToday) + '</td><td>' + number.format(row.parcelsD7) +
    '</td><td>' + difference(row.parcelsToday - row.parcelsD7) + '</td><td>' +
    (total ? decimal.format(100 * row.parcelsToday / total) + ' %' : '—') + '</td></tr>').join('')
    : '<tr><td colspan="5" class="empty-cell">Aucun colis pour ces deux périodes.</td></tr>';
  $('depot-foot').innerHTML = '<tr><td>Total</td><td>' + number.format(total) + '</td><td>' +
    number.format(d7) + '</td><td>' + difference(total - d7) + '</td><td>' + (total ? '100 %' : '—') + '</td></tr>';
}

async function loadDepots(date, version) {
  try {
    const response = await fetch('/api/edi/depots?' + new URLSearchParams({ date }), { cache: 'no-store' });
    if (!response.ok) throw new Error('Réponse ' + response.status);
    const data = await response.json();
    if (version !== requestVersion) return;
    renderDepots(data);
  } catch {
    if (version !== requestVersion) return;
    $('depot-body').innerHTML = '<tr><td colspan="5" class="empty-cell">Dépôts indisponibles. Nouvelle tentative à la prochaine actualisation.</td></tr>';
    $('depot-foot').replaceChildren();
  }
}

function renderParcelSnapshot(data) {
  globalThis.updateEdiHistoryContext?.({ date: selectedAnalysisDate, asOf: data.nowcast?.asOf });
  globalThis.updateEdiFiscalHistoryContext?.({ date: selectedAnalysisDate, asOf: data.nowcast?.asOf });
  globalThis.updateEdiMapContext?.({ date: selectedAnalysisDate, asOf: data.nowcast?.asOf });
  const isToday = selectedAnalysisDate === currentEdiDate();
  const selectedDateLabel = formatDate(selectedAnalysisDate);
  $('snapshot-today-label').textContent = isToday ? 'Colis aujourd’hui' : `Colis · ${selectedDateLabel}`;
  const todayVolume = data.parcelsTodaySnapshot;
  const lastWeekVolume = data.parcelsLastWeekSameTime;
  const change = Number.isFinite(todayVolume) && Number.isFinite(lastWeekVolume) && lastWeekVolume > 0
    ? Math.round((todayVolume - lastWeekVolume) / lastWeekVolume * 100) : null;
  $('snapshot-today-context').textContent = change === null
    ? 'Comparaison indisponible avec la semaine passée'
    : `${change > 0 ? '+' : ''}${number.format(change === 0 ? 0 : change)} % par rapport à la semaine passée`;
  $('snapshot-today-context').title = isToday
    ? 'Comparaison avec le même jour la semaine passée, depuis 4 h et à la même heure.'
    : 'Comparaison des journées complètes, de 4 h à 4 h, à sept jours d’intervalle.';
  $('snapshot-d7-context').textContent = isToday ? 'Même période et même heure' : 'Même journée, sept jours plus tôt';
  renderNowcast(data.nowcast);
  $('snapshot-parcels-today').textContent = number.format(data.parcelsTodaySnapshot || 0);
  $('snapshot-parcels-d7').textContent = number.format(data.parcelsLastWeekSameTime || 0);
}

function render(data) {
  const regions = data.regions || [];
  const days = data.days || [];
  const clients = data.clients || [];
  selectedAnalysisDate = data.executionDate || selectedAnalysisDate;
  syncDateSelector();
  const isToday = selectedAnalysisDate === currentEdiDate();
  const selectedDateLabel = formatDate(selectedAnalysisDate);

  if (FORECAST_PAGE) {
    renderForecast(data.forecast, data.forecastArchive);
    renderForecastVersions(data.forecastArchive);
    $('week-range').textContent = `${formatDate(data.weekStart)} au ${formatDate(data.weekEnd)}`;
    $('database-time').textContent = formatTime(data.databaseNow);
    $('last-refresh').textContent = `Actualisé à ${formatTime(data.databaseNow)}`;
    $('forecast-next-refresh').textContent = new Date(data.forecastArchive.nextRefresh).toLocaleString('fr-CA', {
      timeZone: 'America/Toronto', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
    });
    clearTimeout(forecastRefreshTimer);
    const pending = data.actualsPending || data.forecastArchive.refreshPending;
    forecastRefreshTimer = setTimeout(() => {
      if (deliveryFollowToday) { selectedAnalysisDate = currentEdiDate(); syncDateSelector(); }
      load();
    }, pending ? 60000 : Math.max(1000, new Date(data.forecastArchive.nextRefresh).getTime() - Date.now()));
    return;
  }

  if (CLIENT_PAGE) {
    renderClients(clients, selectedAnalysisDate, data.databaseNow);
    $('week-range').textContent = `${formatDate(data.weekStart)} au ${formatDate(data.weekEnd)}`;
    $('database-time').textContent = formatTime(data.databaseNow);
    $('last-refresh').textContent = `Actualisé à ${formatTime(data.databaseNow)}`;
    return;
  }

  if (TRANSPORT_PAGE) {
    renderParcelSnapshot(data);
    $('linehaul-parcels-label').textContent = isToday ? 'Colis linehaul aujourd’hui' : 'Colis linehaul · ' + selectedDateLabel;
    $('linehaul-pallets-label').textContent = isToday ? 'Palettes linehaul aujourd’hui' : 'Palettes linehaul · ' + selectedDateLabel;
    $('linehaul-parcels-context').textContent = isToday ? 'Expéditions par région jusqu’à maintenant' : 'Expéditions par région pour la journée';
    $('regions-period-label').textContent = isToday
      ? 'Aujourd’hui, hier et même période la semaine dernière'
      : `${selectedDateLabel}, veille et même journée la semaine précédente`;
    $('regions-current-label').textContent = isToday ? 'Aujourd’hui' : selectedDateLabel;

    renderRegions(regions);
    $('week-range').textContent = formatDate(data.weekStart) + ' au ' + formatDate(data.weekEnd);
    $('database-time').textContent = formatTime(data.databaseNow);
    $('last-refresh').textContent = 'Actualisé à ' + formatTime(data.databaseNow);
    return;
  }

  renderParcelSnapshot(data);
  renderWeek(days, Number(data.weeklyBudget || 0), selectedAnalysisDate);
  $('week-range').textContent = `${formatDate(data.weekStart)} au ${formatDate(data.weekEnd)}`;
  $('database-time').textContent = formatTime(data.databaseNow);
  $('last-refresh').textContent = `Actualisé à ${formatTime(data.databaseNow)}`;
}

async function load(snapshotId) {
  const version = ++requestVersion;
  globalThis.resetEdiMapContext?.(selectedAnalysisDate);
  globalThis.resetEdiHistoryContext?.(selectedAnalysisDate);
  globalThis.resetEdiFiscalHistoryContext?.(selectedAnalysisDate);
  $('refresh-button').disabled = true;
  if ($('depot-body')) {
    globalThis.resetDepotClientContext?.(selectedAnalysisDate);
    $('depot-body').innerHTML = '<tr><td colspan="5" class="empty-cell">Chargement des dépôts…</td></tr>';
    $('depot-foot').replaceChildren();
  }
  $('error-banner').hidden = true;
  setConnection('waiting', 'Actualisation…');
  try {
    const query = new URLSearchParams({ date: selectedAnalysisDate, t: Date.now().toString() });
    if (FORECAST_PAGE && snapshotId) query.set('version', snapshotId);
    const endpoint = DELIVERY_PAGE ? '/api/edi/sectors' : FORECAST_PAGE ? '/api/edi/forecasts' : CLIENT_PAGE ? '/api/edi/clients' : TRANSPORT_PAGE ? '/api/edi/transport' : '/api/edi';
    const readData = async (path) => {
      const response = await fetch(path + '?' + query, { cache: 'no-store' });
      if (!response.ok) throw new Error('Réponse ' + response.status);
      return response.json();
    };
    const [pageData, snapshotData] = await Promise.all([
      readData(endpoint), TRANSPORT_PAGE ? readData('/api/edi') : Promise.resolve(null)
    ]);
    const data = snapshotData ? { ...pageData,
      parcelsTodaySnapshot: snapshotData.parcelsTodaySnapshot,
      parcelsLastWeekSameTime: snapshotData.parcelsLastWeekSameTime,
      nowcast: snapshotData.nowcast
    } : pageData;
    if (version !== requestVersion) return;
    if (DELIVERY_PAGE) {
      renderSectors(data);
      const days = (data.sectors[0]?.forecast.days || []).filter(day => ![0, 6].includes(new Date(`${day.date}T12:00:00`).getDay()));
      $('week-range').textContent = days.length ? `${formatDate(days[0].date)} au ${formatDate(days[days.length - 1].date)}` : '—';
      const refreshed = formatTime(data.weekly?.actualsUpdatedAt || new Date().toISOString());
      if (data.weekly) {
        $('delivery-next-refresh').textContent = new Date(data.weekly.nextRefresh).toLocaleString('fr-CA', { timeZone: 'America/Toronto', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
        clearTimeout(deliveryRefreshTimer);
        let refreshAt = new Date(data.weekly.nextRefresh).getTime();
        const refreshDay = new Intl.DateTimeFormat('en-US', { timeZone: 'America/Toronto', weekday: 'short' }).format(new Date(refreshAt));
        const saturdayMidnight = refreshAt - 6 * 60 * 60 * 1000;
        if (deliveryFollowToday && refreshDay === 'Sat' && saturdayMidnight > Date.now()) refreshAt = saturdayMidnight;
        deliveryRefreshTimer = setTimeout(() => {
          if (deliveryFollowToday) { selectedAnalysisDate = currentEdiDate(); syncDateSelector(); }
          load();
        }, Math.max(1000, refreshAt - Date.now()));
      }
      $('database-time').textContent = refreshed;
      $('last-refresh').textContent = `Actualisé à ${refreshed}`;
    } else {
      render(data);
      if ($('depot-body')) loadDepots(selectedAnalysisDate, version);
    }
    setConnection('ok', DELIVERY_PAGE || FORECAST_PAGE ? 'Relevé quotidien' : 'Données en direct');
    countdown = REFRESH_SECONDS;
  } catch (error) {
    if (version !== requestVersion) return;
    setConnection('error', 'Connexion interrompue');
    if (FORECAST_PAGE) {
      clearTimeout(forecastRefreshTimer);
      forecastRefreshTimer = setTimeout(() => load(), 60000);
    }
    if (DELIVERY_PAGE) {
      sectorData = null;
      globalThis.updateSectorForecastCharts?.(null);
      $('sector-body').replaceChildren();
      $('sector-foot').replaceChildren();
      $('sector-quality').textContent = '';
      $('sector-status').textContent = 'Prévisions de livraison indisponibles. Une nouvelle tentative sera faite automatiquement.';
      clearTimeout(deliveryRefreshTimer);
      deliveryRefreshTimer = setTimeout(() => load(), 15 * 60 * 1000);
    }
    $('error-banner').textContent = `Impossible de charger les ${DELIVERY_PAGE ? 'prévisions de livraison' : 'données EDI'}. Une nouvelle tentative sera faite automatiquement.`;
    $('error-banner').hidden = false;
  } finally {
    if (version === requestVersion) $('refresh-button').disabled = false;
  }
}

$('refresh-button').addEventListener('click', () => { countdown = REFRESH_SECONDS; load(); });
$('previous-date').addEventListener('click', () => moveAnalysisDate(-1));
$('next-date').addEventListener('click', () => moveAnalysisDate(1));
$('analysis-date').addEventListener('change', (event) => selectAnalysisDate(event.target.value));
$('client-filter')?.addEventListener('input', renderClientRows);
$('sector-filter')?.addEventListener('input', () => { if (sectorData) renderSectors(sectorData); });
$('sector-show-empty')?.addEventListener('change', () => { if (sectorData) renderSectors(sectorData); });
['pallet-unit', 'pallet-height', 'pallet-fill'].forEach(id => {
  const control = $(id);
  if (!control) return;
  try { const saved = localStorage.getItem(id); if (saved && [...control.options].some(option => option.value === saved)) control.value = saved; } catch { /* Storage can be disabled. */ }
  control.addEventListener('change', () => {
    try { localStorage.setItem(id, control.value); } catch { /* Keep the current selection in memory. */ }
    renderRegions(regionRows);
  });
});
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
if (!DELIVERY_PAGE && !FORECAST_PAGE) setInterval(() => {
  countdown -= 1;
  if (countdown <= 0) {
    countdown = REFRESH_SECONDS;
    load();
  }
  $('countdown').textContent = countdown;
}, 1000);

syncDateSelector();
load();
