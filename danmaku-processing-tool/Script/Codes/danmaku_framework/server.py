#!/usr/bin/env python3
"""Local HTTP server for XML danmaku upload and processing."""

from __future__ import annotations

import argparse
import json
import mimetypes
import re
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlparse

from .pipeline import PipelineOptions, run_pipeline_for_bytes, sanitize_name


WORKSPACE_ROOT = Path(__file__).resolve().parents[3]
STATIC_DIR = Path(__file__).resolve().parent / "static"
DEFAULT_OUTPUT_DIR = WORKSPACE_ROOT / "Danmu" / "json"

BOUNDARY_RE = re.compile(r'boundary=(?:"([^"]+)"|([^;]+))', re.IGNORECASE)
DISPOSITION_PARAM_RE = re.compile(r';\s*([^=]+)="?([^";]+)"?')


def parse_multipart_form(content_type: str, body: bytes) -> tuple[dict[str, str], dict[str, tuple[str, bytes]]]:
    """Parse the small multipart/form-data payload produced by the local UI."""
    match = BOUNDARY_RE.search(content_type)
    if not match:
        raise ValueError("Missing multipart boundary.")

    boundary = (match.group(1) or match.group(2)).encode("utf-8")
    delimiter = b"--" + boundary
    fields: dict[str, str] = {}
    files: dict[str, tuple[str, bytes]] = {}

    for part in body.split(delimiter):
        part = part.strip()
        if not part or part == b"--":
            continue
        if part.endswith(b"--"):
            part = part[:-2].strip()
        if b"\r\n\r\n" not in part:
            continue

        header_blob, content = part.split(b"\r\n\r\n", 1)
        content = content.rstrip(b"\r\n")
        headers = header_blob.decode("utf-8", errors="replace").split("\r\n")
        disposition = ""
        for header in headers:
            if header.lower().startswith("content-disposition:"):
                disposition = header.split(":", 1)[1].strip()
                break
        if not disposition:
            continue

        params = {key.lower(): value for key, value in DISPOSITION_PARAM_RE.findall(disposition)}
        name = params.get("name", "")
        filename = params.get("filename", "")
        if not name:
            continue
        if filename:
            files[name] = (Path(filename).name, content)
        else:
            fields[name] = content.decode("utf-8", errors="replace")

    return fields, files


def field_bool(fields: dict[str, str], name: str, default: bool = False) -> bool:
    if name not in fields:
        return default
    return fields[name].strip().lower() in {"1", "true", "yes", "on"}


def field_int(fields: dict[str, str], name: str, default: int) -> int:
    value = fields.get(name, "").strip()
    if not value:
        return default
    return int(value)


def options_from_fields(fields: dict[str, str]) -> PipelineOptions:
    return PipelineOptions(
        export_raw_minimal=field_bool(fields, "export_raw_minimal", False),
        normalize=field_bool(fields, "normalize", True),
        normalized_profile=fields.get("normalized_profile", "full").strip() or "full",
        filter_enabled=field_bool(fields, "filter_enabled", True),
        export_filter_outputs=field_bool(fields, "export_filter_outputs", True),
        sample_enabled=field_bool(fields, "sample_enabled", False),
        sample_source=fields.get("sample_source", "filtered").strip() or "filtered",
        sample_size=field_int(fields, "sample_size", 200),
        sample_strata=field_int(fields, "sample_strata", 10),
        sample_seed=field_int(fields, "sample_seed", 20260630),
    )


