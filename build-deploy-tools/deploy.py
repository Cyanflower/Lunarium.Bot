#!/usr/bin/env python3
"""Lunarium bot 构建与部署工具。

零第三方依赖（Python 3.11+，只用 stdlib）。用法：

    python3 build-deploy-tools/deploy.py                 # 交互式菜单
    python3 build-deploy-tools/deploy.py deploy Ticket   # 构建 + 推送 + 重启
    python3 build-deploy-tools/deploy.py status Ticket
    python3 build-deploy-tools/deploy.py --dry-run deploy Ticket

设计要点（都是踩过的坑，不要"优化"掉）：
  * rsync 绝不加 --delete：远端部署目录同时也是运行时数据目录
    （appsettings.json / Logs / storage / .bundle）。
  * rsync 绝不加 --inplace：原地覆写正在运行的可执行文件会 ETXTBSY。
    默认的"临时文件 + rename"只会换目录项，老进程继续持有旧 inode。
  * 远端路径一律用绝对路径，不能用 ~：它和 systemd 单元里的路径必须逐字一致。
  * 重启超时 >= 程序 ShutdownTimeout + 30s（单元里 TimeoutStopSec=150）。
"""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import os
import shlex
import shutil
import subprocess
import sys
import time
import tomllib
from dataclasses import dataclass
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOL_DIR.parent
DEFAULT_CONFIG = TOOL_DIR / "config.toml"

# SSH 连接复用：把 ssh / rsync / 校验 / systemctl 的握手合成一次
SSH_CONTROL = ["-o", "ControlMaster=auto", "-o", "ControlPath=/tmp/lunarium-ssh-%C",
               "-o", "ControlPersist=60", "-o", "BatchMode=yes"]

EXIT_OK = 0
EXIT_CONFIG = 10
EXIT_BUILD = 20
EXIT_PUSH = 30
EXIT_RESTART = 40

# =============================================================================
# 输出
# =============================================================================

_COLOR = sys.stdout.isatty() and os.environ.get("NO_COLOR") is None


def _c(code: str, text: str) -> str:
    return f"\033[{code}m{text}\033[0m" if _COLOR else text


def step(text: str) -> None:
    print(f"\n{_c('1;36', '==>')} {_c('1', text)}")


def info(text: str) -> None:
    print(f"    {text}")


def ok(text: str) -> None:
    print(f"    {_c('32', '✓')} {text}")


def warn(text: str) -> None:
    print(f"    {_c('33', '!')} {text}")


def fail(text: str) -> None:
    print(f"    {_c('31', '✗')} {text}", file=sys.stderr)


def command(argv: list[str]) -> None:
    print(_c('2', f"    $ {shlex.join(argv)}"))


# =============================================================================
# 配置
# =============================================================================


class ConfigError(Exception):
    pass


@dataclass(frozen=True)
class Host:
    name: str
    ssh_host: str
    remote_base: str


@dataclass(frozen=True)
class Project:
    name: str
    host: Host
    csproj: Path
    assembly: str
    service: str | None
    remote_dir: str
    restart_timeout: float

    @property
    def slug(self) -> str:
        """用于环境变量文件 / 单元名。"""
        return self.assembly.rsplit(".", 1)[-1].lower()


@dataclass(frozen=True)
class Settings:
    configuration: str
    staging_root: Path
    exclude: list[str]


@dataclass(frozen=True)
class Config:
    settings: Settings
    projects: dict[str, Project]


def _require(mapping: dict, key: str, where: str):
    if key not in mapping:
        raise ConfigError(f"{where} 缺少必填字段 `{key}`")
    return mapping[key]


