"""
Legacy engineering-docs export helper.

This script uses only Python stdlib + Win32 APIs, so it can run without
AutoHotkey or pywinauto. It is intentionally recipe-driven: record one
successful manual export flow, write the clicks/keys into JSON, then repeat it.
"""

from __future__ import annotations

import argparse
import ctypes
import json
import string
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any


user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004
KEYEVENTF_KEYUP = 0x0002

VK = {
    "BACKSPACE": 0x08,
    "TAB": 0x09,
    "ENTER": 0x0D,
    "ESC": 0x1B,
    "SPACE": 0x20,
    "LEFT": 0x25,
    "UP": 0x26,
    "RIGHT": 0x27,
    "DOWN": 0x28,
    "DELETE": 0x2E,
    "CTRL": 0x11,
    "ALT": 0x12,
    "SHIFT": 0x10,
    "F2": 0x71,
    "F4": 0x73,
}

for char in string.ascii_uppercase:
    VK[char] = ord(char)


class POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


@dataclass
class WindowInfo:
    hwnd: int
    pid: int
    title: str


def enum_windows() -> list[WindowInfo]:
    items: list[WindowInfo] = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    def callback(hwnd: int, _lparam: int) -> bool:
        if not user32.IsWindowVisible(hwnd):
            return True

        length = user32.GetWindowTextLengthW(hwnd)
        if length <= 0:
            return True

        buffer = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, buffer, length + 1)
        pid = ctypes.c_ulong()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        items.append(WindowInfo(hwnd, pid.value, buffer.value))
        return True

    user32.EnumWindows(callback, 0)
    return items


def list_windows() -> None:
    windows = enum_windows()
    if not windows:
        print("未枚举到可见窗口。请确认脚本在当前桌面会话中运行，且目标软件窗口未最小化。")
        return

    for item in sorted(windows, key=lambda value: value.title.lower()):
        print(f"{item.hwnd:#010x}\t{item.pid}\t{item.title}")


def find_window(title_contains: str) -> WindowInfo | None:
    normalized = title_contains.lower()
    for window in enum_windows():
        if normalized in window.title.lower():
            return window

    return None


def activate_window(title_contains: str) -> None:
    window = find_window(title_contains)
    if window is None:
        raise RuntimeError(f"没有找到窗口标题包含“{title_contains}”的可见窗口。")

    user32.ShowWindow(window.hwnd, 9)
    user32.SetForegroundWindow(window.hwnd)
    time.sleep(0.3)


def mouse_position_loop() -> None:
    print("每 0.5 秒输出一次鼠标坐标。按 Ctrl+C 结束。")
    point = POINT()
    try:
        while True:
            user32.GetCursorPos(ctypes.byref(point))
            print(f"x={point.x}, y={point.y}")
            time.sleep(0.5)
    except KeyboardInterrupt:
        print("\n已结束。")


def set_cursor(x: int, y: int) -> None:
    user32.SetCursorPos(x, y)


def click(x: int, y: int, times: int = 1, interval: float = 0.08) -> None:
    set_cursor(x, y)
    time.sleep(0.05)
    for _ in range(times):
        user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        time.sleep(0.03)
        user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
        time.sleep(interval)


def key_down(vk: int) -> None:
    user32.keybd_event(vk, 0, 0, 0)


def key_up(vk: int) -> None:
    user32.keybd_event(vk, 0, KEYEVENTF_KEYUP, 0)


def press_key(name: str, times: int = 1, interval: float = 0.05) -> None:
    vk = resolve_key(name)
    for _ in range(times):
        key_down(vk)
        time.sleep(0.02)
        key_up(vk)
        time.sleep(interval)


def hotkey(keys: list[str]) -> None:
    vks = [resolve_key(key) for key in keys]
    for vk in vks:
        key_down(vk)
        time.sleep(0.02)

    for vk in reversed(vks):
        key_up(vk)
        time.sleep(0.02)


def resolve_key(name: str) -> int:
    normalized = name.strip().upper()
    if normalized not in VK:
        raise RuntimeError(f"暂不支持按键：{name}")

    return VK[normalized]


