#!/usr/bin/env python3
"""Compare AI classifications against the manually agreed consensus category list."""

from collections import Counter
from pathlib import Path

import pandas as pd


GEMINI_PATH = Path(r"c:\Users\33326\Downloads\gemini3.1pro分类结果.xlsx")
GPT_PATH = Path(r"c:\Users\33326\Downloads\gpt5.5分类结果.xlsx")
OUT_XLSX = Path(r"c:\Users\33326\Desktop\danmaku\ai_vs_consensus_category_analysis.xlsx")
OUT_TXT = Path(r"c:\Users\33326\Desktop\danmaku\ai_vs_consensus_category_analysis.txt")


CONSENSUS_TEXT = """
球员/球队/裁判相关
情绪
互动
互动
无关弹幕
互动
互动
情绪
球员/球队/裁判相关
梗
互动
互动
无关弹幕
互动
球员/球队/裁判相关
比赛内容/历史信息
比赛内容/历史信息
互动
互动
球员/球队/裁判相关
情绪
比赛内容/历史信息
球员/球队/裁判相关
球员/球队/裁判相关
赛点/剧透
比赛内容/历史信息
球员/球队/裁判相关
球员/球队/裁判相关
比赛内容/历史信息
比赛内容/历史信息
球员/球队/裁判相关
比赛内容/历史信息
情绪
无关弹幕
无关弹幕
无关弹幕
球员/球队/裁判相关
情绪
球员/球队/裁判相关
比赛内容/历史信息
球员/球队/裁判相关
赛点/剧透
比赛内容/历史信息
情绪
情绪
球员/球队/裁判相关
情绪
情绪
情绪
情绪
情绪
球员/球队/裁判相关
情绪
赛点/剧透
互动
球员/球队/裁判相关
情绪
比赛内容/历史信息
比赛内容/历史信息
比赛内容/历史信息
球员/球队/裁判相关
互动
比赛内容/历史信息
比赛内容/历史信息
互动
情绪
梗
梗
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
比赛内容/历史信息
无关弹幕
球员/球队/裁判相关
比赛内容/历史信息
比赛内容/历史信息
球员/球队/裁判相关
互动
互动
互动
比赛内容/历史信息
比赛内容/历史信息
比赛内容/历史信息
比赛内容/历史信息
比赛内容/历史信息
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
梗
比赛内容/历史信息
情绪
梗
情绪
球员/球队/裁判相关
互动
比赛内容/历史信息
互动
情绪
梗
球员/球队/裁判相关
赛点/剧透
球员/球队/裁判相关
比赛内容/历史信息
赛点/剧透
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
比赛内容/历史信息
赛点/剧透
赛点/剧透
比赛内容/历史信息
赛点/剧透
比赛内容/历史信息
赛点/剧透
情绪
球员/球队/裁判相关
赛点/剧透
赛点/剧透
情绪
情绪
情绪
球员/球队/裁判相关
情绪
比赛内容/历史信息
互动
梗
情绪
情绪
球员/球队/裁判相关
无关弹幕
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
比赛内容/历史信息
情绪
情绪
比赛内容/历史信息
球员/球队/裁判相关
比赛内容/历史信息
比赛内容/历史信息
球员/球队/裁判相关
球员/球队/裁判相关
梗
互动
球员/球队/裁判相关
情绪
球员/球队/裁判相关
互动
互动
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
情绪
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
球员/球队/裁判相关
比赛内容/历史信息
情绪
球员/球队/裁判相关
球员/球队/裁判相关
情绪
球员/球队/裁判相关
互动
梗
互动
球员/球队/裁判相关
比赛内容/历史信息
情绪
比赛内容/历史信息
球员/球队/裁判相关
球员/球队/裁判相关
比赛内容/历史信息
球员/球队/裁判相关
球员/球队/裁判相关
情绪
球员/球队/裁判相关
球员/球队/裁判相关
比赛内容/历史信息
球员/球队/裁判相关
赛点/剧透
赛点/剧透
赛点/剧透
比赛内容/历史信息
比赛内容/历史信息
情绪
情绪
比赛内容/历史信息
比赛内容/历史信息
梗
球员/球队/裁判相关
球员/球队/裁判相关
赛点/剧透
"""