def load_config(path: Path) -> Config:
    if not path.is_file():
        raise ConfigError(f"找不到配置文件: {path}")
    with path.open("rb") as fh:
        raw = tomllib.load(fh)

    hosts: dict[str, Host] = {}
    for name, table in raw.get("host", {}).items():
        where = f"[host.{name}]"
        hosts[name] = Host(
            name=name,
            ssh_host=_require(table, "ssh_host", where),
            remote_base=_require(table, "remote_lum_base_path", where).rstrip("/"),
        )
    if not hosts:
        raise ConfigError("配置里没有任何 [host.*] 段")

    local = raw.get("local_artifact", {})
    settings = Settings(
        configuration=local.get("configuration", "Release"),
        staging_root=(REPO_ROOT / local.get("staging_root", "build-deploy-tools/.staging")).resolve(),
        exclude=list(local.get("exclude", [])),
    )

    projects: dict[str, Project] = {}
    for name, table in raw.get("projects", {}).items():
        where = f"[projects.{name}]"
        host_name = _require(table, "host", where)
        if host_name not in hosts:
            raise ConfigError(f"{where}.host = {host_name!r} 未在 [host.*] 中定义")

        csproj = (REPO_ROOT / _require(table, "csproj", where)).resolve()
        if not csproj.is_file():
            raise ConfigError(f"{where}.csproj 不存在: {csproj}")

        assembly = csproj.stem
        host = hosts[host_name]
        projects[name] = Project(
            name=name,
            host=host,
            csproj=csproj,
            assembly=assembly,
            service=table.get("service"),
            remote_dir=f"{host.remote_base}/{assembly}",
            restart_timeout=float(table.get("restart_timeout", 180)),
        )
    if not projects:
        raise ConfigError("配置里没有任何 [projects.*] 段")

    return Config(settings=settings, projects=projects)


def resolve_project(cfg: Config, name: str) -> Project:
    for key, proj in cfg.projects.items():
        if key.lower() == name.lower():
            return proj
    known = ", ".join(cfg.projects)
    raise ConfigError(f"未知项目 {name!r}（可用: {known}）")


# =============================================================================
# 子进程
# =============================================================================


def preflight(*tools: str) -> None:
    missing = [t for t in tools if shutil.which(t) is None]
    if missing:
        raise ConfigError(f"缺少必需的可执行文件: {', '.join(missing)}")


def run_stream(argv: list[str], *, timeout: float | None = None, dry_run: bool = False) -> int:
    """执行并逐行回显；失败时打印返回码。"""
    command(argv)
    if dry_run:
        return 0
    proc = subprocess.Popen(argv, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, bufsize=1)
    try:
        assert proc.stdout is not None
        for line in proc.stdout:
            print(f"      {line.rstrip()}")
        return proc.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        proc.kill()
        fail(f"超时（{timeout}s）")
        return 124


def run_capture(argv: list[str], *, timeout: float | None = None,
                stdin_text: str | None = None, dry_run: bool = False) -> tuple[int, str]:
    """执行并返回 (rc, stdout+stderr)。"""
    command(argv)
    if dry_run:
        return 0, ""
    proc = subprocess.run(argv, input=stdin_text, capture_output=True, text=True,
                          timeout=timeout, check=False)
    return proc.returncode, (proc.stdout + proc.stderr)


def ssh_argv(host: Host, script: str) -> list[str]:
    return ["ssh", *SSH_CONTROL, host.ssh_host, script]


def ssh_run(host: Host, script: str, *, timeout: float | None = 60,
            dry_run: bool = False) -> tuple[int, str]:
    return run_capture(ssh_argv(host, script), timeout=timeout, dry_run=dry_run)


def ssh_stream(host: Host, script: str, *, timeout: float | None = 60,
               dry_run: bool = False) -> int:
    return run_stream(ssh_argv(host, script), timeout=timeout, dry_run=dry_run)


# =============================================================================
# 构建
# =============================================================================


def staging_dir(cfg: Config, proj: Project) -> Path:
    return cfg.settings.staging_root / proj.name


