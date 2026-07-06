#!/usr/bin/env python3
"""Compare model categories with human majority categories per danmaku."""

from collections import Counter
from pathlib import Path

import pandas as pd


HUMAN_PATH = Path(r"c:\Users\33326\Downloads\人工打分对比.xlsx")
MODEL_PATH = Path(r"c:\Users\33326\Desktop\danmaku\合并对比.xlsx")
OUT_XLSX = Path(r"c:\Users\33326\Desktop\danmaku\model_human_agreement.xlsx")
OUT_TXT = Path(r"c:\Users\33326\Desktop\danmaku\人工模型一致性总结.txt")

MODEL_COLS = {
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


def human_set(value):
    if pd.isna(value):
        return set()
    text = str(value).replace("并列", "").strip()
    return {norm(part.strip()) for part in text.split("/") if norm(part.strip())}


def main():
    human = pd.read_excel(HUMAN_PATH, sheet_name="Sheet1_清晰分析")
    models = pd.read_excel(MODEL_PATH, sheet_name="合并对比")

    merge_cols = ["序号", "time_sec", "时间", "关键事件", "弹幕内容"]
    base = human[
        merge_cols + ["多数类别", "最高票数", "一致程度", "类别票数"]
    ].copy()
    model_part = models[merge_cols + list(MODEL_COLS.values())].copy()
    df = base.merge(model_part, on=merge_cols, how="left", validate="one_to_one")

    rows = []
    counts = Counter()
    strict_counts = Counter()
    category_acc_rows = []

    for _, row in df.iterrows():
        hset = human_set(row["多数类别"])
        row_out = row.to_dict()
        row_out["人工类别集合"] = "/".join(sorted(hset))
        matching_models = []

        for model, col in MODEL_COLS.items():
            model_cat = norm(row.get(col))
            is_match = model_cat in hset if model_cat else False
            row_out[f"{model}_标准化分类"] = model_cat
            row_out[f"{model}_是否与人工一致"] = "是" if is_match else "否"

            if is_match:
                matching_models.append(model)
                counts[model] += 1
                if len(hset) == 1:
                    strict_counts[model] += 1

        row_out["与人工一致的模型"] = "、".join(matching_models) if matching_models else "无"
        row_out["一致模型数量"] = len(matching_models)
        rows.append(row_out)

    result = pd.DataFrame(rows)

    summary_rows = []
    for model in MODEL_COLS:
        summary_rows.append(
            {
                "模型": model,
                "与人工一致条数_含并列": counts[model],
                "一致率_含并列": counts[model] / len(df),
                "与人工一致条数_仅非并列人工": strict_counts[model],
            }
        )
    summary = pd.DataFrame(summary_rows).sort_values(
        "与人工一致条数_含并列", ascending=False
    )

    non_tie = df[df["多数类别"].apply(lambda x: len(human_set(x)) == 1)].copy()
    for model, col in MODEL_COLS.items():
        model_norm = non_tie[col].apply(norm)
        human_norm = non_tie["多数类别"].apply(norm)
        for hcat in ["arousal", "information", "mixture", "inert"]:
            mask = human_norm == hcat
            total = int(mask.sum())
            if total == 0:
                continue
            hit = int((model_norm[mask] == hcat).sum())
            category_acc_rows.append(
                {
                    "模型": model,
                    "人工类别": hcat,
                    "该人工类别条数": total,
                    "命中条数": hit,
                    "命中率": hit / total,
                }
            )
    category_acc = pd.DataFrame(category_acc_rows)

    with pd.ExcelWriter(OUT_XLSX, engine="openpyxl") as writer:
        result.to_excel(writer, sheet_name="逐条一致模型", index=False)
        summary.to_excel(writer, sheet_name="模型一致数汇总", index=False)
        category_acc.to_excel(writer, sheet_name="按人工类别命中率", index=False)

    lines = []
    lines.append(f"总弹幕数: {len(df)}")
    lines.append("人工多数类别含并列时，模型命中任一并列类别即算一致。")
    lines.append("")
    lines.append("模型与人工一致条数（含并列命中）:")
    for _, row in summary.iterrows():
        lines.append(
            f"- {row['模型']}: {int(row['与人工一致条数_含并列'])}/{len(df)} "
            f"({row['一致率_含并列']:.1%})"
        )
    lines.append("")
    lines.append("按人工类别命中率（仅非并列人工多数类别）:")
    for model in MODEL_COLS:
        lines.append(f"\n{model}:")
        sub = category_acc[category_acc["模型"] == model]
        for _, row in sub.iterrows():
            lines.append(
                f"  - {row['人工类别']}: {int(row['命中条数'])}/"
                f"{int(row['该人工类别条数'])} ({row['命中率']:.1%})"
            )
    lines.append("")
    lines.append(f"逐条结果见 Excel: {OUT_XLSX}")
    OUT_TXT.write_text("\n".join(lines), encoding="utf-8")

    print(summary.to_string(index=False))
    print()
    print(category_acc.to_string(index=False))
    print()
    print(f"Saved: {OUT_XLSX}")
    print(f"Saved: {OUT_TXT}")


if __name__ == "__main__":
    main()
