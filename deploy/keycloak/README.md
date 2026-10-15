# Keycloak for Deleguard console humans

Keycloak authenticates **humans** for the approval console. Agent keys (`dgk_`) and Admin MCP tokens (`dgm_`) never go through this realm.

## Local stack

`deploy/docker-compose.yml` starts:

- Deleguard landing + console at http://localhost:8082 (host 8080 is often taken)
- Docs at http://localhost:4321
- Keycloak at http://localhost:8081
- Mailpit at http://localhost:8025 for verification emails
- Realm `deleguard` with self-registration, Mailpit SMTP, brute-force protection, PKCE, and the Deleguard login theme

Sign in and Create account on the landing page send the browser to Keycloak (styled as Deleguard). Local compose does **not** block first login on email verification (`verifyEmail=false`); Mailpit is still wired if you turn verification on later. Google and Microsoft buttons appear on that Keycloak page after you set OAuth client secrets.

The API uses Authorization Code + PKCE. Do not enable the resource-owner password grant on this client.

`--import-realm` only runs when the realm does not already exist. After changing console ports or redirect URIs, update client `deleguard-console` in Keycloak Admin (Valid redirect URIs) or recreate the `keycloak` database and restart the Keycloak container.

## Super admin

Two local identities:

| Who | Where | Use |
| --- | --- | --- |
| `admin` / `deleguard-dev-keycloak` | http://localhost:8081/admin (master) | Bootstrap Keycloak operator |
| `platform-admin` / `DeleguardPlatform!23` | Same admin console | Day-to-day identity admin: users, roles, IdPs |
| `platform-admin@deleguard.local` / `DeleguardPlatform!23` | Deleguard console | Signs into the product and sees **Identity (Keycloak)** |

Rotate these before any shared environment.

## Google and Microsoft

1. Copy `deploy/.env.example` to `deploy/.env`.
2. Create an OAuth client in Google Cloud and/or Microsoft Entra ID.
3. Authorized redirect URIs:

   - Google: `http://localhost:8081/realms/deleguard/broker/google/endpoint`
   - Microsoft: `http://localhost:8081/realms/deleguard/broker/microsoft/endpoint`

4. Fill `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` and `MICROSOFT_CLIENT_ID` / `MICROSOFT_CLIENT_SECRET`.
5. Restart compose so `keycloak-configure` enables the identity providers.

Production: use HTTPS, `sslRequired=external`, rotate the console client secret, and keep `DemoAuth__AllowEmergencyConsoleToken=false`.
