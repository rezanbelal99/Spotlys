# deploy/k3s

Manifests for the single-node k3s deployment (docs/ARCHITECTURE.md §8, docs/DEVOPS.md §1).

**Status: prepared, not applied.** This session built and verified everything locally
(docker compose, real Postgres, real ingestion against hvakosterstrommen.no, real API, real
UI) but does not have SSH/kubeconfig access to the droplet -- confirmed with the user before
Phase 1 started. These manifests are the next step, not yet exercised against a real
cluster.

## Order of application

```bash
kubectl apply -f namespace.yaml
kubectl apply -f secrets.example.yaml   # copy to secrets.yaml with real values first, and
                                         # never commit the filled-in version
kubectl apply -f postgres.yaml
kubectl wait --for=condition=ready pod -l app=postgres -n spotlys --timeout=120s
kubectl apply -f migrator-job.yaml
kubectl wait --for=condition=complete job/spotlys-migrator -n spotlys --timeout=120s
kubectl apply -f api.yaml
kubectl apply -f ingestion.yaml
kubectl apply -f web.yaml
kubectl apply -f ingress.yaml
```

## What's deliberately not here yet

- No `container.yml` push target configured -- these manifests reference image names, not
  a specific registry, since the self-hosted Gitea Actions runner and its registry aren't
  wired up from this session.
- Grafana/Prometheus/Loki k3s manifests -- Phase 5.
- TLS on the Ingress -- add cert-manager + an Issuer once the real domain is pointed here.
