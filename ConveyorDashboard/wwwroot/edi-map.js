(() => {
  const $ = id => document.getElementById(id);
  const dialog = $('edi-map-dialog');
  if (!dialog) return;
  const number = new Intl.NumberFormat('fr-CA');
  let context = null, map = null, clusters = null, libraries = null, request = 0, controller = null;
  function asset(url, css = false) {
    return new Promise((resolve, reject) => {
      const element = document.createElement(css ? 'link' : 'script');
      if (css) { element.rel = 'stylesheet'; element.href = url; } else element.src = url;
      element.onload = resolve;
      element.onerror = () => { element.remove(); reject(new Error('Carte indisponible')); };
      document.head.append(element);
    });
  }
  function ensureLibraries() {
    if (!libraries) libraries = Promise.all([
      asset('/vendor/leaflet/leaflet.css', true), asset('/vendor/markercluster/MarkerCluster.css', true),
      asset('/vendor/leaflet/leaflet.js').then(() => asset('/vendor/markercluster/leaflet.markercluster.js'))
    ]).catch(error => { libraries = null; throw error; });
    return libraries;
  }
  function clusterTotal(cluster) {
    return cluster.getAllChildMarkers().reduce((sum, marker) => sum + marker.options.parcelCount, 0);
  }
  function draw(data, fit) {
    const L = globalThis.L;
    if (!map) {
      map = L.map('edi-map-canvas', { minZoom: 2, maxZoom: 19 }).setView([46.2, -73.6], 6);
      L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a>'
      }).on('tileerror', () => {
        $('edi-map-background-status').textContent = 'Le fond de carte est indisponible; les points restent consultables.';
      }).addTo(map);
    }
    map.invalidateSize();
    if (clusters) map.removeLayer(clusters);
    clusters = L.markerClusterGroup({
      maxClusterRadius: 55, showCoverageOnHover: false, chunkedLoading: true,
      iconCreateFunction: cluster => L.divIcon({
        html: '<span>' + number.format(clusterTotal(cluster)) + '</span>',
        className: 'edi-map-cluster', iconSize: [52, 52]
      })
    });
    clusters.on('clustermouseover', event => {
      event.layer.bindTooltip(number.format(clusterTotal(event.layer)) + ' colis · ' + number.format(event.layer.getChildCount()) + ' positions', { direction: 'top' }).openTooltip();
    });
    clusters.on('clustermouseout', event => event.layer.closeTooltip());
    clusters.addTo(map);
    clusters.addLayers(data.points.map(point => {
      const label = number.format(point.parcels) + ' colis' + (point.postalParcels ? '<br>' + number.format(point.postalParcels) + ' avec position approximative par code postal' : '<br>Coordonnées de destination de l’expédition');
      return L.marker([point.latitude, point.longitude], {
        parcelCount: point.parcels, title: number.format(point.parcels) + ' colis',
        icon: L.divIcon({ className: 'edi-map-point' + (point.postalParcels ? ' edi-map-approximate' : ''), iconSize: [16, 16] })
      }).bindTooltip(label, { direction: 'top' }).bindPopup(label);
    }));
    if (fit && data.points.length) map.fitBounds(L.latLngBounds(data.points.map(p => [p.latitude, p.longitude])), { padding: [30, 30], maxZoom: 13 });
    const at = new Intl.DateTimeFormat('fr-CA', { timeZone: 'America/Toronto', dateStyle: 'medium', timeStyle: 'short' }).format(new Date(data.asOf));
    $('edi-map-status').textContent = `${data.date} · ${number.format(data.mapped)} / ${number.format(data.total)} colis positionnés · ${number.format(data.unmapped)} sans position fiable · ${number.format(data.postalParcels)} positionnés par code postal · relevé ${at}`;
    if (!data.points.length) $('edi-map-status').textContent += ' · Aucun point disponible pour cette journée.';
  }
  async function load(fit = false) {
    if (!context) { $('edi-map-status').textContent = 'Chargement des données EDI en cours…'; return; }
    const version = ++request;
    const date = context.date;
    controller?.abort();
    controller = new AbortController();
    $('edi-map-status').textContent = 'Chargement des destinations…';
    if (map && clusters) { map.removeLayer(clusters); clusters = null; }
    try {
      const [, data] = await Promise.all([
        ensureLibraries(),
        fetch('/api/edi/map?' + new URLSearchParams({ date }), { cache: 'no-store', signal: controller.signal })
          .then(response => { if (!response.ok) throw new Error('Carte indisponible'); return response.json(); })
      ]);
      if (version !== request || !dialog.open) return;
      if (data.date !== date) throw new Error('Date incorrecte');
      draw(data, fit);
    } catch (error) {
      if (version === request && dialog.open && error.name !== 'AbortError')
        $('edi-map-status').textContent = 'Carte indisponible. Fermez puis rouvrez pour réessayer.';
    }
  }
  globalThis.updateEdiMapContext = next => {
    const changed = context?.date !== next.date || context?.asOf !== next.asOf;
    const newDate = context?.date !== next.date;
    context = next;
    if (dialog.open && changed) load(newDate);
  };
  globalThis.resetEdiMapContext = date => {
    if (context && context.date !== date) { dialog.close(); context = null; }
  };
  $('edi-map-open').addEventListener('click', () => { dialog.showModal(); load(true); });
  $('edi-map-close').addEventListener('click', () => dialog.close());
  dialog.addEventListener('close', () => { ++request; controller?.abort(); });
  dialog.addEventListener('click', event => { if (event.target === dialog) {
    const box = dialog.getBoundingClientRect();
    if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) dialog.close();
  } });
})();