def set_clipboard(text: str) -> None:
    # Tkinter ships with Python and is a compact way to write Unicode clipboard text.
    import tkinter

    root = tkinter.Tk()
    root.withdraw()
    root.clipboard_clear()
    root.clipboard_append(text)
    root.update()
    root.destroy()


def render(value: Any, variables: dict[str, Any]) -> Any:
    if isinstance(value, str):
        return value.format(**variables)
    if isinstance(value, list):
        return [render(item, variables) for item in value]
    if isinstance(value, dict):
        return {key: render(item, variables) for key, item in value.items()}
    return value


def run_action(action: dict[str, Any], variables: dict[str, Any]) -> None:
    item = render(action, variables)
    action_type = item["type"]

    if action_type == "activate":
        activate_window(item["title_contains"])
    elif action_type == "wait":
        time.sleep(float(item.get("seconds", 1)))
    elif action_type == "click":
        click(int(item["x"]), int(item["y"]), int(item.get("times", 1)))
    elif action_type == "double_click":
        click(int(item["x"]), int(item["y"]), 2)
    elif action_type == "key":
        press_key(item["key"], int(item.get("times", 1)))
    elif action_type == "hotkey":
        hotkey(item["keys"])
    elif action_type == "clipboard":
        set_clipboard(item["text"])
    elif action_type == "paste":
        hotkey(["CTRL", "V"])
    elif action_type == "print":
        print(item["message"])
    else:
        raise RuntimeError(f"未知动作类型：{action_type}")


def load_names(path: str | None, count: int) -> list[str]:
    if not path:
        return [f"template-{index + 1:04d}" for index in range(count)]

    names = [
        line.strip()
        for line in Path(path).read_text(encoding="utf-8-sig").splitlines()
        if line.strip()
    ]
    if count > len(names):
        raise RuntimeError(f"名称文件只有 {len(names)} 行，不足以导出 {count} 个。")

    return names[:count]


def run_recipe(recipe_path: Path, count: int, output_dir: Path, names_file: str | None) -> None:
    recipe = json.loads(recipe_path.read_text(encoding="utf-8-sig"))
    output_dir.mkdir(parents=True, exist_ok=True)
    names = load_names(names_file, count)

    before_all = recipe.get("before_all", [])
    per_item = recipe.get("per_item", [])
    after_all = recipe.get("after_all", [])

    base_variables = {
        "output_dir": str(output_dir),
    }

    for action in before_all:
        run_action(action, base_variables)

    for index, name in enumerate(names, start=1):
        variables = {
            **base_variables,
            "index": index,
            "name": sanitize_file_name(name),
            "display_name": name,
            "output_path": str(output_dir / f"{index:04d}_{sanitize_file_name(name)}.xls"),
        }
        print(f"[{index}/{count}] {name}")
        for action in per_item:
            run_action(action, variables)

    for action in after_all:
        run_action(action, base_variables)


def sanitize_file_name(value: str) -> str:
    invalid = '<>:"/\\|?*'
    result = "".join("_" if char in invalid else char for char in value).strip()
    return result or "未命名"


def main() -> int:
    parser = argparse.ArgumentParser(description="旧工程资料软件批量导出辅助工具")
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("list-windows", help="列出当前桌面可见窗口")
    sub.add_parser("mouse-pos", help="持续输出鼠标坐标，用于填写 recipe")

    run_parser = sub.add_parser("run", help="按 recipe 循环执行导出动作")
    run_parser.add_argument("--recipe", required=True, type=Path)
    run_parser.add_argument("--count", required=True, type=int)
    run_parser.add_argument("--output-dir", required=True, type=Path)
    run_parser.add_argument("--names-file", help="每行一个模板名；不传则自动用 template-0001")

    args = parser.parse_args()
    try:
        if args.command == "list-windows":
            list_windows()
        elif args.command == "mouse-pos":
            mouse_position_loop()
        elif args.command == "run":
            run_recipe(args.recipe, args.count, args.output_dir, args.names_file)
        else:
            parser.error("未知命令")
    except Exception as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
