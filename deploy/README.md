# Pirate Goob.Admin deployment

The production panel runs as `pirate-admin.service` on `127.0.0.1:27689` and is exposed only through Caddy at `https://admin.troyeschina.space/`.

- Install the verified `Build & Test` artifact into `/opt/pirate-admin/bin`.
- Keep the base settings at `/opt/pirate-admin/appsettings.yml`.
- Keep OAuth and PostgreSQL credentials in `/opt/pirate-admin/appsettings.Secret.yml`, owned by `root:pirate-admin` with mode `0640`.
- Register the SS14 OAuth application with callback `https://admin.troyeschina.space/signin-oidc` and homepage `https://admin.troyeschina.space/`.
- The dedicated PostgreSQL role is `pirate_admin_web`; it has no superuser, database-creation, role-creation, schema-creation, or admin-table write privileges.
- Do not expose port `27689` publicly. Caddy terminates TLS and proxies the hostname to localhost.

The panel reads the shared Pirate `delta` database. Ban and whitelist mutations are performed by the panel role; game, Watchdog, CDN, and bot processes use their existing configuration.
