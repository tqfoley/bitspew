# bitspew

Post message or thread with proof of funds on blockchains.

A message board where identity is a Bitcoin keypair: every post is signed with the
"Bitcoin Signed Message" standard and verified against the poster's address. Post and
thread ids are content hashes (double-SHA256), so they are self-verifying and
tamper-evident, like Bitcoin txids.

## Projects

- `src/Bitspew.Core` — signature verification (messages and raw transactions), canonical
  signing payloads, content-hash ids. Built on NBitcoin.
- `src/Bitspew.Web` — ASP.NET Core (Razor Pages) board: thread list, thread view with
  per-post signature badges, and a sign-in-your-own-wallet posting flow.
- `tests/Bitspew.Core.Tests` — xUnit tests, including wallet-interop vectors.

## Setup

Requires the .NET 10 SDK and a [Neon](https://neon.tech) Postgres database.

1. Create a Neon project and copy the **.NET (Npgsql)** connection string from its dashboard.
2. Store it (from the repo root):

   ```
   dotnet user-secrets set "ConnectionStrings:BitspewDb" "<connection string>" --project src/Bitspew.Web
   ```

   In production, set the `ConnectionStrings__BitspewDb` environment variable instead.
3. Run the site — migrations apply automatically at startup:

   ```
   dotnet run --project src/Bitspew.Web
   ```

## Posting flow

1. Fill in title/body and your Bitcoin address (`1...`, `3...`, or `bc1q...`).
2. The site shows the exact canonical text to sign; it embeds the thread id, address, and a
   timestamp so signatures cannot be replayed elsewhere or later (15-minute window).
3. Sign it in your own wallet (Electrum: Tools → Sign/Verify Message; Bitcoin Core:
   `signmessage`) — private keys never touch the server — and paste the base64 signature.

## Tests

```
dotnet test
```