def norm(value):
    if pd.isna(value):
        return None
    s = str(value).strip().strip("。")
    if not s:
        return None
    s = s.replace("－", "-").replace("—", "-").replace("–", "-")
    s = s.replace(" - ", "-").replace(" -", "-").replace("- ", "-")
    if "-" in s:
        s = s.split("-")[-1].strip()
    mapping = {
        "盲点/剧透": "赛点/剧透",
        "其它无关弹幕": "无关弹幕",
        "其他无关弹幕": "无关弹幕",
        "无关": "无关弹幕",
        "球员/球队/裁判": "球员/球队/裁判相关",
        "比赛内容/历史": "比赛内容/历史信息",
    }
    return mapping.get(s, s)


def load_ai(path, model_name):
    xls = pd.ExcelFile(path)
    df = pd.read_excel(path, sheet_name=xls.sheet_names[0])
    return pd.DataFrame(
        {
            "弹幕内容": df[df.columns[0]].astype(str),
            model_name: df[df.columns[1]].apply(norm),
            f"{model_name}_原始分类": df[df.columns[1]],
        }
    )


def metrics(df, model_col):
    rows = []
    cats = sorted(set(df["人工共识"].dropna()) | set(df[model_col].dropna()))
    for cat in cats:
        tp = int(((df["人工共识"] == cat) & (df[model_col] == cat)).sum())
        pred = int((df[model_col] == cat).sum())
        ref = int((df["人工共识"] == cat).sum())
        fp = pred - tp
        fn = ref - tp
        rows.append(
            {
                "模型": model_col,
                "类别": cat,
                "AI预测数": pred,
                "人工共识数": ref,
                "命中数": tp,
                "误报数_FP": fp,
                "漏报数_FN": fn,
                "precision": tp / pred if pred else None,
                "recall": tp / ref if ref else None,
                "F1": 2 * tp / (pred + ref) if (pred + ref) else None,
                "AI-人工数量差": pred - ref,
            }
        )
    return pd.DataFrame(rows)


def summarize_model(df, model_col):
    total = len(df)
    agree = int((df["人工共识"] == df[model_col]).sum())
    return {
        "模型": model_col,
        "一致条数": agree,
        "总条数": total,
        "一致率": agree / total,
    }


def pct(v):
    return "" if pd.isna(v) else f"{v:.1%}"


