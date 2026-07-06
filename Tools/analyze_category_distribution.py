#!/usr/bin/env python3
"""Category distribution and threshold sensitivity for danmaku scoring."""
import pandas as pd
from collections import Counter


def classify(arou, info, t_hi=0.5, t_lo=0.5, d_mix=0.3):
    if pd.isna(arou) or pd.isna(info):
        return None
    try:
        a, i = float(arou), float(info)
    except (TypeError, ValueError):
        return None
    D = abs(a - i)
    if a < t_hi and i < t_lo:
        return "inert"
    if D <= d_mix:
        return "mixture"
    if i >= t_hi and a < t_lo:
        return "information"
    if a >= t_hi and i < t_lo:
        return "arousal"
    if i > a:
        return "information"
    if a > i:
        return "arousal"
    return "mixture"


def load_sources():
    out = {}
    out["DS_score"] = pd.read_excel(
        r"c:\Users\33326\Desktop\danmaku\DS_score.xlsx", sheet_name="DS"
    )
    df2 = pd.read_excel(
        r"c:\Users\33326\Downloads\弹幕打分_完整数据.xlsx",
        sheet_name="弹幕打分数据",
        header=1,
    )
    df2.columns = ["time", "text", "arou", "info", "D", "cat", "len"]
    out["完整数据"] = df2
    df3 = pd.read_excel(
        r"c:\Users\33326\Downloads\geimini 3.5.xlsx", sheet_name="gemini-total"
    )
    df3["arou"] = df3["情绪唤醒强度(arou)"]
    df3["info"] = df3["信息价值(info)"]
    out["gemini3.5"] = df3
    out["arou_info_table"] = pd.read_excel(
        r"c:\Users\33326\Downloads\danmaku_arou_info_table.xlsx", sheet_name="Data"
    )
    df5 = pd.read_csv(
        r"c:\Users\33326\Downloads\弹幕打分.csv", encoding="utf-8-sig", on_bad_lines="skip"
    )
    df5["arou"] = pd.to_numeric(df5.iloc[:, 2], errors="coerce")
    df5["info"] = pd.to_numeric(df5.iloc[:, 3], errors="coerce")
    out["弹幕打分csv"] = df5
    return out


def norm_file_cat(s):
    s = str(s).strip().lower()
    if s in ("arou", "arousal"):
        return "arousal"
    if s in ("info", "information"):
        return "information"
    if s in ("mix", "mixture"):
        return "mixture"
    return s


def print_dist(title, counts, n):
    print(f"\n{title}")
    for cat in ["inert", "arousal", "information", "mixture"]:
        c = counts.get(cat, 0)
        print(f"  {cat:14s} {c:4d}  ({c/n*100:5.1f}%)")


