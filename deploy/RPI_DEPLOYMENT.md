# LifeLog Raspberry Pi 5 Deployment

## One-Time Setup

1. Point your Dynamic DNS hostname at your home public IP.
2. On your router, forward public ports `80` and `443` to the Raspberry Pi.
3. Clone/copy `LifeLogBackend` onto the Pi.
4. Run:

```bash
cd LifeLogBackend
chmod +x scripts/rpi-*.sh
scripts/rpi-first-setup.sh
```

5. Edit `deploy/.env`:
   - `LIFELOG_HOSTNAME`
   - `LIFELOG_API_KEY`
   - `POSTGRES_PASSWORD`
   - `GEMINI_API_KEY`

6. Deploy:

```bash
scripts/rpi-deploy.sh
```

## Verify

```bash
curl https://YOUR_HOSTNAME/api/admin/health
curl -H "X-Api-Key: YOUR_KEY" https://YOUR_HOSTNAME/api/admin/ready
```

Caddy obtains a free Let's Encrypt certificate automatically. If this fails, verify Dynamic DNS and router forwarding.

## Syncthing

Open the Syncthing UI from your laptop using an SSH tunnel:

```bash
ssh -L 8384:127.0.0.1:8384 pi@YOUR_PI_IP
```

Then open `http://127.0.0.1:8384` and share `/var/syncthing/vault-live` with your Obsidian devices.

## Redeploy Safely

For new app versions:

```bash
scripts/rpi-deploy.sh
```

This rebuilds/restarts containers. It does not remove the Postgres volume or `/srv/lifelog/vaults`.

Never use `docker compose down -v` for this app unless you intentionally want to delete the database volume.

## Backup

```bash
scripts/rpi-backup.sh
```

The backup contains a Postgres dump and compressed vault copy.

## Wear OS Debug Build

In `LifeLogWearOsApp/local.properties`, add:

```properties
LIFELOG_BASE_URL=https://YOUR_HOSTNAME/
LIFELOG_API_KEY=YOUR_KEY
```

Then build and sideload the debug APK from Android Studio or with Gradle/adb.
