#!/usr/bin/env python3
"""Pairwise agreement using 人工打分.xlsx + 合并对比.xlsx."""

from collections import Counter
from itertools import combinations
from pathlib import Path

import pandas as pd

HUMAN_PATH = Path(r"c:\Users\33326\Downloads\人工打分.xlsx")
MODEL_PATH = Path(r"c:\Users\33326\Desktop\danmaku\合并对比.xlsx")
OUT_XLSX = Path(r"c:\Users\33326\Desktop\danmaku\pairwise_agreement_v2.xlsx")
OUT_TXT = Path(r"c:\Users\33326\Desktop\danmaku\pairwise_agreement_v2.txt")

HUMAN_COLS = [f"类别{i}" for i in range(1, 8)]
HUMAN_LABELS = [f"人工{i}" for i in range(1, 8)]
MODEL_MAP = {
    "GPT-5.5": "GPT-5.5类别",
    "豆包": "豆包类别",
    "Gemini3.5": "Gemini3.5类别",
    "Gemini3.1Pro": "Gemini3.1Pro类别",
    "DeepSeek": "DeepSeek类别",
}


def norm(cat):
    if pd.isna(cat):
        return None
    s = str(cat).strip().lower()
    mapping = {
        "arou": "arousal",
        "arousal": "arousal",
        "info": "information",
        "information": "information",
        "mix": "mixture",
        "mixture": "mixture",
        "inert": "inert",
        "insert": "inert",
    }
    return mapping.get(s, s)


def majority_vote(labels):
    votes = [norm(x) for x in labels if norm(x)]
    if not votes:
        return None, set()
    c = Counter(votes)
    top = c.most_common()
    max_count = top[0][1]
    winners = {k for k, v in c.items() if v == max_count}
    if len(winners) == 1:
        return next(iter(winners)), winners
    return None, winners  # tie


def pairwise_detail(df, cols, labels):
    rows = []
    for (ci, li), (cj, lj) in combinations(list(zip(cols, labels)), 2):
        a = df[ci]
        b = df[cj]
        valid = a.notna() & b.notna()
        total = int(valid.sum())
        agree = int((a[valid] == b[valid]).sum()) if total else 0
        rows.append(
            {
                "A": li,
                "B": lj,
                "一致条数": agree,
                "可比对条数": total,
                "一致率": agree / total if total else None,
            }
        )
    return pd.DataFrame(rows).sort_values("一致率", ascending=False)


def pairwise_matrix(df, cols, labels):
    mat = pd.DataFrame(index=labels, columns=labels, dtype=float)
    for i, (ci, li) in enumerate(zip(cols, labels)):
        for j, (cj, lj) in enumerate(zip(cols, labels)):
            if i == j:
                mat.loc[li, lj] = 1.0
                continue
            valid = df[ci].notna() & df[cj].notna()
            mat.loc[li, lj] = (
                (df.loc[valid, ci] == df.loc[valid, cj]).mean() if valid.sum() else float("nan")
            )
    return mat


