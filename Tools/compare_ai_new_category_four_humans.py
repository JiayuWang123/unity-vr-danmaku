#!/usr/bin/env python3
"""Compare two AI new-category outputs against the four-human comparison sheet."""

from collections import Counter
from itertools import combinations
from pathlib import Path

import pandas as pd


HUMAN_PATH = Path(r"c:\Users\33326\Downloads\人工打分.xlsx")
GEMINI_PATH = Path(r"c:\Users\33326\Downloads\gemini3.1pro分类结果.xlsx")
GPT_PATH = Path(r"c:\Users\33326\Downloads\gpt5.5分类结果.xlsx")

OUT_XLSX = Path(r"c:\Users\33326\Desktop\danmaku\ai_new_category_four_human_analysis.xlsx")
OUT_TXT = Path(r"c:\Users\33326\Desktop\danmaku\ai_new_category_four_human_analysis.txt")

HUMAN_SHEET = "对比"
HUMAN_COLS = ["new category1", "new category3", "new category5", "new category7"]
HUMAN_LABELS = ["人工1", "人工3", "人工5", "人工7"]


def norm(value):
    if pd.isna(value):
        return None
    s = str(value).strip()
    if not s:
        return None
    s = s.replace("－", "-").replace("—", "-").replace("–", "-")
    s = s.replace(" - ", "-").replace(" -", "-").replace("- ", "-")
    # AI files store "一级-二级"; the requested comparison is on new category.
    if "-" in s:
        s = s.split("-")[-1].strip()

    mapping = {
        "盲点/剧透": "赛点/剧透",
        "其它无关弹幕": "无关弹幕",
        "其他无关弹幕": "无关弹幕",
        "无关": "无关弹幕",
        "球员/球队/裁判": "球员/球队/裁判相关",
        "比赛内容/历史": "比赛内容/历史信息",
        "解说": "解说相关",
    }
    return mapping.get(s, s)


def majority(votes):
    clean = [v for v in votes if v]
    if not clean:
        return None, set(), "无有效标注"
    counts = Counter(clean)
    max_count = max(counts.values())
    winners = {k for k, v in counts.items() if v == max_count}
    if len(winners) == 1:
        label = next(iter(winners))
        if max_count == len(clean):
            pattern = f"{len(clean)}/{len(clean)}一致"
        else:
            ordered_counts = sorted(counts.values(), reverse=True)
            pattern = "-".join(str(x) for x in ordered_counts)
        return label, winners, pattern
    return None, winners, f"并列{'/'.join(str(counts[w]) for w in sorted(winners))}"


def agreement_rows(df, cols, labels):
    rows = []
    for (col_a, lab_a), (col_b, lab_b) in combinations(zip(cols, labels), 2):
        valid = df[col_a].notna() & df[col_b].notna()
        total = int(valid.sum())
        agree = int((df.loc[valid, col_a] == df.loc[valid, col_b]).sum())
        rows.append(
            {
                "A": lab_a,
                "B": lab_b,
                "一致条数": agree,
                "可比对条数": total,
                "一致率": agree / total if total else None,
            }
        )
    return pd.DataFrame(rows).sort_values("一致率", ascending=False)


def model_vs_reference(df, model_col):
    valid = df[model_col].notna()
    non_tie = valid & df["人工多数_单一"].notna()
    strict_agree = int((df.loc[non_tie, model_col] == df.loc[non_tie, "人工多数_单一"]).sum())

    def tie_hit(row):
        if pd.isna(row[model_col]):
            return False
        return row[model_col] in row["人工多数_集合"]

    tie_total = int(valid.sum())
    tie_agree = int(df.loc[valid].apply(tie_hit, axis=1).sum())
    return {
        "模型": model_col,
        "非并列一致条数": strict_agree,
        "非并列总数": int(non_tie.sum()),
        "非并列一致率": strict_agree / int(non_tie.sum()) if int(non_tie.sum()) else None,
        "含并列命中条数": tie_agree,
        "含并列总数": tie_total,
        "含并列命中率": tie_agree / tie_total if tie_total else None,
    }


def per_human_model_agreement(df, model_cols):
    rows = []
    for model_col in model_cols:
        for human_col, human_label in zip(HUMAN_COLS, HUMAN_LABELS):
            valid = df[model_col].notna() & df[human_col].notna()
            total = int(valid.sum())
            agree = int((df.loc[valid, model_col] == df.loc[valid, human_col]).sum())
            rows.append(
                {
                    "模型": model_col,
                    "人工": human_label,
                    "一致条数": agree,
                    "可比对条数": total,
                    "一致率": agree / total if total else None,
                }
            )
    return pd.DataFrame(rows).sort_values(["模型", "一致率"], ascending=[True, False])


