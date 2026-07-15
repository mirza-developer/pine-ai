self.importScripts('./service-worker-assets.js');
self.mode = 'NoPrerender';
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => {
	if (event.request.method === 'GET' && event.request.url.startsWith(self.location.origin)) {
		event.respondWith(onFetch(event));
	}
});
self.addEventListener('message', event => onMessage(event));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Broadcast a message to all controlled clients
async function broadcast(message) {
	const clients = await self.clients.matchAll({ includeUncontrolled: true });
	clients.forEach(client => client.postMessage(message));
}

async function onInstall(event) {
	const assets = self.assetsManifest.assets
		.filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
		.filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)));

	// Find the most recent existing cache to reuse unchanged assets (reduces re-download volume)
	const existingCacheKeys = (await caches.keys()).filter(k => k.startsWith(cacheNamePrefix) && k !== cacheName);
	const previousCache = existingCacheKeys.length > 0 ? await caches.open(existingCacheKeys[existingCacheKeys.length - 1]) : null;

	const newCache = await caches.open(cacheName);
	let downloaded = 0;
	let reused = 0;
	const total = assets.length;

	// Notify client that download is starting
	await broadcast(JSON.stringify({ type: 'install', data: { total } }));

	for (const asset of assets) {
		// Try to reuse from previous cache if the hash matches (asset unchanged)
		if (previousCache) {
			const previousResponse = await previousCache.match(asset.url);
			if (previousResponse) {
				const previousEtag = previousResponse.headers.get('ETag');
				// Use cached copy when integrity hash matches the previous response's ETag
				if (previousEtag && previousEtag === asset.hash) {
					await newCache.put(asset.url, previousResponse.clone());
					reused++;
					await broadcast(JSON.stringify({ type: 'progress', data: { percent: Math.round(((downloaded + reused) / total) * 100), total, downloaded, reused } }));
					continue;
				}
			}
		}

		// Asset is new or changed — fetch from network
		try {
			const request = new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' });
			await newCache.add(request);
			downloaded++;
		} catch (err) {
			console.error('Service worker cache install failed for asset:', asset.url, err);
		}

		await broadcast(JSON.stringify({ type: 'progress', data: { percent: Math.round(((downloaded + reused) / total) * 100), total, downloaded, reused } }));
	}

	// If nothing was downloaded (all reused), send bypass so Bit.js skips the progress UI
	if (downloaded === 0) {
		await broadcast(JSON.stringify({ type: 'bypass', data: { firstTime: false } }));
	}
}

async function onActivate(event) {
	const cacheKeys = await caches.keys();
	await Promise.all(cacheKeys
		.filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
		.map(key => caches.delete(key)));

	await self.clients.claim();
	await broadcast(JSON.stringify({ type: 'activate', data: {} }));
}

async function onFetch(event) {
	const shouldServeIndexHtml = event.request.mode === 'navigate';
	const request = shouldServeIndexHtml ? 'index.html' : event.request;
	const cache = await caches.open(cacheName);
	const cachedResponse = await cache.match(request);
	return cachedResponse || fetch(event.request);
}

async function onMessage(event) {
	if (event.data === 'SKIP_WAITING') {
		await self.skipWaiting();
		await broadcast('WAITING_SKIPPED');
		return;
	}

	if (event.data === 'CLAIM_CLIENTS') {
		await self.clients.claim();
		await broadcast('CLIENTS_CLAIMED');
		return;
	}

	if (event.data === 'CLEAN_UP') {
		const cacheKeys = await caches.keys();
		await Promise.all(cacheKeys
			.filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
			.map(key => caches.delete(key)));
		return;
	}
}