def do_build(cfg: Config, proj: Project, *, dry_run: bool) -> bool:
    step(f"构建 {proj.name}（{cfg.settings.configuration}）")
    if cfg.settings.configuration != "Release":
        warn("非 Release 配置：Bot.props 只在 Release 下启用自包含单文件，"
             "产物将无法在服务器上运行（服务器没装 dotnet）")

    out = staging_dir(cfg, proj)
    if not dry_run:
        if out.exists():
            shutil.rmtree(out)
        out.mkdir(parents=True, exist_ok=True)

    rc = run_stream(["dotnet", "publish", str(proj.csproj),
                     "-c", cfg.settings.configuration,
                     "-o", str(out), "--nologo"],
                    timeout=1800, dry_run=dry_run)
    if rc != 0:
        fail(f"dotnet publish 失败（rc={rc}）")
        return False

    if dry_run:
        return True

    _prune(cfg, out)
    artifact = detect_artifact(out)
    if artifact is None:
        fail(f"在 {out} 中找不到唯一可执行文件（发布形态不符合预期）")
        return False
    if artifact.name != proj.assembly:
        warn(f"产物名 {artifact.name!r} 与项目名 {proj.assembly!r} 不一致；"
             f"远端 systemd 的 ExecStart 必须用 {artifact.name}")
    size_mb = artifact.stat().st_size / 1024 / 1024
    ok(f"产物: {artifact.name}（{size_mb:.1f} MB），目录内 {len(list(out.iterdir()))} 个文件")
    return True


def _prune(cfg: Config, folder: Path) -> None:
    """按 local_artifact.exclude 删除 staging 里的本地配置，避免覆盖服务器上的真实配置。"""
    removed = []
    for path in sorted(folder.rglob("*")):
        if not path.is_file():
            continue
        rel = path.relative_to(folder).as_posix()
        if any(fnmatch.fnmatch(rel, pat) or fnmatch.fnmatch(path.name, pat)
               for pat in cfg.settings.exclude):
            path.unlink()
            removed.append(rel)
    if removed:
        info(f"已剔除本地配置: {', '.join(removed)}")


def detect_artifact(folder: Path) -> Path | None:
    """单文件发布的产物 = 目录里唯一可执行、且不是脚本/配置的文件。"""
    candidates = [
        p for p in folder.iterdir()
        if p.is_file() and os.access(p, os.X_OK)
        and not p.name.endswith((".json", ".sh", ".dll", ".pdb"))
    ]
    return candidates[0] if len(candidates) == 1 else None


# =============================================================================
# 推送
# =============================================================================


def do_push(cfg: Config, proj: Project, *, dry_run: bool) -> bool:
    step(f"推送到 {proj.host.ssh_host}:{proj.remote_dir}")
    folder = staging_dir(cfg, proj)
    artifact = None if dry_run else detect_artifact(folder)
    if not dry_run and artifact is None:
        fail(f"暂存目录没有可用产物: {folder}（先跑 build）")
        return False

    rc, _ = ssh_run(proj.host, f"mkdir -p {shlex.quote(proj.remote_dir)}", dry_run=dry_run)
    if rc != 0:
        fail(f"远端创建目录失败（rc={rc}）")
        return False

    rc = run_stream(["rsync", "-avz", "--partial", "--info=progress2", "--chmod=F755",
                     "-e", "ssh " + " ".join(SSH_CONTROL),
                     f"{folder}/", f"{proj.host.ssh_host}:{proj.remote_dir}/"],
                    timeout=1800, dry_run=dry_run)
    if rc != 0:
        fail(f"rsync 失败（rc={rc}）")
        return False

    if dry_run:
        return True

    assert artifact is not None
    local = _sha256(artifact)
    rc, out = ssh_run(proj.host, f"sha256sum {shlex.quote(proj.remote_dir + '/' + artifact.name)}")
    remote = out.split()[0] if rc == 0 and out.split() else ""
    if remote != local:
        fail(f"校验失败：本地 {local[:16]}… 远端 {remote[:16] or '(空)'}")
        return False
    ok(f"sha256 校验通过（{local[:16]}…）")
    return True


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


# =============================================================================
# systemd
# =============================================================================