def main():
    sources = load_sources()
    n = 200

    print("=" * 70)
    print("一、各文件「已有分类」列的分布（200 条样本）")
    print("=" * 70)
    for name, df in sources.items():
        if "category" in df.columns:
            col = df["category"]
        elif "分类" in df.columns:
            col = df["分类"]
        else:
            col = df["cat"]
        vc = Counter(norm_file_cat(x) for x in col)
        print_dist(name + " [文件原分类]", vc, len(df))

    print("\n" + "=" * 70)
    print("二、统一规则重算（t=0.5, D<=0.3）")
    print("=" * 70)
    recomputed = {}
    for name, df in sources.items():
        cats = [classify(r["arou"], r["info"]) for _, r in df.iterrows()]
        vc = Counter(c for c in cats if c)
        recomputed[name] = cats
        print_dist(name + " [重算]", vc, len(df))

    print("\n" + "=" * 70)
    print("三、DS_score 分数分布（边界是否卡在中间）")
    print("=" * 70)
    base = sources["DS_score"].copy()
    base["arou"] = pd.to_numeric(base["arou"], errors="coerce")
    base["info"] = pd.to_numeric(base["info"], errors="coerce")
    for col in ["arou", "info"]:
        v = base[col].astype(float)
        border = ((v >= 0.45) & (v <= 0.55)).sum()
        print(
            f"{col}: min={v.min():.2f} med={v.median():.2f} mean={v.mean():.2f} max={v.max():.2f} | "
            f"<0.5:{(v<0.5).sum()} >=0.5:{(v>=0.5).sum()} 边界带0.45-0.55:{border}"
        )
    D = (base["arou"].astype(float) - base["info"].astype(float)).abs()
    print(
        f"D: med={D.median():.2f} | <=0.3:{(D<=0.3).sum()}  (0.28-0.32]:{((D>0.28)&(D<=0.32)).sum()}  >0.3:{(D>0.3).sum()}"
    )

    print("\n" + "=" * 70)
    print("四、阈值敏感性（用 DS_score 的 arou/info）")
    print("=" * 70)
    arous = base["arou"].astype(float).values
    infos = base["info"].astype(float).values

    print("\n扫 t_hi（inert 双低阈值）, D_mix=0.3:")
    for t in [0.45, 0.48, 0.50, 0.52, 0.55]:
        c = Counter(classify(a, i, t_hi=t, t_lo=t) for a, i in zip(arous, infos))
        print(
            f"  t={t:.2f}: inert={c['inert']:3d} arou={c['arousal']:3d} "
            f"info={c['information']:3d} mix={c['mixture']:3d}"
        )

    print("\n扫 D_mix, t=0.5:")
    for d in [0.25, 0.28, 0.30, 0.32, 0.35]:
        c = Counter(classify(a, i, d_mix=d) for a, i in zip(arous, infos))
        print(
            f"  D<={d:.2f}: inert={c['inert']:3d} arou={c['arousal']:3d} "
            f"info={c['information']:3d} mix={c['mixture']:3d}"
        )

    print("\n" + "=" * 70)
    print("五、4 文件重算后多数票（共识近似）")
    print("=" * 70)
    names = ["DS_score", "完整数据", "gemini3.5", "arou_info_table"]
    consensus = []
    for idx in range(n):
        votes = []
        for name in names:
            df = sources[name]
            if idx < len(df):
                c = classify(df.iloc[idx]["arou"], df.iloc[idx]["info"])
                if c:
                    votes.append(c)
        if votes:
            consensus.append(Counter(votes).most_common(1)[0][0])
    print_dist("多数票(4文件)", Counter(consensus), len(consensus))

    print("\n" + "=" * 70)
    print("六、VR 层映射预期（近=arousal+短, 远=info+长, 字<=12/>=18）")
    print("=" * 70)
    base["recomputed"] = [classify(a, i) for a, i in zip(base["arou"], base["info"])]
    base["char_len"] = base["text"].astype(str).str.len()
    near = ((base["recomputed"] == "arousal") & (base["char_len"] <= 12)).sum()
    far = ((base["recomputed"] == "information") & (base["char_len"] >= 18)).sum()
    print(f"DS重算后 近层候选(arousal且<=12字): {near} ({near/n*100:.1f}%)")
    print(f"DS重算后 远层候选(info且>=18字): {far} ({far/n*100:.1f}%)")
    print(f"inert+mixture+其余 → 主要进中层/旁路")

    print("\n" + "=" * 70)
    print("七、完整数据 阈值扫 + 近远层候选")
    print("=" * 70)
    full = sources["完整数据"].copy()
    full["arou"] = pd.to_numeric(full["arou"], errors="coerce")
    full["info"] = pd.to_numeric(full["info"], errors="coerce")
    full["char_len"] = full["text"].astype(str).str.len()
    for t in [0.45, 0.50, 0.55]:
        for d in [0.25, 0.30, 0.35]:
            cats = [classify(r["arou"], r["info"], t_hi=t, t_lo=t, d_mix=d) for _, r in full.iterrows()]
            vc = Counter(cats)
            near = sum(
                1
                for (_, r), c in zip(full.iterrows(), cats)
                if c == "arousal" and r["char_len"] <= 12
            )
            far = sum(
                1
                for (_, r), c in zip(full.iterrows(), cats)
                if c == "information" and r["char_len"] >= 18
            )
            print(
                f"  t={t} D<={d}: inert={vc['inert']:3d} arou={vc['arousal']:3d} "
                f"info={vc['information']:3d} mix={vc['mixture']:3d} | near={near:3d} far={far:3d}"
            )
        print()


if __name__ == "__main__":
    main()