def main():
    consensus = [norm(line) for line in CONSENSUS_TEXT.splitlines() if norm(line)]
    gemini = load_ai(GEMINI_PATH, "Gemini3.1Pro")
    gpt = load_ai(GPT_PATH, "GPT-5.5")
    if len(consensus) != len(gemini) or len(consensus) != len(gpt):
        raise ValueError(f"Consensus={len(consensus)}, Gemini={len(gemini)}, GPT={len(gpt)}")

    df = pd.DataFrame({"序号": range(1, len(consensus) + 1), "人工共识": consensus})
    df["弹幕内容"] = gemini["弹幕内容"]
    df["Gemini3.1Pro"] = gemini["Gemini3.1Pro"]
    df["GPT-5.5"] = gpt["GPT-5.5"]
    df["Gemini3.1Pro_原始分类"] = gemini["Gemini3.1Pro_原始分类"]
    df["GPT-5.5_原始分类"] = gpt["GPT-5.5_原始分类"]
    df["Gemini是否一致"] = df["人工共识"] == df["Gemini3.1Pro"]
    df["GPT是否一致"] = df["人工共识"] == df["GPT-5.5"]
    df["两个AI是否一致"] = df["Gemini3.1Pro"] == df["GPT-5.5"]

    summary = pd.DataFrame([summarize_model(df, "Gemini3.1Pro"), summarize_model(df, "GPT-5.5")])
    ai_pair_agree = int(df["两个AI是否一致"].sum())
    both_correct = int((df["Gemini是否一致"] & df["GPT是否一致"]).sum())
    gemini_only = int((df["Gemini是否一致"] & ~df["GPT是否一致"]).sum())
    gpt_only = int((~df["Gemini是否一致"] & df["GPT是否一致"]).sum())
    both_wrong = int((~df["Gemini是否一致"] & ~df["GPT是否一致"]).sum())

    metric_df = pd.concat([metrics(df, "Gemini3.1Pro"), metrics(df, "GPT-5.5")], ignore_index=True)
    conf_gemini = pd.crosstab(df["人工共识"], df["Gemini3.1Pro"], dropna=False)
    conf_gpt = pd.crosstab(df["人工共识"], df["GPT-5.5"], dropna=False)

    dist_rows = []
    for source, col in [("人工共识", "人工共识"), ("Gemini3.1Pro", "Gemini3.1Pro"), ("GPT-5.5", "GPT-5.5")]:
        counts = df[col].value_counts()
        for cat, count in counts.items():
            dist_rows.append({"来源": source, "类别": cat, "条数": int(count), "占比": count / len(df)})
    dist = pd.DataFrame(dist_rows)

    with pd.ExcelWriter(OUT_XLSX, engine="openpyxl") as writer:
        df.to_excel(writer, sheet_name="逐条对比", index=False)
        summary.to_excel(writer, sheet_name="总体一致率", index=False)
        dist.to_excel(writer, sheet_name="类别分布", index=False)
        metric_df.to_excel(writer, sheet_name="各类别指标", index=False)
        conf_gemini.to_excel(writer, sheet_name="混淆矩阵_Gemini")
        conf_gpt.to_excel(writer, sheet_name="混淆矩阵_GPT")
        df[~df["Gemini是否一致"]].to_excel(writer, sheet_name="Gemini错分", index=False)
        df[~df["GPT是否一致"]].to_excel(writer, sheet_name="GPT错分", index=False)

    lines = []
    lines.append(f"总条数: {len(df)}")
    lines.append("【总体一致率】")
    for _, r in summary.iterrows():
        lines.append(f"  {r['模型']}: {int(r['一致条数'])}/{int(r['总条数'])} ({r['一致率']:.1%})")
    lines.append(f"  两个AI彼此一致: {ai_pair_agree}/{len(df)} ({ai_pair_agree / len(df):.1%})")
    lines.append(f"  两个AI都对: {both_correct}; 仅Gemini对: {gemini_only}; 仅GPT对: {gpt_only}; 两个都错: {both_wrong}")
    lines.append("")

    lines.append("【类别分布】")
    for source in ["人工共识", "Gemini3.1Pro", "GPT-5.5"]:
        lines.append(f"  {source}")
        sub = dist[dist["来源"] == source].sort_values("条数", ascending=False)
        for _, r in sub.iterrows():
            lines.append(f"    {r['类别']}: {int(r['条数'])} ({r['占比']:.1%})")
    lines.append("")

    lines.append("【各类别指标】")
    for model in ["Gemini3.1Pro", "GPT-5.5"]:
        lines.append(f"  {model}")
        sub = metric_df[metric_df["模型"] == model].sort_values("F1", ascending=False)
        for _, r in sub.iterrows():
            lines.append(
                f"    {r['类别']}: AI {int(r['AI预测数'])}, 人工 {int(r['人工共识数'])}, "
                f"命中 {int(r['命中数'])}, P {pct(r['precision'])}, R {pct(r['recall'])}, "
                f"F1 {pct(r['F1'])}, 差 {int(r['AI-人工数量差'])}"
            )
    lines.append("")

    lines.append("【主要错分方向】")
    for model, conf in [("Gemini3.1Pro", conf_gemini), ("GPT-5.5", conf_gpt)]:
        lines.append(f"  {model}")
        wrongs = []
        for human_cat in conf.index:
            for ai_cat in conf.columns:
                count = int(conf.loc[human_cat, ai_cat])
                if count and human_cat != ai_cat:
                    wrongs.append((count, human_cat, ai_cat))
        for count, human_cat, ai_cat in sorted(wrongs, key=lambda x: x[0], reverse=True)[:10]:
            lines.append(f"    人工 {human_cat} -> AI {ai_cat}: {count}")

    OUT_TXT.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"\nSaved: {OUT_XLSX}")


if __name__ == "__main__":
    main()
