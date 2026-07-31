# Microsoft Teams Local Integration

- **Status**: Current
- **Last reviewed**: 2026-07-31
- **Audience**: Developers running the real Teams transport against the local synthetic application

This guide covers one Microsoft 365 developer tenant, one Teams-managed bot
registration, one persistent Dev Tunnel, and the local ASP.NET Core host. It does not
use the Azure subscription or its tenant.

The helper script keeps non-secret identifiers in the ignored
`.teams-dev.local.json` file. Bot credentials are stored outside the repository under
the current user's local application-data directory. It never prints the client
secret.

## Prerequisites

- .NET 10 SDK and the project dependencies described in
  [Local Development](local-development.md).
- Teams Developer CLI 3.x: `teams`.
- Dev Tunnels CLI: `devtunnel`.
- A Microsoft 365 developer-tenant account allowed to upload custom Teams apps.
- The repository's current Teams integration code. Do not use an older build that
  predates the scoped background dispatcher or dedicated JWT identity mapping.

Log in with the Teams developer-tenant account:

```powershell
teams login
devtunnel user login
teams status
```

`teams status` must report that sideloading is enabled for both the tenant and the
user. An Azure CLI tenant mismatch is irrelevant because this workflow always creates
a Teams-managed bot.

## Fresh integration

Use this only when no Teams app, bot registration, tunnel, or local state exists for
the development environment.

From the repository root:

```powershell
.\scripts\teams-local.ps1 Fresh -ExpectedTenantId "<microsoft-365-developer-tenant-guid>" -AppName "governed-access-dev"
```

The command:

1. verifies Teams login, tenant selection, and sideloading;
2. creates an anonymous persistent Dev Tunnel and exposes local port `5136`;
3. derives the complete public `https://...devtunnels.ms` URL from CLI JSON;
4. creates a multitenant Teams-managed bot and registers the full
   `/api/messages` endpoint;
5. writes the client ID, client secret, and tenant ID outside the repository;
6. stores only non-secret continuation state in `.teams-dev.local.json`; and
7. prints the Install in Teams link.

Open the install link while signed into the same Microsoft 365 developer tenant and
add the app in personal scope. Do not copy credentials into source files, commit them,
or paste them into chat.

Start the integration using the two-terminal continuation workflow below.

### Adopt an existing integration

If the Teams app and tunnel already exist but `.teams-dev.local.json` does not, adopt
them instead of creating duplicate cloud registrations:

```powershell
.\scripts\teams-local.ps1 Adopt -TunnelId "<persistent-tunnel-id>" -TeamsAppId "<teams-app-id>" -CredentialFile "<full-path-to-generated-env-file>" -ExpectedTenantId "<microsoft-365-developer-tenant-guid>"
```

The credential file must contain `CLIENT_ID`, `CLIENT_SECRET`, and `TENANT_ID`, as
written by `teams app create --env`.

## Continuation on later days

Routine startup uses two PowerShell terminals. Run both commands from the repository
root.

Terminal 1 hosts the persistent tunnel:

```powershell
.\scripts\teams-local.ps1 Tunnel
```

Keep it open.

Terminal 2 synchronizes the current public endpoint, loads credentials without
printing them, configures the correct multitenant Bot Framework authority, and starts
ASP.NET Core:

```powershell
.\scripts\teams-local.ps1 Run
```

Keep it open. The host must report both:

```text
https://localhost:7251
http://localhost:5136
```

Send a new message to the bot in its personal Teams chat. The deterministic client
should return the fixed Client Alpha confirmation card. Confirming the card creates
the immutable request and returns its ID and browser link.

The `Run` action always uses the configuration key `Authority`, with value
`https://login.microsoftonline.com/botframework.com`. `AuthorityEndpoint` is not read
by the installed Agents SDK version and must not be used.

## Health checks

With both the tunnel and application running, use a third terminal:

```powershell
.\scripts\teams-local.ps1 Check
```

Both the local and public unauthenticated probes should return `401`. That result is
expected: it proves the tunnel reaches the protected `/api/messages` route without
weakening Bot Framework bearer-token authentication.

Registration diagnostics are also available:

```powershell
.\scripts\teams-local.ps1 Doctor
```

Some Teams CLI versions label an authenticated endpoint "unreachable" when their
unauthenticated probe receives `401`. If `Check` returns `401` for both URLs and real
Teams messages arrive in the application log, use those results as the authoritative
transport check.

## Database schema refresh

The local synthetic database uses `EnsureCreated`, not migrations. After an EF model
change, an older database can start but fail on the first Teams request because a new
table or column is absent. The safe symptom is:

```text
Request preparation is temporarily unavailable. No request was submitted.
```

Stop the ASP.NET Core host, preserve the old database, and restart:

```powershell
.\scripts\teams-local.ps1 BackupDatabase
.\scripts\teams-local.ps1 Run
```

`BackupDatabase` moves only the explicitly named local SQLite database and sidecar
files into a timestamped directory under `src/GovernedAccess.Web`. It does not delete
them. The next application start creates and seeds the current schema.

## Credential rotation

If a secret is exposed or approaching expiry, generate a replacement without printing
it:

```powershell
.\scripts\teams-local.ps1 RotateSecret
```

Restart the `Run` action and verify that Teams replies. Then open Microsoft Entra,
select the bot app registration, and delete the older secret under **Certificates &
secrets**. Creating a replacement does not revoke the old secret automatically.

## Troubleshooting map

| Symptom | Cause and response |
|---|---|
| Bot is installed but never replies | Start `Tunnel` and `Run`; then run `Check`. Confirm the registered endpoint contains the complete `devtunnels.ms/api/messages` URL. |
| `Cannot resolve ... from root provider` | The local build is stale. Stop and rebuild the current code, which creates a DI scope for every background Teams turn. |
| Connector error `-50500` or outbound `401` | Restart through `Run`. It sets `Connections__BotServiceConnection__Settings__Authority`, not the ignored `AuthorityEndpoint` key. |
| Bot says it accepts only an authenticated personal Teams chat | Confirm the app is installed in personal scope, the Teams CLI tenant matches local state, and the current JWT identity-mapping code is running. |
| Preparation is temporarily unavailable immediately after a schema change | Stop the host, run `BackupDatabase`, and start `Run` again. |
| `teams app doctor` says endpoint unreachable | Run `Check`; two `401` results are correct for unauthenticated probes. |
| Tunnel public URL changed | Restart through `Run`; it derives the live port URI and synchronizes the bot endpoint automatically. |

## Security notes

- Anonymous tunnel access does not make the bot endpoint anonymous. `/api/messages`
  still requires a signed Bot Framework bearer token for the configured bot audience.
- The browser-submitted actor is never trusted. The application binds the requester
  only after the dedicated JWT scheme, tenant, Teams channel, personal-conversation,
  actor, and conversation checks succeed.
- The Teams bot can prepare and confirm requests but cannot approve, provision, or
  change the fixed eight-hour grant scope.
- `.teams-dev.local.json`, credential files, SQLite databases, and database backups
  must remain uncommitted.
