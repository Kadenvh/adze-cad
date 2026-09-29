---
name: deploy
description: "Build and deploy a project — customize the deploy step per project"
allowed-tools:
  - Bash
  - Read
---

# Deploy Skill

Build the project and deploy to production. **The deploy step (Step 4) must be customized per project** — update the commands below with your actual deployment method.

## Prerequisites

Before running, verify:
- You are on the correct branch (usually `main`)
- All changes are committed
- No unresolved merge conflicts

## Steps

1. **Pre-flight type check**
   - Run `npx tsc --noEmit` in the project directory
   - STOP and report if any type errors are found

2. **Pre-flight lint**
   - Run `npm run lint` in the project directory
   - STOP and report if any lint errors are found

3. **Build**
   - Run `npm run build`
   - Verify the output directory (usually `dist/`) was created with fresh timestamps

4. **Deploy** *(customize per project)*
   - Replace these placeholder commands with your project's deploy method:
   ```bash
   # Examples — pick or adapt one:
   # systemd: systemctl --user restart <service-name>
   # docker:  docker compose up -d --build
   # cloud:   fly deploy / vercel --prod / aws deploy
   # simple:  rsync -avz dist/ user@host:/path/to/app/
   ```
   - Verify the deployment succeeded (check service status, health endpoint, etc.)

5. **Smoke test**
   - If a health URL is known, curl it and verify a 200 response
   - Report the result

## On Failure

- Report which step failed with the full command output
- Do NOT retry automatically — ask the user what they want to do
- Common fixes:
  - Type errors → fix the code first
  - Lint errors → run `npm run lint -- --fix`
  - Build failure → check for missing dependencies
  - Deploy failure → check logs for the specific deploy method used