def category_metrics(df, model_col):
    rows = []
    valid = df[model_col].notna() & df["人工多数_单一"].notna()
    sub = df.loc[valid]
    categories = sorted(set(sub["人工多数_单一"].dropna()) | set(sub[model_col].dropna()))
    for cat in categories:
        tp = int(((sub[model_col] == cat) & (sub["人工多数_单一"] == cat)).sum())
        pred = int((sub[model_col] == cat).sum())
        ref = int((sub["人工多数_单一"] == cat).sum())
        rows.append(
            {
                "模型": model_col,
                "类别": cat,
                "AI预测数": pred,
                "人工多数数": ref,
                "命中数": tp,
                "precision_AI预测中命中": tp / pred if pred else None,
                "recall_人工中被命中": tp / ref if ref else None,
                "AI-人工数量差": pred - ref,
            }
        )
    return pd.DataFrame(rows)


def load_ai(path, model_name):
    xls = pd.ExcelFile(path)
    df = pd.read_excel(path, sheet_name=xls.sheet_names[0])
    content_col = df.columns[0]
    category_col = df.columns[1]
    return pd.DataFrame(
        {
            "弹幕内容": df[content_col].astype(str),
            model_name: df[category_col].apply(norm),
            f"{model_name}_原始分类": df[category_col],
        }
    )


def pct(value):
    return "" if pd.isna(value) else f"{value:.1%}"


