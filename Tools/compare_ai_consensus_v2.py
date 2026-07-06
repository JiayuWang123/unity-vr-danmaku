#!/usr/bin/env python3
"""Compare prompt-improved AI results against human consensus categories."""

import re
from pathlib import Path

import pandas as pd


GEMINI_PATH = Path(r"c:\Users\33326\Downloads\Gemini 3.1pro 分类结果.xlsx")
GPT_PATH = Path(r"c:\Users\33326\Downloads\GPT5.5分类结果(1).xlsx")

# Previous run (before prompt improvement)
OLD_GEMINI_PATH = Path(r"c:\Users\33326\Downloads\gemini3.1pro分类结果.xlsx")
OLD_GPT_PATH = Path(r"c:\Users\33326\Downloads\gpt5.5分类结果.xlsx")

OUT_XLSX = Path(r"c:\Users\33326\Desktop\danmaku\ai_vs_consensus_v2_analysis.xlsx")
OUT_TXT = Path(r"c:\Users\33326\Desktop\danmaku\ai_vs_consensus_v2_analysis.txt")

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

    # Remove English parenthetical suffixes, e.g. "情绪 (Supportive Interactions)"
    s = re.sub(r"\s*\([^)]*\)\s*", " ", s).strip()

    s = s.replace("－", "-").replace("—", "-").replace("–", "-")
    s = s.replace(" - ", "-").replace(" -", "-").replace("- ", "-")

    # Take the last segment after dash (二级分类)
    if "-" in s:
        s = s.split("-")[-1].strip()

    mapping = {
        "盲点/剧透": "赛点/剧透",
        "其它无关弹幕": "无关弹幕",
        "其他无关弹幕": "无关弹幕",
        "无关": "无关弹幕",
        "球员/球队/裁判": "球员/球队/裁判相关",
        "比赛内容/历史": "比赛内容/历史信息",
        "社交互动/接话/应答": "互动",
        "社交互动/接话/回复": "互动",
        "社交互动/喊话/分享": "互动",
        "梗/玩梗": "梗",
        "梗/玩笑梗": "梗",
        "梗/搞笑内容": "梗",
        "memes&joking": "梗",
        "socialization": "互动",
        "random messages": "无关弹幕",
        "解说相关": "比赛内容/历史信息",
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
        rows.append(
            {
                "模型": model_col,
                "类别": cat,
                "AI预测数": pred,
                "人工共识数": ref,
                "命中数": tp,
                "precision": tp / pred if pred else None,
                "recall": tp / ref if ref else None,
                "F1": 2 * tp / (pred + ref) if (pred + ref) else None,
                "AI-人工数量差": pred - ref,
            }
        )
    return pd.DataFrame(rows)


def pct(v):
    return "" if pd.isna(v) else f"{v:.1%}"


def main():
    consensus = [norm(line) for line in CONSENSUS_TEXT.splitlines() if norm(line)]
    gemini = load_ai(GEMINI_PATH, "Gemini3.1Pro")
    gpt = load_ai(GPT_PATH, "GPT-5.5")

    old_gemini = load_ai(OLD_GEMINI_PATH, "Gemini3.1Pro") if OLD_GEMINI_PATH.exists() else None
    old_gpt = load_ai(OLD_GPT_PATH, "GPT-5.5") if OLD_GPT_PATH.exists() else None

    df = pd.DataFrame({"序号": range(1, len(consensus) + 1), "人工共识": consensus})
    df["弹幕内容"] = gemini["弹幕内容"]
    df["Gemini3.1Pro"] = gemini["Gemini3.1Pro"]
    df["GPT-5.5"] = gpt["GPT-5.5"]
    df["Gemini3.1Pro_原始"] = gemini["Gemini3.1Pro_原始分类"]
    df["GPT-5.5_原始"] = gpt["GPT-5.5_原始分类"]
    df["Gemini一致"] = df["人工共识"] == df["Gemini3.1Pro"]
    df["GPT一致"] = df["人工共识"] == df["GPT-5.5"]

    if old_gemini is not None:
        df["Gemini3.1Pro_旧"] = old_gemini["Gemini3.1Pro"]
        df["Gemini旧一致"] = df["人工共识"] == df["Gemini3.1Pro_旧"]
    if old_gpt is not None:
        df["GPT-5.5_旧"] = old_gpt["GPT-5.5"]
        df["GPT旧一致"] = df["人工共识"] == df["GPT-5.5_旧"]

    summary_rows = []
    for col in ["Gemini3.1Pro", "GPT-5.5"]:
        agree = int((df["人工共识"] == df[col]).sum())
        summary_rows.append({"模型": col, "一致条数": agree, "总条数": len(df), "一致率": agree / len(df)})
    summary = pd.DataFrame(summary_rows)

    metric_df = pd.concat([metrics(df, "Gemini3.1Pro"), metrics(df, "GPT-5.5")], ignore_index=True)
    conf_g = pd.crosstab(df["人工共识"], df["Gemini3.1Pro"], dropna=False)
    conf_p = pd.crosstab(df["人工共识"], df["GPT-5.5"], dropna=False)

    dist_rows = []
    for source, col in [("人工共识", "人工共识"), ("Gemini3.1Pro", "Gemini3.1Pro"), ("GPT-5.5", "GPT-5.5")]:
        for cat, count in df[col].value_counts().items():
            dist_rows.append({"来源": source, "类别": cat, "条数": int(count), "占比": count / len(df)})
    dist = pd.DataFrame(dist_rows)

    # Improvement vs old
    improve_rows = []
    if "Gemini旧一致" in df.columns:
        improve_rows.append(
            {
                "模型": "Gemini3.1Pro",
                "旧一致率": df["Gemini旧一致"].mean(),
                "新一致率": df["Gemini一致"].mean(),
                "提升": df["Gemini一致"].mean() - df["Gemini旧一致"].mean(),
                "旧一致条数": int(df["Gemini旧一致"].sum()),
                "新一致条数": int(df["Gemini一致"].sum()),
            }
        )
    if "GPT旧一致" in df.columns:
        improve_rows.append(
            {
                "模型": "GPT-5.5",
                "旧一致率": df["GPT旧一致"].mean(),
                "新一致率": df["GPT一致"].mean(),
                "提升": df["GPT一致"].mean() - df["GPT旧一致"].mean(),
                "旧一致条数": int(df["GPT旧一致"].sum()),
                "新一致条数": int(df["GPT一致"].sum()),
            }
        )
    improve = pd.DataFrame(improve_rows)

    with pd.ExcelWriter(OUT_XLSX, engine="openpyxl") as writer:
        df.to_excel(writer, sheet_name="逐条对比", index=False)
        summary.to_excel(writer, sheet_name="总体一致率", index=False)
        improve.to_excel(writer, sheet_name="新旧对比", index=False)
        dist.to_excel(writer, sheet_name="类别分布", index=False)
        metric_df.to_excel(writer, sheet_name="各类别指标", index=False)
        conf_g.to_excel(writer, sheet_name="混淆矩阵_Gemini")
        conf_p.to_excel(writer, sheet_name="混淆矩阵_GPT")
        df[~df["Gemini一致"]].to_excel(writer, sheet_name="Gemini错分", index=False)
        df[~df["GPT一致"]].to_excel(writer, sheet_name="GPT错分", index=False)

    lines = []
    lines.append("【完善提示词后 vs 人工共识】")
    lines.append(f"总条数: {len(df)}")
    for _, r in summary.iterrows():
        lines.append(f"  {r['模型']}: {int(r['一致条数'])}/{int(r['总条数'])} ({r['一致率']:.1%})")
    lines.append(f"  两AI彼此一致: {int((df['Gemini3.1Pro']==df['GPT-5.5']).sum())}/200")
    lines.append(f"  两AI都对: {int((df['Gemini一致']&df['GPT一致']).sum())}")
    lines.append(f"  两AI都错: {int((~df['Gemini一致']&~df['GPT一致']).sum())}")
    lines.append("")

    if not improve.empty:
        lines.append("【与完善提示词前对比】")
        for _, r in improve.iterrows():
            lines.append(
                f"  {r['模型']}: {int(r['旧一致条数'])} -> {int(r['新一致条数'])} "
                f"({r['旧一致率']:.1%} -> {r['新一致率']:.1%}, {'+' if r['提升']>=0 else ''}{r['提升']:.1%})"
            )
        lines.append("")

    lines.append("【类别分布】")
    for source in ["人工共识", "Gemini3.1Pro", "GPT-5.5"]:
        lines.append(f"  {source}")
        sub = dist[dist["来源"] == source].sort_values("条数", ascending=False)
        for _, r in sub.iterrows():
            lines.append(f"    {r['类别']}: {int(r['条数'])} ({r['占比']:.1%})")
    lines.append("")

    lines.append("【各类别 F1】")
    for model in ["Gemini3.1Pro", "GPT-5.5"]:
        lines.append(f"  {model}")
        sub = metric_df[metric_df["模型"] == model].sort_values("F1", ascending=False)
        for _, r in sub.iterrows():
            lines.append(
                f"    {r['类别']}: 命中{int(r['命中数'])}, AI{int(r['AI预测数'])}/人工{int(r['人工共识数'])}, "
                f"P{pct(r['precision'])}, R{pct(r['recall'])}, F1{pct(r['F1'])}"
            )
    lines.append("")

    lines.append("【主要错分方向】")
    for model, conf in [("Gemini3.1Pro", conf_g), ("GPT-5.5", conf_p)]:
        lines.append(f"  {model}")
        wrongs = []
        for h in conf.index:
            for a in conf.columns:
                c = int(conf.loc[h, a])
                if c and h != a:
                    wrongs.append((c, h, a))
        for c, h, a in sorted(wrongs, key=lambda x: x[0], reverse=True)[:8]:
            lines.append(f"    人工 {h} -> AI {a}: {c}")

    OUT_TXT.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"\nSaved: {OUT_XLSX}")


if __name__ == "__main__":
    main()
