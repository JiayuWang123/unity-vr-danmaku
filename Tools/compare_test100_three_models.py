#!/usr/bin/env python3
"""Build 100-item gold from 2+adjudication labels; compare 3 models."""

import re
from pathlib import Path

import pandas as pd
from sklearn.metrics import (
    accuracy_score,
    classification_report,
    cohen_kappa_score,
    confusion_matrix,
    f1_score,
    precision_score,
    recall_score,
)

HUMAN_PATH = Path(r"c:\Users\33326\Downloads\人工打分2（7.6）.xlsx")
MODEL_PATHS = {
    "DeepSeek": Path(r"c:\Users\33326\Desktop\ds100.xlsx"),
    "Gemini3.1Pro": Path(r"c:\Users\33326\Downloads\100条分类结果-gemini3.1pro.xlsx"),
    "GPT-5.5": Path(r"c:\Users\33326\Downloads\100gpt5.5.xlsx"),
}
OUT_XLSX = Path(r"c:\Users\33326\Desktop\danmaku\test100_three_models_vs_gold.xlsx")
OUT_TXT = Path(r"c:\Users\33326\Desktop\danmaku\test100_three_models_vs_gold.txt")


def norm(value):
    if pd.isna(value):
        return None
    s = str(value).strip().strip("。")
    if not s or s.lower() == "nan":
        return None
    s = re.sub(r"\s*\([^)]*\)\s*", " ", s).strip()
    s = s.replace("－", "-").replace("—", "-").replace("–", "-")
    s = s.replace(" - ", "-").replace(" -", "-").replace("- ", "-")
    for prefix in ("信息-", "惰性-", "情感-"):
        if s.startswith(prefix):
            s = s[len(prefix) :].strip()
            break
    mapping = {
        "盲点/剧透": "赛点/剧透",
        "其它无关弹幕": "无关弹幕",
        "其他无关弹幕": "无关弹幕",
        "无关": "无关弹幕",
        "社交互动/喊话/分享": "互动",
        "社交互动": "互动",
        "梗/搞笑内容": "梗",
        "梗/玩梗": "梗",
        "解说相关": "比赛内容/历史信息",
    }
    return mapping.get(s, s)


def clean_series(s):
    return s.apply(norm)


def load_model(path):
    xl = pd.ExcelFile(path)
    df = pd.read_excel(path, sheet_name=xl.sheet_names[0])
    content_col = df.columns[0]
    cat_col = df.columns[1]
    return pd.DataFrame(
        {
            "弹幕内容": df[content_col].astype(str).str.strip(),
            "pred_raw": df[cat_col],
            "pred": df[cat_col].apply(norm),
        }
    )


def build_gold(human_df):
    r1 = clean_series(human_df.iloc[:, 5])
    r2 = clean_series(human_df.iloc[:, 6])
    adj = clean_series(human_df.iloc[:, 7])
    agree = r1 == r2
    gold = r1.where(agree, adj)
    source = pd.Series(["两人一致"] * len(human_df))
    source.loc[~agree] = "第三人仲裁"
    return gold, r1, r2, adj, agree, source


def per_class_df(y_true, y_pred, model_name):
    labels = sorted(set(y_true) | set(y_pred))
    rows = []
    for cat in labels:
        tp = int(((y_true == cat) & (y_pred == cat)).sum())
        pred_n = int((y_pred == cat).sum())
        true_n = int((y_true == cat).sum())
        p = tp / pred_n if pred_n else None
        rec = tp / true_n if true_n else None
        f1 = 2 * tp / (pred_n + true_n) if (pred_n + true_n) else None
        rows.append(
            {
                "模型": model_name,
                "类别": cat,
                "gold条数": true_n,
                "预测条数": pred_n,
                "命中TP": tp,
                "precision": p,
                "recall": rec,
                "F1": f1,
            }
        )
    return pd.DataFrame(rows)


