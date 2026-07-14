# bitspew

Post message or thread with proof of funds on blockchains.

Right now it's a minimal message board: post a message, it's saved to Postgres, and
everyone sees it on the "For you" feed. The whole site is served under the `/bitspew/`
path prefix.

Bitcoin-signed posts (identity = a Bitcoin keypair, every post verified with the
"Bitcoin Signed Message" standard) are planned; the signature verification machinery
lives in `Bitspew.Core` and is fully tested, but the posting flow doesn't require
signing yet.

## Projects

- `src/Bitspew.Core` — signature verification (messages and raw transactions), canonical
  signing payloads, content-hash ids, and the `Message` entity. Built on NBitcoin.
- `src/Bitspew.Web` — ASP.NET Core (Razor Pages) site: the "For you" feed at
  `/bitspew/ForYou/` where anyone can post a message and read all messages, plus an
  in-browser message-signing utility page at `/bitspew/SignMessage`.
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

   Then open `http://localhost:5013/bitspew/ForYou/`.

## Path base / reverse proxy

The app treats `/bitspew` as its public root. It accepts requests both with the prefix
intact (local dev) and with the prefix already stripped by a reverse proxy (production:
Cloudflare → AWS, where the proxy forwards `/bitspew/*` to the app without the prefix).
Either way, every generated link, form action, and static-asset URL carries `/bitspew/`.

## Tests

```
dotnet test
```
