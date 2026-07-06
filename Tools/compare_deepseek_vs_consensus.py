#!/usr/bin/env python3
"""Compare DeepSeek ds.xlsx against human unified consensus (200 items)."""

import re
from pathlib import Path

import pandas as pd
from sklearn.metrics import cohen_kappa_score

DS_PATH = Path(r"c:\Users\33326\Desktop\ds.xlsx")
HUMAN_PATH = Path(r"c:\Users\33326\Downloads\人工打分(1).xlsx")
OUT_XLSX = Path(r"c:\Users\33326\Desktop\danmaku\deepseek_vs_consensus_200.xlsx")
OUT_TXT = Path(r"c:\Users\33326\Desktop\danmaku\deepseek_vs_consensus_200.txt")


def norm(value):
    if pd.isna(value):
        return None
    s = str(value).strip().strip("。")
    if not s or s.lower() == "nan":
        return None
    s = re.sub(r"\s*\([^)]*\)\s*", " ", s).strip()
    s = s.replace("－", "-").replace("—", "-").replace("–", "-")
    s = s.replace(" - ", "-").replace(" -", "-").replace("- ", "-")
    if "-" in s:
        s = s.split("-")[-1].strip()
    mapping = {
        "盲点/剧透": "赛点/剧透",
        "其它无关弹幕": "无关弹幕",
        "其他无关弹幕": "无关弹幕",
        "无关": "无关弹幕",
        "社交互动/喊话/分享": "互动",
        "梗/搞笑内容": "梗",
        "梗/玩梗": "梗",
        "解说相关": "比赛内容/历史信息",
    }
    return mapping.get(s, s)


def metrics(df):
    rows = []
    cats = sorted(set(df["人工统一"].dropna()) | set(df["DeepSeek"].dropna()))
    for cat in cats:
        tp = int(((df["人工统一"] == cat) & (df["DeepSeek"] == cat)).sum())
        pred = int((df["DeepSeek"] == cat).sum())
        ref = int((df["人工统一"] == cat).sum())
        rows.append(
            {
                "类别": cat,
                "DeepSeek预测数": pred,
                "人工统一数": ref,
                "命中数": tp,
                "precision": tp / pred if pred else None,
                "recall": tp / ref if ref else None,
                "F1": 2 * tp / (pred + ref) if (pred + ref) else None,
            }
        )
    return pd.DataFrame(rows)


def pct(v):
    return "N/A" if pd.isna(v) else f"{v * 100:.1f}%"


def main():
    ds = pd.read_excel(DS_PATH, sheet_name=0)
    human = pd.read_excel(HUMAN_PATH, sheet_name="对比")

    ds = ds.rename(columns={ds.columns[0]: "弹幕内容", ds.columns[1]: "DeepSeek_原始"})
    human = human.rename(columns={"统一后category": "人工统一"})

    if len(ds) != 200 or len(human) != 200:
        raise ValueError(f"Expected 200 rows, got ds={len(ds)}, human={len(human)}")

    ds["弹幕内容"] = ds["弹幕内容"].astype(str).str.strip()
    human["弹幕内容"] = human["弹幕内容"].astype(str).str.strip()
    ds["DeepSeek"] = ds["DeepSeek_原始"].apply(norm)
    human["人工统一"] = human["人工统一"].apply(norm)

    # Same 200-item set in row order; avoid duplicate-content merge inflation.
    order_match = (ds["弹幕内容"].values == human.sort_values("序号")["弹幕内容"].values).sum()
    human = human.sort_values("序号").reset_index(drop=True)
    ds = ds.reset_index(drop=True)

    df = human[["序号", "time_sec", "弹幕内容", "人工统一"]].copy()
    df["DeepSeek"] = ds["DeepSeek"].values
    df["DeepSeek_原始"] = ds["DeepSeek_原始"].values
    df["内容顺序一致"] = df["弹幕内容"] == ds["弹幕内容"].values
    df["一致"] = df["人工统一"] == df["DeepSeek"]

    missing = int(df["DeepSeek"].isna().sum())
    order_mismatch = int((~df["内容顺序一致"]).sum())
    valid = df["DeepSeek"].notna() & df["人工统一"].notna()
    agree = int((df.loc[valid, "人工统一"] == df.loc[valid, "DeepSeek"]).sum())
    total = 200
    kappa = cohen_kappa_score(df.loc[valid, "人工统一"], df.loc[valid, "DeepSeek"])

    metric_df = metrics(df.dropna(subset=["DeepSeek"]))
    conf = pd.crosstab(df["人工统一"], df["DeepSeek"], dropna=False)

    dist_rows = []
    for src, col in [("人工统一", "人工统一"), ("DeepSeek", "DeepSeek")]:
        for cat, cnt in df[col].value_counts().items():
            dist_rows.append({"来源": src, "类别": cat, "条数": int(cnt), "占比": cnt / total})
    dist = pd.DataFrame(dist_rows)

    with pd.ExcelWriter(OUT_XLSX, engine="openpyxl") as writer:
        df.to_excel(writer, sheet_name="逐条对比", index=False)
        pd.DataFrame(
            [{"指标": "一致条数", "值": agree}, {"指标": "总条数", "值": total}, {"指标": "一致率", "值": agree / total}, {"指标": "Cohen_kappa", "值": kappa}, {"指标": "未匹配DeepSeek", "值": missing}]
        ).to_excel(writer, sheet_name="总体", index=False)
        dist.to_excel(writer, sheet_name="类别分布", index=False)
        metric_df.to_excel(writer, sheet_name="各类别指标", index=False)
        conf.to_excel(writer, sheet_name="混淆矩阵")
        df[~df["一致"]].to_excel(writer, sheet_name="错分", index=False)

    lines = []
    lines.append("DeepSeek (ds.xlsx) vs 人工统一后category (人工打分(1).xlsx 对比页)")
    lines.append(f"总条数: {total}")
    lines.append(f"行序与弹幕内容一致: {total - order_mismatch}/{total}")
    lines.append(f"一致: {agree}/{total} ({agree/total:.1%})")
    lines.append(f"Cohen's kappa: {kappa:.4f}")
    lines.append("")
    lines.append("【类别分布】")
    for src in ["人工统一", "DeepSeek"]:
        lines.append(f"  {src}")
        sub = dist[dist["来源"] == src].sort_values("条数", ascending=False)
        for _, r in sub.iterrows():
            lines.append(f"    {r['类别']}: {int(r['条数'])} ({r['占比']:.1%})")
    lines.append("")
    lines.append("【各类别 F1】")
    for _, r in metric_df.sort_values("F1", ascending=False).iterrows():
        lines.append(
            f"  {r['类别']}: 命中{int(r['命中数'])}, DS{int(r['DeepSeek预测数'])}/人工{int(r['人工统一数'])}, "
            f"P{pct(r['precision'])}, R{pct(r['recall'])}, F1{pct(r['F1'])}"
        )
    lines.append("")
    lines.append("【主要错分方向】")
    wrongs = []
    for h in conf.index:
        for a in conf.columns:
            c = int(conf.loc[h, a])
            if c and h != a:
                wrongs.append((c, h, a))
    for c, h, a in sorted(wrongs, key=lambda x: x[0], reverse=True)[:10]:
        lines.append(f"  人工 {h} -> DS {a}: {c}")

    OUT_TXT.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"\nSaved: {OUT_XLSX}")


if __name__ == "__main__":
    main()
