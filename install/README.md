# Install Assets

This directory is reserved for packaged install and registration assets.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `README.md` | Documents the intended install boundary until real assets are added |

## Conventions

- Keep development-time registration in `scripts/setup/` until packaging work begins.
- Add packaged assets here only when they are part of a real install flow.

## Adding New Items

- Add user-scope packaging assets under a dedicated subfolder when a repeatable dev installer exists.
- Add machine-scope assets only for explicit packaging, beta, or deployment work.
