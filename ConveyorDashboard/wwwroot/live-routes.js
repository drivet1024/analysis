const REFRESH_SECONDS = 30;
const $ = (id) => document.getElementById(id);
const number = new Intl.NumberFormat('fr-CA');
const time = new Intl.DateTimeFormat('fr-CA', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const shortDateTime = new Intl.DateTimeFormat('fr-CA', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
let countdown = REFRESH_SECONDS;
let loading = false;

function formatTime(value) {
  if (!value) return '—';
  return time.format(new Date(value));
}

function formatShortDateTime(value) {
  if (!value) return '—';
  return shortDateTime.format(new Date(value));
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
  $('conveyor-high-total').textContent = number.format(data.totalHighParcels);
  $('conveyor-floor-total').textContent = number.format(data.totalFloorParcels);
  $('conveyor-manual-total').textContent = number.format(data.totalManualParcels);
  $('conveyor-high-start').textContent = formatTime(data.firstHighScan);
  $('conveyor-floor-start').textContent = formatTime(data.firstFloorScan);
  $('conveyor-manual-start').textContent = formatTime(data.firstManualScan);
  const commonMax = Math.max(1, ...rows.map((row) => Number(row.parcels) || 0));
  const charts = [
    ['high', 'hourly-high-chart'],
    ['floor', 'hourly-floor-chart'],
    ['manual', 'hourly-manual-chart']
  ];
  charts.forEach(([source, elementId]) => {
    const container = $(elementId);
    container.replaceChildren();
    rows.filter((row) => row.source === source).forEach((row) => {
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

function renderCapacity(data) {
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

  $('capacity-benchmark').textContent = benchmarkHourly ? `${number.format(benchmarkHourly)}/h` : '—';
  $('capacity-maximum').textContent = maximumHourly ? `${number.format(maximumHourly)}/h` : '—';
  $('capacity-current-peak').textContent = beforeShift ? '—' : `${number.format(currentHourly)}/h`;
  $('capacity-average-utilization').textContent = beforeShift ? '—' : `${number.format(averageHourly)}/h`;
  $('capacity-current-context').textContent = beforeShift
    ? 'Le quart commence à 16 h'
    : `${Number(currentBucket?.utilizationPercent || 0).toLocaleString('fr-CA', { maximumFractionDigits: 1 })} % de la capacité · mise à jour aux 30 s`;
  $('capacity-benchmark-context').textContent = `${number.format(data.benchmarkShifts || 0)} quarts complétés · 75e percentile`;
  $('capacity-average-context').textContent = beforeShift
    ? 'Le quart commence à 16 h'
    : `${averageUtilization.toLocaleString('fr-CA', { maximumFractionDigits: 1 })} % de la capacité depuis le premier colis`;
  $('capacity-summary').textContent = dates.length
    ? `${number.format(data.benchmarkShifts)} quarts · ${dates[0]} au ${dates[dates.length - 1]}`
    : 'Aucun quart historique disponible';

  const maxRate = Math.max(1, benchmarkHourly * 1.2, ...buckets.map((bucket) => Number(bucket.parcelsPerHour) || 0));
  const chart = $('capacity-chart');
  chart.style.setProperty('--benchmark-position', `${Math.min(100, 100 * benchmarkHourly / maxRate)}%`);
  chart.replaceChildren();
  buckets.forEach((bucket, index) => {
    const hourlyRate = Number(bucket.parcelsPerHour) || 0;
    const utilization = Number(bucket.utilizationPercent) || 0;
    const bucketDate = new Date(bucket.bucketStart);
    const endDate = new Date(bucketDate.getTime() + 15 * 60 * 1000);
    const column = document.createElement('div');
    column.className = `capacity-bar ${bucket.status}`;
    const height = bucket.isFuture ? 0 : Math.min(100, 100 * hourlyRate / maxRate);
    column.title = bucket.isFuture
      ? `${bucketDate.getHours()} h ${String(bucketDate.getMinutes()).padStart(2, '0')} · à venir`
      : `${number.format(bucket.parcels)} colis · ${number.format(hourlyRate)} colis/heure · ${utilization.toLocaleString('fr-CA')} % · ${formatTime(bucket.bucketStart)} à ${formatTime(endDate)}`;
    column.innerHTML = `<i style="height:${height}%"></i>${index % 4 === 0 ? `<small>${bucketDate.getHours()} h</small>` : ''}`;
    chart.append(column);
  });

  const gapMinutes = Number(data.gapMinutes) || 0;
  $('capacity-gap-minutes').textContent = beforeShift ? '—' : `${number.format(gapMinutes)} min`;
  $('capacity-gap-context').textContent = beforeShift
    ? 'L’analyse commencera avec le premier colis'
    : `${number.format((data.gaps || []).length)} période(s) sous ${number.format(Math.ceil(benchmarkHourly * .4))} colis/heure`;
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
}

async function load() {
  if (loading) return;
  loading = true;
  $('refresh-button').disabled = true;
  $('error-banner').hidden = true;
  try {
    const timestamp = Date.now();
    const capacityPromise = fetch(`/api/high-conveyor-capacity?t=${timestamp}`, { cache: 'no-store' }).catch(() => null);
    const [routesResponse, unprocessedResponse, hourlyResponse] = await Promise.all([
      fetch(`/api/live-routes?t=${timestamp}`, { cache: 'no-store' }),
      fetch(`/api/unprocessed-parcels?t=${timestamp}`, { cache: 'no-store' }),
      fetch(`/api/conveyor-hourly?t=${timestamp}`, { cache: 'no-store' })
    ]);
    if (!routesResponse.ok || !unprocessedResponse.ok || !hourlyResponse.ok) throw new Error(`Réponse ${routesResponse.status}/${unprocessedResponse.status}/${hourlyResponse.status}`);
    const [data, unprocessed, hourly] = await Promise.all([routesResponse.json(), unprocessedResponse.json(), hourlyResponse.json()]);
    render(data);
    renderUnprocessed(unprocessed);
    renderHourly(hourly);
    setConnection('ok', 'Données en direct');
    $('last-refresh').textContent = `Actualisé à ${formatTime(data.databaseNow)}`;
    countdown = REFRESH_SECONDS;
    try {
      const capacityResponse = await capacityPromise;
      if (!capacityResponse?.ok) throw new Error(`Capacity response ${capacityResponse?.status || 'unavailable'}`);
      renderCapacity(await capacityResponse.json());
    } catch (capacityError) {
      renderCapacityError();
    }
  } catch (error) {
    setConnection('error', 'Connexion interrompue');
    $('error-banner').textContent = `Impossible d’actualiser les données. Nouvelle tentative automatique dans ${countdown} secondes.`;
    $('error-banner').hidden = false;
  } finally {
    loading = false;
    $('refresh-button').disabled = false;
  }
}

$('refresh-button').addEventListener('click', () => { countdown = REFRESH_SECONDS; load(); });
$('routes-tab-button').addEventListener('click', () => activateTab('routes-tab'));
$('conveyor-tab-button').addEventListener('click', () => activateTab('conveyor-tab'));
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

load();

function activateTab(tabId) {
  document.querySelectorAll('.tab-panel').forEach((panel) => { panel.hidden = panel.id !== tabId; });
  document.querySelectorAll('.dashboard-tab').forEach((button) => {
    const active = button.dataset.tab === tabId;
    button.classList.toggle('active', active);
    button.setAttribute('aria-selected', String(active));
  });
}
