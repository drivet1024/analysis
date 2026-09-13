(() => {
  const footer = document.createElement('footer');
  footer.className = 'deployment-footer';
  footer.textContent = 'Date de déploiement : chargement…';
  (document.querySelector('main') || document.body).append(footer);
  fetch('/api/deployment', { cache: 'no-store' }).then(response => {
    if (!response.ok) throw new Error('Unavailable');
    return response.json();
  }).then(data => {
    const date = data.deployedAt && new Date(data.deployedAt);
    footer.textContent = date && !Number.isNaN(date.getTime())
      ? 'Dernier déploiement : ' + new Intl.DateTimeFormat('fr-CA', { timeZone: 'America/Toronto', dateStyle: 'long', timeStyle: 'medium' }).format(date) + ' (Montréal)'
      : 'Date de déploiement non disponible';
  }).catch(() => { footer.textContent = 'Date de déploiement non disponible'; });
})();
