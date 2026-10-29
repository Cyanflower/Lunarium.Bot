#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
旧工单数据迁移工具：old-data/ -> storage/{archive,tickets}/
=========================================================

旧版 bot 的数据布局::

    old-data/ticket-0009/
    ├── db.json                 工单元数据
    ├── ticket-0009.html        归档 HTML（base64 内联头像与图片）
    ├── origin-image/           图片原件
    └── file/                   其他附件原件

新版 bot 只认::

    storage/archive/ticket-00009/ticket.json    ← 必须存在，LoadAny 靠它
    storage/archive/ticket-00009/archive.zip    ← 存在且非空则直接复用，不重新打包
    storage/archive/ticket-00009/database.zip   ← 同上（「获取数据库原件」）

所以本工具给每张旧工单（**包括 db.json 里还停在 open/closed 的 12 张**）产出
`ticket.json` + `archive.zip`（附件原件全在里面），**不保留解压后的目录**，
磁盘占用不会翻倍。

那 12 张只是旧 bot 状态没写完，对应频道早已不存在，
归档后 `Status` 一律为 `Archived`；需要恢复原状态时加 `--keep-active`。

`database.zip` 只打包元数据（`ticket.json` + `legacy/db.json`，约 2 KB），
不再复刻一遍附件——旧工单的「获取数据库原件」拿到的是数据而非原件，
这是迁移时明确的约定；要看附件请取 `archive.zip`。

base64 处理
-----------
旧 HTML 里 97% 的体积是 `data:image/...;base64,` 内联数据，本工具全部改成路径引用：

* 头像（CSS 变量 `--avatar_N`）：解码后写入 zip 内的 `assets/avatar_<sha>.webp`。
* 消息图片（`<img src="data:...">`）：`title="原文件: X (WebP压缩)"` 里带着原始文件名，
  能在 `origin-image/` 按名对上原件时**直接引用原件**（零额外空间，且保留最高画质）；
  对不上的才解码写入 `assets/img_<sha>.webp`。
* 兜底：任何漏网的 `data:image` 都会解码成 `assets/inline_<sha>.*`。

附件处理
--------
* **PNG → JPEG（质量 95）**：铺白底后转码，转出来比原图大的（小图标）保留原 PNG，
  动图（APNG）也保留。HTML 里指向 `origin-image/x.png` 的引用会同步改成 `x.jpg`。
  与新版 `TicketArchiveService.TryConvertToJpegAsync` 的行为一致。
* `--drop-archives <工单号>`：丢掉指定工单里的压缩包（zip/7z/rar/tar/gz… 以及
  `.001` `.002` 分卷），用于清理个别占了几 GB 的分卷包。

图片与原件按「原始文件名 + 下载时间戳」配对：旧 bot 保存时会在原名后追加
`_YYYYMMDD_HHMMSS`，剥掉该后缀即可还原原名。

用法
----
    python3 tools/legacy_migrate.py --dry-run          # 只统计，不落盘
    python3 tools/legacy_migrate.py --only 900         # 单张工单试跑
    python3 tools/legacy_migrate.py --limit 20         # 前 20 张
    python3 tools/legacy_migrate.py --drop-archives 879 --force
    python3 tools/legacy_migrate.py                    # 全量
