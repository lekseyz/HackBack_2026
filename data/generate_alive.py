# навайбкожен генератор с более полным набором и большей разнообразностью датасета

import argparse
import json
import random
from datetime import datetime, timedelta, timezone
from pathlib import Path

TERMS = ("hour", "week", "month", "quarter_year", "half_year", "year")
TERM_DAYS = {"hour": 1 / 24, "week": 7, "month": 30, "quarter_year": 91, "half_year": 182, "year": 365}
ARCHETYPES = ["solid"] * 25 + ["average"] * 33 + ["struggling"] * 25 + ["critical"] * 12 + ["empty"] * 5
TERM_WEIGHTS = [("month", 0.34), ("year", 0.2), ("quarter_year", 0.16),
                ("half_year", 0.1), ("week", 0.1), ("hour", 0.1)]


def weighted_term(rng):
    roll, acc = rng.random(), 0.0
    for term, w in TERM_WEIGHTS:
        acc += w
        if roll <= acc:
            return term
    return "month"


def iso(dt):
    return dt.strftime("%Y-%m-%dT%H:%M:%S.000Z")


def pick_status(arch, rng):
    roll = rng.random()
    if arch == "solid":
        if roll < 0.85: return "active"
        if roll < 0.95: return "deleted"
        return "suspended"
    if arch == "average":
        if roll < 0.72: return "active"
        if roll < 0.78: return "prolong_wait"
        if roll < 0.85: return "suspended"
        if roll < 0.93: return "deleted"
        return "blocked"
    if arch == "struggling":
        if roll < 0.52: return "active"
        if roll < 0.76: return "prolong_wait"
        if roll < 0.90: return "suspended"
        return "deleted"
    if arch == "critical":
        if roll < 0.48: return "active"
        if roll < 0.74: return "prolong_wait"
        if roll < 0.90: return "suspended"
        if roll < 0.95: return "blocked"
        return "deleted"
    return "active"  # empty: без услуг, статус не важен


