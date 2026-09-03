const REFRESH_SECONDS = 30;
const $ = (id) => document.getElementById(id);
const number = new Intl.NumberFormat('fr-CA');
const decimal = new Intl.NumberFormat('fr-CA', { maximumFractionDigits: 2 });
const time = new Intl.DateTimeFormat('fr-CA', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const shortTime = new Intl.DateTimeFormat('fr-CA', { hour: '2-digit', minute: '2-digit' });
const isoLocalDate = (date = new Date()) => {
  const pad = (part) => String(part).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
};
let countdown = REFRESH_SECONDS;
let loading = false;
let selectedDepotId = null;
let selectedParcelId = null;
let selectedSourceDepotId = 2;
let selectedSourceDepotName = 'QUEBEC';
let selectedWindowStart = '00:00';
let selectedWindowEnd = '23:59';
let selectedAnalysisDate = isoLocalDate();
const WINDOW_STORAGE_KEY = 'scan-time-windows-by-depot';

function formatTime(value) {
  return value ? time.format(new Date(value)) : '—';
}

function formatDateTime(value) {
  if (!value) return '—';
  const date = new Date(value);
  const pad = (part) => String(part).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
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

function applySelectedDepot(depotId, depotName) {
  selectedSourceDepotId = Number(depotId);
  selectedSourceDepotName = depotName || `Dépôt ${depotId}`;
  $('st-hubert-rerouted').hidden = selectedSourceDepotId !== 1;
  $('header-depot-name').textContent = selectedSourceDepotName;
  $('measurement-source-depot').textContent = selectedSourceDepotName;
  document.title = `${selectedSourceDepotName} · Analyse code 25`;
}

function readSavedWindows() {
  try { return JSON.parse(localStorage.getItem(WINDOW_STORAGE_KEY) || '{}'); }
  catch { return {}; }
}

function applyTimeWindow(startTime, endTime) {
  selectedWindowStart = startTime || '00:00';
  selectedWindowEnd = endTime || '23:59';
  $('window-start').value = selectedWindowStart;
  $('window-end').value = selectedWindowEnd;
  const crossesMidnight = selectedWindowEnd < selectedWindowStart;
  $('window-label').textContent = `${selectedWindowStart} à ${selectedWindowEnd}${crossesMidnight ? ' · lendemain' : ''}`;
}

function loadTimeWindowForDepot(depotId) {
  const saved = readSavedWindows()[String(depotId)];
  applyTimeWindow(saved?.startTime || '00:00', saved?.endTime || '23:59');
}

function timeWindowQuery() {
  return `date=${encodeURIComponent(selectedAnalysisDate)}&startTime=${encodeURIComponent(selectedWindowStart)}&endTime=${encodeURIComponent(selectedWindowEnd)}`;
}

function applyAnalysisDate(value, reload = true) {
  const today = isoLocalDate();
  selectedAnalysisDate = value && value <= today ? value : today;
  $('analysis-date').value = selectedAnalysisDate;
  $('analysis-date').max = today;
  $('next-date').disabled = selectedAnalysisDate >= today;
  if (!reload) return;
  selectedDepotId = null;
  selectedParcelId = null;
  $('history-dialog').close();
  $('attributed-dialog').close();
  $('parcel-dialog').close();
  $('code25-dialog').close();
  countdown = REFRESH_SECONDS;
  if (loading) setTimeout(load, 500); else load();
}

function moveAnalysisDate(days) {
  const date = new Date(`${selectedAnalysisDate}T12:00:00`);
  date.setDate(date.getDate() + days);
  applyAnalysisDate(isoLocalDate(date));
}

function saveTimeWindow() {
  const startTime = $('window-start').value;
  const endTime = $('window-end').value;
  if (!startTime || !endTime || startTime === endTime) {
    $('error-banner').textContent = 'Choisissez deux heures différentes pour la plage horaire.';
    $('error-banner').hidden = false;
    return;
  }
  const windows = readSavedWindows();
  windows[String(selectedSourceDepotId)] = { startTime, endTime };
  localStorage.setItem(WINDOW_STORAGE_KEY, JSON.stringify(windows));
  applyTimeWindow(startTime, endTime);
  $('settings-menu').open = false;
  $('error-banner').hidden = true;
  const button = $('save-window');
  button.textContent = 'Sauvegardé';
  button.classList.add('saved');
  setTimeout(() => { button.textContent = 'Sauvegarder'; button.classList.remove('saved'); }, 1600);
  selectedDepotId = null;
  selectedParcelId = null;
  $('history-dialog').close();
  $('parcel-dialog').close();
  $('code25-dialog').close();
  countdown = REFRESH_SECONDS;
  if (loading) setTimeout(load, 500); else load();
}

async function loadDepots() {
  const select = $('depot-select');
  try {
    const response = await fetch(`/api/scan-depots?t=${Date.now()}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`Réponse ${response.status}`);
    const depots = await response.json();
    select.replaceChildren();
    depots.forEach((depot) => {
      const option = document.createElement('option');
      option.value = depot.depotId;
      option.textContent = `${depot.depotName} · dépôt ${depot.depotId}`;
      option.dataset.name = depot.depotName;
      select.append(option);
    });
    const savedDepotId = Number(localStorage.getItem('scan-source-depot')) || 2;
    const selected = depots.find((depot) => depot.depotId === savedDepotId)
      || depots.find((depot) => depot.depotId === 2)
      || depots[0];
    if (selected) {
      select.value = selected.depotId;
      applySelectedDepot(selected.depotId, selected.depotName);
      loadTimeWindowForDepot(selected.depotId);
    }
  } catch (error) {
    applySelectedDepot(2, 'QUEBEC');
    loadTimeWindowForDepot(2);
  }
}

function render(data) {
  $('total-903').textContent = number.format(data.totalConveyor903 || 0);
  $('total-904').textContent = number.format(data.totalFloor904 || 0);
  $('total-25').textContent = number.format(data.totalCode25 || 0);
  const showStHubertRerouted = selectedSourceDepotId === 1;
  $('st-hubert-rerouted').hidden = !showStHubertRerouted;
  $('st-hubert-rerouted-total').textContent = number.format(data.totalCode25ReroutedElsewhere || 0);
  $('st-hubert-rerouted-note').textContent = `Codes 25 reçus dans tous les dépôts depuis ${formatDateTime(data.code25AttributionSince)}; dernier scan convoyeur 903 à St-Hubert et dépôt du code 25 différent.`;
  $('latest-scan').textContent = formatTime(data.latestScan);
  $('last-refresh').textContent = `Actualisé à ${formatTime(data.databaseNow)}`;

  const rows = data.rows || [];
  const totals = rows.map((row) => Number(row.conveyor903) + Number(row.floor904) + Number(row.code25));
  const maxTotal = Math.max(1, ...totals);
  const databaseNow = new Date(data.databaseNow);
  const windowEnd = new Date(data.dayEnd);
  const chart = $('stacked-chart');
  chart.replaceChildren();
  chart.style.gridTemplateColumns = `repeat(${Math.max(1, rows.length)}, minmax(0, 1fr))`;

  rows.forEach((row, index) => {
    const conveyor = Number(row.conveyor903) || 0;
    const floor = Number(row.floor904) || 0;
    const code25 = Number(row.code25) || 0;
    const total = conveyor + floor + code25;
    const bucketStart = new Date(row.bucketStart);
    const bucketEnd = new Date(Math.min(bucketStart.getTime() + 3600000, windowEnd.getTime()));
    const isCurrent = databaseNow >= bucketStart && databaseNow < bucketEnd;
    const stackHeight = 100 * total / maxTotal;
    const column = document.createElement('div');
    column.className = `hour-column${bucketStart > databaseNow ? ' future' : ''}`;
    const bucketLabel = `${shortTime.format(bucketStart)}–${shortTime.format(bucketEnd)}`;
    const title = `${bucketLabel} · scan dépôt 903: ${number.format(conveyor)} · plancher 904: ${number.format(floor)} · code 25: ${number.format(code25)}`;
    const conveyorShare = total ? 100 * conveyor / total : 0;
    const floorShare = total ? 100 * floor / total : 0;
    const code25Share = total ? 100 * code25 / total : 0;
    column.innerHTML = `
      <span class="hour-total">${total ? number.format(total) : ''}</span>
      <div class="bar-track${isCurrent ? ' current' : ''}" title="${title}">
        <div class="bar-stack" style="height:${stackHeight}%">
          <i class="segment segment-903" style="height:${conveyorShare}%"></i>
          <i class="segment segment-904" style="height:${floorShare}%"></i>
          <i class="segment segment-25" style="height:${code25Share}%"></i>
        </div>
      </div>
      <small>${index % 2 === 0 ? shortTime.format(bucketStart) : ''}</small>`;
    chart.append(column);
  });
}

function renderCode25Destinations(data) {
  $('dialog-total-25').textContent = number.format(data.totalCode25 || 0);
  $('dialog-date').textContent = `${data.date} · ${selectedSourceDepotName} · ${selectedWindowStart} à ${selectedWindowEnd}`;
  const body = $('destination-body');
  body.replaceChildren();
  if (!(data.destinations || []).length) {
    body.innerHTML = '<tr><td colspan="5" class="empty-cell">Aucun code 25 dans cette plage horaire.</td></tr>';
    return;
  }
  data.destinations.forEach((destination) => {
    const row = document.createElement('tr');
    const share = Math.max(0, Math.min(100, Number(destination.sharePercent) || 0));
    const depotId = destination.destinationDepotId == null ? 'N/D' : number.format(destination.destinationDepotId);
    const routeCount = Number(destination.routeCount) || 0;
    const routeLabel = `${number.format(routeCount)} route${routeCount > 1 ? 's' : ''}`;
    row.innerHTML = `
      <td><span class="destination-name">${escapeHtml(destination.destinationDepotName)}</span><br><span class="destination-id">Dépôt ${depotId} · ${routeLabel}</span></td>
      <td>${number.format(destination.parcels)}</td>
      <td><span class="share-cell"><span class="share-track"><i style="width:${share}%"></i></span>${share.toLocaleString('fr-CA')} %</span></td>
      <td>${formatTime(destination.firstScan)}</td>
      <td>${formatTime(destination.lastScan)}</td>`;
    if (destination.destinationDepotId != null) {
      row.className = 'clickable-depot';
      row.tabIndex = 0;
      row.setAttribute('role', 'button');
      row.setAttribute('aria-label', `Voir les colis du dépôt ${destination.destinationDepotName}`);
      const open = () => openDepotParcels(destination.destinationDepotId);
      row.addEventListener('click', open);
      row.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          open();
        }
      });
    }
    body.append(row);
  });
}

function renderDepotParcels(data) {
  $('parcel-dialog-title').textContent = data.depotName;
  $('parcel-dialog-total').textContent = number.format(data.totalParcels || 0);
  $('parcel-dialog-date').textContent = `${data.date} · ${selectedWindowStart} à ${selectedWindowEnd} · ${selectedSourceDepotName}`;
  const body = $('parcel-body');
  body.replaceChildren();
  if (!(data.parcels || []).length) {
    body.innerHTML = '<tr><td colspan="6" class="empty-cell">Aucun colis pour ce dépôt.</td></tr>';
    return;
  }
  data.parcels.forEach((parcel) => {
    const row = document.createElement('tr');
    const customerId = parcel.customerId == null ? 'N/D' : number.format(parcel.customerId);
    const sectorId = parcel.destinationSectorId == null ? 'N/D' : number.format(parcel.destinationSectorId);
    const dimensions = [parcel.previousLength, parcel.previousWidth, parcel.previousHeight].every((value) => Number(value) > 0)
      ? `${decimal.format(parcel.previousLength)} × ${decimal.format(parcel.previousWidth)} × ${decimal.format(parcel.previousHeight)}`
      : 'N/D';
    const weight = Number(parcel.previousWeight) > 0 ? decimal.format(parcel.previousWeight) : 'N/D';
    const measurementTitle = parcel.previousScanDate
      ? `Code ${parcel.previousScanCode} · ${formatDateTime(parcel.previousScanDate)} · avant ${selectedSourceDepotName}`
      : `Aucun scan convoyeur 903 ni scan dépôt 901 avant ${selectedSourceDepotName}`;
    row.innerHTML = `
      <td><span class="destination-name">${escapeHtml(parcel.parcelId)}</span></td>
      <td><span class="destination-name">${escapeHtml(parcel.customerName)}</span><br><span class="destination-id">Client ${customerId}</span></td>
      <td>${sectorId}</td>
      <td title="${escapeHtml(measurementTitle)}">${dimensions}</td>
      <td title="${escapeHtml(measurementTitle)}">${weight}</td>
      <td>${formatTime(parcel.scanTime)}</td>`;
    row.className = 'clickable-parcel';
    row.tabIndex = 0;
    row.setAttribute('role', 'button');
    row.setAttribute('aria-label', `Voir l’historique du colis ${parcel.parcelId}`);
    const open = () => openParcelHistory(parcel.parcelId);
    row.addEventListener('click', open);
    row.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        open();
      }
    });
    body.append(row);
  });
}

function renderParcelHistory(data) {
  $('history-dialog-title').textContent = `Colis ${data.parcelId}`;
  const destinationLocation = [data.destinationAddress, data.destinationCity].filter(Boolean).join(' · ');
  $('history-dialog-address').textContent = destinationLocation || 'Adresse non disponible';
  $('history-dialog-total').textContent = number.format((data.events || []).length);
  $('history-dialog-refresh').textContent = `Actualisé à ${formatTime(data.databaseNow)}`;
  const body = $('history-body');
  body.replaceChildren();
  if (!(data.events || []).length) {
    body.innerHTML = '<tr><td colspan="4" class="empty-cell">Aucun historique disponible pour ce colis.</td></tr>';
    return;
  }
  data.events.forEach((historyEvent) => {
    const row = document.createElement('tr');
    row.innerHTML = `
      <td><span class="history-code">${number.format(historyEvent.exceptionCode)}</span><span class="history-description">${escapeHtml(historyEvent.description)}</span></td>
      <td>${formatDateTime(historyEvent.eventDate)}</td>
      <td>${escapeHtml(historyEvent.userOrTpsl)}</td>
      <td>${escapeHtml(historyEvent.depotName)}</td>`;
    body.append(row);
  });
}

function renderAttributedParcels(data) {
  $('attributed-dialog-title').textContent = `Codes 25 attribués à ${data.attributionDepotName}`;
  $('attributed-dialog-total').textContent = number.format(data.totalParcels || 0);
  $('attributed-dialog-date').textContent = `Depuis ${formatDateTime(data.since)} · actualisé à ${formatTime(data.databaseNow)}`;
  const body = $('attributed-body');
  body.replaceChildren();
  if (!(data.parcels || []).length) {
    body.innerHTML = '<tr><td colspan="5" class="empty-cell">Aucun colis attribué à ce dépôt.</td></tr>';
    return;
  }
  data.parcels.forEach((parcel) => {
    const row = document.createElement('tr');
    const customerId = parcel.customerId == null ? 'N/D' : number.format(parcel.customerId);
    row.innerHTML = `
      <td><span class="destination-name">${escapeHtml(parcel.parcelId)}</span></td>
      <td><span class="destination-name">${escapeHtml(parcel.customerName)}</span><br><span class="destination-id">Client ${customerId}</span></td>
      <td>${formatDateTime(parcel.lastConveyorScan)}</td>
      <td>${formatDateTime(parcel.code25Time)}</td>
      <td><span class="destination-name">${escapeHtml(parcel.code25DepotName)}</span><br><span class="destination-id">Dépôt ${number.format(parcel.code25DepotId)}</span></td>`;
    row.className = 'clickable-parcel';
    row.tabIndex = 0;
    row.setAttribute('role', 'button');
    row.setAttribute('aria-label', `Voir l’historique du colis ${parcel.parcelId}`);
    const open = () => openParcelHistory(parcel.parcelId);
    row.addEventListener('click', open);
    row.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        open();
      }
    });
    body.append(row);
  });
}

async function loadAttributedParcels(showLoading = false) {
  if (showLoading) $('attributed-body').innerHTML = '<tr><td colspan="5" class="empty-cell">Chargement des colis…</td></tr>';
  try {
    const response = await fetch(`/api/quebec-depot-scans/code25-attributed?depotId=${encodeURIComponent(selectedSourceDepotId)}&date=${encodeURIComponent(selectedAnalysisDate)}&t=${Date.now()}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`Réponse ${response.status}`);
    renderAttributedParcels(await response.json());
  } catch (error) {
    $('attributed-body').innerHTML = '<tr><td colspan="5" class="empty-cell">Impossible de charger les colis attribués.</td></tr>';
  }
}

function openAttributedParcels() {
  const dialog = $('attributed-dialog');
  if (!dialog.open) dialog.showModal();
  loadAttributedParcels(true);
}

async function loadParcelHistory(parcelId, showLoading = false) {
  if (showLoading) $('history-body').innerHTML = '<tr><td colspan="4" class="empty-cell">Chargement de l’historique…</td></tr>';
  try {
    const response = await fetch(`/api/parcels/${encodeURIComponent(parcelId)}/history?t=${Date.now()}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`Réponse ${response.status}`);
    renderParcelHistory(await response.json());
  } catch (error) {
    $('history-body').innerHTML = '<tr><td colspan="4" class="empty-cell">Impossible de charger l’historique du colis.</td></tr>';
  }
}

function openParcelHistory(parcelId) {
  selectedParcelId = parcelId;
  const dialog = $('history-dialog');
  if (!dialog.open) dialog.showModal();
  loadParcelHistory(parcelId, true);
}

async function loadDepotParcels(depotId, showLoading = false) {
  if (showLoading) $('parcel-body').innerHTML = '<tr><td colspan="6" class="empty-cell">Chargement des colis…</td></tr>';
  try {
    const response = await fetch(`/api/quebec-depot-scans/code25-destinations/${encodeURIComponent(depotId)}/parcels?sourceDepotId=${encodeURIComponent(selectedSourceDepotId)}&${timeWindowQuery()}&t=${Date.now()}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`Réponse ${response.status}`);
    renderDepotParcels(await response.json());
  } catch (error) {
    $('parcel-body').innerHTML = '<tr><td colspan="6" class="empty-cell">Impossible de charger les colis de ce dépôt.</td></tr>';
  }
}

function openDepotParcels(depotId) {
  selectedDepotId = depotId;
  const dialog = $('parcel-dialog');
  if (!dialog.open) dialog.showModal();
  loadDepotParcels(depotId, true);
}

async function loadCode25Destinations() {
  $('destination-body').innerHTML = '<tr><td colspan="5" class="empty-cell">Chargement des destinations…</td></tr>';
  try {
    const response = await fetch(`/api/quebec-depot-scans/code25-destinations?depotId=${encodeURIComponent(selectedSourceDepotId)}&${timeWindowQuery()}&t=${Date.now()}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`Réponse ${response.status}`);
    renderCode25Destinations(await response.json());
  } catch (error) {
    $('destination-body').innerHTML = '<tr><td colspan="5" class="empty-cell">Impossible de charger les dépôts de destination.</td></tr>';
  }
}

function openCode25Details() {
  const dialog = $('code25-dialog');
  if (!dialog.open) dialog.showModal();
  loadCode25Destinations();
}

async function load() {
  if (loading) return;
  loading = true;
  $('error-banner').hidden = true;
  try {
    const response = await fetch(`/api/quebec-depot-scans?depotId=${encodeURIComponent(selectedSourceDepotId)}&${timeWindowQuery()}&t=${Date.now()}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`Réponse ${response.status}`);
    render(await response.json());
    if ($('attributed-dialog').open) loadAttributedParcels();
    if ($('code25-dialog').open) loadCode25Destinations();
    if ($('parcel-dialog').open && selectedDepotId != null) loadDepotParcels(selectedDepotId);
    if ($('history-dialog').open && selectedParcelId != null) loadParcelHistory(selectedParcelId);
    setConnection('ok', 'Données en direct');
    countdown = REFRESH_SECONDS;
  } catch (error) {
    setConnection('error', 'Connexion interrompue');
    $('error-banner').textContent = `Impossible d’actualiser les scans. Nouvelle tentative dans ${countdown} secondes.`;
    $('error-banner').hidden = false;
  } finally {
    loading = false;
  }
}

$('save-window').addEventListener('click', saveTimeWindow);
$('previous-date').addEventListener('click', () => moveAnalysisDate(-1));
$('next-date').addEventListener('click', () => moveAnalysisDate(1));
$('analysis-date').addEventListener('change', (event) => applyAnalysisDate(event.target.value));
document.addEventListener('click', (event) => {
  const menu = $('settings-menu');
  if (menu.open && !menu.contains(event.target)) menu.open = false;
});
$('depot-select').addEventListener('change', (event) => {
  const option = event.target.selectedOptions[0];
  applySelectedDepot(Number(event.target.value), option?.dataset.name || option?.textContent || 'Dépôt');
  loadTimeWindowForDepot(selectedSourceDepotId);
  localStorage.setItem('scan-source-depot', String(selectedSourceDepotId));
  selectedDepotId = null;
  selectedParcelId = null;
  $('history-dialog').close();
  $('attributed-dialog').close();
  $('parcel-dialog').close();
  $('code25-dialog').close();
  countdown = REFRESH_SECONDS;
  if (loading) setTimeout(load, 500); else load();
});
$('code25-card').addEventListener('click', openCode25Details);
$('st-hubert-rerouted').addEventListener('click', openAttributedParcels);
$('st-hubert-rerouted').addEventListener('keydown', (event) => {
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault();
    openAttributedParcels();
  }
});
$('code25-card').addEventListener('keydown', (event) => {
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault();
    openCode25Details();
  }
});
$('dialog-close').addEventListener('click', () => $('code25-dialog').close());
$('parcel-dialog-close').addEventListener('click', () => $('parcel-dialog').close());
$('history-dialog-close').addEventListener('click', () => $('history-dialog').close());
$('attributed-dialog-close').addEventListener('click', () => $('attributed-dialog').close());
$('code25-dialog').addEventListener('click', (event) => {
  if (event.target === $('code25-dialog')) $('code25-dialog').close();
});
$('parcel-dialog').addEventListener('click', (event) => {
  if (event.target === $('parcel-dialog')) $('parcel-dialog').close();
});
$('history-dialog').addEventListener('click', (event) => {
  if (event.target === $('history-dialog')) $('history-dialog').close();
});
$('attributed-dialog').addEventListener('click', (event) => {
  if (event.target === $('attributed-dialog')) $('attributed-dialog').close();
});
setInterval(() => {
  countdown -= 1;
  if (countdown <= 0) {
    countdown = REFRESH_SECONDS;
    load();
  }
}, 1000);
async function initialize() {
  applyAnalysisDate(isoLocalDate(), false);
  await loadDepots();
  await load();
}

initialize();
