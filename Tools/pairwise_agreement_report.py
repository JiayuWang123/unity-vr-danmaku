#!/usr/bin/env python3
"""Pairwise agreement rates + human-majority vs model-majority comparison."""

from itertools import combinations
from pathlib import Path

import pandas as pd

HUMAN_PATH = Path(r"c:\Users\33326\Downloads\人工打分对比.xlsx")
MODEL_PATH = Path(r"c:\Users\33326\Desktop\danmaku\合并对比.xlsx")
OUT_XLSX = Path(r"c:\Users\33326\Desktop\danmaku\pairwise_agreement_report.xlsx")
OUT_TXT = Path(r"c:\Users\33326\Desktop\danmaku\pairwise_agreement_report.txt")

HUMAN_COLS = [f"类别{i}" for i in range(1, 8)]
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


def human_majority_set(value):
    if pd.isna(value):
        return set()
    text = str(value).replace("并列", "").strip()
    return {norm(p.strip()) for p in text.split("/") if norm(p.strip())}


def human_majority_single(value):
    s = human_majority_set(value)
    return next(iter(s)) if len(s) == 1 else None


def pairwise_matrix(df: pd.DataFrame, cols: list[str], labels: list[str]) -> pd.DataFrame:
    """Simple agreement rate matrix between column pairs."""
    n = len(df)
    mat = pd.DataFrame(index=labels, columns=labels, dtype=float)
    for i, (ci, li) in enumerate(zip(cols, labels)):
        for j, (cj, lj) in enumerate(zip(cols, labels)):
            if i == j:
                mat.loc[li, lj] = 1.0
                continue
            a = df[ci].apply(norm)
            b = df[cj].apply(norm)
            valid = a.notna() & b.notna()
            if valid.sum() == 0:
                mat.loc[li, lj] = float("nan")
            else:
                mat.loc[li, lj] = (a[valid] == b[valid]).mean()
    return mat


def pairwise_detail(df: pd.DataFrame, cols: list[str], labels: list[str]) -> pd.DataFrame:
    rows = []
    for (ci, li), (cj, lj) in combinations(list(zip(cols, labels)), 2):
        a = df[ci].apply(norm)
        b = df[cj].apply(norm)
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


def model_majority_from_row(row, model_cols):
    votes = [norm(row[c]) for c in model_cols if norm(row[c])]
    if not votes:
        return None
    from collections import Counter

    c = Counter(votes)
    top = c.most_common()
    if len(top) > 1 and top[0][1] == top[1][1]:
        return None  # tie
    return top[0][0]


