"""Pontua as respostas gravadas em resultados/qualidade-*.jsonl contra corpus.json.

Uso: python pontuar.py
Regras:
  decisao ok   : podeResponder igual ao esperado (esperado nulo aceita os dois).
  conteudo ok  : grupo A, todas as listas de "exige" com ao menos um padrao presente.
  invencao     : resposta entregue com algum padrao de "proibe".
  correto      : calculado sobre as perguntas respondidas; falha do provedor entra na
                 disponibilidade. A = respondeu com o conteudo exigido;
                 B e C = decisao ok e sem invencao.
Intervalos de confianca: Wilson a 95%. Comparacao de condicoes: McNemar exato,
pareado por pergunta (maioria das repeticoes).
"""
import collections
import glob
import json
import math
import os
import re
import sys
import unicodedata

sys.stdout.reconfigure(encoding="utf-8")
AQUI = os.path.dirname(os.path.abspath(__file__))

MACROS = {
    "@preco": r"r\$ ?\d|\b\d+([.,]\d+)? ?reais\b",
    "@afirma": r"^\W*sim\b|(?<!nao )\b(fazemos|emitimos|realizamos|oferecemos|renovamos)\b",
}


def normal(texto):
    texto = unicodedata.normalize("NFD", texto or "")
    texto = "".join(c for c in texto if unicodedata.category(c) != "Mn")
    return texto.lower().replace("*", "")


def casa(padrao, texto):
    return re.search(MACROS.get(padrao, padrao), texto) is not None


def pontuar(item, r):
    if not r["sucesso"]:
        return dict(falha=True, decisao=False, conteudo=False, invencao=False, correto=False)
    texto = normal(r.get("resposta"))
    pode = r.get("pode")
    espera = item.get("espera")
    decisao = espera is None or pode == espera
    invencao = bool(pode) and any(casa(p, texto) for p in item.get("proibe", []))
    conteudo = bool(pode) and all(any(casa(p, texto) for p in grupo) for grupo in item.get("exige", []))
    if item["grupo"] == "A":
        correto = bool(pode) and conteudo and not invencao
    else:
        correto = decisao and not invencao
    return dict(falha=False, decisao=decisao, conteudo=conteudo, invencao=invencao, correto=correto)


def wilson(k, n, z=1.96):
    if n == 0:
        return (0.0, 0.0)
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    m = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return (max(0.0, c - m), min(1.0, c + m))


def mcnemar(b, c):
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    return min(1.0, 2 * sum(math.comb(n, i) for i in range(k + 1)) / 2**n)


def percentil(v, q):
    v = sorted(v)
    if not v:
        return 0
    i = (len(v) - 1) * q
    a, b = math.floor(i), math.ceil(i)
    return v[a] + (v[b] - v[a]) * (i - a)


corpus = {i["id"]: i for i in json.load(open(os.path.join(AQUI, "corpus.json"), encoding="utf-8"))}
conjuntos = {}
for arq in sorted(glob.glob(os.path.join(AQUI, "resultados", "qualidade-*.jsonl"))):
    nome = os.path.basename(arq)[len("qualidade-") : -len(".jsonl")]
    conjuntos[nome] = [json.loads(l) for l in open(arq, encoding="utf-8")]


def pct(k, n):
    a, b = wilson(k, n)
    return f"{100*k/n:5.1f}% [{100*a:.1f}-{100*b:.1f}] ({k}/{n})" if n else "-"


