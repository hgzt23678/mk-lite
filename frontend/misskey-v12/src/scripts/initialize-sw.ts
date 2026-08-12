export async function initializeSw(): Promise<void> {
	// Misskey's root-scoped service worker is not registered by this port.
	// Push and cache updates require the matching server protocol; registering
	// the upstream worker without it would create a stale, over-broad cache.
}