def do_restart(proj: Project, *, dry_run: bool) -> bool:
    if not proj.service:
        warn("该项目未配置 service，跳过重启")
        return True

    step(f"重启 {proj.service}")
    rc, before = ssh_run(proj.host, f"systemctl show {proj.service} -p NRestarts --value",
                         dry_run=dry_run)
    restarts_before = before.strip() or "?"

    info(f"等待优雅退出（单元 TimeoutStopSec 默认 90s，本项目允许 {proj.restart_timeout:.0f}s）")
    rc = ssh_run(proj.host, f"systemctl restart {proj.service}",
                 timeout=proj.restart_timeout, dry_run=dry_run)[0]
    if rc != 0:
        fail(f"systemctl restart 失败（rc={rc}）")
        _tail_journal(proj)
        return False

    if dry_run:
        return True

    deadline = time.time() + 30
    state = ""
    while time.time() < deadline:
        state = ssh_run(proj.host, f"systemctl is-active {proj.service}", timeout=15)[1].strip()
        if state == "active":
            break
        time.sleep(2)
    if state != "active":
        fail(f"服务未进入 active（当前: {state or '未知'}）")
        _tail_journal(proj)
        return False

    # 崩溃循环时 is-active 也会短暂返回 active，等一会儿再确认一次
    time.sleep(3)
    state = ssh_run(proj.host, f"systemctl is-active {proj.service}", timeout=15)[1].strip()
    _, after = ssh_run(proj.host, f"systemctl show {proj.service} -p NRestarts -p MainPID",
                       timeout=15)
    if state != "active":
        fail(f"重启后不稳定（当前: {state}）")
        _tail_journal(proj)
        return False

    summary = " ".join(after.split())
    ok(f"active（重启前 NRestarts={restarts_before}，之后 {summary}）")
    return True


def _tail_journal(proj: Project, lines: int = 40) -> None:
    if not proj.service:
        return
    print()
    warn("最近日志:")
    ssh_stream(proj.host, f"journalctl -u {proj.service} -n {lines} --no-pager -o short-iso",
               timeout=30)


UNIT_TEMPLATE = """\
# managed-by: build-deploy-tools v1
[Unit]
Description=LunariumBot {name}
After=network-online.target{after_extra}
Wants=network-online.target
StartLimitIntervalSec=300
StartLimitBurst=10

[Service]
Type=simple
User=root
WorkingDirectory={remote_dir}
ExecStart={remote_dir}/{assembly}

Restart=always
RestartSec=10
# 程序自身 ShutdownTimeout=120s，必须高于它
TimeoutStopSec=150

StandardOutput=journal
StandardError=journal
SyslogIdentifier={service}

EnvironmentFile=-/etc/lunariumbot/{slug}.env
# 单文件发布 + ProtectHome=read-only 时必须指定解压目录
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR={remote_dir}/.bundle
Environment=FILE_LOG_LEVEL={log_level}
Environment=CONSOLE_LOG_LEVEL={log_level}

NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=read-only
ReadWritePaths={remote_dir}

[Install]
WantedBy=multi-user.target
"""


def render_unit(proj: Project, *, log_level: str = "Info", after_extra: str = "") -> str:
    return UNIT_TEMPLATE.format(
        name=proj.name,
        after_extra=after_extra,
        remote_dir=proj.remote_dir,
        assembly=proj.assembly,
        service=proj.service or f"lunariumbot-{proj.slug}",
        slug=proj.slug,
        log_level=log_level,
    )


