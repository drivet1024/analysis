const REFRESH_SECONDS = 10;
const $ = (id) => document.getElementById(id);
const number = new Intl.NumberFormat('fr-CA');
const time = new Intl.DateTimeFormat('fr-CA', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const blockTime = new Intl.DateTimeFormat('fr-CA', { hour: '2-digit', minute: '2-digit' });
const shortDateTime = new Intl.DateTimeFormat('fr-CA', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
const efficiencyMonth = new Intl.DateTimeFormat('fr-CA', { month: 'short', year: '2-digit', timeZone: 'UTC' });
const fullDate = new Intl.DateTimeFormat('fr-CA', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
const DEPOTS = {
  'st-hubert': { name: 'Saint-Hubert', startHour: 16, endHour: 4, hasFloor: true, supportsMeasurements: true },
  quebec: { name: 'Québec', startHour: 13, endHour: 7, hasFloor: false, supportsMeasurements: true },
  toronto: { name: 'Toronto', startHour: 15, endHour: 9, hasFloor: false, supportsMeasurements: true },
  gilmore: { name: 'Gilmore', startHour: 15, endHour: 9, hasFloor: false, supportsMeasurements: false }
};
const requestedDepot = new URLSearchParams(window.location.search).get('depot');
const requestedTab = new URLSearchParams(window.location.search).get('tab');
const rememberedDepot = window.localStorage.getItem('nationex-dashboard-depot');
let selectedDepotKey = Object.hasOwn(DEPOTS, requestedDepot) ? requestedDepot
  : Object.hasOwn(DEPOTS, rememberedDepot) ? rememberedDepot
  : 'st-hubert';
let countdown = REFRESH_SECONDS;
let loading = false;
let conveyorRequestVersion = 0;
let dashboardRequestVersion = 0;
let conveyorEfficiencyData = null;
let conveyorEfficiencyPromise = null;

function isoLocalDate(date = new Date()) {
  const pad = (value) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function currentOperationalDate() {
  const date = new Date();
  if (date.getHours() < DEPOTS[selectedDepotKey].endHour) date.setDate(date.getDate() - 1);
  return isoLocalDate(date);
}

let selectedConveyorDate = currentOperationalDate();

function applyDepotSelection(depotKey, reload = true) {
  if (!Object.hasOwn(DEPOTS, depotKey)) return;
  selectedDepotKey = depotKey;
  const depot = DEPOTS[depotKey];
  $('depot-select').value = depotKey;
  $('depot-heading').textContent = depot.name;
  $('conveyor-shift-kicker').textContent = `Quart de ${String(depot.startHour).padStart(2, '0')}:00 à ${String((depot.endHour + 23) % 24).padStart(2, '0')}:59`;
  $('conveyor-high-label').textContent = depot.hasFloor ? 'Convoyeur du haut' : 'Convoyeur';
  $('hourly-high-title').textContent = depot.hasFloor ? 'Convoyeur du haut' : 'Convoyeur';
  const shiftContext = `${depot.startHour} h à ${(depot.endHour + 23) % 24} h · colis uniques · échelle commune`;
  $('hourly-high-context').textContent = shiftContext;
  $('hourly-manual-context').textContent = shiftContext;
  $('hourly-high-chart').setAttribute('aria-label', `Nombre de colis par heure sur le convoyeur de ${depot.name}`);
  $('capacity-chart').setAttribute('aria-label', `Débit du convoyeur de ${depot.name} par bloc de quinze minutes`);
  $('conveyor-quality-title').textContent = `Incidents du quart · ${depot.hasFloor ? 'convoyeur du haut' : 'convoyeur'}`;
  $('capacity-kicker').textContent = `Rendement du ${depot.hasFloor ? 'convoyeur du haut' : 'convoyeur'}`;
  $('conveyor-floor-card').hidden = !depot.hasFloor;
  $('hourly-floor-card').hidden = !depot.hasFloor;
  $('conveyor-pill-grid').classList.toggle('single-conveyor', !depot.hasFloor);
  document.querySelector('.conveyor-charts-grid').classList.toggle('single-conveyor', !depot.hasFloor);
  const underTwoCard = $('quality-under2-card');
  underTwoCard.classList.toggle('measurement-unavailable', !depot.supportsMeasurements);
  underTwoCard.setAttribute('aria-disabled', String(!depot.supportsMeasurements));
  underTwoCard.tabIndex = depot.supportsMeasurements ? 0 : -1;
  $('routes-tab-button').hidden = depotKey !== 'st-hubert';
  if (depotKey !== 'st-hubert') activateTab('conveyor-tab');
  document.title = `Nationex - ${depot.name}`;
  window.localStorage.setItem('nationex-dashboard-depot', depotKey);
  const url = new URL(window.location.href);
  url.searchParams.set('depot', depotKey);
  window.history.replaceState(null, '', url);
  const maximumDate = currentOperationalDate();
  if (selectedConveyorDate > maximumDate) selectedConveyorDate = maximumDate;
  syncConveyorDateControls();
  if (reload) {
    countdown = REFRESH_SECONDS;
    loading = false;
    load();
  }
}

function syncConveyorDateControls() {
  const maximumDate = currentOperationalDate();
  $('conveyor-analysis-date').value = selectedConveyorDate;
  $('conveyor-analysis-date').max = maximumDate;
  $('next-conveyor-date').disabled = selectedConveyorDate >= maximumDate;
}

function applyConveyorDate(value) {
  if (!value) return;
  selectedConveyorDate = value > currentOperationalDate() ? currentOperationalDate() : value;
  syncConveyorDateControls();
  loadConveyorData();
}

function moveConveyorDate(days) {
  const date = new Date(`${selectedConveyorDate}T12:00:00`);
  date.setDate(date.getDate() + days);
  applyConveyorDate(isoLocalDate(date));
}

function formatTime(value) {
  if (!value) return '—';
  return time.format(new Date(value));
}

function formatShortDateTime(value) {
  if (!value) return '—';
  return shortDateTime.format(new Date(value));
}

function formatDuration(firstScan, lastScan) {
  if (!firstScan || !lastScan) return '—';
  const totalMinutes = Math.max(0, Math.floor((new Date(lastScan) - new Date(firstScan)) / 60000));
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  return `${hours} h ${String(minutes).padStart(2, '0')} min`;
}

function statusLabel(status) {
  return status === 'active' ? 'Active' : status === 'recent' ? 'Récente' : status === 'pending' ? 'À venir' : 'Inactive';
}

function progressWidth(value) {
  return Math.max(0, Math.min(100, Number(value) || 0));
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>'"]/g, (character) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
  })[character]);
}

function makeRouteInteractive(element, routeId) {
  element.dataset.routeId = routeId;
  element.setAttribute('role', 'button');
  element.tabIndex = 0;
  element.setAttribute('aria-label', `Voir les clients de la route ${routeId}`);
  element.addEventListener('click', () => openRouteDetails(routeId));
  element.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      openRouteDetails(routeId);
    }
  });
}