def main():
    human = pd.read_excel(HUMAN_PATH, sheet_name=HUMAN_SHEET)
    human = human.rename(columns={human.columns[4]: "弹幕内容"})
    for col in HUMAN_COLS:
        human[col] = human[col].apply(norm)

    gemini = load_ai(GEMINI_PATH, "Gemini3.1Pro")
    gpt = load_ai(GPT_PATH, "GPT-5.5")

    df = human.copy()
    df["弹幕内容"] = df["弹幕内容"].astype(str)
    df["Gemini3.1Pro"] = gemini["Gemini3.1Pro"].values
    df["GPT-5.5"] = gpt["GPT-5.5"].values
    df["Gemini3.1Pro_原始分类"] = gemini["Gemini3.1Pro_原始分类"].values
    df["GPT-5.5_原始分类"] = gpt["GPT-5.5_原始分类"].values
    df["AI内容是否与人工同序"] = (
        df["弹幕内容"].astype(str).str.strip()
        == gemini["弹幕内容"].astype(str).str.strip()
    ) & (
        df["弹幕内容"].astype(str).str.strip()
        == gpt["弹幕内容"].astype(str).str.strip()
    )

    maj = df[HUMAN_COLS].apply(lambda row: majority(row.tolist()), axis=1)
    df["人工多数_单一"] = [x[0] for x in maj]
    df["人工多数_集合"] = [x[1] for x in maj]
    df["人工多数_集合文本"] = ["/".join(sorted(x[1])) for x in maj]
    df["人工一致模式"] = [x[2] for x in maj]
    df["人工是否并列"] = df["人工多数_单一"].isna()

    model_cols = ["Gemini3.1Pro", "GPT-5.5"]

    human_pairs = agreement_rows(df, HUMAN_COLS, HUMAN_LABELS)
    model_summary = pd.DataFrame([model_vs_reference(df, col) for col in model_cols])
    per_human = per_human_model_agreement(df, model_cols)
    ai_pair = agreement_rows(df, model_cols, model_cols)
    human_pattern = df["人工一致模式"].value_counts(dropna=False).rename_axis("人工一致模式").reset_index(name="条数")

    dist_rows = []
    for label, col in [*zip(HUMAN_LABELS, HUMAN_COLS), ("人工多数_非并列", "人工多数_单一"), *[(m, m) for m in model_cols]]:
        counts = df[col].dropna().value_counts()
        for cat, count in counts.items():
            dist_rows.append({"来源": label, "类别": cat, "条数": int(count), "占比": count / df[col].notna().sum()})
    distribution = pd.DataFrame(dist_rows)

    metrics = pd.concat([category_metrics(df, col) for col in model_cols], ignore_index=True)
    conf_base = df[df["人工多数_单一"].notna()]
    confusion_gemini = pd.crosstab(conf_base["人工多数_单一"], conf_base["Gemini3.1Pro"], dropna=False)
    confusion_gpt = pd.crosstab(conf_base["人工多数_单一"], conf_base["GPT-5.5"], dropna=False)

    mismatch_cases = df[
        df["人工多数_单一"].notna()
        & (
            (df["Gemini3.1Pro"] != df["人工多数_单一"])
            | (df["GPT-5.5"] != df["人工多数_单一"])
        )
    ][
        [
            "序号",
            "time_sec",
            "弹幕内容",
            *HUMAN_COLS,
            "人工多数_单一",
            "Gemini3.1Pro",
            "GPT-5.5",
        ]
    ]

    with pd.ExcelWriter(OUT_XLSX, engine="openpyxl") as writer:
        df.drop(columns=["人工多数_集合"]).to_excel(writer, sheet_name="逐条对比", index=False)
        human_pairs.to_excel(writer, sheet_name="人工两两一致率", index=False)
        human_pattern.to_excel(writer, sheet_name="人工一致模式", index=False)
        distribution.to_excel(writer, sheet_name="类别分布", index=False)
        model_summary.to_excel(writer, sheet_name="AI_vs人工多数", index=False)
        per_human.to_excel(writer, sheet_name="AI_vs单个人工", index=False)
        ai_pair.to_excel(writer, sheet_name="AI两两一致率", index=False)
        metrics.to_excel(writer, sheet_name="各类别precision_recall", index=False)
        confusion_gemini.to_excel(writer, sheet_name="混淆矩阵_Gemini")
        confusion_gpt.to_excel(writer, sheet_name="混淆矩阵_GPT")
        mismatch_cases.to_excel(writer, sheet_name="AI错分案例_非并列", index=False)

    lines = []
    lines.append(f"数据源: {HUMAN_PATH.name} / {GEMINI_PATH.name} / {GPT_PATH.name}")
    lines.append(f"总条数: {len(df)}")
    lines.append(f"AI与人工内容同序条数: {int(df['AI内容是否与人工同序'].sum())}/{len(df)}")
    lines.append("")
    lines.append("【一】4个人工 new category 两两一致率")
    lines.append(f"平均一致率: {human_pairs['一致率'].mean():.1%}")
    for _, row in human_pairs.iterrows():
        lines.append(f"  {row['A']} vs {row['B']}: {int(row['一致条数'])}/{int(row['可比对条数'])} ({row['一致率']:.1%})")
    lines.append("")
    lines.append("【二】4个人工多数情况")
    for _, row in human_pattern.iterrows():
        lines.append(f"  {row['人工一致模式']}: {int(row['条数'])}")
    lines.append("")
    lines.append("【三】AI vs 人工多数")
    for _, row in model_summary.iterrows():
        lines.append(
            f"  {row['模型']}: 非并列 {int(row['非并列一致条数'])}/{int(row['非并列总数'])} ({row['非并列一致率']:.1%}); "
            f"含并列命中 {int(row['含并列命中条数'])}/{int(row['含并列总数'])} ({row['含并列命中率']:.1%})"
        )
    lines.append("")
    lines.append("【四】AI vs 单个人工")
    for model in model_cols:
        lines.append(f"  {model}")
        sub = per_human[per_human["模型"] == model].sort_values("一致率", ascending=False)
        for _, row in sub.iterrows():
            lines.append(
                f"    {row['人工']}: {int(row['一致条数'])}/{int(row['可比对条数'])} ({row['一致率']:.1%})"
            )
    lines.append("")
    lines.append("【五】AI之间一致率")
    for _, row in ai_pair.iterrows():
        lines.append(f"  {row['A']} vs {row['B']}: {int(row['一致条数'])}/{int(row['可比对条数'])} ({row['一致率']:.1%})")
    lines.append("")
    lines.append("【六】类别分布")
    for source in ["人工多数_非并列", *model_cols]:
        lines.append(f"  {source}")
        sub = distribution[distribution["来源"] == source].sort_values("条数", ascending=False)
        for _, row in sub.iterrows():
            lines.append(f"    {row['类别']}: {int(row['条数'])} ({row['占比']:.1%})")
    lines.append("")
    lines.append("【七】AI类别数量与人工多数数量差（非并列人工多数作参照）")
    for model in model_cols:
        lines.append(f"  {model}")
        sub = metrics[metrics["模型"] == model].sort_values("AI-人工数量差", ascending=False)
        for _, row in sub.iterrows():
            lines.append(
                f"    {row['类别']}: AI {int(row['AI预测数'])}, 人工 {int(row['人工多数数'])}, "
                f"差 {int(row['AI-人工数量差'])}, precision {pct(row['precision_AI预测中命中'])}, recall {pct(row['recall_人工中被命中'])}"
            )
    lines.append("")
    lines.append("【八】主要错分方向（人工多数非并列）")
    for model, confusion in [("Gemini3.1Pro", confusion_gemini), ("GPT-5.5", confusion_gpt)]:
        lines.append(f"  {model}")
        off_diag = []
        for human_cat in confusion.index:
            for ai_cat in confusion.columns:
                count = int(confusion.loc[human_cat, ai_cat])
                if count and human_cat != ai_cat:
                    off_diag.append((count, human_cat, ai_cat))
        for count, human_cat, ai_cat in sorted(off_diag, key=lambda x: x[0], reverse=True)[:8]:
            lines.append(f"    人工 {human_cat} -> AI {ai_cat}: {count}")
    OUT_TXT.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"\nSaved: {OUT_XLSX}")


if __name__ == "__main__":
    main()
