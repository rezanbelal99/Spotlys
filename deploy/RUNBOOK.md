# Deploy runbook — Phase 1 manual step

Everything up to here (Dockerfiles, `docker-compose.yml`, `deploy/k3s/`, `container.yml`)
was built and verified locally this session. Actually pushing to the droplet needs your own
access -- this session doesn't have SSH/kubeconfig/Gitea credentials (confirmed before
starting Phase 1). This is what's left, by hand:

## 1. Confirm the local stack works end to end

```bash
cd /path/to/Spotlys
cp .env.example .env   # edit if your local Postgres password differs
make bootstrap
```

Open http://localhost:5173 -- you should see today's NO2 prices as a ribbon within five
minutes of a clean clone. `curl localhost:8080/api/v1/status` should show a recent,
non-stale `IngestDayAheadPrices` run.

## 2. Point Gitea Actions at a registry

`container.yml` builds and scans four images (`api`, `ingestion`, `web`, `migrator`) but
doesn't push anywhere yet -- it has `TODO` placeholders for the registry host and the
`docker push` step. Fill those in with wherever your self-hosted runner already pushes
images (the same registry your IKT206 Gitea setup uses).

## 3. Provision secrets on the cluster

```bash
cp deploy/k3s/secrets.example.yaml deploy/k3s/secrets.yaml
# edit secrets.yaml with the real Postgres password -- never commit this file
kubectl apply -f deploy/k3s/namespace.yaml
kubectl apply -f deploy/k3s/secrets.yaml
```

## 4. Apply the rest, in order

Follow `deploy/k3s/README.md` -- Postgres first, wait for it to be ready, then the
migrator Job, wait for it to complete, then api/ingestion/web/ingress.

## 5. Point DNS at the droplet

Update the `host:` in `deploy/k3s/ingress.yaml` to the real domain (spotlys.no or
spotlys.app, per README.md §5) and point its DNS A record at the droplet.

## 6. TLS

Not set up. Add cert-manager and a Let's Encrypt `Issuer` once DNS is live, then add a
`tls:` block to `deploy/k3s/ingress.yaml`.

## Done when

A stranger can open the real domain, see today's NO2 prices, and the page tells them
honestly if the data is stale -- the actual Phase 1 done-criterion from `docs/ROADMAP.md`.