def pick_autoprolong(arch, rng):
    return rng.random() < {"solid": 0.8, "average": 0.55, "struggling": 0.5, "critical": 0.45, "empty": 0.5}[arch]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--accounts", type=int, default=100)
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--reference", default=None, help="ISO 8601 с таймзоной, напр. 2026-11-01T00:00:00.000Z")
    ap.add_argument("--out", default=".")
    args = ap.parse_args()

    rng = random.Random(args.seed)
    now = (datetime.fromisoformat(args.reference.replace("Z", "+00:00"))
           if args.reference else datetime.now(timezone.utc))

    pool = (ARCHETYPES * (args.accounts // len(ARCHETYPES) + 1))[:max(args.accounts, 1)]
    rng.shuffle(pool)

    accounts, services, transactions = [], [], []
    sid = tid = 0

    for aid in range(1, args.accounts + 1):
        arch = pool[aid - 1]
        n_services = {"solid": rng.randint(2, 5), "average": rng.randint(1, 4),
                      "struggling": rng.randint(1, 3), "critical": rng.randint(1, 4),
                      "empty": 0 if rng.random() < 0.8 else 1}[arch]
        tenure_days = {"solid": rng.uniform(60, 700), "average": rng.uniform(40, 600),
                       "struggling": rng.uniform(30, 500), "critical": rng.uniform(10, 400),
                       "empty": 0}[arch]

        own = []
        for _ in range(n_services):
            sid += 1
            status = pick_status(arch, rng)
            term = weighted_term(rng)
            price = rng.randint(*{"hour": (50, 500), "week": (100, 1500), "month": (500, 15000),
                                  "quarter_year": (1000, 25000), "half_year": (2000, 40000),
                                  "year": (3000, 50000)}[term])
            soon = {"solid": 0.22, "average": 0.28, "struggling": 0.5, "critical": 0.55, "empty": 0.25}[arch]
            if status == "active":
                expires = now + timedelta(days=(rng.uniform(-20, 2) if rng.random() < soon else rng.uniform(5, 300)))
            elif status == "prolong_wait":
                expires = now - timedelta(days=rng.uniform(0, 60))
            elif status in ("suspended", "blocked"):
                expires = now - timedelta(days=rng.uniform(1, 40))
            else:
                expires = now - timedelta(days=rng.uniform(30, 400))
            created = expires - rng.randint(1, {"solid": 8, "average": 6, "struggling": 3, "critical": 3, "empty": 1}[arch]) * timedelta(days=TERM_DAYS[term])
            created = max(created, now - timedelta(days=600))
            if created > expires:
                created = expires
            if status == "active":
                created = min(created, now - timedelta(minutes=1))
            services.append({
                "id": sid, "name": f"service-{sid:05d}", "ownerId": aid, "paymentTerm": term,
                "autoProlong": pick_autoprolong(arch, rng), "createdAt": iso(created),
                "expiresAt": iso(expires), "status": status, "price": price,
            })
            tid += 1
            performed = status != "deleted" or rng.random() < 0.8
            t_created = created + timedelta(minutes=rng.randint(1, 60))
            transactions.append({
                "id": tid, "amount": rng.randint(0, 1000),
                "status": "performed" if performed else "not_performed",
                "performedAt": iso(t_created + timedelta(seconds=rng.randint(1, 3600))) if performed else None,
                "createdAt": iso(t_created), "type": "order", "serviceId": sid,
            })
            own.append(services[-1])

        billable = [s for s in own if s["status"] in ("active", "prolong_wait")]
        obligation = sum(s["price"] for s in billable) or 1000
        balance = {
            "solid": int(obligation * rng.uniform(2, 6)),
            "average": int(obligation * rng.uniform(0.4, 2.0)),
            "struggling": int(obligation * rng.uniform(0.03, 0.35)),
            "critical": rng.randint(0, 800),
            "empty": rng.randint(0, 5000),
        }[arch]
        accounts.append({
            "id": aid, "email": f"user-{aid:05d}@example.test", "balance": balance,
            "withdrawBalance": int(balance * rng.uniform(0, 0.6)),
            "totalReplenished": balance + int(rng.uniform(2, 120) * 1000 * (rng.uniform(1, 1.8) if arch == "solid" else 1)),
            "bonusBalance": rng.randint(0, 8000),
            "bonusState": ("locked", "unlocked")[rng.getrandbits(1)],
        })

        # F5: неуспешные продления за последние 25 дней
        failed = {"solid": (0, 0.1), "average": (0, 0.35), "struggling": (1, 0.7),
                  "critical": (2, 0.9), "empty": (0, 0)}[arch]
        n_failed = failed[0] + (1 if rng.random() < failed[1] else 0)
        for _ in range(min(n_failed, len(own) * 2)):
            if not own:
                break
            tid += 1
            transactions.append({
                "id": tid, "amount": rng.randint(0, 1000), "status": "not_performed",
                "performedAt": None, "createdAt": iso(now - timedelta(days=rng.uniform(1, 25))),
                "type": "prolong", "serviceId": rng.choice(own)["id"],
            })
        # recency для лояльности: свежее успешное продление у платёжеспособных
        if arch in ("solid", "average") and own and rng.random() < 0.3:
            tid += 1
            t_created = now - timedelta(days=rng.uniform(1, 20))
            transactions.append({
                "id": tid, "amount": rng.randint(0, 1000), "status": "performed",
                "performedAt": iso(t_created + timedelta(minutes=rng.randint(1, 60))),
                "createdAt": iso(t_created), "type": "prolong", "serviceId": rng.choice(own)["id"],
            })

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    payload = {"accounts": accounts, "services": services, "transactions": transactions}
    (out / "data.json").write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"OK: {len(accounts)} accounts / {len(services)} services / {len(transactions)} transactions -> {out / 'data.json'}")


if __name__ == "__main__":
    main()
