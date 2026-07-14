// bitcoin-sign.js — self-contained Bitcoin "signmessage" implementation for the browser.
// Zero dependencies: secp256k1 over BigInt, RFC 6979 deterministic nonces, RIPEMD-160 for
// address derivation. Private keys never leave this script.
//
// Deliberately a CLASSIC script (not an ES module): proxies and optimizers such as
// Cloudflare Rocket Loader rewrite <script type="module"> tags in ways that break import
// statements. The public API is exposed as globalThis.bitcoinSign.

(function () {
'use strict';

const VERSION = 4;

// Debug logging; filter the browser console on "[bitcoin-sign]". Guarded so environments
// without a console (e.g. the V8 test harness) don't break.
function debug(...args) {
    try { console.log('[bitcoin-sign]', ...args); } catch { /* no console available */ }
}

debug('script evaluating, version', VERSION);

// ---------- byte helpers ----------

function concatBytes(...parts) {
    const arrays = parts.map(p => p instanceof Uint8Array ? p : Uint8Array.from(p));
    const out = new Uint8Array(arrays.reduce((n, a) => n + a.length, 0));
    let offset = 0;
    for (const a of arrays) { out.set(a, offset); offset += a.length; }
    return out;
}

function bytesToBigInt(bytes) {
    let n = 0n;
    for (const b of bytes) n = (n << 8n) | BigInt(b);
    return n;
}

function bigIntTo32Bytes(n) {
    const out = new Uint8Array(32);
    for (let i = 31; i >= 0; i--) { out[i] = Number(n & 0xffn); n >>= 8n; }
    return out;
}

function bytesToHex(bytes) {
    return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
}

function hexToBytes(hex) {
    const out = new Uint8Array(hex.length / 2);
    for (let i = 0; i < out.length; i++) out[i] = parseInt(hex.substr(i * 2, 2), 16);
    return out;
}

function utf8Encode(text) {
    const out = [];
    for (const ch of text) {
        const cp = ch.codePointAt(0);
        if (cp < 0x80) out.push(cp);
        else if (cp < 0x800) out.push(0xc0 | (cp >> 6), 0x80 | (cp & 0x3f));
        else if (cp < 0x10000) out.push(0xe0 | (cp >> 12), 0x80 | ((cp >> 6) & 0x3f), 0x80 | (cp & 0x3f));
        else out.push(0xf0 | (cp >> 18), 0x80 | ((cp >> 12) & 0x3f), 0x80 | ((cp >> 6) & 0x3f), 0x80 | (cp & 0x3f));
    }
    return Uint8Array.from(out);
}

function base64Encode(bytes) {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
    let out = '';
    for (let i = 0; i < bytes.length; i += 3) {
        const b0 = bytes[i], b1 = bytes[i + 1], b2 = bytes[i + 2];
        out += alphabet[b0 >> 2];
        out += alphabet[((b0 & 3) << 4) | (b1 === undefined ? 0 : b1 >> 4)];
        out += b1 === undefined ? '=' : alphabet[((b1 & 15) << 2) | (b2 === undefined ? 0 : b2 >> 6)];
        out += b2 === undefined ? '=' : alphabet[b2 & 63];
    }
    return out;
}

// ---------- hashing ----------
// WebCrypto is used when available, but it only exists in secure contexts (HTTPS or
// localhost), so a pure-JS SHA-256 keeps this page working on plain http:// deployments.

const hasSubtleCrypto = typeof globalThis.crypto !== 'undefined' && !!globalThis.crypto.subtle;
debug('hash backend:', hasSubtleCrypto ? 'WebCrypto (secure context)' : 'pure-JS fallback (no crypto.subtle)');

const SHA256_K = [
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
    0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
    0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
    0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
    0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2];

function sha256Js(bytes) {
    const rotr = (x, n) => ((x >>> n) | (x << (32 - n))) >>> 0;

    // big-endian padding: 0x80, zeros, 64-bit bit length
    const bitLen = bytes.length * 8;
    const padded = new Uint8Array((Math.floor((bytes.length + 8) / 64) + 1) * 64);
    padded.set(bytes);
    padded[bytes.length] = 0x80;
    for (let i = 0; i < 8; i++) padded[padded.length - 1 - i] = Math.floor(bitLen / 2 ** (8 * i)) & 0xff;

    const h = [0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19];
    const w = new Uint32Array(64);
    for (let block = 0; block < padded.length; block += 64) {
        for (let i = 0; i < 16; i++) {
            w[i] = (padded[block + 4 * i] << 24) | (padded[block + 4 * i + 1] << 16)
                 | (padded[block + 4 * i + 2] << 8) | padded[block + 4 * i + 3];
        }
        for (let i = 16; i < 64; i++) {
            const s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >>> 3);
            const s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >>> 10);
            w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0;
        }
        let [a, b, c, d, e, f, g, hh] = h;
        for (let i = 0; i < 64; i++) {
            const S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
            const ch = (e & f) ^ (~e & g);
            const t1 = (hh + S1 + ch + SHA256_K[i] + w[i]) >>> 0;
            const S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
            const maj = (a & b) ^ (a & c) ^ (b & c);
            const t2 = (S0 + maj) >>> 0;
            hh = g; g = f; f = e; e = (d + t1) >>> 0; d = c; c = b; b = a; a = (t1 + t2) >>> 0;
        }
        h[0] = (h[0] + a) >>> 0; h[1] = (h[1] + b) >>> 0; h[2] = (h[2] + c) >>> 0; h[3] = (h[3] + d) >>> 0;
        h[4] = (h[4] + e) >>> 0; h[5] = (h[5] + f) >>> 0; h[6] = (h[6] + g) >>> 0; h[7] = (h[7] + hh) >>> 0;
    }
    const out = new Uint8Array(32);
    for (let i = 0; i < 8; i++) {
        out[4 * i] = h[i] >>> 24;
        out[4 * i + 1] = (h[i] >> 16) & 0xff;
        out[4 * i + 2] = (h[i] >> 8) & 0xff;
        out[4 * i + 3] = h[i] & 0xff;
    }
    return out;
}