function setConnection(state, label) {
  $('connection-dot').className = `live-dot ${state}`;
  $('connection-label').textContent = label;
}

function routeCard(route) {
  const article = document.createElement('article');
  article.className = `route-card ${route.status}`;
  const pct = progressWidth(route.estimatedProgressPercent);
  article.innerHTML = `
    <div class="route-card-head"><span class="route-number">${route.routeId}</span><span class="route-state ${route.status}">${statusLabel(route.status)}</span></div>
    <div class="route-values"><div><span>Total traités</span><strong>${number.format(route.parcelsPassed)}</strong></div><div><span>Restants estimés</span><strong>${number.format(route.estimatedRemaining)}</strong></div></div>
    <div class="source-breakdown"><span>Haut ${number.format(route.parcelsHigh)}</span><span>Sol ${number.format(route.parcelsFloor)}</span><span>Manuel ${number.format(route.parcelsManual)}</span></div>
    <div class="progress-track"><div class="progress-fill" style="width:${pct}%"></div></div>
    <div class="progress-caption"><span>${pct.toLocaleString('fr-CA')} % estimé</span><span>${number.format(route.parcelsLast5Minutes)} dans les 5 min</span></div>`;
  makeRouteInteractive(article, route.routeId);
  return article;
}

function tableRow(route) {
  const row = document.createElement('tr');
  const pct = progressWidth(route.estimatedProgressPercent);
  row.innerHTML = `
    <td data-label="Route" class="route-cell">${route.routeId}</td>
    <td data-label="État"><span class="route-state ${route.status}">${statusLabel(route.status)}</span></td>
    <td data-label="Total traités">${number.format(route.parcelsPassed)}</td>
    <td data-label="Haut">${number.format(route.parcelsHigh)}</td>
    <td data-label="Sol">${number.format(route.parcelsFloor)}</td>
    <td data-label="Scan manuel">${number.format(route.parcelsManual)}</td>
    <td data-label="5 min">${number.format(route.parcelsLast5Minutes)}</td>
    <td data-label="Total estimé">${number.format(route.estimatedTotal)}</td>
    <td data-label="Restants estimés" class="remaining-cell">${number.format(route.estimatedRemaining)}</td>
    <td data-label="Progression"><span class="mini-progress"><span class="mini-progress-track"><i style="width:${pct}%"></i></span>${pct.toLocaleString('fr-CA')} %</span></td>
    <td data-label="Premier colis">${formatTime(route.firstSeen)}</td>
    <td data-label="Dernier colis">${formatTime(route.lastSeen)}</td>
    <td data-label="Confiance"><span class="confidence-pill">${route.confidence}</span></td>`;
  makeRouteInteractive(row, route.routeId);
  return row;
}

function renderClientDetails(data) {
  const scheduleDay = data.scheduleDay || 'jour';
  $('dialog-title').textContent = `Route ${data.routeId} · clients du ${scheduleDay.toLowerCase()}`;
  $('pickup-day-heading').textContent = `Pickup ${scheduleDay.toLowerCase()}`;
  $('dialog-source').textContent = `Horaire officiel du ${scheduleDay.toLowerCase()} sur le serveur 101, vérifié avec les colis observés en haut, au sol et aux postes manuels depuis 16:00.`;
  $('client-summary').innerHTML = `
    <article><span>Clients planifiés</span><strong>${number.format(data.scheduledClients)}</strong></article>
    <article><span>Clients observés</span><strong>${number.format(data.observedClients)}</strong></article>
    <article><span>Colis traités uniques</span><strong>${number.format(data.parcelsPassed)}</strong></article>`;
  const body = $('clients-body');
  body.replaceChildren();
  if (!data.clients.length) {
    const row = document.createElement('tr');
    row.innerHTML = `<td colspan="12" class="empty-cell">Aucun client planifié le ${escapeHtml(scheduleDay.toLowerCase())} pour cette route.</td>`;
    body.append(row);
    return;
  }
  data.clients.forEach((client) => {
    const observed = client.parcelsPassed > 0;
    const row = document.createElement('tr');
    row.innerHTML = `
      <td data-label="Client" class="client-name">${escapeHtml(client.customerName)}</td>
      <td data-label="Nº client">${number.format(client.customerId)}</td>
      <td data-label="Pickup">${escapeHtml(client.pickupTime)}</td>
      <td data-label="Créés aujourd’hui">${number.format(client.parcelsCreatedToday)}</td>
      <td data-label="Total traités">${number.format(client.parcelsPassed)}</td>
      <td data-label="Haut">${number.format(client.parcelsHigh)}</td>
      <td data-label="Sol">${number.format(client.parcelsFloor)}</td>
      <td data-label="Manuel">${number.format(client.parcelsManual)}</td>
      <td data-label="Premier">${formatTime(client.firstSeen)}</td>
      <td data-label="Dernier">${formatTime(client.lastSeen)}</td>
      <td data-label="Vérification"><span class="verify-badge ${observed ? 'observed' : 'planned'}">${observed ? 'Planifié + observé' : 'Planifié seulement'}</span></td>
      <td data-label="Note">${escapeHtml(client.note || '—')}</td>`;
    body.append(row);
  });
}