def main():
    human = pd.read_excel(HUMAN_PATH, sheet_name="Sheet1_清晰分析")
    models = pd.read_excel(MODEL_PATH, sheet_name="合并对比")

    merge_cols = ["序号", "time_sec", "弹幕内容"]
    df = human.merge(
        models[[*merge_cols, *MODEL_MAP.values(), "多数类别"]].rename(
            columns={"多数类别": "模型表_多数类别"}
        ),
        on=merge_cols,
        how="left",
        suffixes=("", "_dup"),
    )

    # normalize human columns
    for c in HUMAN_COLS:
        df[c] = df[c].apply(norm)

    human_labels = [f"人工{i}" for i in range(1, 8)]
    model_labels = list(MODEL_MAP.keys())
    model_cols = list(MODEL_MAP.values())

    for c in model_cols:
        df[c] = df[c].apply(norm)

    # --- pairwise human ---
    human_mat = pairwise_matrix(df, HUMAN_COLS, human_labels)
    human_pairs = pairwise_detail(df, HUMAN_COLS, human_labels)

    # --- pairwise model ---
    model_mat = pairwise_matrix(df, model_cols, model_labels)
    model_pairs = pairwise_detail(df, model_cols, model_labels)

    # --- human vs model pairwise ---
    cross_rows = []
    for hc, hl in zip(HUMAN_COLS, human_labels):
        for mc, ml in zip(model_cols, model_labels):
            a = df[hc]
            b = df[mc]
            valid = a.notna() & b.notna()
            total = int(valid.sum())
            agree = int((a[valid] == b[valid]).sum()) if total else 0
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
    df["人工多数_标准化"] = df["多数类别"].apply(human_majority_single)
    df["人工多数_集合"] = df["多数类别"].apply(
        lambda x: "/".join(sorted(human_majority_set(x)))
    )
    df["模型多数_计算"] = df.apply(
        lambda r: model_majority_from_row(r, model_cols), axis=1
    )
    # use sheet value if present and no tie
    df["模型多数_标准化"] = df["模型表_多数类别"].apply(norm)
    df.loc[df["模型多数_标准化"].isna(), "模型多数_标准化"] = df["模型多数_计算"]

    # exact match (non-tie human only)
    non_tie = df[df["人工多数_标准化"].notna()].copy()
    exact_agree = (non_tie["人工多数_标准化"] == non_tie["模型多数_标准化"]).sum()
    exact_total = len(non_tie)

    # tie-friendly: model majority in human set
    all_rows = df[df["模型多数_标准化"].notna()].copy()
    tie_friendly = all_rows.apply(
        lambda r: r["模型多数_标准化"] in human_majority_set(r["多数类别"]),
        axis=1,
    ).sum()
    tie_total = len(all_rows)

    majority_summary = pd.DataFrame(
        [
            {
                "对比项": "人工多数 vs 模型多数（仅人工非并列）",
                "一致条数": int(exact_agree),
                "总条数": int(exact_total),
                "一致率": exact_agree / exact_total if exact_total else None,
            },
            {
                "对比项": "人工多数 vs 模型多数（含并列，模型命中任一并列类）",
                "一致条数": int(tie_friendly),
                "总条数": int(tie_total),
                "一致率": tie_friendly / tie_total if tie_total else None,
            },
        ]
    )

    # per-model vs human majority
    hm_rows = []
    for ml, mc in zip(model_labels, model_cols):
        valid = df[mc].notna()
        # non-tie human
        sub = df[valid & df["人工多数_标准化"].notna()]
        agree_exact = (sub[mc] == sub["人工多数_标准化"]).sum()
        # tie-friendly
        agree_tie = sub.apply(
            lambda r: r[mc] in human_majority_set(r["多数类别"]), axis=1
        ).sum()
        hm_rows.append(
            {
                "模型": ml,
                "vs人工多数_非并列一致条数": int(agree_exact),
                "vs人工多数_非并列总条数": len(sub),
                "vs人工多数_非并列一致率": agree_exact / len(sub) if len(sub) else None,
                "vs人工多数_含并列一致条数": int(
                    df[valid]
                    .apply(lambda r: r[mc] in human_majority_set(r["多数类别"]), axis=1)
                    .sum()
                ),
                "vs人工多数_含并列总条数": int(valid.sum()),
                "vs人工多数_含并列一致率": df[valid]
                .apply(lambda r: r[mc] in human_majority_set(r["多数类别"]), axis=1)
                .mean(),
            }
        )
    model_vs_human_majority = pd.DataFrame(hm_rows).sort_values(
        "vs人工多数_含并列一致率", ascending=False
    )

    with pd.ExcelWriter(OUT_XLSX, engine="openpyxl") as writer:
        human_mat.to_excel(writer, sheet_name="人工两两矩阵")
        human_pairs.to_excel(writer, sheet_name="人工两两明细", index=False)
        model_mat.to_excel(writer, sheet_name="模型两两矩阵")
        model_pairs.to_excel(writer, sheet_name="模型两两明细", index=False)
        cross_pairs.to_excel(writer, sheet_name="人工模型两两", index=False)
        majority_summary.to_excel(writer, sheet_name="多数类一致率", index=False)
        model_vs_human_majority.to_excel(
            writer, sheet_name="各模型vs人工多数", index=False
        )

    lines = []
    lines.append("=" * 60)
    lines.append("一、7位人工 两两一致率（简单一致率）")
    lines.append("=" * 60)
    lines.append("\n平均一致率（矩阵非对角）:")
    vals = []
    for i, li in enumerate(human_labels):
        for j, lj in enumerate(human_labels):
            if i < j:
                vals.append(human_mat.loc[li, lj])
    lines.append(f"  全体平均: {sum(vals)/len(vals):.1%}")
    lines.append("\n一致率最高的人工对:")
    for _, r in human_pairs.head(5).iterrows():
        lines.append(f"  {r['A']} vs {r['B']}: {r['一致条数']}/{r['可比对条数']} ({r['一致率']:.1%})")
    lines.append("\n一致率最低的人工对:")
    for _, r in human_pairs.tail(5).iterrows():
        lines.append(f"  {r['A']} vs {r['B']}: {r['一致条数']}/{r['可比对条数']} ({r['一致率']:.1%})")

    lines.append("\n" + "=" * 60)
    lines.append("二、5个模型 两两一致率")
    lines.append("=" * 60)
    for _, r in model_pairs.iterrows():
        lines.append(f"  {r['A']} vs {r['B']}: {r['一致条数']}/{r['可比对条数']} ({r['一致率']:.1%})")

    lines.append("\n" + "=" * 60)
    lines.append("三、人工多数 vs 模型多数")
    lines.append("=" * 60)
    for _, r in majority_summary.iterrows():
        lines.append(
            f"  {r['对比项']}: {int(r['一致条数'])}/{int(r['总条数'])} ({r['一致率']:.1%})"
        )

    lines.append("\n各模型 vs 人工多数（含并列）:")
    for _, r in model_vs_human_majority.iterrows():
        lines.append(
            f"  {r['模型']}: {int(r['vs人工多数_含并列一致条数'])}/{int(r['vs人工多数_含并列总条数'])} "
            f"({r['vs人工多数_含并列一致率']:.1%})"
        )

    OUT_TXT.write_text("\n".join(lines), encoding="utf-8")

    print("\n".join(lines))
    print(f"\nSaved: {OUT_XLSX}")
    print(f"Saved: {OUT_TXT}")


if __name__ == "__main__":
    main()
