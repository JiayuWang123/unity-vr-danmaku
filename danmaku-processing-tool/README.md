# Danmaku Processing Tool

Local Bilibili XML danmaku processing tool for the SURF VR sports danmaku project.

## What It Does

- Upload or drag a `.xml` file in a local web page.
- Export raw review JSON with only `time_sec`, `time_mmss`, and `text_raw`.
- Export normalized JSON with full XML-derived fields by default.
- Optionally run stage-1 obvious-noise filtering.
- Optionally export filtered and noise JSON files.
- Optionally generate a time-stratified random sample from raw minimal, normalized, or filtered records.

Generated files are written under:

```text
Danmu/json/
```

The raw XML file is not deleted or overwritten.

## Run The Web Tool

From this directory:

```powershell
python .\Script\Codes\run_danmaku_web_tool.py
```

Then open:

```text
http://127.0.0.1:8000
```

In PowerShell, type the URL into a browser address bar, or run:

```powershell
start http://127.0.0.1:8000
```

## CLI Examples

Default normalized + filtered + noise outputs:

```powershell
python .\Script\Codes\process_xml_danmaku.py .\Danmu\Qatar.xml --video-id Qatar --output-dir .\Danmu\json
```

Manual review sample from raw minimal records:

```powershell
python .\Script\Codes\process_xml_danmaku.py .\Danmu\Qatar.xml `
  --video-id Qatar `
  --output-dir .\Danmu\json `
  --raw-minimal `
  --sample `
  --sample-source raw_minimal `
  --sample-size 200 `
  --sample-strata 10 `
  --sample-seed 20260630
```

Minimal normalized profile:

```powershell
python .\Script\Codes\process_xml_danmaku.py .\Danmu\Qatar.xml `
  --video-id Qatar `
  --output-dir .\Danmu\json `
  --normalized-profile minimal
```

## Output Profiles

Full normalized records keep:

```text
id, video_id, source_file, index, time_sec, time_mmss, text_raw, text_norm,
mode, mode_name, font_size, color_decimal, color_hex, created_at_unix,
pool, user_hash, danmaku_id, weight
```

Minimal normalized records keep:

```text
id, video_id, source_file, index, time_sec, time_mmss, text_raw, text_norm,
mode, color_hex, danmaku_id
```

Raw review records keep:

```text
time_sec, time_mmss, text_raw
```

Noise records add:

```text
noise_stage, noise_reason
```

All JSON outputs are top-level arrays.
