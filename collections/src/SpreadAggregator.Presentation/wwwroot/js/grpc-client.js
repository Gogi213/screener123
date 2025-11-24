/**
 * gRPC-Web Client для Trade Screener (БЕЗ КОМПИЛЯЦИИ PROTO!)
 * Использует gRPC-Web протокол напрямую
 */

class GrpcTradeClient {
    constructor(baseUrl = 'http://localhost:5000') {
        this.baseUrl = baseUrl;
    }

    /**
     * Get symbols metadata - unary call
     */
    async getSymbols() {
        const url = `${this.baseUrl}/tradestreamer.TradeStreamer/GetSymbols`;

        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/grpc-web+proto',
                    'X-Grpc-Web': '1',
                },
                // Empty EmptyRequest message
                body: new Uint8Array([0, 0, 0, 0, 0])
            });

            if (!response.ok) {
                throw new Error(`gRPC GetSymbols failed: ${response.status}`);
            }

            const data = await response.arrayBuffer();
            return this._parseSymbolsResponse(data);
        } catch (err) {
            console.error('[gRPC] GetSymbols error:', err);
            throw err;
        }
    }

    /**
     * Stream trades - server streaming
     * Использует Fetch API streaming response
     */
    async streamTrades(page, pageSize, onMessage, onError) {
        const url = `${this.baseUrl}/tradestreamer.TradeStreamer/StreamTrades`;

        // Encode StreamRequest (Protobuf)
        const requestBody = this._encodeStreamRequest(page, pageSize);

        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/grpc-web+proto',
                    'X-Grpc-Web': '1',
                },
                body: requestBody
            });

            if (!response.ok) {
                throw new Error(`gRPC StreamTrades failed: ${response.status}`);
            }

            // Read streaming response
            const reader = response.body.getReader();
            let buffer = new Uint8Array(0);

            while (true) {
                const { done, value } = await reader.read();

                if (done) {
                    console.log('[gRPC] Stream ended');
                    break;
                }

                // Append to buffer
                const newBuffer = new Uint8Array(buffer.length + value.length);
                newBuffer.set(buffer);
                newBuffer.set(value, buffer.length);
                buffer = newBuffer;

                // Try to parse complete messages from buffer
                while (buffer.length >= 5) {
                    // gRPC frame: [compressed:1][length:4][message:length]
                    const compressed = buffer[0];
                    const length = new DataView(buffer.buffer, buffer.byteOffset + 1, 4).getUint32(0, false);

                    if (buffer.length < 5 + length) {
                        break; // Wait for more data
                    }

                    const messageBytes = buffer.slice(5, 5 + length);
                    buffer = buffer.slice(5 + length);

                    try {
                        const tradeUpdate = this._parseTradeUpdate(messageBytes);
                        if (onMessage) {
                            onMessage(tradeUpdate);
                        }
                    } catch (err) {
                        console.error('[gRPC] Parse error:', err);
                    }
                }
            }
        } catch (err) {
            console.error('[gRPC] Stream error:', err);
            if (onError) onError(err);
        }
    }

    // ========================================================================
    // PROTOBUF ENCODING/DECODING (упрощенная версия для наших сообщений)
    // ========================================================================

    _encodeStreamRequest(page, pageSize) {
        const buffer = [];

        // Field 1: page (int32) - tag = (1 << 3) | 0 = 0x08
        buffer.push(0x08);
        this._encodeVarint(buffer, page);

        // Field 2: pageSize (int32) - tag = (2 << 3) | 0 = 0x10
        buffer.push(0x10);
        this._encodeVarint(buffer, pageSize);

        return new Uint8Array(buffer);
    }

    _parseSymbolsResponse(arrayBuffer) {
        // TODO: Полный парсинг Protobuf
        // Пока возвращаем заглушку - можно доделать позже
        console.warn('[gRPC] SymbolsResponse parsing not fully implemented');
        return { totalSymbols: 0, symbols: [] };
    }

    _parseTradeUpdate(bytes) {
        // Simplified Protobuf parser для TradeUpdate message
        const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
        let offset = 0;

        const result = {
            symbol: '',
            trades: []
        };

        while (offset < bytes.length) {
            if (offset + 1 > bytes.length) break;

            const tag = bytes[offset++];
            const fieldNumber = tag >> 3;
            const wireType = tag & 0x07;

            if (fieldNumber === 1 && wireType === 2) { // string symbol
                const [len, newOffset] = this._decodeVarint(bytes, offset);
                offset = newOffset;
                result.symbol = new TextDecoder().decode(bytes.slice(offset, offset + len));
                offset += len;
            } else if (fieldNumber === 2 && wireType === 2) { // repeated Trade
                const [len, newOffset] = this._decodeVarint(bytes, offset);
                offset = newOffset;
                const trade = this._parseTrade(bytes.slice(offset, offset + len));
                result.trades.push(trade);
                offset += len;
            } else {
                // Skip unknown field
                offset = this._skipField(bytes, offset, wireType);
            }
        }

        return result;
    }

    _parseTrade(bytes) {
        const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
        let offset = 0;

        const trade = {
            price: 0,
            quantity: 0,
            side: '',
            timestamp: 0
        };

        while (offset < bytes.length) {
            if (offset + 1 > bytes.length) break;

            const tag = bytes[offset++];
            const fieldNumber = tag >> 3;
            const wireType = tag & 0x07;

            if (fieldNumber === 1 && wireType === 1) { // double price
                trade.price = view.getFloat64(offset, true);
                offset += 8;
            } else if (fieldNumber === 2 && wireType === 1) { // double quantity
                trade.quantity = view.getFloat64(offset, true);
                offset += 8;
            } else if (fieldNumber === 3 && wireType === 2) { // string side
                const [len, newOffset] = this._decodeVarint(bytes, offset);
                offset = newOffset;
                trade.side = new TextDecoder().decode(bytes.slice(offset, offset + len));
                offset += len;
            } else if (fieldNumber === 4 && wireType === 0) { // int64 timestamp
                const [val, newOffset] = this._decodeVarint(bytes, offset);
                offset = newOffset;
                trade.timestamp = new Date(Number(val));
            } else {
                offset = this._skipField(bytes, offset, wireType);
            }
        }

        return trade;
    }

    _encodeVarint(buffer, value) {
        while (value >= 0x80) {
            buffer.push((value & 0x7F) | 0x80);
            value >>>= 7;
        }
        buffer.push(value & 0x7F);
    }

    _decodeVarint(bytes, offset) {
        let result = 0;
        let shift = 0;

        while (offset < bytes.length) {
            const byte = bytes[offset++];
            result |= (byte & 0x7F) << shift;

            if ((byte & 0x80) === 0) {
                return [result, offset];
            }

            shift += 7;
        }

        throw new Error('Truncated varint');
    }

    _skipField(bytes, offset, wireType) {
        switch (wireType) {
            case 0: // Varint
                while (offset < bytes.length && (bytes[offset] & 0x80) !== 0) offset++;
                return offset + 1;
            case 1: // 64-bit
                return offset + 8;
            case 2: // Length-delimited
                const [len, newOffset] = this._decodeVarint(bytes, offset);
                return newOffset + len;
            case 5: // 32-bit
                return offset + 4;
            default:
                throw new Error(`Unknown wire type: ${wireType}`);
        }
    }
}

// Export
window.GrpcTradeClient = GrpcTradeClient;
