# Repo Instructions

## Workflow: branch → PR → review → merge

All work happens on a branch and ships as a pull request — never a direct commit or fast-forward merge to `main`, regardless of how small or "milestone" the change is. Push the branch, open a PR, and wait for review before merging. This applies to every session, not just planned feature work.

## Runtime verification before merge

Accounting-critical paths (closing, orders, expenses, inventory — anything that touches `DailyClosing`, money totals, or oversell/allowance guards) must be exercised at runtime — not just covered by unit tests — before the PR is merged, not after.

## Worktree sessions use a Docker DB, never the shared LocalDB

When verifying against a real database from an isolated worktree, spin up an isolated `docker compose` stack (its own network/volume, scoped to the worktree). Never point a worktree session at the shared SQL Server LocalDB instance (`(localdb)\mssqllocaldb`) used by the main checkout — it can lock or mutate the same dev/demo data other sessions rely on.