async function openRouteDetails(routeId) {
  const dialog = $('client-dialog');
  $('dialog-title').textContent = `Route ${routeId} · clients du jour`;
  $('client-summary').innerHTML = '<article><span>Vérification</span><strong>Chargement…</strong></article>';
  $('clients-body').innerHTML = '<tr><td colspan="12" class="empty-cell">Croisement de l’horaire et des scans…</td></tr>';
  if (!dialog.open) dialog.showModal();
  try {
    const response = await fetch(`/api/live-routes/${routeId}/clients?t=${Date.now()}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`Réponse ${response.status}`);
    renderClientDetails(await response.json());
  } catch (error) {
    $('clients-body').innerHTML = '<tr><td colspan="12" class="empty-cell">Impossible de charger les clients de cette route.</td></tr>';
  }
}

function render(data) {
  const routes = data.routes || [];
  const active = routes.filter((route) => route.status === 'active');
  const activeOrRecent = routes.filter((route) => route.status !== 'inactive');
  const mappedTotal = routes.reduce((sum, route) => sum + route.parcelsPassed, 0);
  const estimatedEveningTotal = routes.reduce((sum, route) => sum + route.estimatedTotal, 0);
  const remaining = routes.reduce((sum, route) => sum + route.estimatedRemaining, 0);
  const coverage = data.totalProcessedParcels ? (100 * data.mappedProcessedParcels / data.totalProcessedParcels) : 0;

  $('latest-scan').textContent = formatTime(data.latestScan);
  $('active-routes').textContent = number.format(active.length);
  $('mapped-parcels').textContent = number.format(mappedTotal);
  $('estimated-evening-total').textContent = number.format(estimatedEveningTotal);
  $('remaining-parcels').textContent = number.format(remaining);
  $('mapping-coverage').textContent = `${coverage.toLocaleString('fr-CA', { maximumFractionDigits: 1 })} %`;
  $('coverage-context').textContent = `${number.format(data.mappedProcessedParcels)} associés · ${number.format(data.unmappedProcessedParcels)} hors 500xx`;
  $('mapping-note').textContent = `${number.format(data.ambiguousProcessedParcels)} colis associés à plusieurs routes et ${number.format(data.unmappedProcessedParcels)} colis sans route 500xx unique sont exclus afin d’éviter le double comptage.`;

  const cards = $('active-route-grid');
  cards.replaceChildren();
  const highlighted = active.length ? active : activeOrRecent.slice(0, 4);
  if (!highlighted.length) {
    const empty = document.createElement('article');
    empty.className = 'route-card';
    empty.textContent = 'Aucune route 500xx active depuis 16:00.';
    cards.append(empty);
  } else {
    highlighted.slice(0, 6).forEach((route) => cards.append(routeCard(route)));
  }

  const body = $('routes-body');
  body.replaceChildren();
  if (!routes.length) {
    const row = document.createElement('tr');
    row.innerHTML = '<td colspan="13" class="empty-cell">Aucune route 500xx planifiée aujourd’hui.</td>';
    body.append(row);
  } else {
    routes.forEach((route) => body.append(tableRow(route)));
  }
}

function renderUnprocessed(data) {
  $('unprocessed-summary').textContent = `${number.format(data.unprocessedParcels)} colis · ${number.format(data.clients)} clients · ${data.windowStart} au ${data.windowEnd}`;
  const body = $('unprocessed-body');
  body.replaceChildren();
  if (!data.rows.length) {
    const row = document.createElement('tr');
    row.innerHTML = '<td colspan="10" class="empty-cell">Aucun colis créé dans les trois derniers jours n’est en attente pour les clients des routes du jour.</td>';
    body.append(row);
    return;
  }
  data.rows.forEach((client) => {
    const row = document.createElement('tr');
    row.innerHTML = `
      <td data-label="Client" class="client-name">${escapeHtml(client.customerName)}</td>
      <td data-label="Nº client">${number.format(client.customerId)}</td>
      <td data-label="Route du jour" class="route-cell">${escapeHtml(client.routes)}</td>
      <td data-label="Pickup du jour">${escapeHtml(client.pickupTime)}</td>
      <td data-label="Aujourd’hui">${number.format(client.createdToday)}</td>
      <td data-label="Hier">${number.format(client.createdYesterday)}</td>
      <td data-label="Avant-hier">${number.format(client.createdTwoDaysAgo)}</td>
      <td data-label="Total non passés" class="remaining-cell">${number.format(client.unprocessedParcels)}</td>
      <td data-label="Plus ancien">${formatShortDateTime(client.oldestCreated)}</td>
      <td data-label="Plus récent">${formatShortDateTime(client.newestCreated)}</td>`;
    body.append(row);
  });
}

function renderHourly(data) {
  const rows = data.rows || [];
  const historical = data.date < currentOperationalDate();
  const sources = [
    ['high', data.totalHighParcels, data.firstHighScan, data.lastHighScan],
    ['floor', data.totalFloorParcels, data.firstFloorScan, data.lastFloorScan],
    ['manual', data.totalManualParcels, data.firstManualScan, data.lastManualScan]
  ];
  sources.forEach(([source, total, firstScan, lastScan]) => {
    $(`conveyor-${source}-total`).textContent = number.format(total);
    $(`hourly-${source}-total`).textContent = number.format(total);
    $(`conveyor-${source}-start`).textContent = firstScan ? blockTime.format(new Date(firstScan)) : '—';
    $(`conveyor-${source}-end`).textContent = lastScan ? blockTime.format(new Date(lastScan)) : '—';
    $(`conveyor-${source}-duration`).textContent = formatDuration(firstScan, lastScan);
    const elapsedMinutes = firstScan && lastScan
      ? Math.max(1, (new Date(lastScan) - new Date(firstScan)) / 60000)
      : 0;
    $(`conveyor-${source}-average`).textContent = elapsedMinutes
      ? `${number.format(Math.round(Number(total) * 60 / elapsedMinutes))} colis/h`
      : '—';
    $(`conveyor-${source}-average-stat`).hidden = !historical;
    $(`conveyor-${source}-end-stat`).hidden = !historical;
    $(`conveyor-${source}-duration-stat`).hidden = !historical;
  });
  const commonMax = Math.max(1, ...rows.map((row) => Number(row.parcels) || 0));
  const charts = [
    ['high', 'hourly-high-chart'],
    ['floor', 'hourly-floor-chart'],
    ['manual', 'hourly-manual-chart']
  ];
  charts.forEach(([source, elementId]) => {
    const container = $(elementId);
    container.replaceChildren();
    const sourceRows = rows.filter((row) => row.source === source);
    container.style.gridTemplateColumns = `repeat(${Math.max(1, sourceRows.length)}, minmax(0, 1fr))`;
    sourceRows.forEach((row) => {
      const parcels = Number(row.parcels) || 0;
      const height = parcels ? Math.max(2, 100 * parcels / commonMax) : 0;
      const nextHour = (row.hour + 1) % 24;
      const column = document.createElement('div');
      column.className = 'hour-column';
      column.innerHTML = `
        <span class="hour-value">${number.format(parcels)}</span>
        <div class="hour-bar-track" title="${number.format(parcels)} colis entre ${row.hour} h et ${nextHour} h"><i style="height:${height}%"></i></div>
        <small>${row.hour} h</small>`;
      container.append(column);
    });
  });
}

function renderHourlyError() {
  ['conveyor-high-total', 'conveyor-floor-total', 'conveyor-manual-total',
    'hourly-high-total', 'hourly-floor-total', 'hourly-manual-total',
    'conveyor-high-start', 'conveyor-floor-start', 'conveyor-manual-start',
    'conveyor-high-average', 'conveyor-floor-average', 'conveyor-manual-average',
    'conveyor-high-end', 'conveyor-floor-end', 'conveyor-manual-end',
    'conveyor-high-duration', 'conveyor-floor-duration', 'conveyor-manual-duration']
    .forEach((id) => { $(id).textContent = '—'; });
  ['high', 'floor', 'manual'].forEach((source) => {
    const hidden = selectedConveyorDate >= currentOperationalDate();
    $(`conveyor-${source}-average-stat`).hidden = hidden;
    $(`conveyor-${source}-end-stat`).hidden = hidden;
    $(`conveyor-${source}-duration-stat`).hidden = hidden;
  });
  ['hourly-high-chart', 'hourly-floor-chart', 'hourly-manual-chart'].forEach((id) => {
    $(id).innerHTML = '<span class="empty-chart">Données indisponibles pour cette date.</span>';
  });
}

function renderConveyorQuality(data) {
  const depot = DEPOTS[selectedDepotKey];
  const metrics = [
    ['chute98', data.chute98, data.chute98Percent],
    ['chute16', data.chute16, data.chute16Percent],
    ['noread', data.noRead, data.noReadPercent],
    ['recirculated', data.sameChuteRecirculated, data.sameChuteRecirculatedPercent]
  ];
  metrics.forEach(([metric, total, rate]) => {
    $(`quality-${metric}-rate`).textContent = `${Number(rate || 0).toLocaleString('fr-CA', { minimumFractionDigits: 1, maximumFractionDigits: 2 })} %`;
    $(`quality-${metric}-total`).textContent = `${number.format(total)} passages`;
  });
  $('quality-under2-rate').textContent = depot.supportsMeasurements
    ? `${Number(data.underTwoPoundsPercent || 0).toLocaleString('fr-CA', { minimumFractionDigits: 1, maximumFractionDigits: 2 })} %`
    : 'N/D';
  $('quality-under2-total').textContent = depot.supportsMeasurements
    ? `${number.format(data.underTwoPounds || 0)} colis sur ${number.format(data.highConveyorParcels || 0)}`
    : 'Poids non mesuré à Gilmore';
  const topChutes = $('quality-recirculated-top');
  topChutes.replaceChildren();
  if (!(data.topRecirculationChutes || []).length) {
    const empty = document.createElement('li');
    empty.textContent = 'Aucune';
    topChutes.append(empty);
  } else {
    data.topRecirculationChutes.forEach((item) => {
      const row = document.createElement('li');
      const chute = document.createElement('span');
      const parcels = document.createElement('b');
      chute.textContent = `Chute ${number.format(item.chute)}`;
      parcels.textContent = number.format(item.parcels);
      row.append(chute, parcels);
      topChutes.append(row);
    });
  }
  $('conveyor-quality-context').textContent = `${number.format(data.totalConveyed)} passages · ${depot.hasFloor ? 'convoyeur du haut' : 'convoyeur'} · ${depot.name}`;
}

function renderConveyorQualityError() {
  ['chute98', 'chute16', 'noread', 'recirculated'].forEach((metric) => {
    $(`quality-${metric}-rate`).textContent = '— %';
    $(`quality-${metric}-total`).textContent = 'Données indisponibles';
  });
  $('quality-under2-rate').textContent = '— %';
  $('quality-under2-total').textContent = 'Données indisponibles';
  $('quality-recirculated-top').innerHTML = '<li>—</li>';
  $('conveyor-quality-context').textContent = 'Les indicateurs de qualité n’ont pas pu être chargés.';
}

function renderCapacity(data) {
  const depot = DEPOTS[selectedDepotKey];
  const benchmarkHourly = Number(data.practicalCapacityPerHour) || 0;
  const maximumHourly = Number(data.maximumObservedPerHour) || 0;
  const averageUtilization = Number(data.utilizationSinceStartPercent) || 0;
  const databaseNow = new Date(data.databaseNow);
  const shiftStart = new Date(data.shiftStart);
  const beforeShift = databaseNow < shiftStart;
  const peaks = data.dailyPeaks || [];
  const dates = peaks.map((peak) => peak.shiftDate).sort();
  const buckets = data.buckets || [];
  const observedBuckets = buckets.filter((bucket) => !bucket.isFuture);
  const currentBucket = observedBuckets.at(-1);
  const currentHourly = Number(data.currentRatePerHour) || 0;
  const averageHourly = Number(data.averagePerHourSinceStart) || 0;
  const potentialMinutes = Number(data.potentialMinutes) || 0;
  const excludedZeroMinutes = Number(data.excludedZeroMinutes) || 0;
  const potentialParcels = Number(data.potentialParcelsAtPracticalCapacity) || 0;

  $('quality-capacity-potential').textContent = beforeShift || !potentialParcels
    ? '—'
    : `${number.format(potentialParcels)} colis`;
  $('quality-capacity-potential-context').textContent = beforeShift
    ? `Le quart commence à ${depot.startHour} h`
    : `sur ${number.format(Math.floor(potentialMinutes / 60))} h ${String(potentialMinutes % 60).padStart(2, '0')} min · ${number.format(excludedZeroMinutes)} min à zéro exclues · ${number.format(benchmarkHourly)}/h`;

  $('capacity-benchmark').textContent = benchmarkHourly ? `${number.format(benchmarkHourly)}/h` : '—';
  $('capacity-maximum').textContent = maximumHourly ? `${number.format(maximumHourly)}/h` : '—';
  $('capacity-current-peak').textContent = beforeShift ? '—' : `${number.format(currentHourly)}/h`;
  $('capacity-average-utilization').textContent = beforeShift ? '—' : `${number.format(averageHourly)}/h`;
  $('capacity-current-context').textContent = beforeShift
    ? `Le quart commence à ${depot.startHour} h`
    : `${Number(currentBucket?.utilizationPercent || 0).toLocaleString('fr-CA', { maximumFractionDigits: 1 })} % de la capacité · mise à jour aux 10 s`;
  $('capacity-benchmark-context').textContent = `${number.format(data.benchmarkShifts || 0)} quarts complétés · 75e percentile`;
  $('capacity-average-context').textContent = beforeShift
    ? `Le quart commence à ${depot.startHour} h`
    : `${averageUtilization.toLocaleString('fr-CA', { maximumFractionDigits: 1 })} % de la capacité depuis le premier colis`;
  $('capacity-summary').textContent = dates.length
    ? `${number.format(data.benchmarkShifts)} quarts · ${dates[0]} au ${dates[dates.length - 1]}`
    : 'Aucun quart historique disponible';

  const measuredMinutes = (bucket) => {
    if (bucket.isFuture) return 0;
    const elapsed = Math.ceil((databaseNow - new Date(bucket.bucketStart)) / 60000);
    return Math.max(1, Math.min(15, elapsed));
  };
  const maxRate = Math.max(1, benchmarkHourly * 1.2, ...buckets.map((bucket) => {
    const total = Number(bucket.totalParcels ?? bucket.parcels) || 0;
    const totalRate = measuredMinutes(bucket) ? 60 * total / measuredMinutes(bucket) : 0;
    return Math.max(totalRate, Number(bucket.parcelsPerHour) || 0);
  }));
  const chart = $('capacity-chart');
  chart.style.gridTemplateColumns = `repeat(${Math.max(1, buckets.length)}, minmax(0, 1fr))`;
  chart.style.setProperty('--benchmark-position', `${Math.min(100, 100 * benchmarkHourly / maxRate)}%`);
  chart.replaceChildren();
  buckets.forEach((bucket, index) => {
    const uniqueParcels = Number(bucket.uniqueParcels ?? bucket.parcels) || 0;
    const totalParcels = Number(bucket.totalParcels ?? bucket.parcels) || 0;
    const recirculated = Number(bucket.recirculated ?? Math.max(0, totalParcels - uniqueParcels)) || 0;
    const chute98 = Number(bucket.chute98) || 0;
    const hourlyRate = Number(bucket.parcelsPerHour) || 0;
    const totalHourlyRate = measuredMinutes(bucket) ? Math.round(60 * totalParcels / measuredMinutes(bucket)) : 0;
    const utilization = Number(bucket.utilizationPercent) || 0;
    const bucketDate = new Date(bucket.bucketStart);
    const endDate = new Date(bucketDate.getTime() + 15 * 60 * 1000);
    const column = document.createElement('div');
    column.className = `capacity-bar ${bucket.status}`;
    const uniqueHeight = bucket.isFuture ? 0 : Math.min(100, 100 * hourlyRate / maxRate);
    const totalHeight = bucket.isFuture ? 0 : Math.min(100, 100 * totalHourlyRate / maxRate);
    const accessibleSummary = bucket.isFuture
      ? `${bucketDate.getHours()} h ${String(bucketDate.getMinutes()).padStart(2, '0')} · à venir`
      : `${number.format(hourlyRate)} colis uniques par heure · ${number.format(totalHourlyRate)} passages par heure · ${number.format(recirculated)} recirculation(s) · ${number.format(chute98)} colis chute 98 · ${utilization.toLocaleString('fr-CA')} % de capacité · ${formatTime(bucket.bucketStart)} à ${formatTime(endDate)}`;
    column.setAttribute('aria-label', accessibleSummary);
    if (!bucket.isFuture) column.tabIndex = 0;
    const tooltip = bucket.isFuture ? '' : `
      <div class="capacity-tooltip" role="tooltip">
        <strong>${blockTime.format(bucketDate)} – ${blockTime.format(endDate)}</strong>
        <div class="capacity-tooltip-throughput">
          <span><small>Débit</small><b>${number.format(hourlyRate)} colis/h</b></span>
          <em>${number.format(totalHourlyRate)} passages/h au total</em>
        </div>
        <div class="capacity-tooltip-values">
          <span class="recirculated"><small>Recirculations</small><b>${number.format(recirculated)}</b></span>
          <span class="chute"><small>Chute 98</small><b>${number.format(chute98)}</b></span>
        </div>
        <div class="capacity-tooltip-rates"><span>${utilization.toLocaleString('fr-CA')} % capacité</span></div>
      </div>`;
    column.innerHTML = `<i class="capacity-total-bar" style="height:${totalHeight}%"></i><i class="capacity-unique-bar" style="height:${uniqueHeight}%"></i>${tooltip}${index % 4 === 0 ? `<small>${bucketDate.getHours()} h</small>` : ''}`;
    chart.append(column);
  });

  const gapMinutes = Number(data.gapMinutes) || 0;
  $('capacity-gap-minutes').textContent = beforeShift ? '—' : `${number.format(gapMinutes)} min`;
  $('capacity-gap-context').textContent = beforeShift
    ? 'L’analyse commencera avec le premier colis'
    : `${number.format((data.gaps || []).length)} période(s) sous ${number.format(Math.ceil(benchmarkHourly * .4))} colis/heure · ouverture et fermeture exclues`;
  const body = $('capacity-gap-body');
  body.replaceChildren();
  if (beforeShift) {
    body.innerHTML = '<tr><td colspan="6" class="empty-cell">Le quart courant n’a pas encore commencé.</td></tr>';
  } else if (!(data.gaps || []).length) {
    body.innerHTML = '<tr><td colspan="6" class="empty-cell">Aucun creux soutenu détecté depuis le début du traitement.</td></tr>';
  } else {
    data.gaps.forEach((gap) => {
      const row = document.createElement('tr');
      row.innerHTML = `
        <td data-label="Début">${formatTime(gap.start)}</td>
        <td data-label="Fin">${formatTime(gap.end)}</td>
        <td data-label="Durée">${number.format(gap.durationMinutes)} min</td>
        <td data-label="Colis">${number.format(gap.parcels)}</td>
        <td data-label="Moyenne/heure">${number.format(Number(gap.averagePerHour) || 0)}</td>
        <td data-label="Utilisation">${Number(gap.utilizationPercent).toLocaleString('fr-CA')} %</td>`;
      body.append(row);
    });
  }
}

function renderCapacityError() {
  $('capacity-summary').textContent = 'Analyse de capacité temporairement indisponible';
  $('capacity-gap-body').innerHTML = '<tr><td colspan="6" class="empty-cell">Impossible de charger le benchmark et les creux.</td></tr>';
  $('quality-capacity-potential').textContent = '—';
  $('quality-capacity-potential-context').textContent = 'Données indisponibles';
}

function formatEfficiency(value) {
  return `${Number(value || 0).toLocaleString('fr-CA', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} %`;
}

function efficiencyMonthLabel(value) {
  return efficiencyMonth.format(new Date(`${String(value).slice(0, 10)}T00:00:00Z`)).replace('.', '');
}

function efficiencyCurve(points) {
  if (!points.length) return '';
  let path = `M ${points[0].x} ${points[0].y}`;
  for (let index = 1; index < points.length; index += 1) {
    const previous = points[index - 1];
    const point = points[index];
    const midpoint = (previous.x + point.x) / 2;
    path += ` C ${midpoint} ${previous.y}, ${midpoint} ${point.y}, ${point.x} ${point.y}`;
  }
  return path;
}

function renderConveyorEfficiencyChart(data) {
  const months = [...(data.months || [])].sort((left, right) => String(left.month).localeCompare(String(right.month)));
  const chart = $('conveyor-efficiency-chart');
  if (!months.length) {
    chart.innerHTML = '<p class="empty-cell">Aucun historique mensuel disponible.</p>';
    return;
  }
  const width = 1040;
  const height = 350;
  const margin = { top: 38, right: 26, bottom: 54, left: 58 };
  const values = months.map((month) => Number(month.efficiencyPercent) || 0);
  let yMinimum = Math.max(0, Math.floor((Math.min(...values) - 3) / 5) * 5);
  let yMaximum = Math.min(100, Math.ceil((Math.max(...values) + 3) / 5) * 5);
  if (yMaximum - yMinimum < 10) {
    yMinimum = Math.max(0, yMinimum - 5);
    yMaximum = Math.min(100, yMaximum + 5);
  }
  const plotWidth = width - margin.left - margin.right;
  const plotHeight = height - margin.top - margin.bottom;
  const x = (index) => margin.left + (months.length === 1 ? plotWidth / 2 : (plotWidth * index) / (months.length - 1));
  const y = (value) => margin.top + ((yMaximum - value) / Math.max(1, yMaximum - yMinimum)) * plotHeight;
  const points = months.map((month, index) => ({ x: x(index), y: y(values[index]), month }));
  const linePath = efficiencyCurve(points);
  const areaPath = `${linePath} L ${points.at(-1).x} ${margin.top + plotHeight} L ${points[0].x} ${margin.top + plotHeight} Z`;
  const grid = Array.from({ length: 5 }, (_, index) => {
    const value = yMaximum - ((yMaximum - yMinimum) * index) / 4;
    const yPosition = y(value);
    return `<line class="efficiency-grid-line" x1="${margin.left}" y1="${yPosition}" x2="${width - margin.right}" y2="${yPosition}"></line><text class="efficiency-axis-label" x="${margin.left - 11}" y="${yPosition + 4}" text-anchor="end">${value.toLocaleString('fr-CA', { maximumFractionDigits: 1 })} %</text>`;
  }).join('');
  const pointMarkup = points.map((point) => {
    const month = point.month;
    const currentClass = month.isPartial ? ' current' : '';
    const label = `${efficiencyMonthLabel(month.month)} : ${formatEfficiency(month.efficiencyPercent)} · ${number.format(month.successfulOutcomes)} réussis sur ${number.format(month.assessedOutcomes)} résultats`;
    return `<g><title>${label}</title><circle class="efficiency-point${currentClass}" cx="${point.x}" cy="${point.y}" r="6"></circle><text class="efficiency-value-label" x="${point.x}" y="${point.y - 14}">${Number(month.efficiencyPercent).toLocaleString('fr-CA', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} %</text><text class="efficiency-month-label" x="${point.x}" y="${height - 20}">${efficiencyMonthLabel(month.month)}</text></g>`;
  }).join('');
  chart.innerHTML = `<svg viewBox="0 0 ${width} ${height}" aria-hidden="true"><defs><linearGradient id="efficiency-area-gradient" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#38dc9a" stop-opacity=".26"></stop><stop offset="100%" stop-color="#38dc9a" stop-opacity="0"></stop></linearGradient></defs>${grid}<path class="efficiency-area" d="${areaPath}"></path><path class="efficiency-line" d="${linePath}"></path>${pointMarkup}</svg>`;
}

function renderConveyorEfficiency(data) {
  conveyorEfficiencyData = data;
  const months = data.months || [];
  const current = months.find((month) => String(month.month).slice(0, 7) === String(data.currentMonth).slice(0, 7)) || months.at(-1);
  if (!current) throw new Error('Mois courant absent');
  $('conveyor-efficiency-rate').textContent = formatEfficiency(current.efficiencyPercent);
  $('conveyor-efficiency-context').textContent = `${number.format(current.successfulOutcomes)} résultats réussis sur ${number.format(current.assessedOutcomes)} · ${number.format(current.revenueRiskParcels)} colis à risque de revenu`;
  $('conveyor-efficiency-card').classList.remove('loading-card');
  $('conveyor-efficiency-summary').innerHTML = `<div><span>Mois courant</span><strong>${formatEfficiency(current.efficiencyPercent)}</strong></div><div><span>Résultats évalués</span><strong>${number.format(current.assessedOutcomes)}</strong></div><div><span>Colis à risque de revenu</span><strong>${number.format(current.revenueRiskParcels)}</strong></div>`;
  $('conveyor-efficiency-generated').textContent = `Dernier calcul : ${formatShortDateTime(data.generatedAt)}${current.lastScan ? ` · données reçues jusqu’au ${formatShortDateTime(current.lastScan)}` : ''}. Le point bleu représente le mois en cours.`;
  renderConveyorEfficiencyChart(data);
}

function renderConveyorEfficiencyError() {
  $('conveyor-efficiency-rate').textContent = '— %';
  $('conveyor-efficiency-context').textContent = 'Calcul temporairement indisponible. Cliquez pour réessayer.';
  $('conveyor-efficiency-card').classList.remove('loading-card');
}

async function loadConveyorEfficiency(force = false) {
  if (conveyorEfficiencyData && !force) return conveyorEfficiencyData;
  if (conveyorEfficiencyPromise && !force) return conveyorEfficiencyPromise;
  $('conveyor-efficiency-card').classList.add('loading-card');
  conveyorEfficiencyPromise = fetch(`/api/conveyor-efficiency?t=${Date.now()}`, { cache: 'no-store' })
    .then((response) => {
      if (!response.ok) throw new Error(`Efficiency response ${response.status}`);
      return response.json();
    })
    .then((data) => {
      renderConveyorEfficiency(data);
      return data;
    })
    .catch((error) => {
      renderConveyorEfficiencyError();
      throw error;
    })
    .finally(() => { conveyorEfficiencyPromise = null; });
  return conveyorEfficiencyPromise;
}

async function openConveyorEfficiencyDialog() {
  try {
    const data = await loadConveyorEfficiency(!conveyorEfficiencyData);
    renderConveyorEfficiencyChart(data);
    $('conveyor-efficiency-dialog').showModal();
  } catch { /* La pastille affiche déjà l'état d'erreur. */ }
}

function renderUnderTwoPoundsClients(data) {
  const dateLabel = fullDate.format(new Date(`${data.date}T12:00:00`));
  $('under2-clients-title').textContent = `${data.depot} · ${dateLabel}`;
  const largest = data.clients?.[0];
  $('under2-clients-summary').innerHTML = `
    <article><span>Colis sous 2 lb</span><strong>${number.format(data.totalParcels || 0)}</strong></article>
    <article><span>Clients</span><strong>${number.format(data.clientCount || 0)}</strong></article>
    <article><span>Plus gros client</span><strong>${largest ? escapeHtml(largest.customerName) : '—'}</strong></article>`;
  const body = $('under2-clients-body');
  body.replaceChildren();
  if (!(data.clients || []).length) {
    body.innerHTML = '<tr><td colspan="7" class="empty-cell">Aucun colis sous 2 lb pour ce quart.</td></tr>';
  } else {
    data.clients.forEach((client) => {
      const row = document.createElement('tr');
      const dimensions = [client.averageLength, client.averageHeight, client.averageWidth];
      const dimensionLabel = dimensions.every(value => Number.isFinite(value) && value > 0)
        ? dimensions.map(value => value.toLocaleString('fr-CA', { maximumFractionDigits: 1 })).join(' × ') + ' po' : '—';
      row.innerHTML = `
        <td data-label="Client" class="client-name">${escapeHtml(client.customerName)}</td>
        <td data-label="Nº client">${client.customerId ? number.format(client.customerId) : '—'}</td>
        <td data-label="Colis sous 2 lb"><strong>${number.format(client.parcels)}</strong></td>
        <td data-label="Part du total">${Number(client.sharePercent || 0).toLocaleString('fr-CA', { minimumFractionDigits: 1, maximumFractionDigits: 2 })} %</td>
        <td data-label="Dimensions moyennes L × H × l (po)" class="under2-dimensions">${dimensionLabel}<small>${number.format(client.dimensionedParcels || 0)} / ${number.format(client.parcels)} colis mesurés</small></td>
        <td data-label="Premier passage">${formatTime(client.firstScan)}</td>
        <td data-label="Dernier passage">${formatTime(client.lastScan)}</td>`;
      body.append(row);
    });
  }
  $('under2-clients-source').textContent = (data.notes || []).join(' ');
}

async function openUnderTwoPoundsClientsDialog() {
  if (!DEPOTS[selectedDepotKey].supportsMeasurements) return;
  const requestedDepot = selectedDepotKey;
  const requestedDate = selectedConveyorDate;
  const dialog = $('under2-clients-dialog');
  $('under2-clients-title').textContent = `${DEPOTS[requestedDepot].name} · chargement…`;
  $('under2-clients-summary').innerHTML = '<article><span>Analyse</span><strong>Chargement…</strong></article>';
  $('under2-clients-body').innerHTML = '<tr><td colspan="7" class="empty-cell">Regroupement des colis par client…</td></tr>';
  $('under2-clients-source').textContent = 'Colis uniques du convoyeur automatisé; les scans manuels sont exclus.';
  if (!dialog.open) dialog.showModal();
  try {
    const query = new URLSearchParams({ date: requestedDate, depot: requestedDepot, t: String(Date.now()) });
    const response = await fetch(`/api/conveyor-under-two-pounds/clients?${query}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`Réponse ${response.status}`);
    if (requestedDepot !== selectedDepotKey || requestedDate !== selectedConveyorDate) return;
    renderUnderTwoPoundsClients(await response.json());
  } catch (error) {
    $('under2-clients-summary').innerHTML = '<article><span>Analyse</span><strong>Indisponible</strong></article>';
    $('under2-clients-body').innerHTML = '<tr><td colspan="7" class="empty-cell">Impossible de charger les clients pour ce quart.</td></tr>';
  }
}

async function loadConveyorData(timestamp = Date.now()) {
  const requestVersion = ++conveyorRequestVersion;
  const requestedDate = selectedConveyorDate;
  const requestedDepot = selectedDepotKey;
  const query = `date=${encodeURIComponent(requestedDate)}&depot=${encodeURIComponent(requestedDepot)}&t=${timestamp}`;
  const [hourlyResult, qualityResult, capacityResult] = await Promise.allSettled([
    fetch(`/api/conveyor-hourly?${query}`, { cache: 'no-store' }).then((response) => {
      if (!response.ok) throw new Error(`Hourly response ${response.status}`);
      return response.json();
    }),
    fetch(`/api/conveyor-quality?${query}`, { cache: 'no-store' }).then((response) => {
      if (!response.ok) throw new Error(`Quality response ${response.status}`);
      return response.json();
    }),
    fetch(`/api/high-conveyor-capacity?${query}`, { cache: 'no-store' }).then((response) => {
      if (!response.ok) throw new Error(`Capacity response ${response.status}`);
      return response.json();
    })
  ]);
  if (requestVersion !== conveyorRequestVersion || requestedDepot !== selectedDepotKey) return { success: false };
  if (hourlyResult.status === 'fulfilled') renderHourly(hourlyResult.value);
  else renderHourlyError();
  if (qualityResult.status === 'fulfilled') renderConveyorQuality(qualityResult.value);
  else renderConveyorQualityError();
  if (capacityResult.status === 'fulfilled') renderCapacity(capacityResult.value);
  else renderCapacityError();
  return {
    success: hourlyResult.status === 'fulfilled',
    databaseNow: hourlyResult.status === 'fulfilled' ? hourlyResult.value.databaseNow : null
  };
}

async function load() {
  if (loading) return;
  const requestVersion = ++dashboardRequestVersion;
  const requestedDepot = selectedDepotKey;
  loading = true;
  $('refresh-button').disabled = true;
  $('error-banner').hidden = true;
  try {
    const timestamp = Date.now();
    const conveyorPromise = loadConveyorData(timestamp);
    if (requestedDepot !== 'st-hubert') {
      const result = await conveyorPromise;
      if (requestVersion !== dashboardRequestVersion || requestedDepot !== selectedDepotKey) return;
      if (!result?.success) throw new Error('Données convoyeur indisponibles');
      setConnection('ok', 'Données en direct');
      $('last-refresh').textContent = `Actualisé à ${formatTime(result.databaseNow)}`;
      countdown = REFRESH_SECONDS;
      return;
    }
    const [routesResponse, unprocessedResponse] = await Promise.all([
      fetch(`/api/live-routes?t=${timestamp}`, { cache: 'no-store' }),
      fetch(`/api/unprocessed-parcels?t=${timestamp}`, { cache: 'no-store' })
    ]);
    if (requestVersion !== dashboardRequestVersion || requestedDepot !== selectedDepotKey) return;
    if (!routesResponse.ok || !unprocessedResponse.ok) throw new Error(`Réponse ${routesResponse.status}/${unprocessedResponse.status}`);
    const [data, unprocessed] = await Promise.all([routesResponse.json(), unprocessedResponse.json()]);
    render(data);
    renderUnprocessed(unprocessed);
    setConnection('ok', 'Données en direct');
    $('last-refresh').textContent = `Actualisé à ${formatTime(data.databaseNow)}`;
    countdown = REFRESH_SECONDS;
    await conveyorPromise;
  } catch (error) {
    if (requestVersion !== dashboardRequestVersion || requestedDepot !== selectedDepotKey) return;
    setConnection('error', 'Connexion interrompue');
    $('error-banner').textContent = `Impossible d’actualiser les données. Nouvelle tentative automatique dans ${countdown} secondes.`;
    $('error-banner').hidden = false;
  } finally {
    if (requestVersion === dashboardRequestVersion) {
      loading = false;
      $('refresh-button').disabled = false;
    }
  }
}

$('refresh-button').addEventListener('click', () => { countdown = REFRESH_SECONDS; load(); });
$('depot-select').addEventListener('change', (event) => applyDepotSelection(event.target.value));
$('routes-tab-button').addEventListener('click', () => activateTab('routes-tab'));
$('conveyor-tab-button').addEventListener('click', () => activateTab('conveyor-tab'));
$('previous-conveyor-date').addEventListener('click', () => moveConveyorDate(-1));
$('next-conveyor-date').addEventListener('click', () => moveConveyorDate(1));
$('conveyor-analysis-date').addEventListener('change', (event) => applyConveyorDate(event.target.value));
$('conveyor-efficiency-card').addEventListener('click', openConveyorEfficiencyDialog);
$('conveyor-efficiency-close').addEventListener('click', () => $('conveyor-efficiency-dialog').close());
$('conveyor-efficiency-dialog').addEventListener('click', (event) => {
  if (event.target === $('conveyor-efficiency-dialog')) $('conveyor-efficiency-dialog').close();
});
$('quality-under2-card').addEventListener('click', openUnderTwoPoundsClientsDialog);
$('quality-under2-card').addEventListener('keydown', (event) => {
  if (event.key !== 'Enter' && event.key !== ' ') return;
  event.preventDefault();
  openUnderTwoPoundsClientsDialog();
});
$('under2-clients-close').addEventListener('click', () => $('under2-clients-dialog').close());
$('under2-clients-dialog').addEventListener('click', (event) => {
  if (event.target === $('under2-clients-dialog')) $('under2-clients-dialog').close();
});
$('dialog-close').addEventListener('click', () => $('client-dialog').close());
$('client-dialog').addEventListener('click', (event) => {
  if (event.target === $('client-dialog')) $('client-dialog').close();
});
setInterval(() => {
  countdown -= 1;
  if (countdown <= 0) {
    countdown = REFRESH_SECONDS;
    load();
  }
  $('countdown').textContent = countdown;
}, 1000);

applyDepotSelection(selectedDepotKey, false);
if (requestedTab === 'routes-tab' && selectedDepotKey === 'st-hubert') activateTab('routes-tab');
if (requestedTab === 'conveyor-tab') activateTab('conveyor-tab');
load();

function activateTab(tabId) {
  document.querySelectorAll('.tab-panel').forEach((panel) => { panel.hidden = panel.id !== tabId; });
  document.querySelectorAll('.dashboard-tab').forEach((button) => {
    const active = button.dataset.tab === tabId;
    button.classList.toggle('active', active);
    button.setAttribute('aria-selected', String(active));
  });
  if (tabId === 'conveyor-tab') loadConveyorEfficiency().catch(() => {});
}
