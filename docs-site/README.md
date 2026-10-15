# Deleguard docs

Sitio de documentación independiente (Astro [Starlight](https://starlight.astro.build)). Español en `/`, inglés en `/en/`. No forma parte del proceso de la API.

## Docker

```powershell
docker compose -f deploy/docker-compose.yml up -d --build docs
```

Abre `http://localhost:4321`. El servicio `docs` no está en el path ALLOW/DENY.

## Local

```powershell
cd docs-site
npm install
npm run dev
```

Abre `http://localhost:4321`. La búsqueda (Pagefind) se indexa en `npm run build`.

```powershell
npm run build
npm run preview
```

## Contenido

Markdown/MDX en `src/content/docs/` (ES) y `src/content/docs/en/` (EN). Capturas de la consola en `public/console/`. El plugin `starlight-openapi` lee `deleguard_dev_pack/spec/openapi.yaml`.

Para regenerar capturas: consola en `http://127.0.0.1:5080` con el token demo, recorre cada menú y sustituye los PNG.