"""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import hashlib
import json
import os
import re
import shutil
import sys
import tempfile
import threading
import time
import zipfile
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor, as_completed
from html import escape as html_escape
from typing import Iterable
from urllib.parse import quote

from PIL import Image

PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_SOURCE = os.path.join(PROJECT_ROOT, "old-data")
DEFAULT_DEST = os.path.join(PROJECT_ROOT, "storage")
DEFAULT_GUILD_ID = 1291925535324110879

# 旧 bot 的 created_at / last_updated 是不带偏移的本地时间
DISPLAY_TZ = dt.timezone(dt.timedelta(hours=8), "Asia/Shanghai")

DIR_ID_RE = re.compile(r"^ticket-(\d+)$")
HTML_NAME_RE = re.compile(r"^ticket-(\d+)\.html$")
STAMP_RE = re.compile(r"_(\d{8}_\d{6})$")

CSS_AVATAR_RE = re.compile(
    r'--avatar_(?P<idx>\d+):\s*url\(data:image/(?P<fmt>[A-Za-z0-9.+-]+);base64,(?P<payload>[A-Za-z0-9+/=]+)\)'
)
IMG_DATA_RE = re.compile(
    r'<img\b(?P<pre>[^>]*?)src="data:image/(?P<fmt>[A-Za-z0-9.+-]+);base64,(?P<payload>[A-Za-z0-9+/=]+)"(?P<post>[^>]*)>'
)
ANY_DATA_RE = re.compile(
    r'data:image/(?P<fmt>[A-Za-z0-9.+-]+);base64,(?P<payload>[A-Za-z0-9+/=]+)'
)
ORIG_TITLE_RE = re.compile(r'title="原文件:\s*(?P<name>.+?)\s*\(WebP压缩\)"')
TITLE_TAG_RE = re.compile(r"<title>(?P<text>[^<]*)</title>")

EXT_MAP = {
    "webp": ".webp",
    "jpeg": ".jpg",
    "jpg": ".jpg",
    "png": ".png",
    "gif": ".gif",
    "avif": ".avif",
    "bmp": ".bmp",
    "svg+xml": ".svg",
}

# 已经是压缩格式的附件直接 store，省掉无意义的 deflate
STORED_EXT = {
    ".webp", ".jpg", ".jpeg", ".png", ".gif", ".avif", ".bmp", ".mp4", ".mkv",
    ".webm", ".mp3", ".flac", ".ogg", ".opus", ".zip", ".7z", ".rar", ".gz",
    ".xz", ".bz2", ".zst", ".pdf", ".docx", ".xlsx", ".pptx", ".apk",
}

STORED_SIZE_THRESHOLD = 2 * 1024 * 1024

PNG_EXT = {".png", ".apng"}

# 视为“压缩包”的扩展名（含 7z/zip 分卷 .001 .002 ...）
ARCHIVE_EXT = {".zip", ".7z", ".rar", ".tar", ".gz", ".xz", ".bz2", ".zst"}

DEFAULT_PNG_QUALITY = 95


# --------------------------------------------------------------------------
# 小工具
# --------------------------------------------------------------------------
def parse_ticket_id(name: str) -> int | None:
    m = DIR_ID_RE.match(name)
    return int(m.group(1)) if m else None


def stamp_key(filename: str) -> str:
    stem = os.path.splitext(filename)[0]
    m = STAMP_RE.search(stem)
    return m.group(1) if m else ""


def original_name(filename: str) -> str:
    """剥掉旧 bot 追加的 `_YYYYMMDD_HHMMSS` 后缀，还原上传时的原始文件名。"""
    stem, ext = os.path.splitext(filename)
    m = STAMP_RE.search(stem)
    return (stem[: m.start()] + ext) if m else filename


def parse_legacy_datetime(value) -> str | None:
    if not value or not isinstance(value, str):
        return None
    try:
        parsed = dt.datetime.fromisoformat(value)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=DISPLAY_TZ)
    return parsed.isoformat()


def iso_now() -> str:
    return dt.datetime.now(DISPLAY_TZ).isoformat(timespec="seconds")


def human(size: float) -> str:
    for unit in ("B", "KB", "MB", "GB"):
        if abs(size) < 1024 or unit == "GB":
            return f"{size:.1f}{unit}" if unit != "B" else f"{int(size)}B"
        size /= 1024
    return f"{size:.1f}GB"


# --------------------------------------------------------------------------
# ticket.json
# --------------------------------------------------------------------------
def build_ticket_json(db: dict, ticket_id: int, guild_id: int, keep_active: bool = False) -> dict:
    creator = db.get("complainant") or {}
    created_at = parse_legacy_datetime(db.get("created_at")) or iso_now()
    updated_at = parse_legacy_datetime(db.get("last_updated")) or created_at
    status = (db.get("status") or "").lower()

    def actor(record) -> dict | None:
        if not isinstance(record, dict) or not record.get("user_id"):
            return None
        return {
            "UserId": int(record["user_id"]),
            "DisplayName": record.get("display_name") or "",
            "UserName": record.get("username") or "",
        }

    closed_by = actor(db.get("closed_by"))
    archived_by = actor(db.get("archived_by"))

    migration_actor = {
        "UserId": 0,
        "DisplayName": "旧版数据迁移",
        "UserName": "legacy-migration",
    }

    # 旧数据没有"审核通过"记录；但如果 ApproveInfo 为空，Status 会推导成 Pending，
    # 启动对账会给它重发审核卡（AuditChannelId=0）。所以除了已拒绝的，一律补一条。
    approver = closed_by or archived_by or migration_actor
    closer = closed_by or approver
    archiver = archived_by or closer

    close_info = dict(closer, ClosedAt=updated_at)
    archive_info = dict(archiver, ArchivedAt=updated_at)

    # 旧库里非 archived 的 12 张只是状态没写完，频道早已经不在了。
    # 默认一律归入归档；--keep-active 时才按 db.json 的 status 还原。
    if keep_active and status != "archived":
        archive_info = None
        close_info = dict(closed_by, ClosedAt=updated_at) if closed_by else None

    return {
        "SchemaVersion": 1,
        "Id": ticket_id,
        "GuildId": guild_id,
        "ChannelId": int(db.get("channel_id") or 0),
        # 旧 bot 的状态卡发在它的 NotifierChannel 里，频道 ID 没有落盘，
        # 置 0 让新版跳过状态卡/审核卡的原地编辑与认领。
        "StatusChannelId": 0,
        "StatusMessageId": 0,
        "AuditChannelId": 0,
        "AuditMessageId": 0,
        "TicketType": "Complaint",
        "TicketCategory": "Unknown",
        "TicketCreator": {
            "UserId": int(creator.get("user_id") or 0),
            "DisplayName": creator.get("display_name") or "",
            "UserName": creator.get("username") or "",
        },
        "Content": db.get("complaint_content") or "",
        "SecondaryContent": None,
        "CreatedAt": created_at,
        "LastUpdatedAt": updated_at,
        "AutoApproveTime": None,
        "IsAutoApprove": False,
        "ApproveInfo": dict(approver, ApprovedAt=created_at),
        "RejectInfo": None,
        "CloseInfo": close_info,
        "ArchiveInfo": archive_info,
        "TicketMessages": None,
        "LLMSuggestion": None,
    }


# --------------------------------------------------------------------------
# HTML 重写
# --------------------------------------------------------------------------
def index_originals(ticket_dir: str) -> dict[str, list[str]]:
    """原始文件名 -> 该名字在 origin-image/ 下的所有落盘文件（按下载时间升序）。"""
    directory = os.path.join(ticket_dir, "origin-image")
    if not os.path.isdir(directory):
        return {}
    groups: dict[str, list[str]] = defaultdict(list)
    for name in os.listdir(directory):
        if os.path.isfile(os.path.join(directory, name)):
            groups[original_name(name)].append(name)
    for files in groups.values():
        files.sort(key=lambda f: (stamp_key(f), f))
    return groups


def rewrite_html(
    raw: bytes,
    ticket_dir: str,
    ticket_id: int,
    stats: Counter,
    rename: dict[str, str] | None = None,
) -> tuple[bytes, dict[str, bytes]]:
    rename = rename or {}
    text = raw.decode("utf-8-sig")
    assets: dict[str, bytes] = {}
    by_digest: dict[str, str] = {}

    def asset(prefix: str, fmt: str, payload: str) -> str:
        data = base64.b64decode(payload)
        digest = hashlib.sha256(data).hexdigest()
        name = by_digest.get(digest)
        if name is None:
            ext = EXT_MAP.get(fmt.lower(), "." + fmt.lower())
            name = f"assets/{prefix}_{digest[:16]}{ext}"
            by_digest[digest] = name
            assets[name] = data
            stats["asset_new"] += 1
        else:
            stats["asset_dedup"] += 1
        stats["asset_ref"] += 1
        return name

    # 1) 头像 CSS 变量
    def replace_avatar(m: re.Match) -> str:
        stats["b64_avatar_count"] += 1
        stats["b64_avatar_bytes"] += len(m.group("payload"))
        return f'--avatar_{m.group("idx")}: url({asset("avatar", m.group("fmt"), m.group("payload"))})'

    text = CSS_AVATAR_RE.sub(replace_avatar, text)

    # 2) 消息图片：优先复用 origin-image/ 里的原件
    originals = index_originals(ticket_dir)
    used: Counter = Counter()
    plan: list[str | None] = []
    for m in IMG_DATA_RE.finditer(text):
        target = None
        title = ORIG_TITLE_RE.search(m.group(0))
        if title:
            name = title.group("name")
            candidates = originals.get(name)
            if candidates and used[name] < len(candidates):
                arcname = "origin-image/" + candidates[used[name]]
                target = rename.get(arcname, arcname)
                used[name] += 1
                stats["img_reuse_original"] += 1
        plan.append(target)

    cursor = {"i": 0}

    def replace_img(m: re.Match) -> str:
        index = cursor["i"]
        cursor["i"] += 1
        stats["b64_img_bytes"] += len(m.group("payload"))
        target = plan[index] if index < len(plan) else None
        if target is None:
            stats["b64_img_count"] += 1
            target = asset("img", m.group("fmt"), m.group("payload"))
        return f'<img{m.group("pre")}src="{quote(target, safe="/")}"{m.group("post")}>'

    text = IMG_DATA_RE.sub(replace_img, text)

    # 3) 兜底：任何漏网的 data:image
    def replace_any(m: re.Match) -> str:
        stats["b64_other_count"] += 1
        stats["b64_other_bytes"] += len(m.group("payload"))
        return asset("inline", m.group("fmt"), m.group("payload"))

    text = ANY_DATA_RE.sub(replace_any, text)

    # 4) 标题里的旧 4 位编号统一成 5 位
    def replace_title(m: re.Match) -> str:
        return "<title>" + m.group("text").replace(
            f"ticket-{ticket_id:04d}", f"ticket-{ticket_id:05d}"
        ) + "</title>"

    text = TITLE_TAG_RE.sub(replace_title, text, count=1)

    return text.encode("utf-8"), assets


def build_stub_html(ticket_id: int, attachments: Iterable[str], ticket: dict | None = None) -> bytes:
    rows = "\n".join(f"        <li><code>{quote(name, safe='/')}</code></li>" for name in attachments)
    ticket = ticket or {}
    creator = ticket.get("TicketCreator") or {}
    content = html_escape(ticket.get("Content") or "")
    created = html_escape(ticket.get("CreatedAt") or "")
    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <title>ticket-{ticket_id:05d}</title>
    <style>
        body {{ margin: 0; padding: 24px; background: #313338; color: #dbdee1;
               font-family: "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif;
               line-height: 1.6; }}
        h1 {{ margin: 0 0 16px; font-size: 22px; color: #f2f3f5; }}
        dl {{ display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px;
              margin: 0 0 24px; font-size: 14px; color: #b5bac1; }}
        dt {{ color: #949ba4; }}
        .content {{ white-space: pre-wrap; word-break: break-word; padding: 12px 16px;
                    background: #2b2d31; border-left: 3px solid #5865f2; border-radius: 4px; }}
        ul {{ padding-left: 20px; }}
        code {{ color: #00a8fc; }}
        .note {{ color: #949ba4; font-size: 13px; }}
    </style>
</head>
<body>
    <h1>Ticket-{ticket_id:05d}</h1>
    <dl>
        <dt>创建时间</dt><dd>{created}</dd>
        <dt>提交人</dt><dd>{html_escape(creator.get('DisplayName') or '')}</dd>
    </dl>
    <div class="content">{content}</div>
    <h2>附件原件</h2>
    <p class="note">旧版 bot 未为该工单生成归档 HTML，此处仅列出本地留存的附件。</p>
    <ul>
{rows}
    </ul>
</body>
</html>
""".encode("utf-8")