class DanmakuRequestHandler(BaseHTTPRequestHandler):
    server_version = "DanmakuFramework/1.0"

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path in ("", "/"):
            self.serve_file(STATIC_DIR / "index.html", "text/html; charset=utf-8")
            return
        if parsed.path.startswith("/static/"):
            requested = STATIC_DIR / unquote(parsed.path.removeprefix("/static/"))
            self.serve_static(requested)
            return
        if parsed.path.startswith("/outputs/"):
            filename = Path(unquote(parsed.path.removeprefix("/outputs/"))).name
            self.serve_output(DEFAULT_OUTPUT_DIR / filename)
            return
        self.send_json({"error": "Not found"}, HTTPStatus.NOT_FOUND)

    def do_POST(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path != "/api/process":
            self.send_json({"error": "Not found"}, HTTPStatus.NOT_FOUND)
            return

        content_type = self.headers.get("Content-Type", "")
        if not content_type.startswith("multipart/form-data"):
            self.send_json({"error": "Please upload XML as multipart/form-data."}, HTTPStatus.BAD_REQUEST)
            return

        try:
            content_length = int(self.headers.get("Content-Length", "0"))
            body = self.rfile.read(content_length)
            fields, files = parse_multipart_form(content_type, body)
            if "xml_file" not in files:
                self.send_json({"error": "No XML file uploaded."}, HTTPStatus.BAD_REQUEST)
                return

            filename, xml_bytes = files["xml_file"]
            if not filename.lower().endswith(".xml"):
                self.send_json({"error": "Please upload a .xml file."}, HTTPStatus.BAD_REQUEST)
                return

            raw_video_id = fields.get("video_id", "").strip()
            video_id = sanitize_name(raw_video_id) if raw_video_id else ""
            options = options_from_fields(fields)
            result = run_pipeline_for_bytes(xml_bytes, filename, DEFAULT_OUTPUT_DIR, video_id, options)
        except Exception as exc:  # noqa: BLE001 - user-facing local tool.
            self.send_json({"error": f"Failed to process XML: {exc}"}, HTTPStatus.BAD_REQUEST)
            return

        response = result.to_response(WORKSPACE_ROOT)
        response["downloads"] = {
            key: f"/outputs/{Path(path).name}" for key, path in response["files"].items()
        }
        self.send_json(response)

    def serve_static(self, path: Path) -> None:
        try:
            resolved = path.resolve()
            if STATIC_DIR.resolve() not in resolved.parents and resolved != STATIC_DIR.resolve():
                self.send_json({"error": "Invalid static path."}, HTTPStatus.BAD_REQUEST)
                return
        except OSError:
            self.send_json({"error": "Invalid static path."}, HTTPStatus.BAD_REQUEST)
            return
        content_type = mimetypes.guess_type(str(path))[0] or "application/octet-stream"
        self.serve_file(path, content_type)

    def serve_output(self, path: Path) -> None:
        try:
            resolved = path.resolve()
            if DEFAULT_OUTPUT_DIR.resolve() not in resolved.parents and resolved != DEFAULT_OUTPUT_DIR.resolve():
                self.send_json({"error": "Invalid output path."}, HTTPStatus.BAD_REQUEST)
                return
        except OSError:
            self.send_json({"error": "Invalid output path."}, HTTPStatus.BAD_REQUEST)
            return
        self.serve_file(path, "application/json; charset=utf-8", as_attachment=True)

    def serve_file(self, path: Path, content_type: str, as_attachment: bool = False) -> None:
        if not path.exists() or not path.is_file():
            self.send_json({"error": "File not found."}, HTTPStatus.NOT_FOUND)
            return
        data = path.read_bytes()
        self.send_response(HTTPStatus.OK)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        if as_attachment:
            self.send_header("Content-Disposition", f'attachment; filename="{path.name}"')
        self.end_headers()
        self.wfile.write(data)

    def send_json(self, payload: dict, status: HTTPStatus = HTTPStatus.OK) -> None:
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, format: str, *args: object) -> None:
        print(f"{self.address_string()} - {format % args}")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Run the local XML danmaku processing web tool.")
    parser.add_argument("--host", default="127.0.0.1", help="Host to bind. Defaults to 127.0.0.1.")
    parser.add_argument("--port", type=int, default=8000, help="Port to bind. Defaults to 8000.")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    DEFAULT_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    server = ThreadingHTTPServer((args.host, args.port), DanmakuRequestHandler)
    print(f"Danmaku XML tool is running at http://{args.host}:{args.port}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("Server stopped.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