def main():
    human = pd.read_excel(HUMAN_PATH, sheet_name=0)
    human = human.sort_values("序号").reset_index(drop=True)
    gold, r1, r2, adj, agree_two, gold_source = build_gold(human)

    base = pd.DataFrame(
        {
            "序号": human["序号"],
            "time_sec": human["time_sec"],
            "弹幕内容": human["弹幕内容"].astype(str).str.strip(),
            "人工1": r1,
            "人工2": r2,
            "两人是否一致": agree_two,
            "第三人仲裁": adj,
            "gold来源": gold_source,
            "人工统一gold": gold,
        }
    )

    kappa_12 = cohen_kappa_score(r1, r2)
    agree_rate_12 = agree_two.mean()

    all_class = []
    all_conf = []
    per_model_summary = []

    for model_name, path in MODEL_PATHS.items():
        m = load_model(path).reset_index(drop=True)
        if len(m) != 100:
            raise ValueError(f"{model_name} rows={len(m)}")
        content_match = (base["弹幕内容"].values == m["弹幕内容"].values).sum()

        base[f"{model_name}_原始"] = m["pred_raw"].values
        base[f"{model_name}"] = m["pred"].values
        base[f"{model_name}_一致"] = base["人工统一gold"] == base[f"{model_name}"]

        y_true = base["人工统一gold"]
        y_pred = base[f"{model_name}"]

        acc = accuracy_score(y_true, y_pred)
        kappa = cohen_kappa_score(y_true, y_pred)
        macro_p = precision_score(y_true, y_pred, average="macro", zero_division=0)
        macro_r = recall_score(y_true, y_pred, average="macro", zero_division=0)
        macro_f1 = f1_score(y_true, y_pred, average="macro", zero_division=0)
        weighted_f1 = f1_score(y_true, y_pred, average="weighted", zero_division=0)

        per_model_summary.append(
            {
                "模型": model_name,
                "内容顺序一致条数": content_match,
                "Accuracy一致率": acc,
                "一致条数": int((y_true == y_pred).sum()),
                "Cohen_kappa_vs_gold": kappa,
                "Macro_Precision": macro_p,
                "Macro_Recall": macro_r,
                "Macro_F1": macro_f1,
                "Weighted_F1": weighted_f1,
            }
        )

        all_class.append(per_class_df(y_true, y_pred, model_name))
        cm = pd.DataFrame(
            confusion_matrix(y_true, y_pred, labels=sorted(y_true.unique())),
            index=[f"gold_{x}" for x in sorted(y_true.unique())],
            columns=[f"pred_{x}" for x in sorted(y_true.unique())],
        )
        cm.insert(0, "模型", model_name)
        all_conf.append(cm)

    summary_df = pd.DataFrame(per_model_summary).sort_values("Accuracy一致率", ascending=False)
    class_df = pd.concat(all_class, ignore_index=True)

    gold_dist = gold.value_counts().rename_axis("类别").reset_index(name="gold条数")
    gold_dist["gold占比"] = gold_dist["gold条数"] / 100

    with pd.ExcelWriter(OUT_XLSX, engine="openpyxl") as writer:
        base.to_excel(writer, sheet_name="逐条对比", index=False)
        summary_df.to_excel(writer, sheet_name="模型总体指标", index=False)
        class_df.to_excel(writer, sheet_name="各类别P_R_F1", index=False)
        gold_dist.to_excel(writer, sheet_name="gold分布", index=False)
        pd.DataFrame(
            [
                {"指标": "两人一致率", "值": agree_rate_12, "条数": int(agree_two.sum())},
                {"指标": "两人Cohen_kappa", "值": kappa_12, "条数": ""},
                {"指标": "第三人仲裁", "值": int((~agree_two).sum()), "条数": ""},
            ]
        ).to_excel(writer, sheet_name="人工标注过程", index=False)
        for model_name in MODEL_PATHS:
            wrong = base[~base[f"{model_name}_一致"]]
            wrong.to_excel(writer, sheet_name=f"错分_{model_name}", index=False)

    lines = []
    lines.append("100条 test：人工 gold（两人一致+第三人仲裁）vs 三模型")
    lines.append(f"gold构建: 一致{int(agree_two.sum())}条取两人相同; 不一致{int((~agree_two).sum())}条取第三人仲裁列")
    lines.append(f"两人Percent agreement: {agree_rate_12:.1%}, Cohen kappa: {kappa_12:.4f}")
    lines.append("")
    lines.append("【模型总体】")
    for _, r in summary_df.iterrows():
        lines.append(
            f"  {r['模型']}: Acc={r['Accuracy一致率']:.1%} ({int(r['一致条数'])}/100), "
            f"kappa={r['Cohen_kappa_vs_gold']:.3f}, Macro-F1={r['Macro_F1']:.3f}, "
            f"Macro-P={r['Macro_Precision']:.3f}, Macro-R={r['Macro_Recall']:.3f}, Weighted-F1={r['Weighted_F1']:.3f}"
        )
    lines.append("")
    lines.append("【各类别 F1】")
    for model in MODEL_PATHS:
        lines.append(f"  {model}")
        sub = class_df[class_df["模型"] == model].sort_values("F1", ascending=False)
        for _, row in sub.iterrows():
            def fmt(v):
                return f"{v:.1%}" if pd.notna(v) else "N/A"

            lines.append(
                f"    {row['类别']}: TP={int(row['命中TP'])}, gold={int(row['gold条数'])}, "
                f"pred={int(row['预测条数'])}, P={fmt(row['precision'])}, "
                f"R={fmt(row['recall'])}, F1={fmt(row['F1'])}"
            )

    metric_guide = """
【指标说明 & 论文用途】
1. Percent agreement（两人一致率）: 人工1与人工2相同比例。Methods中报告标注过程与IAA。
2. Cohen's kappa（两人）: 扣除偶然的两人工一致度。Methods/Results标注信度。
3. Accuracy（一致率/准确率）: 模型预测与gold完全相同比例。Results主指标，=test accuracy。
4. Cohen's kappa（模型vs gold）: 模型与gold的chance-corrected一致。Results，可与人工kappa对照。
5. Precision（精确率/P）: 模型判为某类的样本中真正属于该类的比例。分析某类是否乱判过多。
6. Recall（召回率/R）: gold中某类被模型正确找出的比例。分析某类是否漏判。
7. F1: P和R的调和平均。Results表格按类报告，Discussion解释弱类。
8. Macro-F1: 各类F1算术平均，小类与大类权重相同。类别不均衡时必报。
9. Weighted-F1: 按gold类频加权F1，更接近整体Acc。Results补充。
10. 混淆矩阵: 错分方向（如球员->比赛内容）。Discussion/错误分析图。
"""
    lines.append(metric_guide)
    OUT_TXT.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"\nSaved: {OUT_XLSX}")


if __name__ == "__main__":
    main()