# --------------------------------------------------------------------------
# zip 打包
# --------------------------------------------------------------------------
def zip_date_time(path: str) -> tuple[int, int, int, int, int, int]:
    stamp = time.localtime(os.stat(path).st_mtime)
    return (max(stamp.tm_year, 1980), stamp.tm_mon, stamp.tm_mday,
            stamp.tm_hour, stamp.tm_min, stamp.tm_sec)


def is_precompressed(path: str) -> bool:
    if os.path.splitext(path)[1].lower() in STORED_EXT:
        return True
    try:
        return os.path.getsize(path) >= STORED_SIZE_THRESHOLD
    except OSError:
        return False


def write_archive_zip(
    zip_path: str,
    ticket_json: bytes,
    db_json: bytes | None,
    html: bytes,
    assets: dict[str, bytes],
    attachments: list[tuple[str, str]],
) -> None:
    temp_path = zip_path + ".tmp"
    try:
        with zipfile.ZipFile(temp_path, "w", zipfile.ZIP_DEFLATED,
                             compresslevel=6, allowZip64=True) as zf:
            def add_bytes(name: str, data: bytes) -> None:
                info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = 0o644 << 16
                zf.writestr(info, data)

            add_bytes("index.html", html)
            add_bytes("ticket.json", ticket_json)
            if db_json is not None:
                add_bytes("legacy/db.json", db_json)

            for name, data in assets.items():
                add_bytes(name, data)

            for arcname, full in attachments:
                stored = is_precompressed(full)
                if stored:
                    zf.write(full, arcname, compress_type=zipfile.ZIP_STORED)
                else:
                    info = zipfile.ZipInfo(arcname, date_time=zip_date_time(full))
                    info.compress_type = zipfile.ZIP_DEFLATED
                    info.external_attr = 0o644 << 16
                    with open(full, "rb") as src, zf.open(info, "w") as dst:
                        shutil.copyfileobj(src, dst, 1024 * 1024)

        os.replace(temp_path, zip_path)
    except BaseException:
        try:
            os.remove(temp_path)
        except OSError:
            pass
        raise


