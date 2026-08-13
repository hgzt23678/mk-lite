const minReconnectDelay = 500;
const maxReconnectDelay = 30_000;

export function createMisskeyStream(endpoint, receiver, maximumQueuedFrames) {
    const url = new URL(endpoint);
    if ((url.protocol !== 'wss:' && url.protocol !== 'ws:') || url.username || url.password ||
        url.search || url.hash || url.pathname !== '/streaming') {
        throw new Error('Invalid explicit streaming endpoint.');
    }

    let socket = null;
    let disposed = false;
    let reconnectAttempt = 0;
    let reconnectTimer = 0;
    let lastCursor = 0;
    let draining = false;
    const queue = [];
    const subscriptions = new Map();

    async function drain() {
        if (draining || disposed) return;
        draining = true;
        try {
            while (!disposed && queue.length > 0) {
                const frame = queue.shift();
                try {
                    const parsed = JSON.parse(frame);
                    if (parsed.type === 'checkpoint' &&
                        Number.isSafeInteger(parsed.cursor) && parsed.cursor >= lastCursor) {
                        lastCursor = parsed.cursor;
                        reconnectAttempt = 0;
                    }
                } catch {
                    // .NET performs the authoritative bounded JSON validation.
                }
                try {
                    await receiver.invokeMethodAsync('ReceiveFrameAsync', frame);
                } catch {
                    if (!disposed) socket?.close(1011, 'receiver unavailable');
                    return;
                }
            }
        } finally {
            draining = false;
        }
    }

    function enqueue(frame) {
        if (typeof frame !== 'string' || frame.length > 2_000_000) {
            socket?.close(1009, 'frame too large');
            return;
        }
        if (queue.length >= maximumQueuedFrames) {
            socket?.close(1013, 'slow consumer');
            return;
        }
        queue.push(frame);
        void drain();
    }

    function sendSubscription(id, channel) {
        if (socket?.readyState !== WebSocket.OPEN) return;
        socket.send(JSON.stringify({
            type: 'connect',
            body: { id, channel, pong: true },
        }));
    }

    function scheduleReconnect() {
        if (disposed || subscriptions.size === 0 || reconnectTimer !== 0) return;
        const ceiling = Math.min(maxReconnectDelay, minReconnectDelay * (2 ** reconnectAttempt));
        const delay = Math.floor(Math.random() * Math.max(minReconnectDelay, ceiling));
        reconnectAttempt = Math.min(reconnectAttempt + 1, 10);
        reconnectTimer = self.setTimeout(() => {
            reconnectTimer = 0;
            connect();
        }, delay);
    }

    function connect() {
        if (disposed || subscriptions.size === 0 ||
            socket?.readyState === WebSocket.OPEN || socket?.readyState === WebSocket.CONNECTING) return;
        const connectionUrl = new URL(url.href);
        connectionUrl.searchParams.set('resume', 'v1');
        connectionUrl.searchParams.set('cursor', String(lastCursor));
        const candidate = new WebSocket(connectionUrl);
        socket = candidate;
        void receiver.invokeMethodAsync('NotifyConnectionStateAsync', reconnectAttempt === 0 ? 'connecting' : 'reconnecting');
        candidate.addEventListener('open', () => {
            if (socket !== candidate) return;
            for (const [id, channel] of subscriptions) sendSubscription(id, channel);
        });
        candidate.addEventListener('message', event => {
            if (socket === candidate) enqueue(event.data);
        });
        candidate.addEventListener('close', event => {
            if (socket !== candidate) return;
            socket = null;
            void receiver.invokeMethodAsync(
                'NotifyConnectionStateAsync',
                event.code === 4401 ? 'authentication-expired' : 'disconnected');
            // 4408/4409 require a fresh durable cursor. The typed .NET subscription
            // reloads that cursor and creates the replacement subscription; reconnecting
            // the stale JS subscription here would race that recovery and replay it again.
            if (event.code !== 4401 && event.code !== 4408 && event.code !== 4409) {
                scheduleReconnect();
            }
        });
        candidate.addEventListener('error', () => {
            // close owns state transition and reconnect to avoid duplicate attempts.
        });
    }

    return {
        subscribe(id, channel, afterCursor) {
            if (disposed || typeof id !== 'string' || id.length > 128 ||
                typeof channel !== 'string' || channel.length > 64 ||
                !Number.isSafeInteger(afterCursor) || afterCursor < 0) {
                throw new Error('Invalid stream subscription.');
            }
            const requiresRewind = subscriptions.size > 0 && afterCursor < lastCursor;
            if (subscriptions.size === 0) lastCursor = afterCursor;
            else lastCursor = Math.min(lastCursor, afterCursor);
            subscriptions.set(id, channel);
            if (requiresRewind &&
                (socket?.readyState === WebSocket.OPEN || socket?.readyState === WebSocket.CONNECTING)) {
                socket.close(1012, 'cursor rewind');
            } else if (socket?.readyState === WebSocket.OPEN) sendSubscription(id, channel);
            else connect();
        },
        unsubscribe(id) {
            const existed = subscriptions.delete(id);
            if (existed && socket?.readyState === WebSocket.OPEN) {
                socket.send(JSON.stringify({ type: 'disconnect', body: { id } }));
            }
            if (subscriptions.size === 0) {
                if (reconnectTimer !== 0) self.clearTimeout(reconnectTimer);
                reconnectTimer = 0;
                socket?.close(1000, 'no subscriptions');
                socket = null;
            }
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            subscriptions.clear();
            queue.length = 0;
            if (reconnectTimer !== 0) self.clearTimeout(reconnectTimer);
            reconnectTimer = 0;
            socket?.close(1000, 'disposed');
            socket = null;
        },
    };
}