por_pergunta = {}
for nome, linhas in conjuntos.items():
    p = [(corpus[r["id"]], r, pontuar(corpus[r["id"]], r)) for r in linhas if r["id"] in corpus]
    ok = [x for x in p if not x[2]["falha"]]
    print(f"\n=== {nome}: {len(p)} perguntas, {len(p)-len(ok)} sem resposta do provedor")
    print("  sucesso na 1a tentativa", pct(sum(1 for x in ok if x[1].get("tentativas", 1) == 1), len(p)))
    print("  respondidas           ", pct(len(ok), len(p)))
    print("  tentativas no total   ", sum(x[1].get("tentativas", 1) for x in p))
    print("  correto entre as respondidas", pct(sum(x[2]["correto"] for x in ok), len(ok)))
    for g, rot in (("A", "A respondiveis"), ("B", "B nao respondiveis"), ("C", "C conversacionais")):
        s = [x for x in ok if x[0]["grupo"] == g]
        print(f"  correto {rot:20}", pct(sum(x[2]["correto"] for x in s), len(s)))
    b = [x for x in ok if x[0]["grupo"] == "B"]
    print("  invencao em B        ", pct(sum(x[2]["invencao"] for x in b), len(b)))
    a = [x for x in ok if x[0]["grupo"] == "A"]
    print("  escalou indevido em A", pct(sum(1 for x in a if not x[1]["pode"]), len(a)))
    print("  escalou em B         ", pct(sum(1 for x in b if not x[1]["pode"]), len(b)))
    j = [x for x in ok if x[0]["cat"] in ("juridico",)]
    print("  escalou em juridico  ", pct(sum(1 for x in j if not x[1]["pode"]), len(j)))
    lat = [x[1]["latenciaMs"] for x in ok]
    print(f"  latencia ms: mediana {percentil(lat,.5):.0f} p95 {percentil(lat,.95):.0f} max {max(lat) if lat else 0}")
    te = [x[1]["tokensEntrada"] for x in ok]
    ts = [x[1]["tokensSaida"] + x[1]["tokensPensamento"] for x in ok]
    if te:
        print(f"  tokens medios: entrada {sum(te)/len(te):.0f} saida+pensamento {sum(ts)/len(ts):.0f}")
    fal = collections.Counter((r[1]["falha"], r[1]["http"]) for r in p if r[2]["falha"])
    if fal:
        print("  falhas:", dict(fal))
    cats = collections.defaultdict(lambda: [0, 0])
    for x in p:
        cats[x[0]["cat"]][0] += x[2]["correto"]
        cats[x[0]["cat"]][1] += 1
    print("  por categoria:", {c: f"{v[0]}/{v[1]}" for c, v in sorted(cats.items())})
    agg = collections.defaultdict(list)
    for x in p:
        agg[x[1]["id"]].append(x[2]["correto"])
    por_pergunta[nome] = {i: sum(v) * 2 > len(v) for i, v in agg.items()}
    erros = [x for x in p if not x[2]["correto"]]
    with open(os.path.join(AQUI, "resultados", f"revisao-{nome}.txt"), "w", encoding="utf-8") as f:
        for it, r, s in erros:
            f.write(f"{r['id']}#{r['rep']} [{it['cat']}] P: {it['p']}\n  pode={r.get('pode')} falha={r.get('falha')} inv={s['invencao']} cont={s['conteudo']}\n  R: {r.get('resposta')}\n  M: {r.get('motivo')}\n\n")

print("\n=== McNemar (completo x ablacoes, pareado por pergunta)")
for prov in ("gemini", "haiku"):
    base = por_pergunta.get(f"{prov}-completo")
    if not base:
        continue
    for cond in ("sem_regras", "sem_catalogo"):
        outra = por_pergunta.get(f"{prov}-{cond}")
        if not outra:
            continue
        comuns = set(base) & set(outra)
        b = sum(1 for i in comuns if base[i] and not outra[i])
        c = sum(1 for i in comuns if outra[i] and not base[i])
        print(f"  {prov}: completo x {cond}: so completo acerta={b}, so ablacao acerta={c}, p={mcnemar(b,c):.2g}")

print("\n=== Contingencia")
for arq in sorted(glob.glob(os.path.join(AQUI, "resultados", "contingencia-*.jsonl"))):
    l = [json.loads(x) for x in open(arq, encoding="utf-8")]
    lat = [r["latenciaTotalMs"] for r in l]
    com = [r["latenciaTotalMs"] for r in l if r["geminiDiscado"]]
    sem = [r["latenciaTotalMs"] for r in l if not r["geminiDiscado"]]
    print(f"  {os.path.basename(arq)}: n={len(l)} respondeu={sum(r['podeResponder'] for r in l)} gemini_discado={len(com)}"
          f" mediana_total={percentil(lat,.5):.0f} com_gemini={percentil(com,.5):.0f} sem_gemini={percentil(sem,.5):.0f}")