def do_unit(proj: Project, *, write: bool, force: bool, dry_run: bool) -> bool:
    unit = proj.service or f"lunariumbot-{proj.slug}"
    path = f"/etc/systemd/system/{unit}.service"

    step(f"单元文件 {path}")
    if not write:
        info("以下为将要写入的内容（加 --write 才会真正写入）:")
        print()
        print(render_unit(proj))
        return True

    rc, exists = ssh_run(proj.host, f"test -f {path} && echo yes || echo no", dry_run=dry_run)
    if not dry_run and exists.strip() == "yes" and not force:
        warn("单元已存在，未覆盖（需要覆盖请加 --force-unit）")
        return True

    content = render_unit(proj)
    rc, out = run_capture(ssh_argv(proj.host,
                                   f"cat > {path}.tmp && chmod 644 {path}.tmp && "
                                   f"mv {path}.tmp {path} && echo written"),
                          stdin_text=content, timeout=60, dry_run=dry_run)
    if rc != 0 or (not dry_run and "written" not in out):
        fail(f"写入失败: {out.strip()}")
        return False

    # verify 的输出里可能有系统上其它坏单元的噪音，按本单元名过滤
    rc, out = ssh_run(proj.host, f"systemd-analyze verify {path} 2>&1 | grep {unit} || true",
                      timeout=60, dry_run=dry_run)
    if out.strip():
        warn(f"systemd-analyze verify: {out.strip()}")

    ssh_run(proj.host, "systemctl daemon-reload", dry_run=dry_run)
    ssh_run(proj.host, f"systemctl enable {unit}", dry_run=dry_run)
    ok(f"已写入并启用 {unit}（未启动：环境变量文件需先就位）")
    return True


# =============================================================================
# 查询
# =============================================================================


def do_status(cfg: Config, proj: Project) -> bool:
    step(f"{proj.name} 状态")
    info(f"项目 : {proj.csproj.relative_to(REPO_ROOT)}")
    info(f"产物 : {proj.assembly}")
    info(f"远端 : {proj.host.ssh_host}:{proj.remote_dir}")
    info(f"单元 : {proj.service or '(未配置)'}")

    if not proj.service:
        return True
    rc, out = ssh_run(proj.host,
                      f"systemctl is-active {proj.service}; "
                      f"systemctl show {proj.service} -p NRestarts -p MainPID "
                      f"-p ExecStart --no-pager | grep -E '^(NRestarts|MainPID|ExecStart)='")
    if rc != 0 and not out.strip():
        warn("查询失败")
        return False
    for line in out.strip().splitlines():
        if line.startswith("ExecStart="):
            line = "ExecStart=" + line.split("path=", 1)[-1].split(" ;", 1)[0]
        info(line)
    return True


def do_logs(proj: Project, lines: int) -> bool:
    if not proj.service:
        fail("该项目未配置 service")
        return False
    step(f"{proj.service} 最近 {lines} 行")
    return ssh_stream(proj.host,
                      f"journalctl -u {proj.service} -n {lines} --no-pager -o short-iso",
                      timeout=60) == 0


# =============================================================================
# 动作编排
# =============================================================================


ACTIONS = {
    "build": "只构建到暂存目录",
    "push": "构建 + 推送 + 校验（不重启）",
    "deploy": "构建 + 推送 + 校验 + 重启",
    "restart": "只重启远端服务",
    "status": "查看远端服务与产物状态",
    "logs": "查看远端最近日志",
    "unit": "生成 systemd 单元（默认只打印）",
}


def run_action(cfg: Config, proj: Project, action: str, args: argparse.Namespace) -> int:
    if action in ("build", "push", "deploy"):
        preflight("dotnet", "ssh", "rsync")
    elif action != "unit":
        preflight("ssh")

    if action == "build":
        return EXIT_OK if do_build(cfg, proj, dry_run=args.dry_run) else EXIT_BUILD

    if action in ("push", "deploy"):
        if not args.no_build:
            if not do_build(cfg, proj, dry_run=args.dry_run):
                return EXIT_BUILD
        if not do_push(cfg, proj, dry_run=args.dry_run):
            return EXIT_PUSH
        if action == "push":
            ok("推送完成（未重启）")
            return EXIT_OK
        return EXIT_OK if do_restart(proj, dry_run=args.dry_run) else EXIT_RESTART

    if action == "restart":
        return EXIT_OK if do_restart(proj, dry_run=args.dry_run) else EXIT_RESTART
    if action == "status":
        return EXIT_OK if do_status(cfg, proj) else EXIT_RESTART
    if action == "logs":
        return EXIT_OK if do_logs(proj, args.lines) else EXIT_RESTART
    if action == "unit":
        return EXIT_OK if do_unit(proj, write=args.write, force=args.force_unit,
                                  dry_run=args.dry_run) else EXIT_CONFIG

    fail(f"未知动作 {action}")
    return EXIT_CONFIG


