# HTTPS cu CA proprie

Kanabol rulează în rețeaua internă, cu un certificat semnat de o CA proprie. Colegii importă o singură dată `ca.crt`, iar certificatul serverului se poate reînnoi fără ca ei să mai facă ceva. Motivele sunt în `docs/adr/0015-tls-cu-ca-proprie.md`.

## Pe server

Din rădăcina repo-ului, cu toate numele și adresele prin care colegii ajung la server:

```sh
scripts/make-certs.sh kanabol.local kanabol 10.0.0.10
```

Rezultatul e în `certs/`, pe care git îl ignoră, iar `compose.prod.yaml` îl montează în nginx.

- `ca.crt` se dă colegilor.
- `ca.key` nu părăsește serverul. Oricine are acest fișier poate semna certificate în care browserele colegilor au încredere.
- `server.crt` și `server.key` le folosește nginx.

Certificatul serverului ține 825 de zile. Pentru reînnoire sau pentru un nume nou, rulează din nou scriptul, cu toate numele, apoi repornește nginx. CA-ul rămâne același, deci colegii nu au nimic de făcut:

```sh
docker compose -f compose.yaml -f compose.prod.yaml restart web
```

Dacă IT-ul oferă mai târziu un certificat, se înlocuiesc doar `server.crt` și `server.key`.

## Pe stația fiecărui coleg

### Windows, Chrome și Edge

Chrome și Edge folosesc certificatele din Windows.

1. Dublu-clic pe `ca.crt`, apoi **Install Certificate...**
2. La **Store Location**, alege **Current User**.
3. Alege **Place all certificates in the following store**, apoi **Browse...** și **Trusted Root Certification Authorities**.
4. **Next**, **Finish**, apoi confirmă cu **Yes** avertismentul de securitate.
5. Repornește browserul.

### Firefox

Firefox are propriul depozit de certificate. Sunt două variante, oricare e suficientă.

**Preia certificatele din Windows**, după importul de mai sus: deschide `about:config` și setează `security.enterprise_roots.enabled` pe `true`.

**Sau importă direct în Firefox:**

1. Deschide `about:preferences#privacy`, mergi la **Certificates** și apasă **View Certificates...**
2. În tab-ul **Authorities**, apasă **Import...** și alege `ca.crt`.
3. Bifează **Trust this CA to identify websites**, apoi **OK**.

## Verificare

Deschide adresa serverului, de exemplu `https://kanabol.local`. Browserul nu trebuie să afișeze niciun avertisment, iar la detaliile certificatului emitentul e `Kanabol Internal CA`.

Dacă apare un avertisment despre nume, adresa folosită nu e printre cele date scriptului. Rulează din nou scriptul pe server, cu tot cu ea.