def write_database_zip(path: str, ticket_json: bytes, db_json: bytes | None) -> None:
    """「数据库原件」只放元数据（ticket.json + 旧版 db.json），附件都在 archive.zip 里。

    新版 bot 对活跃工单的 database.zip 是“数据 + 附件原件”，旧工单不再复刻那份体积：
    附件原件在 archive.zip 里已经有一份，没必要存两遍。
    """
    temp_path = path + ".tmp"
    try:
        with zipfile.ZipFile(temp_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
            for name, data in (("ticket.json", ticket_json), ("legacy/db.json", db_json)):
                if data is None:
                    continue
                info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = 0o644 << 16
                zf.writestr(info, data)
        os.replace(temp_path, path)
    except BaseException:
        try:
            os.remove(temp_path)
        except OSError:
            pass
        raise


# --------------------------------------------------------------------------
# 单张工单
# --------------------------------------------------------------------------
class Stats:
    def __init__(self) -> None:
        self.totals: Counter = Counter()
        self.failures: list[tuple[int, str]] = []
        self.lock = threading.Lock()

    def merge(self, counter: Counter) -> None:
        with self.lock:
            self.totals.update(counter)

    def fail(self, ticket_id: int, message: str) -> None:
        with self.lock:
            self.failures.append((ticket_id, message))


def is_archive_like(filename: str) -> bool:
    ext = os.path.splitext(filename)[1].lower()
    if ext in ARCHIVE_EXT:
        return True
    # 7z/zip 分卷：.001 .002 ...
    return len(ext) == 4 and ext[1:].isdigit()


def collect_attachments(
    ticket_dir: str,
    drop_archives: bool = False,
    stats: Counter | None = None,
) -> list[tuple[str, str]]:
    out: list[tuple[str, str]] = []
    for sub in ("origin-image", "file"):
        base = os.path.join(ticket_dir, sub)
        if not os.path.isdir(base):
            continue
        for root, _dirs, files in os.walk(base):
            for name in sorted(files):
                full = os.path.join(root, name)
                if drop_archives and is_archive_like(name):
                    if stats is not None:
                        stats["archive_dropped_files"] += 1
                        stats["archive_dropped_bytes"] += os.path.getsize(full)
                    continue
                arcname = os.path.relpath(full, ticket_dir).replace(os.sep, "/")
                out.append((arcname, full))
    out.sort(key=lambda item: item[0])
    return out


def resolve_jpeg_name(arcname: str, taken: set[str]) -> str:
    stem = os.path.splitext(arcname)[0]
    index = 0
    while True:
        candidate = stem + ("" if index == 0 else f"_{index}") + ".jpg"
        if candidate.lower() not in taken:
            return candidate
        index += 1


def convert_png_to_jpeg(source: str, target: str, quality: int) -> bool:
    """白底铺平后存 JPEG；动图返回 False（保留原 PNG）。"""
    with Image.open(source) as image:
        if getattr(image, "n_frames", 1) > 1:
            return False
        if image.mode in ("RGBA", "LA") or (image.mode == "P" and "transparency" in image.info):
            rgba = image.convert("RGBA")
            canvas = Image.new("RGB", rgba.size, (255, 255, 255))
            canvas.paste(rgba, mask=rgba.getchannel("A"))
            out = canvas
        else:
            out = image.convert("RGB")
        out.save(target, "JPEG", quality=quality, optimize=True, subsampling=0)
    return True


def convert_pngs(
    attachments: list[tuple[str, str]],
    stats: Counter,
    quality: int,
    temp_root: str,
) -> tuple[str, dict[str, str], list[tuple[str, str]]]:
    """把附件里的 PNG 转成 JPEG，返回 (临时目录, 改名表, 最终附件列表)。"""
    taken = {arcname.lower() for arcname, _ in attachments}
    rename: dict[str, str] = {}
    result: list[tuple[str, str]] = []
    tmpdir = tempfile.mkdtemp(prefix="jpg-", dir=temp_root)

    for arcname, full in attachments:
        if os.path.splitext(full)[1].lower() not in PNG_EXT:
            result.append((arcname, full))
            continue

        stats["png_seen"] += 1
        original_size = os.path.getsize(full)
        target = resolve_jpeg_name(arcname, taken)
        out_path = os.path.join(tmpdir, hashlib.sha1(target.encode("utf-8")).hexdigest() + ".jpg")

        try:
            ok = convert_png_to_jpeg(full, out_path, quality)
        except Exception as exc:  # noqa: BLE001 - 单张失败只回退原件
            stats["png_failed"] += 1
            print(f"    PNG 转换失败，保留原件: {full}: {type(exc).__name__}: {exc}", file=sys.stderr)
            ok = False

        if not ok or not os.path.exists(out_path) or os.path.getsize(out_path) >= original_size:
            stats["png_kept"] += 1
            result.append((arcname, full))
            if os.path.exists(out_path):
                os.remove(out_path)
            continue

        taken.add(target.lower())
        rename[arcname] = target
        stats["png_converted"] += 1
        stats["png_bytes_in"] += original_size
        stats["png_bytes_out"] += os.path.getsize(out_path)
        result.append((target, out_path))

    return tmpdir, rename, result


def process_ticket(
    ticket_dir: str,
    ticket_id: int,
    dest_root: str,
    guild_id: int,
    force: bool,
    dry_run: bool,
    stats: Stats,
    drop_archives: bool = False,
    png_quality: int | None = DEFAULT_PNG_QUALITY,
    keep_active: bool = False,
) -> Counter:
    local: Counter = Counter()
    db_path = os.path.join(ticket_dir, "db.json")
    db = json.loads(open(db_path, "r", encoding="utf-8-sig").read()) if os.path.exists(db_path) else {}

    ticket_json_obj = build_ticket_json(db, ticket_id, guild_id, keep_active=keep_active)
    archived = ticket_json_obj["ArchiveInfo"] is not None
    ticket_json_bytes = (json.dumps(ticket_json_obj, ensure_ascii=False, indent=2) + "\n").encode("utf-8")

    folder = f"ticket-{ticket_id:05d}"
    target_dir = os.path.join(dest_root, "archive" if archived else "tickets", folder)
    local["tickets_archived" if archived else "tickets_active"] += 1

    if not archived:
        # 活跃工单：新版不读 zip，直接把附件摊进 attachment/legacy/
        if dry_run:
            return local
        os.makedirs(os.path.join(target_dir, "attachment", "legacy"), exist_ok=True)
        with open(os.path.join(target_dir, "ticket.json"), "wb") as fh:
            fh.write(ticket_json_bytes)
        for arcname, full in collect_attachments(ticket_dir, drop_archives=drop_archives, stats=local):
            dst = os.path.join(target_dir, "attachment", "legacy", arcname.replace("/", os.sep))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(full, dst)
            local["active_attachment_copied"] += 1
        return local

    zip_path = os.path.join(target_dir, "archive.zip")
    if os.path.exists(zip_path) and not force:
        local["skipped_existing"] += 1
        return local

    html_path = os.path.join(ticket_dir, f"ticket-{ticket_id:04d}.html")
    if not os.path.exists(html_path):
        # 老编号也可能是 5 位
        html_path = os.path.join(ticket_dir, f"ticket-{ticket_id}.html")

    attachments = collect_attachments(ticket_dir, drop_archives=drop_archives, stats=local)
    for _, full in attachments:
        local["attachment_bytes"] += os.path.getsize(full)

    tmpdir: str | None = None
    rename: dict[str, str] = {}
    final_attachments = attachments
    try:
        if png_quality is not None:
            temp_root = os.path.join(dest_root, ".tmp")
            os.makedirs(temp_root, exist_ok=True)
            tmpdir, rename, final_attachments = convert_pngs(attachments, local, png_quality, temp_root)

        assets: dict[str, bytes] = {}
        if os.path.exists(html_path):
            raw = open(html_path, "rb").read()
            local["html_bytes_in"] += len(raw)
            html, assets = rewrite_html(raw, ticket_dir, ticket_id, local, rename)
            local["html_bytes_out"] += len(html)
        else:
            html = build_stub_html(ticket_id, [name for name, _ in final_attachments], ticket_json_obj)
            local["html_missing"] += 1

        if dry_run:
            return local

        os.makedirs(target_dir, exist_ok=True)
        with open(os.path.join(target_dir, "ticket.json"), "wb") as fh:
            fh.write(ticket_json_bytes)

        db_bytes = open(db_path, "rb").read() if os.path.exists(db_path) else None
        write_archive_zip(zip_path, ticket_json_bytes, db_bytes, html, assets, final_attachments)
        local["zip_bytes"] += os.path.getsize(zip_path)

        database_zip = os.path.join(target_dir, "database.zip")
        write_database_zip(database_zip, ticket_json_bytes, db_bytes)
        local["database_zip_bytes"] += os.path.getsize(database_zip)
        return local
    finally:
        if tmpdir:
            shutil.rmtree(tmpdir, ignore_errors=True)


# --------------------------------------------------------------------------
# 主流程
# --------------------------------------------------------------------------
def main() -> int:
    parser = argparse.ArgumentParser(description="旧工单数据迁移（old-data -> storage）")
    parser.add_argument("--source", default=DEFAULT_SOURCE, help="旧数据根目录")
    parser.add_argument("--dest", default=DEFAULT_DEST, help="新 storage 根目录")
    parser.add_argument("--guild-id", type=int, default=DEFAULT_GUILD_ID, help="工单所属服务器 ID")
    parser.add_argument("--only", action="append", default=[], help="只处理指定工单号，可重复")
    parser.add_argument("--limit", type=int, default=0, help="只处理前 N 张")
    parser.add_argument("--jobs", type=int, default=4, help="并发数（默认 4）")
    parser.add_argument("--force", action="store_true", help="覆盖已存在的 zip")
    parser.add_argument("--dry-run", action="store_true", help="只统计，不写文件")
    parser.add_argument("--drop-archives", action="append", default=[], metavar="TICKET_ID",
                        help="丢掉指定工单里的压缩包（zip/7z 及 .001 分卷），可重复")
    parser.add_argument("--png-quality", type=int, default=DEFAULT_PNG_QUALITY,
                        help=f"PNG→JPEG 质量（默认 {DEFAULT_PNG_QUALITY}）")
    parser.add_argument("--no-png-to-jpeg", action="store_true", help="不做 PNG→JPEG 转换")
    parser.add_argument("--keep-active", action="store_true",
                        help="按 db.json 的 status 还原 open/closed（默认全部归入归档）")
    args = parser.parse_args()

    source = os.path.abspath(args.source)
    dest = os.path.abspath(args.dest)
    if not os.path.isdir(source):
        print(f"旧数据目录不存在: {source}", file=sys.stderr)
        return 1

    tickets: list[tuple[int, str]] = []
    for name in sorted(os.listdir(source)):
        ticket_id = parse_ticket_id(name)
        if ticket_id is None:
            continue
        tickets.append((ticket_id, os.path.join(source, name)))
    tickets.sort()

    if args.only:
        wanted = {int(value) for value in args.only}
        tickets = [item for item in tickets if item[0] in wanted]
        missing = wanted - {item[0] for item in tickets}
        for ticket_id in sorted(missing):
            print(f"跳过：找不到工单 {ticket_id}", file=sys.stderr)
    if args.limit > 0:
        tickets = tickets[: args.limit]

    if not tickets:
        print("没有需要处理的工单", file=sys.stderr)
        return 1

    print(f"源目录 : {source}")
    print(f"目标   : {dest}{'  (dry-run，不落盘)' if args.dry_run else ''}")
    print(f"工单数 : {len(tickets)}   并发: {args.jobs}")
    if args.drop_archives:
        print(f"丢压缩包: {', '.join(f'ticket-{int(v):05d}' for v in args.drop_archives)}")
    print(f"PNG→JPEG: {'关闭' if args.no_png_to_jpeg else f'质量 {args.png_quality}'}")
    print(f"归档策略: {'按 db.json 状态还原' if args.keep_active else '全部归入 archive/'}")
    print("-" * 78)

    drop_ids = {int(value) for value in args.drop_archives}
    png_quality = None if args.no_png_to_jpeg else args.png_quality

    stats = Stats()
    started = time.time()
    finished = 0

    def worker(item: tuple[int, str]) -> None:
        nonlocal finished
        ticket_id, directory = item
        t0 = time.time()
        try:
            counter = process_ticket(directory, ticket_id, dest, args.guild_id,
                                     args.force, args.dry_run, stats,
                                     drop_archives=ticket_id in drop_ids,
                                     png_quality=png_quality,
                                     keep_active=args.keep_active)
            stats.merge(counter)
            finished += 1
            with stats.lock:
                print(
                    f"[{finished:>5}/{len(tickets)}] ticket-{ticket_id:05d} "
                    f"html {human(counter['html_bytes_in'])} -> {human(counter['html_bytes_out'])}, "
                    f"assets {counter['asset_new']} new / {counter['asset_dedup']} dedup, "
                    f"orig-reuse {counter['img_reuse_original']}, "
                    f"extract {counter['b64_img_count'] + counter['b64_other_count']}, "
                    f"png {counter['png_converted']}/{counter['png_seen']}, "
                    f"zip {human(counter['zip_bytes'])}, {time.time() - t0:.1f}s",
                    flush=True,
                )
        except Exception as exc:  # noqa: BLE001 - 单张失败不应中断整体
            stats.fail(ticket_id, f"{type(exc).__name__}: {exc}")
            with stats.lock:
                print(f"[  FAIL] ticket-{ticket_id:05d}: {type(exc).__name__}: {exc}", flush=True)

    if args.jobs <= 1:
        for item in tickets:
            worker(item)
    else:
        with ThreadPoolExecutor(max_workers=args.jobs) as pool:
            futures = [pool.submit(worker, item) for item in tickets]
            for future in as_completed(futures):
                future.result()

    totals = stats.totals
    elapsed = time.time() - started
    print("-" * 78)
    print(f"完成 {len(tickets)} 张工单，用时 {elapsed:.1f}s")
    print(f"  已归档 {totals['tickets_archived']} / 活跃 {totals['tickets_active']} / 跳过 {totals['skipped_existing']}")
    print(f"  HTML   {human(totals['html_bytes_in'])} -> {human(totals['html_bytes_out'])}"
          f"  (缺 HTML {totals['html_missing']})")
    print(f"  base64 头像 {totals['b64_avatar_count']} 处 {human(totals['b64_avatar_bytes'])}")
    print(f"  base64 图片 {totals['b64_img_count']} 处 {human(totals['b64_img_bytes'])}"
          f"  (复用原件 {totals['img_reuse_original']})")
    print(f"  兜底 data:image {totals['b64_other_count']} 处 {human(totals['b64_other_bytes'])}")
    print(f"  产出 assets {totals['asset_new']} 个（去重命中 {totals['asset_dedup']}）")
    if totals["png_seen"]:
        print(f"  PNG→JPEG {totals['png_converted']}/{totals['png_seen']} 个"
              f"  {human(totals['png_bytes_in'])} -> {human(totals['png_bytes_out'])}"
              f"  (保留原件 {totals['png_kept']}, 失败 {totals['png_failed']})")
    if totals["archive_dropped_files"]:
        print(f"  丢弃压缩包 {totals['archive_dropped_files']} 个 {human(totals['archive_dropped_bytes'])}")
    print(f"  附件 {human(totals['attachment_bytes'])} -> zip {human(totals['zip_bytes'])}")
    print(f"  database.zip {human(totals['database_zip_bytes'])}（仅元数据）")

    if stats.failures:
        print(f"\n失败 {len(stats.failures)} 张：", file=sys.stderr)
        for ticket_id, message in stats.failures:
            print(f"  ticket-{ticket_id:05d}: {message}", file=sys.stderr)
        return 2

    return 0


if __name__ == "__main__":
    sys.exit(main())