# =============================================================================
# 交互
# =============================================================================


def ask(prompt: str) -> str:
    try:
        return input(prompt).strip()
    except (EOFError, KeyboardInterrupt):
        print()
        raise SystemExit(EXIT_OK)


def choose_project(cfg: Config) -> Project:
    names = list(cfg.projects)
    print(_c("1", "可用项目:"))
    for i, name in enumerate(names, 1):
        proj = cfg.projects[name]
        print(f"  {i}) {name:<12} {proj.service or '(不重启)'}")
    while True:
        raw = ask(f"选择项目 [1-{len(names)}]: ")
        if raw.isdigit() and 1 <= int(raw) <= len(names):
            return cfg.projects[names[int(raw) - 1]]
        match = next((p for p in cfg.projects.values() if p.name.lower() == raw.lower()), None)
        if match:
            return match


def choose_action() -> str:
    keys = list(ACTIONS)
    print(_c("1", "选择动作:"))
    for i, key in enumerate(keys, 1):
        print(f"  {i}) {key:<8} {ACTIONS[key]}")
    while True:
        raw = ask(f"选择动作 [1-{len(keys)}]: ")
        if raw.isdigit() and 1 <= int(raw) <= len(keys):
            return keys[int(raw) - 1]
        if raw in ACTIONS:
            return raw


def interactive() -> int:
    cfg = load_config(DEFAULT_CONFIG)
    proj = choose_project(cfg)
    action = choose_action()

    args = argparse.Namespace(dry_run=False, no_build=False, lines=60,
                              write=False, force_unit=False)
    print()
    info(f"项目: {proj.name}    动作: {action}    {ACTIONS[action]}")
    if ask("确认执行? [y/N] ").lower() not in ("y", "yes"):
        info("已取消")
        return EXIT_OK
    return run_action(cfg, proj, action, args)


# =============================================================================
# CLI
# =============================================================================


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="deploy.py",
        description="Lunarium bot 构建与部署工具",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="动作:\n" + "\n".join(f"  {k:<8} {v}" for k, v in ACTIONS.items()),
    )
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG, help="配置文件路径")
    parser.add_argument("--dry-run", action="store_true", help="只打印命令，不执行")
    parser.add_argument("--no-build", action="store_true", help="push/deploy 时跳过构建")
    parser.add_argument("--lines", type=int, default=60, help="logs 的行数（默认 60）")
    parser.add_argument("--write", action="store_true", help="unit: 真正写入远端")
    parser.add_argument("--force-unit", action="store_true", help="unit: 覆盖已存在的单元")
    parser.add_argument("action", nargs="?", choices=[*ACTIONS, "menu"], help="要执行的动作")
    parser.add_argument("project", nargs="?", help="项目名（省略则进入菜单）")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.action in (None, "menu"):
        return interactive()

    try:
        cfg = load_config(args.config)
        proj = resolve_project(cfg, args.project) if args.project else None
    except ConfigError as exc:
        fail(str(exc))
        return EXIT_CONFIG

    if proj is None:
        proj = choose_project(cfg)
        print()
        info(f"项目: {proj.name}    动作: {args.action}    {ACTIONS[args.action]}")
        if ask("确认执行? [y/N] ").lower() not in ("y", "yes"):
            info("已取消")
            return EXIT_OK

    if args.dry_run:
        warn("dry-run：只打印命令，不产生任何副作用")

    try:
        return run_action(cfg, proj, args.action, args)
    except ConfigError as exc:
        fail(str(exc))
        return EXIT_CONFIG
    except KeyboardInterrupt:
        print()
        info("已中断")
        return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
