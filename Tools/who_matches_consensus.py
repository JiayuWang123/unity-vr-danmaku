#!/usr/bin/env python3
"""Find which annotators best match the adjudicated consensus category."""

from pathlib import Path

import pandas as pd
from sklearn.metrics import cohen_kappa_score

HUMAN_PATH = Path(r"c:\Users\33326\Downloads\人工打分(1).xlsx")
OUT_PATH = Path(r"c:\Users\33326\Desktop\danmaku\annotator_vs_consensus.txt")

COLS = [f"new category{i}" for i in range(1, 8)]
LABELS = [f"人工{i}" for i in range(1, 8)]
GOLD = "统一后category"


def norm(x):
    if pd.isna(x):
        return None
    s = str(x).strip()
    return s if s and s.lower() != "nan" else None


def main():
    df = pd.read_excel(HUMAN_PATH, sheet_name="对比")
    for c in COLS + [GOLD]:
        df[c] = df[c].apply(norm)

    valid = df[GOLD].notna()
    sub = df[valid].copy()
    n = len(sub)

    rows = []
    for col, lab in zip(COLS, LABELS):
        both = sub[col].notna() & sub[GOLD].notna()
        y_h = sub.loc[both, col]
        y_g = sub.loc[both, GOLD]
        agree = int((y_h == y_g).sum())
        total = int(both.sum())
        kappa = cohen_kappa_score(y_h, y_g) if total else float("nan")
        rows.append(
            {
                "标注员": lab,
                "与统一结果一致条数": agree,
                "可比对条数": total,
                "一致率": agree / total if total else None,
                "Cohen_kappa_vs统一": kappa,
            }
        )

    res = pd.DataFrame(rows).sort_values("一致率", ascending=False)

    lines = []
    lines.append("7位标注员 vs 讨论后「统一后category」")
    lines.append(f"总条数: {n}")
    lines.append("")
    lines.append("【排名】")
    for i, (_, r) in enumerate(res.iterrows(), 1):
        lines.append(
            f"  {i}. {r['标注员']}: {int(r['与统一结果一致条数'])}/{int(r['可比对条数'])} "
            f"({r['一致率']:.1%}), kappa={r['Cohen_kappa_vs统一']:.3f}"
        )
    top2 = res.head(2)
    lines.append("")
    lines.append("【与讨论结果最相近的两人】")
    lines.append(
        f"  {top2.iloc[0]['标注员']} ({top2.iloc[0]['一致率']:.1%})"
    )
    lines.append(
        f"  {top2.iloc[1]['标注员']} ({top2.iloc[1]['一致率']:.1%})"
    )

    OUT_PATH.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"\nSaved: {OUT_PATH}")


if __name__ == "__main__":
    main()
