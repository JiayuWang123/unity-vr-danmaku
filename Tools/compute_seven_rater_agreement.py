#!/usr/bin/env python3
"""Compute inter-rater agreement for 7 human annotators on new category."""

from collections import Counter
from itertools import combinations
from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.metrics import cohen_kappa_score

HUMAN_PATH = Path(r"c:\Users\33326\Downloads\人工打分(1).xlsx")
OUT_TXT = Path(r"c:\Users\33326\Desktop\danmaku\seven_rater_agreement.txt")

COLS = [f"new category{i}" for i in range(1, 8)]
LABELS = [f"人工{i}" for i in range(1, 8)]


def fleiss_kappa(count_matrix, n_raters):
    n_items, n_categories = count_matrix.shape
    p_i = (np.sum(count_matrix * count_matrix, axis=1) - n_raters) / (
        n_raters * (n_raters - 1)
    )
    p_bar = p_i.mean()
    p_j = count_matrix.sum(axis=0) / (n_items * n_raters)
    p_e = np.sum(p_j**2)
    if p_e == 1:
        return float("nan")
    return (p_bar - p_e) / (1 - p_e)


def krippendorff_alpha_nominal(data):
    """Simple nominal alpha without external package."""
    # data: n_units x n_coders, strings
    units = []
    for row in data:
        vals = [v for v in row if pd.notna(v)]
        if len(vals) < 2:
            continue
        units.append(vals)

    if not units:
        return float("nan")

    all_vals = sorted(set(v for u in units for v in u))
    val_idx = {v: i for i, v in enumerate(all_vals)}
    n_cat = len(all_vals)

    # coincidence matrix
    o = np.zeros((n_cat, n_cat))
    for vals in units:
        m = len(vals)
        for i in range(m):
            for j in range(i + 1, m):
                a, b = val_idx[vals[i]], val_idx[vals[j]]
                o[a, b] += 1
                o[b, a] += 1

    n_c = o.sum()
    if n_c == 0:
        return float("nan")

    do = 0.0
    for i in range(n_cat):
        for j in range(n_cat):
            if i != j:
                do += o[i, j]
    do /= n_c

    n_j = o.sum(axis=1)
    de = 0.0
    for i in range(n_cat):
        for j in range(n_cat):
            if i != j:
                de += n_j[i] * n_j[j]
    de /= (n_c * (n_c - 1))

    if de == 0:
        return 1.0
    return 1.0 - do / de


def interpret_kappa(k):
    if pd.isna(k):
        return "N/A"
    if k < 0:
        return "Poor (<0)"
    if k < 0.20:
        return "Slight (0.00-0.20)"
    if k < 0.40:
        return "Fair (0.21-0.40)"
    if k < 0.60:
        return "Moderate (0.41-0.60)"
    if k < 0.80:
        return "Substantial (0.61-0.80)"
    return "Almost perfect (0.81-1.00)"


def main():
    df = pd.read_excel(HUMAN_PATH, sheet_name="对比")
    for col in COLS:
        df[col] = df[col].apply(lambda x: None if pd.isna(x) else str(x).strip())

    complete = df.dropna(subset=COLS).copy()
    cats = sorted(set(complete[COLS].stack().dropna().unique()))
    cat_idx = {c: i for i, c in enumerate(cats)}

    count_matrix = np.zeros((len(complete), len(cats)))
    for i, (_, row) in enumerate(complete.iterrows()):
        for col in COLS:
            count_matrix[i, cat_idx[row[col]]] += 1

    fleiss = fleiss_kappa(count_matrix, n_raters=7)
    alpha = krippendorff_alpha_nominal(complete[COLS].values)

    pair_rows = []
    kappas = []
    agrees = []
    for (c1, l1), (c2, l2) in combinations(list(zip(COLS, LABELS)), 2):
        y1 = complete[c1]
        y2 = complete[c2]
        k = cohen_kappa_score(y1, y2)
        a = (y1 == y2).mean()
        kappas.append(k)
        agrees.append(a)
        pair_rows.append({"A": l1, "B": l2, "Cohen_kappa": k, "一致率": a})

    pair_df = pd.DataFrame(pair_rows).sort_values("Cohen_kappa", ascending=False)

    unanimous = 0
    six_plus = 0
    five_plus = 0
    four_plus = 0
    tied = 0
    for _, row in complete.iterrows():
        votes = [row[c] for c in COLS]
        c = Counter(votes)
        top = c.most_common(1)[0][1]
        winners = sum(1 for v in c.values() if v == top)
        if top == 7:
            unanimous += 1
        if top >= 6:
            six_plus += 1
        if top >= 5:
            five_plus += 1
        if top >= 4:
            four_plus += 1
        if winners > 1 and top <= 3:
            tied += 1

    lines = []
    lines.append("7人 new category 标注一致率分析")
    lines.append(f"文件: {HUMAN_PATH.name}")
    lines.append(f"总条数: {len(df)}")
    lines.append(f"7人全部有标注的条数: {len(complete)}")
    lines.append(f"类别数: {len(cats)} -> {cats}")
    lines.append("")
    lines.append("【重要说明：Cohen's kappa 只适用于 2 个标注者】")
    lines.append("7 人不能直接算一个 Cohen's kappa。")
    lines.append("标准做法是：Fleiss' kappa（7人整体）+ 两两 Cohen's kappa（补充）+ Krippendorff's alpha（更通用）。")
    lines.append("")
    lines.append("【7人整体指标】")
    lines.append(f"Fleiss' kappa: {fleiss:.4f} ({interpret_kappa(fleiss)})")
    lines.append(f"Krippendorff's alpha (nominal): {alpha:.4f} ({interpret_kappa(alpha)})")
    lines.append(f"两两 Cohen's kappa 均值: {np.mean(kappas):.4f} ({interpret_kappa(np.mean(kappas))})")
    lines.append(f"两两 Cohen's kappa 范围: {np.min(kappas):.4f} - {np.max(kappas):.4f}")
    lines.append(f"两两 percent agreement 均值: {np.mean(agrees):.1%}")
    lines.append("")
    lines.append("【多数票情况（7票）】")
    lines.append(f"7/7 全一致: {unanimous}/{len(complete)} ({unanimous/len(complete):.1%})")
    lines.append(f"6票及以上占多数: {six_plus}/{len(complete)} ({six_plus/len(complete):.1%})")
    lines.append(f"5票及以上占多数: {five_plus}/{len(complete)} ({five_plus/len(complete):.1%})")
    lines.append(f"4票及以上占多数: {four_plus}/{len(complete)} ({four_plus/len(complete):.1%})")
    lines.append(f"明显并列/分散 (最高仅2-3票且多类并列): {tied}/{len(complete)}")
    lines.append("")
    lines.append("【两两 Cohen's kappa 明细（21对）】")
    for _, r in pair_df.iterrows():
        lines.append(
            f"  {r['A']} vs {r['B']}: kappa={r['Cohen_kappa']:.3f}, 一致率={r['一致率']:.1%}"
        )

    OUT_TXT.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"\nSaved: {OUT_TXT}")


if __name__ == "__main__":
    main()
