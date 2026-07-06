#!/usr/bin/env python3
"""Compare danmaku category labels across multiple scoring files."""
import pandas as pd
from pathlib import Path

FILES = {
    "DS_score": (Path(r"c:\Users\33326\Desktop\danmaku\DS_score.xlsx"), "DS"),
    "完整数据": (Path(r"c:\Users\33326\Downloads\弹幕打分_完整数据.xlsx"), "弹幕打分数据"),
    "gemini3.5": (Path(r"c:\Users\33326\Downloads\geimini 3.5.xlsx"), "gemini-total"),
    "arou_info_table": (Path(r"c:\Users\33326\Downloads\danmaku_arou_info_table.xlsx"), "Data"),
    "弹幕打分csv": (Path(r"c:\Users\33326\Downloads\弹幕打分.csv"), None),
}


def norm_cat(c):
    if pd.isna(c):
        return None
    s = str(c).strip().lower()
    if s in ("arou", "arousal"):
        return "arousal"
    if s in ("info", "information"):
        return "information"
    if s == "inert":
        return "inert"
    if s in ("mix", "mixture"):
        return "mixture"
    return s


def load_all():
    out = {}
    df = pd.read_excel(FILES["DS_score"][0], sheet_name=FILES["DS_score"][1])
    df["time"] = df["timeSeconds"].astype(float).round(3)
    df["text"] = df["text"].astype(str).str.strip()
    df["cat"] = df["category"].map(norm_cat)
    out["DS_score"] = df

    df = pd.read_excel(FILES["完整数据"][0], sheet_name=FILES["完整数据"][1], header=1)
    df.columns = ["time", "text", "arou", "info", "D", "cat_raw", "length"]
    df["time"] = pd.to_numeric(df["time"], errors="coerce").round(3)
    df["text"] = df["text"].astype(str).str.strip()
    df["cat"] = df["cat_raw"].map(norm_cat)
    out["完整数据"] = df

    df = pd.read_excel(FILES["gemini3.5"][0], sheet_name=FILES["gemini3.5"][1])
    df["time"] = df["时间(秒)"].astype(float).round(3)
    df["text"] = df["弹幕文本"].astype(str).str.strip()
    df["arou"] = df["情绪唤醒强度(arou)"]
    df["info"] = df["信息价值(info)"]
    df["cat"] = df["分类"].map(norm_cat)
    out["gemini3.5"] = df

    df = pd.read_excel(FILES["arou_info_table"][0], sheet_name=FILES["arou_info_table"][1])
    df["text"] = df["text"].astype(str).str.strip()
    df["cat"] = df["category"].map(norm_cat)
    out["arou_info_table"] = df

    df = pd.read_csv(FILES["弹幕打分csv"][0], encoding="utf-8-sig", on_bad_lines="skip")
    df["time"] = pd.to_numeric(df.iloc[:, 0], errors="coerce").round(3)
    df["text"] = df.iloc[:, 1].astype(str).str.strip()
    df["arou"] = pd.to_numeric(df.iloc[:, 2], errors="coerce")
    df["info"] = pd.to_numeric(df.iloc[:, 3], errors="coerce")
    df["cat"] = df.iloc[:, 6].map(norm_cat)
    out["弹幕打分csv"] = df

    return out


def main():
    sources = load_all()
    base = sources["DS_score"][["time", "text"]].copy()
    base["key"] = base["time"].astype(str) + "|||" + base["text"]
    names = list(sources.keys())

    for name, df in sources.items():
        if name == "arou_info_table":
            base[f"cat_{name}"] = df["cat"].values
            base[f"arou_{name}"] = df["arou"].values
            base[f"info_{name}"] = df["info"].values
        else:
            d = df.copy()
            d["key"] = d["time"].astype(str) + "|||" + d["text"]
            m = d[["key", "cat", "arou", "info"]].rename(
                columns={"cat": f"cat_{name}", "arou": f"arou_{name}", "info": f"info_{name}"}
            )
            base = base.merge(m, on="key", how="left")

    cat_cols = [c for c in base.columns if c.startswith("cat_")]

    def disagree(row):
        vals = [row[c] for c in cat_cols if pd.notna(row[c])]
        return len(set(vals)) > 1

    base["disagree"] = base.apply(disagree, axis=1)
    diff = base[base["disagree"]].sort_values("time")

    print("=== Pairwise category disagreement ===")
    for i, a in enumerate(names):
        for b in names[i + 1 :]:
            ca, cb = f"cat_{a}", f"cat_{b}"
            n = ((base[ca].notna()) & (base[cb].notna()) & (base[ca] != base[cb])).sum()
            print(f"  {a} vs {b}: {n}")

    print(f"\nTotal with any mismatch: {len(diff)}/200\n")

    lines = []
    for idx, (_, r) in enumerate(diff.iterrows(), 1):
        block = [f"#{idx}  {r['time']}s  「{r['text']}」"]
        for n in names:
            c = r.get(f"cat_{n}")
            ar = r.get(f"arou_{n}")
            inf = r.get(f"info_{n}")
            if pd.notna(c):
                block.append(f"  {n}: {c}  (arou={ar}, info={inf})")
        lines.append("\n".join(block))

    report = "\n\n".join(lines)
    out_path = Path(r"c:\Users\33326\Downloads\category_disagreements_report.txt")
    out_path.write_text(report, encoding="utf-8")
    base[base["disagree"]].to_csv(
        Path(r"c:\Users\33326\Downloads\category_disagreements.csv"), index=False, encoding="utf-8-sig"
    )
    print(report)
    print(f"\nSaved: {out_path}")


if __name__ == "__main__":
    main()
