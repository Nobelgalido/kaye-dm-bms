# Kaye & DM BMS — Azure Deployment Guide (App Service + Azure SQL)

Goal: live URL running the app against Azure SQL, seeded with demo data, linked in the README. Budget target: ₱0/month using free tiers (with one honest caveat in §7).

Prereqs: Azure account (azure.microsoft.com/free — needs a card, won't charge on free tiers), Azure CLI installed (`winget install Microsoft.AzureCLI`), repo on GitHub.

---

## 1. Sign in and set up the resource group

```powershell
az login
az group create --name kaye-dm-rg --location southeastasia
```

`southeastasia` (Singapore) is the closest region to PH with free-tier availability.

## 2. Azure SQL — free tier

Azure SQL Database has a genuinely free offer: serverless, 100,000 vCore-seconds/month + 32 GB — far more than a demo app uses.

Portal is easier than CLI for this one (the free offer is a portal toggle):

1. Portal → Create resource → **SQL Database**
2. Resource group: `kaye-dm-rg`
3. Database name: `KayeDmBms`
4. Server: Create new → name like `kaye-dm-sql` (globally unique), location Southeast Asia, **SQL authentication**, pick an admin login + strong password — SAVE THESE
5. **"Apply free offer"** toggle → ON (this is the important one; it caps you at free-tier limits instead of billing overage — choose the "auto-pause when limit reached" behavior)
6. Networking: Public endpoint → **Allow Azure services and resources to access this server = YES** (this is what lets App Service connect) → also add your current client IP (so you can inspect the DB from VS if needed)
7. Create. Wait ~3 min.

Get the connection string: Portal → your database → Connection strings → ADO.NET (SQL authentication). It looks like:

```
Server=tcp:kaye-dm-sql.database.windows.net,1433;Initial Catalog=KayeDmBms;User ID=<admin>;Password=<password>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

## 3. App Service — free tier

```powershell
az appservice plan create --name kaye-dm-plan --resource-group kaye-dm-rg --location southeastasia --sku F1 --is-linux
az webapp create --name kaye-dm-bms --resource-group kaye-dm-rg --plan kaye-dm-plan --runtime "DOTNETCORE:8.0"
```

(`kaye-dm-bms` must be globally unique as it becomes kaye-dm-bms.azurewebsites.net — add a suffix like `-galido` if taken.)

**Enable WebSockets — required for Blazor Server:**

```powershell
az webapp config set --name kaye-dm-bms --resource-group kaye-dm-rg --web-sockets-enabled true
```

## 4. Configuration (connection string + seed flag)

```powershell
az webapp config connection-string set --name kaye-dm-bms --resource-group kaye-dm-rg --connection-string-type SQLAzure --settings KayeDmBms="<paste the full ADO.NET string with real password>"

az webapp config appsettings set --name kaye-dm-bms --resource-group kaye-dm-rg --settings SEED_ON_START=true ASPNETCORE_ENVIRONMENT=Production
```

Note the connection string NAME must match what Program.cs reads (`KayeDmBms` per your appsettings). Azure injects it as `ConnectionStrings:KayeDmBms`, overriding appsettings.json — no code change needed.

Check with the agent that Week 5's auto-migrate + seed logic keys off `SEED_ON_START` (that was the CP4 spec for Docker) and runs migrations on startup in Production too, not only in the compose path. If it's Docker-entrypoint-only, have the agent move it into Program.cs startup gated by the env var — small change, one commit.

## 5. Deploy — GitHub Actions from the Deployment Center

Portal → your App Service → **Deployment Center** → Source: GitHub → authorize → pick `Nobelgalido/kaye-dm-bms`, branch `main` → Save.

Azure commits a workflow file (`.github/workflows/…`) to your repo that builds and deploys on every push to main. First run takes ~5–8 min; watch it in the repo's Actions tab.

⚠️ The generated workflow builds the whole solution by default; if it errors on the tests project or picks the wrong project, edit the workflow's build step to target `src/KayeDM.Web/KayeDM.Web.csproj` explicitly. Your agent can fix the YAML in one pass — just paste it the failing Actions log.

## 6. First-boot verification

1. Actions run green → open https://kaye-dm-bms.azurewebsites.net
2. First load is SLOW (free-tier cold start + serverless SQL waking + migrations + seeding) — give it 60–90 seconds, refresh once if it times out
3. Log in as owner → dashboard has the 30-day curve → seeding worked
4. **Then turn seeding off** so a future restart doesn't wipe/reseed live demo data:
```powershell
az webapp config appsettings set --name kaye-dm-bms --resource-group kaye-dm-rg --settings SEED_ON_START=false
```
5. README: replace the placeholder with the live URL + demo logins; commit (which also auto-redeploys — fine).

## 7. Honest limitations of the free stack (know these, they'll come up)

- **F1 cold starts:** the app sleeps after ~20 min idle; first visitor waits 30–60s. Acceptable for a portfolio demo; put "(free tier — first load takes a minute)" next to the README link so reviewers don't bounce.
- **F1 has 60 CPU-min/day quota** — fine for demo traffic, would throttle under real use.
- **Serverless SQL auto-pauses** — same effect, first query after idle is slow. Make sure EF Core's `EnableRetryOnFailure()` is set on the SqlServer options (ask the agent; one line in Program.cs if missing) so the wake-up doesn't surface as an error page.
- **Blazor Server on F1:** websockets work, but each visitor holds a circuit; F1's memory means ~a handful of concurrent users. Again: demo-fine, production-no. Knowing exactly this trade-off is a good interview answer.
- If cold starts annoy you during an active job-search burst, B1 (~US$13/mo) removes them — optional, decide later.

## 8. Troubleshooting quick hits

- **Actions build fails on tests** → scope the workflow build/publish to the Web csproj (§5 note)
- **HTTP 500 on first load** → App Service → Log stream; usually the connection string name mismatch or SQL firewall (recheck "Allow Azure services = Yes")
- **"Login failed for user"** → password typo in the connection string setting; reset via §4 command
- **Migrations didn't run** → confirm the startup-migration code path executes in Production (§4 check)
- **Works then breaks after idle** → the retry-on-failure line from §7

## 9. Post-deploy checklist

- [ ] Live URL + demo logins in README (with cold-start note)
- [ ] SEED_ON_START=false
- [ ] Portfolio site: add Kaye & DM card (live link, GitHub link, screenshots)
- [ ] LinkedIn: add to Projects / feature post
- [ ] Deferred GIF: one recording session against the LIVE site (a live-URL GIF is even better proof)