def main():
    human = pd.read_excel(HUMAN_PATH, sheet_name="Sheet1")
    models = pd.read_excel(MODEL_PATH, sheet_name="合并对比")

    merge_cols = ["序号", "time_sec", "弹幕内容"]
    df = human.merge(
        models[[*merge_cols, *MODEL_MAP.values(), "多数类别"]].rename(
            columns={"多数类别": "模型多数_表"}
        ),
        on=merge_cols,
        how="left",
    )

    for c in HUMAN_COLS:
        df[c] = df[c].apply(norm)
    for col in MODEL_MAP.values():
        df[col] = df[col].apply(norm)

    # human majority from 7 votes
    maj_rows = df[HUMAN_COLS].apply(lambda r: majority_vote(r.tolist()), axis=1)
    df["人工多数_单一"] = [m[0] for m in maj_rows]
    df["人工多数_并列集合"] = ["/".join(sorted(m[1])) for m in maj_rows]
    df["人工是否并列"] = df["人工多数_单一"].isna()

    # model majority from sheet or recompute
    df["模型多数_表"] = df["模型多数_表"].apply(norm)

    def model_maj_row(r):
        votes = [r[c] for c in MODEL_MAP.values() if pd.notna(r[c])]
        single, winners = majority_vote(votes)
        return pd.Series({"模型多数_计算": single, "模型并列": len(winners) > 1})

    mm = df.apply(model_maj_row, axis=1)
    df["模型多数_计算"] = mm["模型多数_计算"]
    df["模型多数_标准化"] = df["模型多数_表"].where(df["模型多数_表"].notna(), df["模型多数_计算"])

    # --- human pairwise ---
    human_pairs = pairwise_detail(df, HUMAN_COLS, HUMAN_LABELS)
    human_mat = pairwise_matrix(df, HUMAN_COLS, HUMAN_LABELS)
    off_diag = []
    for i, li in enumerate(HUMAN_LABELS):
        for j, lj in enumerate(HUMAN_LABELS):
            if i < j:
                off_diag.append(human_mat.loc[li, lj])
    human_avg = sum(off_diag) / len(off_diag)

    # --- model pairwise ---
    model_cols = list(MODEL_MAP.values())
    model_labels = list(MODEL_MAP.keys())
    model_pairs = pairwise_detail(df, model_cols, model_labels)
    model_mat = pairwise_matrix(df, model_cols, model_labels)

    # --- human vs model cross ---
    cross_rows = []
    for hc, hl in zip(HUMAN_COLS, HUMAN_LABELS):
        for mc, ml in zip(model_cols, model_labels):
            valid = df[hc].notna() & df[mc].notna()
            total = int(valid.sum())
            agree = int((df.loc[valid, hc] == df.loc[valid, mc]).sum()) if total else 0
            cross_rows.append(
                {
                    "人工": hl,
                    "模型": ml,
                    "一致条数": agree,
                    "可比对条数": total,
                    "一致率": agree / total if total else None,
                }
            )
    cross_pairs = pd.DataFrame(cross_rows).sort_values("一致率", ascending=False)

    # --- majority vs majority ---
    non_tie = df[df["人工多数_单一"].notna() & df["模型多数_标准化"].notna()]
    exact = (non_tie["人工多数_单一"] == non_tie["模型多数_标准化"]).sum()

    def hit_tie(r):
        if pd.isna(r["模型多数_标准化"]):
            return False
        winners = set(r["人工多数_并列集合"].split("/")) if r["人工多数_并列集合"] else set()
        return r["模型多数_标准化"] in winners

    tie_ok = df[df["模型多数_标准化"].notna()].apply(hit_tie, axis=1).sum()

    majority_summary = pd.DataFrame(
        [
            {
                "对比": "人工7票多数 vs 模型5票多数（人工非并列）",
                "一致条数": int(exact),
                "总条数": len(non_tie),
                "一致率": exact / len(non_tie) if len(non_tie) else None,
            },
            {
                "对比": "人工7票多数 vs 模型5票多数（含人工并列，模型命中任一类）",
                "一致条数": int(tie_ok),
                "总条数": int(df["模型多数_标准化"].notna().sum()),
                "一致率": tie_ok / df["模型多数_标准化"].notna().sum(),
            },
        ]
    )

    def hit_model_human(r, mc):
        if pd.isna(r[mc]):
            return False
        if pd.notna(r["人工多数_单一"]):
            return r[mc] == r["人工多数_单一"]
        winners = set(r["人工多数_并列集合"].split("/")) if r["人工多数_并列集合"] else set()
        return r[mc] in winners

    # per model vs human majority
    hm = []
    for ml, mc in zip(model_labels, model_cols):
        valid = df[mc].notna()
        sub = df[valid]
        non_tie_sub = sub[sub["人工多数_单一"].notna()]
        hm.append(
            {
                "模型": ml,
                "vs人工多数_非并列": int((non_tie_sub[mc] == non_tie_sub["人工多数_单一"]).sum()),
                "非并列总数": len(non_tie_sub),
                "vs人工多数_含并列": int(sub.apply(lambda r: hit_model_human(r, mc), axis=1).sum()),
                "含并列总数": int(valid.sum()),
            }
        )
    hm_df = pd.DataFrame(hm)
    hm_df["非并列一致率"] = hm_df["vs人工多数_非并列"] / hm_df["非并列总数"]
    hm_df["含并列一致率"] = hm_df["vs人工多数_含并列"] / hm_df["含并列总数"]
    hm_df = hm_df.sort_values("含并列一致率", ascending=False)

    human_maj_dist = Counter(df["人工多数_单一"].dropna())
    model_maj_dist = Counter(df["模型多数_标准化"].dropna())

    with pd.ExcelWriter(OUT_XLSX, engine="openpyxl") as writer:
        human_mat.to_excel(writer, sheet_name="人工两两矩阵")
        human_pairs.to_excel(writer, sheet_name="人工两两明细", index=False)
        model_mat.to_excel(writer, sheet_name="模型两两矩阵")
        model_pairs.to_excel(writer, sheet_name="模型两两明细", index=False)
        cross_pairs.to_excel(writer, sheet_name="人工模型两两", index=False)
        majority_summary.to_excel(writer, sheet_name="多数类一致率", index=False)
        hm_df.to_excel(writer, sheet_name="各模型vs人工多数", index=False)

    lines = []
    lines.append(f"数据源: {HUMAN_PATH.name} Sheet1 + {MODEL_PATH.name}")
    lines.append(f"总条数: {len(df)} | 人工并列条数: {int(df['人工是否并列'].sum())}")
    lines.append("")
    lines.append("【一】7位人工两两一致率（21对）")
    lines.append(f"平均一致率: {human_avg:.1%}")
    lines.append("")
    for _, r in human_pairs.iterrows():
        lines.append(
            f"  {r['A']} vs {r['B']}: {int(r['一致条数'])}/{int(r['可比对条数'])} ({r['一致率']:.1%})"
        )
    lines.append("")
    lines.append("【二】5模型两两一致率（10对）")
    for _, r in model_pairs.iterrows():
        lines.append(
            f"  {r['A']} vs {r['B']}: {int(r['一致条数'])}/{int(r['可比对条数'])} ({r['一致率']:.1%})"
        )
    lines.append("")
    lines.append("【三】人工多数 vs 模型多数")
    for _, r in majority_summary.iterrows():
        lines.append(
            f"  {r['对比']}: {int(r['一致条数'])}/{int(r['总条数'])} ({r['一致率']:.1%})"
        )
    lines.append("")
    lines.append("【四】各模型 vs 人工多数（含并列）")
    for _, r in hm_df.iterrows():
        lines.append(
            f"  {r['模型']}: {int(r['vs人工多数_含并列'])}/{int(r['含并列总数'])} ({r['含并列一致率']:.1%})"
        )
    lines.append("")
    lines.append(f"人工多数分布(非并列): {dict(human_maj_dist)}")
    lines.append(f"模型多数分布: {dict(model_maj_dist)}")

    OUT_TXT.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"\nSaved: {OUT_XLSX}")


if __name__ == "__main__":
    main()
