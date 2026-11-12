# CI

Hook-urile de pe mașina ta rulează doar verificări rapide, pe orice branch, `main` inclus. Suita întreagă, `scripts/test-all.sh`, și gate-ul întreg, toate `scripts/check-*.sh` pe o clonă proaspătă, rulează în GitHub Actions. Regula e în `docs/SPEC.md` §14; pagina asta spune cum lucrezi cu ea.

## Regula pentru `main`

- `dev` intră în `main` doar după un gate întreg verde pe GitHub, pornit de mână pe commit-ul din `dev` care intră.
- Push-ul pe `main` rulează gate-ul întreg încă o dată, pe merge commit.
- Un tag de versiune se pune doar pe un `main` al cărui gate e verde.
- Merge-ul în `main` rămâne `--no-ff` (îl setează `scripts/setup.sh`), ca merge commit-ul să treacă prin verificările rapide ale hook-ului.

### Până la primul gate întreg verde pe GitHub

Workflow-ul nu există încă. Până are un gate întreg verde, `dev` intră în `main` doar după ce `scripts/test-all.sh` rulează verde de mână, pe commit-ul din `dev`, cu Docker Desktop pornit:

```sh
git switch dev
scripts/require-docker.sh && scripts/test-all.sh
```

Înainte de un tag de versiune, în perioada asta, rulează și gate-ul întreg local, pe o clonă proaspătă.

## Ce rulează hook-urile

`scripts/verify.sh`, la fel pe orice branch:

- lint-staged pe fișierele din index;
- cu o schimbare în `backend/`: build-ul cu avertismentele ca erori și testele de domeniu;
- cu o schimbare în `frontend/`: specurile.

La un merge, în index e tot ce aduce merge-ul. Nimic din toate astea nu are nevoie de Docker. Nu există hook `pre-push`: un commit făcut cu `--no-verify` îl prinde gate-ul care rulează la push pe `main`.
