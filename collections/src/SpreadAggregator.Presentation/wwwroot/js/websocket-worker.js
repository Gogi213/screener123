// WebSocket Worker - Offload JSON.parse() to background thread
// This prevents UI blocking when processing high-frequency trade data

onmessage = function(e) {
    try {
        // Parse JSON in background thread (doesn't block UI)
        const msg = JSON.parse(e.data);

        // Send parsed message back to main thread
        postMessage(msg);
    } catch (error) {
        // Send error back to main thread
        postMessage({ error: error.message, raw: e.data });
    }
};