async function sha256(bytes) {
    if (hasSubtleCrypto) return new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
    return sha256Js(bytes);
}

async function sha256d(bytes) {
    return sha256(await sha256(bytes));
}

async function hmacSha256(keyBytes, dataBytes) {
    if (hasSubtleCrypto) {
        const key = await crypto.subtle.importKey('raw', keyBytes, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
        return new Uint8Array(await crypto.subtle.sign('HMAC', key, dataBytes));
    }
    // HMAC = H(opad ^ key || H(ipad ^ key || data))
    const key = keyBytes.length > 64 ? sha256Js(keyBytes) : keyBytes;
    const ipad = new Uint8Array(64).fill(0x36);
    const opad = new Uint8Array(64).fill(0x5c);
    for (let i = 0; i < key.length; i++) { ipad[i] ^= key[i]; opad[i] ^= key[i]; }
    return sha256Js(concatBytes(opad, sha256Js(concatBytes(ipad, dataBytes))));
}

const RMD_R = [
    0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
    7, 4, 13, 1, 10, 6, 15, 3, 12, 0, 9, 5, 2, 14, 11, 8,
    3, 10, 14, 4, 9, 15, 8, 1, 2, 7, 0, 6, 13, 11, 5, 12,
    1, 9, 11, 10, 0, 8, 12, 4, 13, 3, 7, 15, 14, 5, 6, 2,
    4, 0, 5, 9, 7, 12, 2, 10, 14, 1, 3, 8, 11, 6, 15, 13];
const RMD_RR = [
    5, 14, 7, 0, 9, 2, 11, 4, 13, 6, 15, 8, 1, 10, 3, 12,
    6, 11, 3, 7, 0, 13, 5, 10, 14, 15, 8, 12, 4, 9, 1, 2,
    15, 5, 1, 3, 7, 14, 6, 9, 11, 8, 12, 2, 10, 0, 4, 13,
    8, 6, 4, 1, 3, 11, 15, 0, 5, 12, 2, 13, 9, 7, 10, 14,
    12, 15, 10, 4, 1, 5, 8, 7, 6, 2, 13, 14, 0, 3, 9, 11];
const RMD_S = [
    11, 14, 15, 12, 5, 8, 7, 9, 11, 13, 14, 15, 6, 7, 9, 8,
    7, 6, 8, 13, 11, 9, 7, 15, 7, 12, 15, 9, 11, 7, 13, 12,
    11, 13, 6, 7, 14, 9, 13, 15, 14, 8, 13, 6, 5, 12, 7, 5,
    11, 12, 14, 15, 14, 15, 9, 8, 9, 14, 5, 6, 8, 6, 5, 12,
    9, 15, 5, 11, 6, 8, 13, 12, 5, 12, 13, 14, 11, 8, 5, 6];
const RMD_SS = [
    8, 9, 9, 11, 13, 15, 15, 5, 7, 7, 8, 11, 14, 14, 12, 6,
    9, 13, 15, 7, 12, 8, 9, 11, 7, 7, 12, 7, 6, 15, 13, 11,
    9, 7, 15, 11, 8, 6, 6, 14, 12, 13, 5, 14, 13, 13, 7, 5,
    15, 5, 8, 11, 14, 14, 6, 14, 6, 9, 12, 9, 12, 5, 15, 8,
    8, 5, 12, 9, 12, 5, 14, 6, 8, 13, 6, 5, 15, 13, 11, 11];

function ripemd160(bytes) {
    const rol = (x, n) => ((x << n) | (x >>> (32 - n))) >>> 0;
    const f = (j, x, y, z) =>
        j < 16 ? x ^ y ^ z :
        j < 32 ? (x & y) | (~x & z) :
        j < 48 ? (x | ~y) ^ z :
        j < 64 ? (x & z) | (y & ~z) :
        x ^ (y | ~z);
    const K = [0x00000000, 0x5a827999, 0x6ed9eba1, 0x8f1bbcdc, 0xa953fd4e];
    const KK = [0x50a28be6, 0x5c4dd124, 0x6d703ef3, 0x7a6d76e9, 0x00000000];

    // MD5-style padding: 0x80, zeros, 64-bit little-endian bit length.
    const bitLen = bytes.length * 8;
    const padded = new Uint8Array((Math.floor((bytes.length + 8) / 64) + 1) * 64);
    padded.set(bytes);
    padded[bytes.length] = 0x80;
    for (let i = 0; i < 8; i++) padded[padded.length - 8 + i] = (bitLen / 2 ** (8 * i)) & 0xff;

    let h = [0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476, 0xc3d2e1f0];
    for (let block = 0; block < padded.length; block += 64) {
        const x = new Uint32Array(16);
        for (let i = 0; i < 16; i++) {
            x[i] = padded[block + 4 * i] | (padded[block + 4 * i + 1] << 8)
                 | (padded[block + 4 * i + 2] << 16) | (padded[block + 4 * i + 3] << 24);
        }
        let [a, b, c, d, e] = h;
        let [aa, bb, cc, dd, ee] = h;
        for (let j = 0; j < 80; j++) {
            let t = (rol((a + f(j, b, c, d) + x[RMD_R[j]] + K[j >> 4]) >>> 0, RMD_S[j]) + e) >>> 0;
            a = e; e = d; d = rol(c, 10); c = b; b = t;
            t = (rol((aa + f(79 - j, bb, cc, dd) + x[RMD_RR[j]] + KK[j >> 4]) >>> 0, RMD_SS[j]) + ee) >>> 0;
            aa = ee; ee = dd; dd = rol(cc, 10); cc = bb; bb = t;
        }
        h = [
            (h[1] + c + dd) >>> 0,
            (h[2] + d + ee) >>> 0,
            (h[3] + e + aa) >>> 0,
            (h[4] + a + bb) >>> 0,
            (h[0] + b + cc) >>> 0,
        ];
    }
    const out = new Uint8Array(20);
    for (let i = 0; i < 5; i++) {
        out[4 * i] = h[i] & 0xff;
        out[4 * i + 1] = (h[i] >> 8) & 0xff;
        out[4 * i + 2] = (h[i] >> 16) & 0xff;
        out[4 * i + 3] = (h[i] >> 24) & 0xff;
    }
    return out;
}

// ---------- secp256k1 ----------

const P = 0xfffffffffffffffffffffffffffffffffffffffffffffffffffffffefffffc2fn;
const N = 0xfffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141n;
const G = {
    x: 0x79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798n,
    y: 0x483ada7726a3c4655da4fbfc0e1108a8fd17b448a68554199c47d08ffb10d4b8n,
};

const mod = (a, m) => ((a % m) + m) % m;

function modInv(a, m) {
    let [r0, r1] = [mod(a, m), m];
    let [s0, s1] = [1n, 0n];
    while (r1 !== 0n) {
        const q = r0 / r1;
        [r0, r1] = [r1, r0 - q * r1];
        [s0, s1] = [s1, s0 - q * s1];
    }
    if (r0 !== 1n) throw new Error('value not invertible');
    return mod(s0, m);
}

function pointDouble(a) {
    if (a === null) return null;
    const lambda = mod(3n * a.x * a.x * modInv(2n * a.y, P), P);
    const x = mod(lambda * lambda - 2n * a.x, P);
    return { x, y: mod(lambda * (a.x - x) - a.y, P) };
}

function pointAdd(a, b) {
    if (a === null) return b;
    if (b === null) return a;
    if (a.x === b.x) {
        return mod(a.y + b.y, P) === 0n ? null : pointDouble(a);
    }
    const lambda = mod((b.y - a.y) * modInv(b.x - a.x, P), P);
    const x = mod(lambda * lambda - a.x - b.x, P);
    return { x, y: mod(lambda * (a.x - x) - a.y, P) };
}

function pointMultiply(k, point) {
    let result = null;
    let addend = point;
    while (k > 0n) {
        if (k & 1n) result = pointAdd(result, addend);
        addend = pointDouble(addend);
        k >>= 1n;
    }
    return result;
}

// ---------- RFC 6979 deterministic nonce ----------

async function rfc6979Nonce(privBytes, hashBytes) {
    let v = new Uint8Array(32).fill(1);
    let k = new Uint8Array(32).fill(0);
    k = await hmacSha256(k, concatBytes(v, [0x00], privBytes, hashBytes));
    v = await hmacSha256(k, v);
    k = await hmacSha256(k, concatBytes(v, [0x01], privBytes, hashBytes));
    v = await hmacSha256(k, v);
    for (;;) {
        v = await hmacSha256(k, v);
        const candidate = bytesToBigInt(v);
        if (candidate >= 1n && candidate < N) return candidate;
        k = await hmacSha256(k, concatBytes(v, [0x00]));
        v = await hmacSha256(k, v);
    }
}

// ---------- Bitcoin formats ----------

const BASE58_ALPHABET = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';

function base58Decode(text) {
    let n = 0n;
    for (const ch of text) {
        const index = BASE58_ALPHABET.indexOf(ch);
        if (index < 0) throw new Error(`invalid base58 character "${ch}"`);
        n = n * 58n + BigInt(index);
    }
    const bytes = [];
    while (n > 0n) { bytes.unshift(Number(n & 0xffn)); n >>= 8n; }
    let leadingZeros = 0;
    for (const ch of text) { if (ch === '1') leadingZeros++; else break; }
    return concatBytes(new Uint8Array(leadingZeros), bytes);
}

async function base58CheckEncode(payload) {
    const checksum = (await sha256d(payload)).slice(0, 4);
    const data = concatBytes(payload, checksum);
    let n = bytesToBigInt(data);
    let out = '';
    while (n > 0n) { out = BASE58_ALPHABET[Number(n % 58n)] + out; n /= 58n; }
    for (const b of data) { if (b === 0) out = '1' + out; else break; }
    return out;
}

async function decodePrivateKey(input) {
    input = input.trim();
    if (/^[0-9a-fA-F]{64}$/.test(input)) {
        debug('decodePrivateKey: raw hex key detected (treated as compressed)');
        return { priv: hexToBytes(input.toLowerCase()), compressed: true };
    }
    debug('decodePrivateKey: attempting WIF decode, length', input.length);
    const decoded = base58Decode(input);
    if (decoded.length < 5) throw new Error('not a valid WIF private key');
    const payload = decoded.slice(0, -4);
    const checksum = decoded.slice(-4);
    const expected = (await sha256d(payload)).slice(0, 4);
    if (bytesToHex(checksum) !== bytesToHex(expected))
        throw new Error('WIF checksum mismatch — check the key for typos');
    if (payload[0] !== 0x80)
        throw new Error('not a mainnet private key (WIF version byte 0x' + payload[0].toString(16) + ')');
    if (payload.length === 34 && payload[33] === 0x01) {
        debug('decodePrivateKey: valid compressed-key WIF');
        return { priv: payload.slice(1, 33), compressed: true };
    }
    if (payload.length === 33) {
        debug('decodePrivateKey: valid uncompressed-key WIF');
        return { priv: payload.slice(1), compressed: false };
    }
    throw new Error('unrecognized WIF payload length');
}

function encodePublicKey(point, compressed) {
    if (compressed) return concatBytes([point.y & 1n ? 0x03 : 0x02], bigIntTo32Bytes(point.x));
    return concatBytes([0x04], bigIntTo32Bytes(point.x), bigIntTo32Bytes(point.y));
}

async function p2pkhAddress(publicKeyBytes) {
    const hash160 = ripemd160(await sha256(publicKeyBytes));
    return base58CheckEncode(concatBytes([0x00], hash160));
}

function varint(n) {
    if (n < 0xfd) return [n];
    if (n <= 0xffff) return [0xfd, n & 0xff, n >> 8];
    throw new Error('message too long');
}

async function bitcoinMessageHash(message) {
    const prefix = utf8Encode('Bitcoin Signed Message:\n');
    const body = utf8Encode(message);
    return sha256d(concatBytes(varint(prefix.length), prefix, varint(body.length), body));
}

// ---------- signing ----------

async function signRecoverable(privBytes, hashBytes) {
    const d = bytesToBigInt(privBytes);
    const z = mod(bytesToBigInt(hashBytes), N);
    const k = await rfc6979Nonce(privBytes, hashBytes);
    const R = pointMultiply(k, G);
    const r = mod(R.x, N);
    if (r === 0n) throw new Error('bad nonce (r = 0); try a different message');
    let recoveryId = Number(R.y & 1n) | (R.x >= N ? 2 : 0);
    let s = mod(modInv(k, N) * (z + r * d), N);
    if (s === 0n) throw new Error('bad nonce (s = 0); try a different message');
    if (s > N / 2n) { // low-S normalization flips which candidate key recovery yields
        s = N - s;
        recoveryId ^= 1;
    }
    return { r, s, recoveryId };
}

/// Generates a fresh random private key (compressed). Returns its WIF, public key, and address.
async function generatePrivateKey() {
    for (;;) {
        const priv = new Uint8Array(32);
        crypto.getRandomValues(priv);
        const d = bytesToBigInt(priv);
        if (d <= 0n || d >= N) continue; // astronomically rare; regenerate
        const publicKeyBytes = encodePublicKey(pointMultiply(d, G), true);
        const result = {
            wif: await base58CheckEncode(concatBytes([0x80], priv, [0x01])),
            publicKeyHex: bytesToHex(publicKeyBytes),
            address: await p2pkhAddress(publicKeyBytes),
        };
        debug('generatePrivateKey: new key created, address', result.address);
        return result;
    }
}

/// Derives the public key and P2PKH address for a WIF or 64-hex private key.
async function derivePublicKey(privateKeyText) {
    const { priv, compressed } = await decodePrivateKey(privateKeyText);
    const d = bytesToBigInt(priv);
    if (d <= 0n || d >= N) throw new Error('private key is out of range');
    const publicKeyBytes = encodePublicKey(pointMultiply(d, G), compressed);
    const result = {
        publicKeyHex: bytesToHex(publicKeyBytes),
        address: await p2pkhAddress(publicKeyBytes),
        compressed,
    };
    debug('derivePublicKey: address', result.address);
    return result;
}

/// Signs a message with the "Bitcoin Signed Message" standard. Returns the public key,
/// P2PKH address, base64 compact signature, and an Electrum-style signed-message block.
async function signBitcoinMessage(privateKeyText, message) {
    debug('signBitcoinMessage: start, message length', message.length);
    const { priv, compressed } = await decodePrivateKey(privateKeyText);
    const d = bytesToBigInt(priv);
    if (d <= 0n || d >= N) throw new Error('private key is out of range');

    const publicKeyBytes = encodePublicKey(pointMultiply(d, G), compressed);
    const address = await p2pkhAddress(publicKeyBytes);
    debug('signBitcoinMessage: signing as', address);

    const hash = await bitcoinMessageHash(message);
    debug('signBitcoinMessage: message digest computed');
    const { r, s, recoveryId } = await signRecoverable(priv, hash);
    const header = 27 + recoveryId + (compressed ? 4 : 0);
    const signature = base64Encode(concatBytes([header], bigIntTo32Bytes(r), bigIntTo32Bytes(s)));
    debug('signBitcoinMessage: done, recoveryId', recoveryId, 'header byte', header);

    return {
        publicKeyHex: bytesToHex(publicKeyBytes),
        address,
        signature,
        signedBlock:
            '-----BEGIN BITCOIN SIGNED MESSAGE-----\n' + message +
            '\n-----BEGIN SIGNATURE-----\n' + address + '\n' + signature +
            '\n-----END BITCOIN SIGNED MESSAGE-----',
    };
}

globalThis.bitcoinSign = {
    version: VERSION,
    signBitcoinMessage,
    derivePublicKey,
    generatePrivateKey,
    _internals: { sha256Js, ripemd160, utf8Encode, bytesToHex }, // exposed for the test harness
};

debug('API registered as window.bitcoinSign');

})();
